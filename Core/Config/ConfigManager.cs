using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace ZoomBridge.Core.Config
{
    public class ConfigManager
    {
        private const string ConfigFileName = "config.json";
        private const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "ZoomBridge";
        private const string LegacyRegistryName = "ZoomBridgeAgent";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly bool _manageAutoStart;

        public static string DefaultDataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZoomBridge");

        public string DataDirectory { get; }
        public string ConfigFilePath => Path.Combine(DataDirectory, ConfigFileName);
        public AppConfig CurrentConfig { get; private set; }

        public event Action? ConfigChanged;

        /// <param name="dataDirectory">Carpeta de datos. Null = %APPDATA%\ZoomBridge.</param>
        /// <param name="manageAutoStart">Si es false nunca toca el registro (pruebas / modo portátil).</param>
        public ConfigManager(string? dataDirectory = null, bool manageAutoStart = true)
        {
            DataDirectory = string.IsNullOrWhiteSpace(dataDirectory) ? DefaultDataDirectory : dataDirectory;
            _manageAutoStart = manageAutoStart;
            Directory.CreateDirectory(DataDirectory);
            CurrentConfig = Load();
        }

        private AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFilePath));
                    if (config != null) return config;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigManager] config.json ilegible, se usa configuración nueva: {ex.Message}");
            }
            // Primera ejecución: no se escribe nada hasta que el usuario termine el asistente.
            return new AppConfig();
        }

        public void Save()
        {
            Directory.CreateDirectory(DataDirectory);
            var tmp = ConfigFilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(CurrentConfig, JsonOptions));
            File.Move(tmp, ConfigFilePath, overwrite: true);

            if (_manageAutoStart && CurrentConfig.IsConfigured)
            {
                SetAutoStart(CurrentConfig.AutoStartWithWindows);
            }
            ConfigChanged?.Invoke();
        }

        /// <summary>Borra configuración y autoarranque (Restablecer ZoomBridge).</summary>
        public void Reset()
        {
            if (_manageAutoStart) SetAutoStart(false);
            try { File.Delete(ConfigFilePath); } catch { }
            CurrentConfig = new AppConfig();
            ConfigChanged?.Invoke();
        }

        private static void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, writable: true);
                if (key == null) return;
                key.DeleteValue(LegacyRegistryName, throwOnMissingValue: false);

                var exePath = Environment.ProcessPath;
                // Sólo registrar el ejecutable real de la app (nunca un host de pruebas)
                if (enable && !string.IsNullOrEmpty(exePath) &&
                    Path.GetFileNameWithoutExtension(exePath).Equals("ZoomBridge", StringComparison.OrdinalIgnoreCase))
                {
                    key.SetValue(AppRegistryName, $"\"{exePath}\" --background");
                }
                else if (!enable)
                {
                    key.DeleteValue(AppRegistryName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigManager] Error al configurar inicio automático: {ex.Message}");
            }
        }
    }
}
