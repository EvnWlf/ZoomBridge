using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ZoomBridge.Core.Config;
using ZoomBridge.Core.Localization;
using ZoomBridge.Core.Monitoring;
using ZoomBridge.Core.Storage;
using ZoomBridge.Core.Transfer;

namespace ZoomBridge.UI
{
    public partial class MainWindow : Window
    {
        private readonly ConfigManager _configManager;
        private readonly DatabaseContext _databaseContext;
        private readonly ZoomWatcherService _watcherService;
        private readonly MeetingTransferService _transferService;
        private readonly AgentState _agentState;

        private List<ProcessedRecording> _allRecordings = new();
        private bool _isRealExit = false;
        private DispatcherTimer? _bannerTimer;

        public MainWindow(
            ConfigManager configManager,
            DatabaseContext databaseContext,
            ZoomWatcherService watcherService,
            MeetingTransferService transferService,
            AgentState agentState)
        {
            InitializeComponent();

            _configManager = configManager;
            _databaseContext = databaseContext;
            _watcherService = watcherService;
            _transferService = transferService;
            _agentState = agentState;

            Icon = IconHelper.GetHighResImageSource();
            ImgMainAppIcon.Source = IconHelper.GetHighResImageSource();

            _agentState.PropertyChanged += (s, e) => UpdateDashboardStatus();
            _agentState.RecordingsUpdated += () => Dispatcher.InvokeAsync(RefreshAllAsync);

            Loaded += async (s, e) =>
            {
                ApplyLanguage(_configManager.CurrentConfig.Language);
                await RefreshAllAsync();
                LoadSettingsValues();
                UpdateDashboardStatus();
            };
        }

        public async Task RefreshAllAsync()
        {
            await RefreshStatsAsync();
            await RefreshRecordingsListAsync();
        }

        private async Task RefreshStatsAsync()
        {
            try
            {
                var (count, bytes, last) = await _databaseContext.GetStatisticsAsync();
                KpiCount.Text = count.ToString();
                KpiSize.Text = Formatting.Bytes(bytes);
                KpiLastTime.Text = last.HasValue ? last.Value.ToLocalTime().ToString("g") : Strings.Get("Never");
            }
            catch { }
        }

        private async Task RefreshRecordingsListAsync()
        {
            try
            {
                _allRecordings = await _databaseContext.GetRecentAsync(500);
                ApplyFilter();
                ListDashboardActivity.ItemsSource = _allRecordings.Take(12).ToList();
            }
            catch { }
        }

        private void ApplyFilter()
        {
            var filter = TxtSearchRecordings?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(filter))
            {
                ListRecordingsItems.ItemsSource = _allRecordings;
            }
            else
            {
                ListRecordingsItems.ItemsSource = _allRecordings
                    .Where(r => r.FolderName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                r.DestinationFolderPath.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        private void UpdateDashboardStatus()
        {
            DashboardStatusTitle.Text = _agentState.StatusTitle;
            DashboardStatusSubtitle.Text = _agentState.StatusSubtitle;

            switch (_agentState.Mode)
            {
                case AgentStatusMode.Transferring:
                    DashboardStatusDot.Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                    DashboardProgressBar.Visibility = Visibility.Visible;
                    DashboardProgressBar.Value = _agentState.ProgressPercent;
                    break;
                case AgentStatusMode.Waiting:
                case AgentStatusMode.Prompting:
                    DashboardStatusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    DashboardProgressBar.Visibility = Visibility.Collapsed;
                    break;
                case AgentStatusMode.Error:
                    DashboardStatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    DashboardProgressBar.Visibility = Visibility.Collapsed;
                    break;
                default:
                    DashboardStatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                    DashboardProgressBar.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewDashboard == null || ViewRecordings == null || ViewSettings == null) return;

            ViewDashboard.Visibility = (NavDashboard.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            ViewRecordings.Visibility = (NavRecordings.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            ViewSettings.Visibility = (NavSettings.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (NavRecordings.IsChecked == true)
            {
                _ = RefreshRecordingsListAsync();
            }
            else if (NavSettings.IsChecked == true)
            {
                LoadSettingsValues();
            }
        }

        private void LoadSettingsValues()
        {
            var config = _configManager.CurrentConfig;
            TxtSettingsZoomPath.Text = config.ZoomDirectory;
            TxtSettingsDestPath.Text = config.DestinationDirectory;
            ChkSettingsAutoStart.IsChecked = config.AutoStartWithWindows;
            ChkSettingsRenamePrompt.IsChecked = config.RenamePromptEnabled;
            ChkSettingsDeleteSource.IsChecked = config.DeleteSourceAfterMove;
            ChkSettingsNotifications.IsChecked = config.ShowNotifications;
            TxtSettingsTimeout.Text = config.RenameTimeoutSeconds.ToString();

            if (config.Theme == "Light")
                RadioThemeLight.IsChecked = true;
            else if (config.Theme == "Dark")
                RadioThemeDark.IsChecked = true;
            else
                RadioThemeSystem.IsChecked = true;

            if (config.Language == "en")
                RadioLangEn.IsChecked = true;
            else
                RadioLangEs.IsChecked = true;
        }

        // ============================================
        // CAMBIO DE TEMA INMEDIATO (SIN CONFIRMACIÓN)
        // ============================================
        private void ThemeRadio_Click(object sender, RoutedEventArgs e)
        {
            string chosenTheme = "System";
            if (RadioThemeLight.IsChecked == true)
                chosenTheme = "Light";
            else if (RadioThemeDark.IsChecked == true)
                chosenTheme = "Dark";

            _configManager.CurrentConfig.Theme = chosenTheme;
            _configManager.Save();
            ApplyTheme(chosenTheme);
        }

        // ============================================
        // CAMBIO DE IDIOMA (BILINGÜE ESPAÑOL / INGLÉS)
        // ============================================
        private void LanguageRadio_Click(object sender, RoutedEventArgs e)
        {
            string chosenLang = RadioLangEn.IsChecked == true ? "en" : "es";
            _configManager.CurrentConfig.Language = chosenLang;
            _configManager.Save();
            ApplyLanguage(chosenLang);
        }

        public void ApplyLanguage(string lang)
        {
            Strings.CurrentLanguage = lang;

            NavDashboard.Content = Strings.Get("NavStatus");
            NavRecordings.Content = Strings.Get("NavRecordings");
            NavSettings.Content = Strings.Get("NavSettings");

            TxtDashboardTitle.Text = Strings.Get("DashboardTitle");
            TxtDashboardSubtitle.Text = Strings.Get("DashboardSubtitle");
            LblKpiCount.Text = Strings.Get("KpiBackedUp");
            LblKpiSize.Text = Strings.Get("KpiSpace");
            LblKpiLastTime.Text = Strings.Get("KpiLastBackup");

            TxtBtnScanNow.Text = Strings.Get("BtnScanNow");
            TxtBtnOpenZoom.Text = Strings.Get("BtnOpenZoom");
            TxtBtnOpenDest.Text = Strings.Get("BtnOpenDest");
            TxtRecentActivityHeader.Text = Strings.Get("RecentActivity");

            TxtRecordingsTitle.Text = Strings.Get("RecordingsTitle");
            TxtRecordingsSubtitle.Text = Strings.Get("RecordingsSubtitle");
            TxtBtnRefresh.Text = Strings.Get("BtnRefresh");

            TxtSettingsTitle.Text = Strings.Get("SettingsTitle");
            TxtSettingsSubtitle.Text = Strings.Get("SettingsSubtitle");
            TxtStorageGroup.Text = Strings.Get("StorageGroup");
            TxtZoomFolderLabel.Text = Strings.Get("ZoomFolderLabel");
            TxtZoomFolderDesc.Text = Strings.Get("ZoomFolderDesc");
            TxtDestFolderLabel.Text = Strings.Get("DestFolderLabel");
            TxtDestFolderDesc.Text = Strings.Get("DestFolderDesc");
            BtnBrowseZoom.Content = Strings.Get("BtnBrowse");
            BtnBrowseDest.Content = Strings.Get("BtnBrowse");

            TxtBehaviorGroup.Text = Strings.Get("BehaviorGroup");
            ChkSettingsAutoStart.Content = Strings.Get("AutoStartLabel");
            ChkSettingsRenamePrompt.Content = Strings.Get("RenamePromptLabel");
            ChkSettingsDeleteSource.Content = Strings.Get("DeleteSourceLabel");
            ChkSettingsNotifications.Content = Strings.Get("NotificationsLabel");
            TxtTimeoutLabel.Text = Strings.Get("TimeoutLabel");
            TxtSecondsLabel.Text = Strings.Get("Seconds");

            TxtAppearanceGroup.Text = Strings.Get("AppearanceGroup");
            TxtThemeLabel.Text = Strings.Get("ThemeLabel");
            TxtThemeDesc.Text = Strings.Get("ThemeDesc");
            RadioThemeSystem.Content = Strings.Get("ThemeSystem");
            RadioThemeLight.Content = Strings.Get("ThemeLight");
            RadioThemeDark.Content = Strings.Get("ThemeDark");

            TxtLanguageLabel.Text = Strings.Get("LanguageLabel");
            TxtLanguageDesc.Text = Strings.Get("LanguageDesc");
            TxtBtnSave.Text = Strings.Get("BtnSave");
        }

        // ============================================
        // ESCANEO DINÁMICO E INTUITIVO (SIN POPUP MODAL)
        // ============================================
        private async void BtnScanNow_Click(object sender, RoutedEventArgs e)
        {
            BtnScanNow.IsEnabled = false;
            ShowScanBanner(Strings.Get("Scanning"), false);

            try
            {
                int count = await _watcherService.ScanPendingFoldersAsync();
                await RefreshAllAsync();

                if (count > 0)
                {
                    ShowScanBanner(string.Format(Strings.Get("ScanFound"), count), true);
                }
                else
                {
                    ShowScanBanner(Strings.Get("ScanNone"), true);
                }
            }
            finally
            {
                BtnScanNow.IsEnabled = true;
            }
        }

        private void ShowScanBanner(string message, bool autoDismiss)
        {
            TxtScanBannerMessage.Text = message;
            BannerScanNotification.Visibility = Visibility.Visible;

            _bannerTimer?.Stop();
            if (autoDismiss)
            {
                _bannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                _bannerTimer.Tick += (s, e) =>
                {
                    BannerScanNotification.Visibility = Visibility.Collapsed;
                    _bannerTimer.Stop();
                };
                _bannerTimer.Start();
            }
        }

        private void BtnCloseScanBanner_Click(object sender, RoutedEventArgs e)
        {
            BannerScanNotification.Visibility = Visibility.Collapsed;
            _bannerTimer?.Stop();
        }

        private void BtnOpenZoomDir_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(_configManager.CurrentConfig.ZoomDirectory);
        }

        private void BtnOpenDestDir_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(_configManager.CurrentConfig.DestinationDirectory);
        }

        private void TxtSearchRecordings_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private async void BtnRefreshRecordings_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAllAsync();
        }

        // ============================================
        // VOLVER A SINCRONIZAR
        // ============================================
        private async void BtnItemResync_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessedRecording record)
            {
                btn.IsEnabled = false;
                try
                {
                    var outcome = await _transferService.ResyncAsync(record);
                    await RefreshAllAsync();

                    string msg = outcome switch
                    {
                        ResyncOutcome.Copied => $"«{record.FolderName}» sincronizada nuevamente con éxito.",
                        ResyncOutcome.Verified => $"«{record.FolderName}» verificada íntegramente en la carpeta de respaldo.",
                        ResyncOutcome.Missing => $"No se encontró la carpeta original en Zoom ni en el destino.",
                        _ => "Ocurrió un error al volver a sincronizar."
                    };

                    ShowScanBanner(msg, true);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private void BtnItemOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessedRecording record)
            {
                var path = Directory.Exists(record.DestinationFolderPath) ? record.DestinationFolderPath : record.OriginalFolderPath;
                OpenFolder(path);
            }
        }

        private void BtnItemPlay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessedRecording record)
            {
                var targetDir = Directory.Exists(record.DestinationFolderPath) ? record.DestinationFolderPath : record.OriginalFolderPath;
                if (Directory.Exists(targetDir))
                {
                    var video = Directory.GetFiles(targetDir, "*.mp4", SearchOption.AllDirectories).FirstOrDefault();
                    if (video != null)
                    {
                        Process.Start(new ProcessStartInfo { FileName = video, UseShellExecute = true });
                    }
                    else
                    {
                        MessageBox.Show("No se encontró ningún archivo de video .mp4 en esta reunión.", "ZoomBridge", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private async void BtnItemRename_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessedRecording record)
            {
                var prompt = new RenamePromptWindow(record.FolderName, 300);
                prompt.Title = Strings.Get("BtnRename");
                prompt.Owner = this;
                prompt.ShowDialog();

                if (prompt.Confirmed && !string.IsNullOrWhiteSpace(prompt.SelectedName) && prompt.SelectedName != record.FolderName)
                {
                    try
                    {
                        var dir = Directory.Exists(record.DestinationFolderPath) ? record.DestinationFolderPath : record.OriginalFolderPath;
                        if (Directory.Exists(dir))
                        {
                            var parent = Path.GetDirectoryName(dir)!;
                            var newPath = Path.Combine(parent, prompt.SelectedName);
                            if (Directory.Exists(newPath))
                            {
                                MessageBox.Show("Ya existe una carpeta con ese nombre.", "ZoomBridge", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                            Directory.Move(dir, newPath);
                            await _databaseContext.UpdateRecordFolderAsync(record.Id, prompt.SelectedName, newPath);
                            await RefreshRecordingsListAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error al renombrar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void BtnItemDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessedRecording record)
            {
                var res = MessageBox.Show(
                    $"¿Deseas quitar «{record.FolderName}» de la lista de ZoomBridge?\n\n(Los archivos en disco no se eliminarán).",
                    Strings.Get("BtnDelete"), MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    await _databaseContext.DeleteRecordAsync(record.Id);
                    await RefreshAllAsync();
                }
            }
        }

        private void BtnSettingsBrowseZoom_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = Strings.Get("ZoomFolderLabel"),
                InitialDirectory = Directory.Exists(TxtSettingsZoomPath.Text) ? TxtSettingsZoomPath.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dialog.ShowDialog() == true)
            {
                TxtSettingsZoomPath.Text = dialog.FolderName;
            }
        }

        private void BtnSettingsBrowseDest_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = Strings.Get("DestFolderLabel"),
                InitialDirectory = Directory.Exists(TxtSettingsDestPath.Text) ? TxtSettingsDestPath.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dialog.ShowDialog() == true)
            {
                TxtSettingsDestPath.Text = dialog.FolderName;
            }
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var zoom = TxtSettingsZoomPath.Text.Trim();
            var dest = TxtSettingsDestPath.Text.Trim();

            if (string.IsNullOrWhiteSpace(zoom) || string.IsNullOrWhiteSpace(dest))
            {
                MessageBox.Show("Debes especificar ambas carpetas.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtSettingsTimeout.Text.Trim(), out int timeout) || timeout < 5 || timeout > 600)
            {
                MessageBox.Show("El tiempo de espera debe ser entre 5 y 600 segundos.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var config = _configManager.CurrentConfig;
            config.ZoomDirectory = zoom;
            config.DestinationDirectory = dest;
            config.AutoStartWithWindows = ChkSettingsAutoStart.IsChecked ?? true;
            config.RenamePromptEnabled = ChkSettingsRenamePrompt.IsChecked ?? true;
            config.DeleteSourceAfterMove = ChkSettingsDeleteSource.IsChecked ?? true;
            config.ShowNotifications = ChkSettingsNotifications.IsChecked ?? true;
            config.RenameTimeoutSeconds = timeout;

            _configManager.Save();

            _watcherService.Stop();
            _watcherService.Start();

            MessageBox.Show(Strings.Get("SettingsSaved"), "ZoomBridge", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public static void ApplyTheme(string theme)
        {
            try
            {
                if (theme == "Light")
                    Application.Current.ThemeMode = ThemeMode.Light;
                else if (theme == "Dark")
                    Application.Current.ThemeMode = ThemeMode.Dark;
                else
                    Application.Current.ThemeMode = ThemeMode.System;
            }
            catch { }
        }

        private static void OpenFolder(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
                }
            }
            catch { }
        }

        private void BtnOpenGitHubRepo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/DiosaAqua/ZoomBridge",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public void RealExit()
        {
            _isRealExit = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isRealExit)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                base.OnClosing(e);
            }
        }
    }
}
