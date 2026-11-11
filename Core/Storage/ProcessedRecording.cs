using ZoomBridge.Core.Localization;

namespace ZoomBridge.Core.Storage
{
    public static class RecordingStatus
    {
        public const string Completed = "Completed";
        public const string Failed = "Failed";
        /// <summary>Existía antes de instalar ZoomBridge y el usuario eligió no respaldarla.</summary>
        public const string Skipped = "Skipped";
        /// <summary>Ya no existe ni en el origen ni en el destino.</summary>
        public const string Missing = "Missing";
    }

    public class ProcessedRecording
    {
        public long Id { get; set; }
        public string FolderName { get; set; } = string.Empty;
        public string OriginalFolderPath { get; set; } = string.Empty;

        /// <summary>
        /// Nombre amigable de la reunión. Si Zoom le puso "YYYY-MM-DD HH.mm.ss Tema",
        /// extrae el tema para que sea legible y no corte los chips de estado.
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FolderName)) return "Reunión de Zoom";
                if (FolderName.Length > 20 && char.IsDigit(FolderName[0]) && FolderName[4] == '-' && FolderName[7] == '-')
                {
                    var topic = FolderName.Substring(19).Trim();
                    if (!string.IsNullOrWhiteSpace(topic)) return topic;
                }
                return FolderName;
            }
        }
        public string DestinationFolderPath { get; set; } = string.Empty;
        public int FileCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public string Status { get; set; } = RecordingStatus.Completed;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        public string? ErrorMessage { get; set; }

        public bool IsCompleted => Status == RecordingStatus.Completed;
        public bool HasProblem => Status is RecordingStatus.Failed or RecordingStatus.Missing;

        public string FormattedSize => Formatting.Bytes(TotalSizeBytes);

        public string StatusText => Status switch
        {
            RecordingStatus.Completed => Strings.Get("StatusSaved"),
            RecordingStatus.Failed => "Error",
            RecordingStatus.Skipped => "Omitida",
            RecordingStatus.Missing => "No encontrada",
            _ => Status
        };

        /// <summary>Segoe Fluent Icons: Video, Error, Info, Unknown.</summary>
        public string StatusGlyph => Status switch
        {
            RecordingStatus.Completed => "\uE714",
            RecordingStatus.Skipped => "\uE8B7",
            _ => "\uE783"
        };

        public string Subtitle
        {
            get
            {
                var parts = new List<string> { ProcessedAt.ToLocalTime().ToString("g") };
                if (TotalSizeBytes > 0) parts.Add(FormattedSize);
                if (FileCount > 0) parts.Add(FileCount == 1 ? "1 archivo" : $"{FileCount} archivos");
                if (!IsCompleted) parts.Add(StatusText);
                return string.Join("  ·  ", parts);
            }
        }
    }

    public static class Formatting
    {
        public static string Bytes(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024L * 1024 => $"{bytes / 1024.0:0.#} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"
        };
    }
}
