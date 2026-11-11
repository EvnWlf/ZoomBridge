using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ZoomBridge.Core.Config;

namespace ZoomBridge.UI
{
    public partial class OnboardingWindow : Window
    {
        private int _currentStep = 1;
        private readonly ConfigManager _configManager;
        public event Action? OnboardingCompleted;

        public OnboardingWindow(ConfigManager configManager)
        {
            InitializeComponent();
            _configManager = configManager;

            Icon = IconHelper.GetHighResImageSource();
            ImgWelcomeIcon.Source = IconHelper.GetHighResImageSource();

            // Sugerir la carpeta de Zoom si existe en disco para facilitar la configuración
            var detectedZoom = AppConfig.DetectZoomFolder();
            if (!string.IsNullOrEmpty(detectedZoom))
            {
                TxtZoomFolder.Text = detectedZoom;
            }

            UpdateStepUI();
        }

        private void UpdateStepUI()
        {
            Step1Panel.Visibility = (_currentStep == 1) ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = (_currentStep == 2) ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = (_currentStep == 3) ? Visibility.Visible : Visibility.Collapsed;

            StepBar1.Value = (_currentStep >= 1) ? 100 : 0;
            StepBar2.Value = (_currentStep >= 2) ? 100 : 0;
            StepBar3.Value = (_currentStep >= 3) ? 100 : 0;

            BtnBack.Visibility = (_currentStep > 1) ? Visibility.Visible : Visibility.Collapsed;

            if (_currentStep == 3)
            {
                BtnNext.Content = "Comenzar";
                BtnNext.IsEnabled = true;
            }
            else
            {
                BtnNext.Content = "Siguiente";
                if (_currentStep == 2)
                {
                    ValidateInputs(null, null);
                }
                else
                {
                    BtnNext.IsEnabled = true;
                }
            }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep == 1)
            {
                _currentStep = 2;
                UpdateStepUI();
            }
            else if (_currentStep == 2)
            {
                var zoom = TxtZoomFolder.Text.Trim();
                var dest = TxtDestFolder.Text.Trim();

                if (string.IsNullOrWhiteSpace(zoom) || string.IsNullOrWhiteSpace(dest))
                {
                    MessageBox.Show("Debes seleccionar ambas carpetas para continuar.", "Configuración requerida", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    if (!Directory.Exists(zoom)) Directory.CreateDirectory(zoom);
                    if (!Directory.Exists(dest)) Directory.CreateDirectory(dest);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al validar las carpetas: {ex.Message}", "Rutas no válidas", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _currentStep = 3;
                UpdateStepUI();
            }
            else if (_currentStep == 3)
            {
                var config = _configManager.CurrentConfig;
                config.ZoomDirectory = TxtZoomFolder.Text.Trim();
                config.DestinationDirectory = TxtDestFolder.Text.Trim();
                config.IsConfigured = true;

                _configManager.Save();

                OnboardingCompleted?.Invoke();
                Close();
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateStepUI();
            }
        }

        private void BtnBrowseZoom_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Selecciona la carpeta local de grabaciones de Zoom",
                InitialDirectory = Directory.Exists(TxtZoomFolder.Text) ? TxtZoomFolder.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() == true)
            {
                TxtZoomFolder.Text = dialog.FolderName;
            }
        }

        private void BtnBrowseDest_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Selecciona la carpeta de destino de respaldo",
                InitialDirectory = Directory.Exists(TxtDestFolder.Text) ? TxtDestFolder.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() == true)
            {
                TxtDestFolder.Text = dialog.FolderName;
            }
        }

        private void ValidateInputs(object? sender, TextChangedEventArgs? e)
        {
            if (BtnNext == null) return;
            var zoom = TxtZoomFolder.Text.Trim();
            var dest = TxtDestFolder.Text.Trim();
            BtnNext.IsEnabled = !string.IsNullOrWhiteSpace(zoom) && !string.IsNullOrWhiteSpace(dest);
        }
    }
}
