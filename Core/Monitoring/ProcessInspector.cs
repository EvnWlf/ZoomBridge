using System.Diagnostics;

namespace ZoomBridge.Core.Monitoring
{
    public class ProcessInspector
    {
        private static readonly string[] TranscoderProcessNames = { "zTscoder", "CptHost" };
        private readonly Func<bool> _isTranscoderRunning;
        private readonly TimeSpan _pollInterval;

        public ProcessInspector(Func<bool>? isTranscoderRunning = null, TimeSpan? pollInterval = null)
        {
            _isTranscoderRunning = isTranscoderRunning ?? DefaultIsTranscoderRunning;
            _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
        }

        public bool IsTranscoderRunning() => _isTranscoderRunning();

        private static bool DefaultIsTranscoderRunning()
        {
            foreach (var name in TranscoderProcessNames)
            {
                var processes = Process.GetProcessesByName(name);
                bool running = processes.Length > 0;
                foreach (var p in processes) p.Dispose();
                if (running) return true;
            }
            return false;
        }

        /// <summary>
        /// Comprueba si la carpeta de Zoom está lista para transferirse.
        /// No se traba si es una reunión sólo de chat (.txt) o grabaciones previas.
        /// </summary>
        public async Task<bool> WaitForFolderReadyAsync(string folderPath, TimeSpan maxWaitTime, CancellationToken ct = default)
        {
            var deadline = DateTime.UtcNow + maxWaitTime;
            try
            {
                while (DateTime.UtcNow < deadline)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Directory.Exists(folderPath)) return false;

                    // 1. Si el transcodificador de Zoom está corriendo actualmente en Windows, esperar
                    if (IsTranscoderRunning())
                    {
                        await Task.Delay(_pollInterval, ct);
                        continue;
                    }

                    var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                    if (files.Length == 0)
                    {
                        // Carpeta vacía recién creada por Zoom, esperar un momento
                        await Task.Delay(_pollInterval, ct);
                        continue;
                    }

                    // 2. Si son archivos antiguos (más de 3 minutos sin modificarse), ya concluyeron
                    var latestWrite = files.Max(f =>
                    {
                        try { return File.GetLastWriteTimeUtc(f); } catch { return DateTime.MinValue; }
                    });

                    bool isOldFolder = (DateTime.UtcNow - latestWrite) > TimeSpan.FromMinutes(2);

                    // 3. Si es reciente, verificar si tiene archivos temporales activos (.tmp o .zoom)
                    bool hasActiveTempFiles = files.Any(f =>
                        f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                        (f.EndsWith(".zoom", StringComparison.OrdinalIgnoreCase) && !isOldFolder));

                    if (hasActiveTempFiles)
                    {
                        await Task.Delay(_pollInterval, ct);
                        continue;
                    }

                    // 4. Verificar estabilidad de tamaño
                    long firstSize = TotalSize(files);
                    if (firstSize < 0)
                    {
                        await Task.Delay(_pollInterval, ct);
                        continue;
                    }

                    // Para carpetas viejas basta 500ms; para nuevas 2 segundos
                    await Task.Delay(isOldFolder ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(2), ct);
                    if (!Directory.Exists(folderPath)) return false;

                    files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                    long secondSize = TotalSize(files);

                    if (firstSize == secondSize && CanOpenAllExclusively(files))
                    {
                        return true;
                    }

                    await Task.Delay(_pollInterval, ct);
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            return false;
        }

        private static long TotalSize(string[] files)
        {
            long total = 0;
            foreach (var file in files)
            {
                try { total += new FileInfo(file).Length; }
                catch { return -1; }
            }
            return total;
        }

        private static bool CanOpenAllExclusively(string[] files)
        {
            foreach (var file in files)
            {
                try
                {
                    using var _ = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }
    }
}
