using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ZoomBridge.UI
{
    public class NativeTrayIcon : IDisposable
    {
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 101;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;
        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;
        private const uint NIF_INFO = 0x00000010;
        private const uint NIIF_INFO = 0x00000001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly HwndSource _msgWindow;
        private NOTIFYICONDATA _nid;
        private bool _isCreated = false;
        private Icon? _icon;

        public event Action<POINT>? LeftClick;
        public event Action<POINT>? RightClick;

        public NativeTrayIcon(Icon icon, string tooltip)
        {
            _icon = icon;

            // Ventana oculta de solo mensajes para capturar clics de la bandeja
            var parameters = new HwndSourceParameters("ZoomBridgeTraySink")
            {
                WindowStyle = 0,
                ExtendedWindowStyle = 0,
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0
            };

            _msgWindow = new HwndSource(parameters);
            _msgWindow.AddHook(WndProc);

            _nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _msgWindow.Handle,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = icon.Handle,
                szTip = Truncate(tooltip, 127),
                szInfo = string.Empty,
                szInfoTitle = string.Empty
            };

            _isCreated = Shell_NotifyIcon(NIM_ADD, ref _nid);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                int message = lParam.ToInt32() & 0xFFFF;
                if (message == WM_LBUTTONUP)
                {
                    GetCursorPos(out var pt);
                    LeftClick?.Invoke(pt);
                    handled = true;
                }
                else if (message == WM_RBUTTONUP)
                {
                    GetCursorPos(out var pt);
                    RightClick?.Invoke(pt);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void UpdateTooltip(string tooltip)
        {
            if (!_isCreated) return;
            _nid.uFlags = NIF_TIP;
            _nid.szTip = Truncate(tooltip, 127);
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }

        public void ShowNotification(string title, string message)
        {
            if (!_isCreated) return;
            _nid.uFlags = NIF_INFO;
            _nid.szInfoTitle = Truncate(title, 63);
            _nid.szInfo = Truncate(message, 255);
            _nid.dwInfoFlags = NIIF_INFO;
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }

        private static string Truncate(string str, int max) =>
            string.IsNullOrEmpty(str) ? string.Empty : (str.Length <= max ? str : str.Substring(0, max - 3) + "...");

        public void Dispose()
        {
            if (_isCreated)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _isCreated = false;
            }
            _msgWindow.Dispose();
        }
    }
}
