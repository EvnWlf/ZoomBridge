using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ZoomBridge.UI
{
    public partial class RenamePromptWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private readonly int _totalSeconds;
        private int _remainingSeconds;
        private bool _userModifiedText = false;

        public string SelectedName { get; private set; }
        public bool Confirmed { get; private set; } = false;

        public RenamePromptWindow(string originalName, int timeoutSeconds)
        {
            InitializeComponent();
            Icon = IconHelper.GetHighResImageSource();

            SelectedName = originalName;
            _totalSeconds = Math.Max(5, timeoutSeconds);
            _remainingSeconds = _totalSeconds;

            TxtMeetingName.Text = originalName;
            TxtMeetingName.SelectAll();
            TxtMeetingName.Focus();

            ProgressTimeout.Maximum = _totalSeconds;
            ProgressTimeout.Value = _totalSeconds;
            UpdateCountdownLabel();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            Closed += (s, e) => _timer.Stop();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _remainingSeconds--;
            ProgressTimeout.Value = _remainingSeconds;
            UpdateCountdownLabel();

            if (_remainingSeconds <= 0)
            {
                _timer.Stop();
                // Si el usuario modificó el texto, se respeta lo escrito; sino, el original
                SelectedName = string.IsNullOrWhiteSpace(TxtMeetingName.Text) ? SelectedName : TxtMeetingName.Text.Trim();
                Confirmed = true;
                Close();
            }
        }

        private void UpdateCountdownLabel()
        {
            if (_userModifiedText)
            {
                LblCountdown.Text = $"Editando... guardando automáticamente en {_remainingSeconds}s";
            }
            else
            {
                LblCountdown.Text = $"Guardando automáticamente en {_remainingSeconds}s...";
            }
        }

        private void TxtMeetingName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _userModifiedText = true;
        }

        private void TxtMeetingName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyAndClose();
            }
            else if (e.Key == Key.Escape)
            {
                SkipAndClose();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            ApplyAndClose();
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            SkipAndClose();
        }

        private void ApplyAndClose()
        {
            _timer.Stop();
            var text = TxtMeetingName.Text.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                SelectedName = text;
            }
            Confirmed = true;
            Close();
        }

        private void SkipAndClose()
        {
            _timer.Stop();
            Confirmed = false;
            Close();
        }
    }
}
