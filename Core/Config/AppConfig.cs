namespace ZoomBridge.Core.Config
{
    /// <summary>
    /// Configuración persistida en config.json. No se asume ninguna ruta:
    /// el usuario debe elegir origen y destino durante el asistente inicial.
    /// </summary>
    public class AppConfig
    {
        public bool IsConfigured { get; set; } = false;

        public string ZoomDirectory { get; set; } = string.Empty;
        public string DestinationDirectory { get; set; } = string.Empty;

        public bool AutoStartWithWindows { get; set; } = true;
        public bool RenamePromptEnabled { get; set; } = true;
        public int RenameTimeoutSeconds { get; set; } = 30;
        public bool DeleteSourceAfterMove { get; set; } = true;
        public bool ShowNotifications { get; set; } = true;

        /// <summary>System, Light o Dark.</summary>
        public string Theme { get; set; } = "System";

        /// <summary>es o en.</summary>
        public string Language { get; set; } = "es";

        /// <summary>
        /// Ubicación típica de Zoom, sólo para sugerirla en el asistente (nunca se aplica sola).
        /// </summary>
        public static string? DetectZoomFolder()
        {
            var candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Zoom");
            return Directory.Exists(candidate) ? candidate : null;
        }
    }
}
