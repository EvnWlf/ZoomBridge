using System.Drawing;
using System.IO;
using System.Windows;
using ZoomBridge.Core.Config;
using ZoomBridge.Core.Monitoring;
using ZoomBridge.Core.Storage;
using ZoomBridge.Core.Transfer;
using ZoomBridge.UI;

namespace ZoomBridge
{
    public partial class App : Application
    {
        private const string AppMutexName = "Global\\ZoomBridge_SingleInstance_Mutex";
        private Mutex? _singleInstanceMutex;

        private ConfigManager? _configManager;
        private DatabaseContext? _databaseContext;
        private ZoomWatcherService? _watcherService;
        private MeetingTransferService? _transferService;
        private AgentState? _agentState;
        private NativeTrayIcon? _trayIcon;
        private TrayFlyoutWindow? _flyoutWindow;
        private MainWindow? _mainWindow;
        private OnboardingWindow? _onboardingWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(true, AppMutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                MessageBox.Show(
                    "ZoomBridge ya se encuentra en ejecución en la bandeja del sistema.",
                    "ZoomBridge", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            InitializeApplication(e.Args);
        }

        private void InitializeApplication(string[] args)
        {
            try
            {
                _configManager = new ConfigManager();
                ZoomBridge.Core.Localization.Strings.CurrentLanguage = _configManager.CurrentConfig.Language;
                ZoomBridge.UI.MainWindow.ApplyTheme(_configManager.CurrentConfig.Theme);

                _databaseContext = new DatabaseContext(_configManager.DataDirectory);
                _agentState = new AgentState();
                _transferService = new MeetingTransferService(_configManager, _databaseContext, _agentState);
                _watcherService = new ZoomWatcherService(_configManager, _databaseContext, status: _agentState);

                // Diálogo para renombrar reunión
                _transferService.PromptRenameHandler = HandleRenamePromptAsync;

                // Evento al estar lista una reunión
                _watcherService.RecordingReady = async (path, interactive) =>
                {
                    await _transferService.TransferMeetingFolderAsync(path, interactive);
                };

                // Inicializar Flyout y Tray Icon nativo en máxima resolución
                _flyoutWindow = new TrayFlyoutWindow(_agentState, _configManager, _watcherService);
                _flyoutWindow.OpenFullWindowRequested += ShowMainWindow;
                _flyoutWindow.ExitRequested += ExitApplication;

                var appIcon = IconHelper.GetCrispTrayIcon();
                _trayIcon = new NativeTrayIcon(appIcon, "ZoomBridge - Agente de Grabaciones");
                _trayIcon.LeftClick += pt => Dispatcher.Invoke(() => _flyoutWindow.ShowNearCursor(pt));
                _trayIcon.RightClick += pt => Dispatcher.Invoke(() => _flyoutWindow.ShowNearCursor(pt));

                _agentState.NotificationRequested += (title, msg) =>
                {
                    if (_configManager.CurrentConfig.ShowNotifications)
                    {
                        Dispatcher.Invoke(() => _trayIcon.ShowNotification(title, msg));
                    }
                };

                bool startInBackground = args.Contains("--background", StringComparer.OrdinalIgnoreCase);

                if (!_configManager.CurrentConfig.IsConfigured)
                {
                    LaunchOnboarding();
                }
                else
                {
                    _watcherService.Start();
                    if (!startInBackground)
                    {
                        ShowMainWindow();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al inicializar ZoomBridge: {ex.Message}", "Error de inicio", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void LaunchOnboarding()
        {
            _onboardingWindow = new OnboardingWindow(_configManager!);
            _onboardingWindow.OnboardingCompleted += () =>
            {
                _watcherService?.Start();
                ShowMainWindow();
            };
            _onboardingWindow.Show();
        }

        public void ShowMainWindow()
        {
            Dispatcher.Invoke(() =>
            {
                if (_mainWindow == null)
                {
                    _mainWindow = new MainWindow(
                        _configManager!,
                        _databaseContext!,
                        _watcherService!,
                        _transferService!,
                        _agentState!);
                }

                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
                _ = _mainWindow.RefreshAllAsync();
            });
        }

        private Task<string> HandleRenamePromptAsync(string originalName, int timeoutSeconds)
        {
            var tcs = new TaskCompletionSource<string>();

            Dispatcher.Invoke(() =>
            {
                try
                {
                    var prompt = new RenamePromptWindow(originalName, timeoutSeconds);
                    prompt.Closed += (s, e) => tcs.TrySetResult(prompt.SelectedName);
                    prompt.Show();
                }
                catch
                {
                    tcs.TrySetResult(originalName);
                }
            });

            return tcs.Task;
        }

        private void ExitApplication()
        {
            _watcherService?.Stop();
            _watcherService?.Dispose();
            _trayIcon?.Dispose();

            if (_mainWindow != null)
            {
                _mainWindow.RealExit();
            }

            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();

            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ExitApplication();
            base.OnExit(e);
        }
    }
}
