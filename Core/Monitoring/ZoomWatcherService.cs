using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using ZoomBridge.Core.Config;
using ZoomBridge.Core.Storage;

namespace ZoomBridge.Core.Monitoring
{
    /// <summary>
    /// Vigila la carpeta de Zoom y entrega, de una en una, las carpetas de reunión listas.
    /// </summary>
    public class ZoomWatcherService : IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly DatabaseContext _database;
        private readonly ProcessInspector _inspector;
        private readonly IAgentStatusSink? _status;

        private readonly Channel<(string Path, bool Interactive)> _queue =
            Channel.CreateUnbounded<(string, bool)>(new UnboundedChannelOptions { SingleReader = true });
        private readonly ConcurrentDictionary<string, byte> _enqueued = new(StringComparer.OrdinalIgnoreCase);

        private FileSystemWatcher? _watcher;
        private CancellationTokenSource? _cts;

        /// <summary>(ruta, interactivo). Interactivo = detectada en vivo, se puede pedir nombre.</summary>
        public Func<string, bool, Task>? RecordingReady { get; set; }

        /// <summary>Tiempo máximo de espera por carpeta (una reunión larga sigue grabándose).</summary>
        public TimeSpan MaxWaitPerFolder { get; set; } = TimeSpan.FromHours(4);

        public bool IsRunning => _watcher?.EnableRaisingEvents == true;

        public ZoomWatcherService(ConfigManager configManager, DatabaseContext database,
                                  ProcessInspector? inspector = null, IAgentStatusSink? status = null)
        {
            _configManager = configManager;
            _database = database;
            _inspector = inspector ?? new ProcessInspector();
            _status = status;
        }

        public bool Start()
        {
            Stop();
            var zoomDir = _configManager.CurrentConfig.ZoomDirectory;
            if (string.IsNullOrWhiteSpace(zoomDir) || !Directory.Exists(zoomDir))
            {
                _status?.ReportError("No se encuentra la carpeta de Zoom. Revisa la configuración.");
                return false;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => ProcessQueueAsync(token));

            _watcher = new FileSystemWatcher(zoomDir)
            {
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                InternalBufferSize = 32 * 1024
            };
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Renamed += OnChanged;
            _watcher.Error += (_, e) => Debug.WriteLine($"[Watcher] {e.GetException().Message}");
            _watcher.EnableRaisingEvents = true;

            _status?.ReportIdle();
            _ = Task.Run(() => ScanPendingFoldersAsync());
            return true;
        }

        public void Stop()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            var folder = ResolveMeetingFolder(e.FullPath);
            if (folder != null) EnqueueFolder(folder, interactive: true);
        }

        /// <summary>Convierte cualquier ruta interna en la subcarpeta de reunión de primer nivel.</summary>
        private string? ResolveMeetingFolder(string fullPath)
        {
            var root = _configManager.CurrentConfig.ZoomDirectory;
            if (string.IsNullOrEmpty(root)) return null;

            var relative = Path.GetRelativePath(root, fullPath);
            if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                return null;

            var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            var candidate = Path.Combine(root, first);
            return Directory.Exists(candidate) ? candidate : null;
        }

        public void EnqueueFolder(string folderPath, bool interactive)
        {
            if (Directory.Exists(folderPath) && _enqueued.TryAdd(folderPath, 0))
            {
                _queue.Writer.TryWrite((folderPath, interactive));
            }
        }

        /// <summary>Subcarpetas de Zoom que aún no están registradas como atendidas.</summary>
        public async Task<List<string>> GetUnprocessedFoldersAsync()
        {
            var result = new List<string>();
            var zoomDir = _configManager.CurrentConfig.ZoomDirectory;
            if (string.IsNullOrWhiteSpace(zoomDir) || !Directory.Exists(zoomDir)) return result;

            foreach (var dir in Directory.GetDirectories(zoomDir))
            {
                if (!await _database.IsFolderProcessedAsync(dir)) result.Add(dir);
            }
            return result;
        }

        public async Task<int> ScanPendingFoldersAsync()
        {
            try
            {
                var pending = await GetUnprocessedFoldersAsync();
                foreach (var dir in pending) EnqueueFolder(dir, interactive: false);
                return pending.Count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Watcher] Error al escanear: {ex.Message}");
                return 0;
            }
        }

        private async Task ProcessQueueAsync(CancellationToken ct)
        {
            try
            {
                while (await _queue.Reader.WaitToReadAsync(ct))
                {
                    while (!ct.IsCancellationRequested && _queue.Reader.TryRead(out var item))
                    {
                        await ProcessFolderAsync(item.Path, item.Interactive, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Supervisión pausada o detenida
            }
        }

        private async Task ProcessFolderAsync(string folderPath, bool interactive, CancellationToken ct)
        {
            try
            {
                if (!Directory.Exists(folderPath) || await _database.IsFolderProcessedAsync(folderPath))
                    return;

                _status?.ReportWaiting(Path.GetFileName(folderPath));
                bool ready = await _inspector.WaitForFolderReadyAsync(folderPath, MaxWaitPerFolder, ct);

                if (ready && Directory.Exists(folderPath) && RecordingReady != null)
                {
                    await RecordingReady(folderPath, interactive);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _status?.ReportError($"Error con «{Path.GetFileName(folderPath)}»: {ex.Message}");
            }
            finally
            {
                _enqueued.TryRemove(folderPath, out _);
                if (!ct.IsCancellationRequested && _enqueued.IsEmpty) _status?.ReportIdle();
            }
        }

        public void Dispose() => Stop();
    }
}
