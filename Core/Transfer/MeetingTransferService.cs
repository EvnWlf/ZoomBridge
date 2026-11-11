using System.Diagnostics;
using ZoomBridge.Core.Config;
using ZoomBridge.Core.Storage;

namespace ZoomBridge.Core.Transfer
{
    public enum ResyncOutcome { Copied, Verified, Missing, Failed }

    public class MeetingTransferService
    {
        private const int BufferSize = 1024 * 1024;
        private readonly ConfigManager _configManager;
        private readonly DatabaseContext _database;
        private readonly IAgentStatusSink? _status;

        /// <summary>(nombre original, segundos) → nombre elegido. Null = sin diálogo.</summary>
        public Func<string, int, Task<string>>? PromptRenameHandler { get; set; }

        public MeetingTransferService(ConfigManager configManager, DatabaseContext database, IAgentStatusSink? status = null)
        {
            _configManager = configManager;
            _database = database;
            _status = status;
        }

        /// <summary>Traslado normal de una carpeta recién terminada por Zoom.</summary>
        public async Task<bool> TransferMeetingFolderAsync(string sourceFolderPath, bool interactive = true, CancellationToken ct = default)
        {
            if (!Directory.Exists(sourceFolderPath)) return false;

            var config = _configManager.CurrentConfig;
            var originalName = Path.GetFileName(sourceFolderPath);
            var chosenName = originalName;

            if (interactive && config.RenamePromptEnabled && PromptRenameHandler != null)
            {
                _status?.ReportPrompting(originalName);
                try
                {
                    var answer = await PromptRenameHandler(originalName, config.RenameTimeoutSeconds);
                    if (!string.IsNullOrWhiteSpace(answer)) chosenName = SanitizeFileName(answer);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Transfer] Diálogo de nombre: {ex.Message}");
                }
            }

            var destRoot = config.DestinationDirectory;
            if (string.IsNullOrWhiteSpace(destRoot))
            {
                await RecordAsync(originalName, sourceFolderPath, string.Empty, 0, 0, RecordingStatus.Failed, "No hay carpeta de destino configurada.");
                _status?.ReportError("No hay carpeta de destino configurada.");
                return false;
            }

            var target = GetUniqueDirectoryPath(Path.Combine(destRoot, chosenName));
            return await CopyAndRecordAsync(sourceFolderPath, target, config.DeleteSourceAfterMove, ct);
        }

        /// <summary>
        /// Volver a sincronizar: si el origen sigue existiendo se copia de nuevo al destino;
        /// si no, se verifica la copia existente en el destino.
        /// </summary>
        public async Task<ResyncOutcome> ResyncAsync(ProcessedRecording record, CancellationToken ct = default)
        {
            var config = _configManager.CurrentConfig;

            if (Directory.Exists(record.OriginalFolderPath))
            {
                var target = !string.IsNullOrWhiteSpace(record.DestinationFolderPath)
                    ? record.DestinationFolderPath
                    : GetUniqueDirectoryPath(Path.Combine(config.DestinationDirectory, Path.GetFileName(record.OriginalFolderPath)));

                bool ok = await CopyAndRecordAsync(record.OriginalFolderPath, target, config.DeleteSourceAfterMove, ct);
                return ok ? ResyncOutcome.Copied : ResyncOutcome.Failed;
            }

            if (Directory.Exists(record.DestinationFolderPath))
            {
                var (count, bytes) = Measure(record.DestinationFolderPath);
                await RecordAsync(Path.GetFileName(record.DestinationFolderPath), record.OriginalFolderPath,
                                  record.DestinationFolderPath, count, bytes, RecordingStatus.Completed, null);
                _status?.RecordingsChanged();
                return ResyncOutcome.Verified;
            }

            await RecordAsync(record.FolderName, record.OriginalFolderPath, record.DestinationFolderPath,
                              record.FileCount, record.TotalSizeBytes, RecordingStatus.Missing,
                              "La grabación ya no existe en la carpeta de Zoom ni en el destino.");
            _status?.RecordingsChanged();
            return ResyncOutcome.Missing;
        }

        /// <summary>Registra carpetas existentes como omitidas (el usuario no quiso respaldarlas).</summary>
        public async Task MarkAsSkippedAsync(IEnumerable<string> folders)
        {
            foreach (var folder in folders)
            {
                var (count, bytes) = Measure(folder);
                await RecordAsync(Path.GetFileName(folder), folder, string.Empty, count, bytes, RecordingStatus.Skipped, null);
            }
            _status?.RecordingsChanged();
        }

        private async Task<bool> CopyAndRecordAsync(string source, string target, bool deleteSource, CancellationToken ct)
        {
            var displayName = Path.GetFileName(target);
            var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            long total = files.Sum(f => new FileInfo(f).Length);
            long copied = 0;

            try
            {
                Directory.CreateDirectory(target);
                _status?.ReportTransfer(displayName, 0, total);

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var destFile = Path.Combine(target, Path.GetRelativePath(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                    long before = copied;
                    await CopyFileWithRetryAsync(file, destFile, written =>
                    {
                        copied = before + written;
                        _status?.ReportTransfer(displayName, copied, total);
                    }, ct);
                    copied = before + new FileInfo(file).Length;
                }

                // Verificación: mismo número de archivos y mismos tamaños antes de borrar el origen
                foreach (var file in files)
                {
                    var destFile = Path.Combine(target, Path.GetRelativePath(source, file));
                    if (!File.Exists(destFile) || new FileInfo(destFile).Length != new FileInfo(file).Length)
                        throw new IOException($"La copia de «{Path.GetFileName(file)}» no coincide con el original.");
                }

                if (deleteSource)
                {
                    try { Directory.Delete(source, recursive: true); }
                    catch (Exception ex) { Debug.WriteLine($"[Transfer] No se pudo borrar el origen: {ex.Message}"); }
                }

                await RecordAsync(displayName, source, target, files.Length, total, RecordingStatus.Completed, null);
                _status?.Notify("Grabación respaldada", $"«{displayName}» ({Formatting.Bytes(total)})");
                _status?.RecordingsChanged();
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await RecordAsync(displayName, source, target, files.Length, total, RecordingStatus.Failed, ex.Message);
                _status?.ReportError($"No se pudo respaldar «{displayName}»: {ex.Message}");
                _status?.Notify("No se pudo respaldar la grabación", $"«{displayName}»: {ex.Message}");
                _status?.RecordingsChanged();
                return false;
            }
        }

        private static async Task CopyFileWithRetryAsync(string source, string dest, Action<long> progress, CancellationToken ct)
        {
            const int maxAttempts = 5;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
                    {
                        var buffer = new byte[BufferSize];
                        long written = 0;
                        int read;
                        while ((read = await input.ReadAsync(buffer, ct)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), ct);
                            written += read;
                            progress(written);
                        }
                    }
                    File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(source));
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    // Destino de red/OneDrive temporalmente ocupado: espera creciente
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                }
            }
        }

        private static (int count, long bytes) Measure(string folder)
        {
            try
            {
                var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
                return (files.Length, files.Sum(f => new FileInfo(f).Length));
            }
            catch { return (0, 0); }
        }

        private async Task RecordAsync(string name, string source, string dest, int count, long bytes, string status, string? error)
        {
            try
            {
                await _database.AddRecordAsync(new ProcessedRecording
                {
                    FolderName = name,
                    OriginalFolderPath = source,
                    DestinationFolderPath = dest,
                    FileCount = count,
                    TotalSizeBytes = bytes,
                    Status = status,
                    ProcessedAt = DateTime.UtcNow,
                    ErrorMessage = error
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Transfer] No se pudo registrar en SQLite: {ex.Message}");
            }
        }

        private static string GetUniqueDirectoryPath(string basePath)
        {
            if (!Directory.Exists(basePath)) return basePath;
            var parent = Path.GetDirectoryName(basePath)!;
            var name = Path.GetFileName(basePath);
            for (int i = 2; ; i++)
            {
                var candidate = Path.Combine(parent, $"{name} ({i})");
                if (!Directory.Exists(candidate)) return candidate;
            }
        }

        public static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(cleaned) ? "Grabación de Zoom" : cleaned;
        }
    }
}
