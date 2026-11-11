using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using ZoomBridge.Core.Config;
using ZoomBridge.Core.Monitoring;

namespace ZoomBridge.UI
{
    public partial class TrayFlyoutWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativeTrayIcon.POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        private readonly AgentState _agentState;
        private readonly ConfigManager _configManager;
        private readonly ZoomWatcherService _watcherService;

        public event Action? OpenFullWindowRequested;
        public event Action? ExitRequested;

        public TrayFlyoutWindow(AgentState agentState, ConfigManager configManager, ZoomWatcherService watcherService)
        {
            InitializeComponent();

            _agentState = agentState;
            _configManager = configManager;
            _watcherService = watcherService;

            ImgFlyoutAppIcon.Source = IconHelper.GetHighResImageSource();

            _agentState.PropertyChanged += (s, e) => UpdateStateUI();
            UpdateStateUI();

            Deactivated += (s, e) => Hide();
        }

        private void UpdateStateUI()
        {
            TxtStateTitle.Text = _agentState.StatusTitle;
            TxtStateSubtitle.Text = _agentState.StatusSubtitle;

            switch (_agentState.Mode)
            {
                case AgentStatusMode.Transferring:
                    StateDot.Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                    ProgressTransfer.Visibility = Visibility.Visible;
                    ProgressTransfer.Value = _agentState.ProgressPercent;
                    break;
                case AgentStatusMode.Waiting:
                case AgentStatusMode.Prompting:
                    StateDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    ProgressTransfer.Visibility = Visibility.Collapsed;
                    break;
                case AgentStatusMode.Error:
                    StateDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    ProgressTransfer.Visibility = Visibility.Collapsed;
                    break;
                default:
                    StateDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                    ProgressTransfer.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        /// <summary>
        /// Posiciona el flyout perfectamente sobre la barra de tareas en el monitor donde está el cursor.
        /// NUNCA se cortará ni quedará detrás de la barra de tareas.
        /// </summary>
        public void ShowNearCursor(NativeTrayIcon.POINT physicalCursorPt)
        {
            // Obtener el monitor exacto donde ocurrió el clic
            IntPtr hMonitor = MonitorFromPoint(physicalCursorPt, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            bool hasMonitorInfo = GetMonitorInfo(hMonitor, ref mi);

            // Obtener escala DPI de esta ventana
            var dpi = VisualTreeHelper.GetDpi(this);
            double flyoutWidth = Width;
            double flyoutHeight = Height;

            double workLeftDip;
            double workTopDip;
            double workRightDip;
            double workBottomDip;
            double dipCursorX;
            double dipCursorY;

            if (hasMonitorInfo)
            {
                // rcWork es el área visible del monitor EXCLUYENDO la barra de tareas
                workLeftDip = mi.rcWork.Left / dpi.DpiScaleX;
                workTopDip = mi.rcWork.Top / dpi.DpiScaleY;
                workRightDip = mi.rcWork.Right / dpi.DpiScaleX;
                workBottomDip = mi.rcWork.Bottom / dpi.DpiScaleY;
                dipCursorX = physicalCursorPt.X / dpi.DpiScaleX;
                dipCursorY = physicalCursorPt.Y / dpi.DpiScaleY;
            }
            else
            {
                var workArea = SystemParameters.WorkArea;
                workLeftDip = workArea.Left;
                workTopDip = workArea.Top;
                workRightDip = workArea.Right;
                workBottomDip = workArea.Bottom;
                dipCursorX = physicalCursorPt.X / dpi.DpiScaleX;
                dipCursorY = physicalCursorPt.Y / dpi.DpiScaleY;
            }

            // Anclado horizontal: centrado sobre el cursor pero dentro del área de trabajo
            double targetX = dipCursorX - (flyoutWidth / 2);
            if (targetX + flyoutWidth > workRightDip - 12)
            {
                targetX = workRightDip - flyoutWidth - 12;
            }
            if (targetX < workLeftDip + 12)
            {
                targetX = workLeftDip + 12;
            }

            // Anclado vertical: por defecto inmediatamente arriba del borde inferior de trabajo (barra de tareas)
            // workBottomDip es EXACTAMENTE donde empieza la barra de tareas por arriba.
            // Restando flyoutHeight y 12px de margen, queda 100% visible ARRIBA de la barra de tareas.
            double targetY = workBottomDip - flyoutHeight - 12;

            // En caso raro de que la barra de tareas esté en la parte superior
            if (dipCursorY < workTopDip + 60)
            {
                targetY = workTopDip + 12;
            }

            Left = targetX;
            Top = targetY;

            Show();
            Activate();
        }

        private async void BtnScanNow_Click(object sender, RoutedEventArgs e)
        {
            BtnScanNow.IsEnabled = false;
            try
            {
                await _watcherService.ScanPendingFoldersAsync();
            }
            finally
            {
                BtnScanNow.IsEnabled = true;
            }
        }

        private void BtnOpenZoomFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(_configManager.CurrentConfig.ZoomDirectory);
        }

        private void BtnOpenDestFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenFolder(_configManager.CurrentConfig.DestinationDirectory);
        }

        private void BtnOpenFull_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            OpenFullWindowRequested?.Invoke();
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            ExitRequested?.Invoke();
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
    }
}
