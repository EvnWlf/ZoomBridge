using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ZoomBridge.Core;
using ZoomBridge.Core.Localization;

namespace ZoomBridge.UI
{
    public enum AgentStatusMode
    {
        Idle,
        Waiting,
        Prompting,
        Transferring,
        Error
    }

    public class AgentState : IAgentStatusSink, INotifyPropertyChanged
    {
        private AgentStatusMode _mode = AgentStatusMode.Idle;
        private string _statusTitle = Strings.Get("DashboardTitle");
        private string _statusSubtitle = Strings.Get("DashboardSubtitle");
        private string _currentFolderName = string.Empty;
        private long _copiedBytes;
        private long _totalBytes;
        private double _progressPercent;
        private bool _isBusy;

        public event Action<string, string>? NotificationRequested;
        public event Action? RecordingsUpdated;
        public event PropertyChangedEventHandler? PropertyChanged;

        public AgentStatusMode Mode
        {
            get => _mode;
            private set => SetField(ref _mode, value);
        }

        public string StatusTitle
        {
            get => _statusTitle;
            private set => SetField(ref _statusTitle, value);
        }

        public string StatusSubtitle
        {
            get => _statusSubtitle;
            private set => SetField(ref _statusSubtitle, value);
        }

        public string CurrentFolderName
        {
            get => _currentFolderName;
            private set => SetField(ref _currentFolderName, value);
        }

        public long CopiedBytes
        {
            get => _copiedBytes;
            private set => SetField(ref _copiedBytes, value);
        }

        public long TotalBytes
        {
            get => _totalBytes;
            private set => SetField(ref _totalBytes, value);
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            private set => SetField(ref _progressPercent, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetField(ref _isBusy, value);
        }

        public void ReportIdle()
        {
            RunOnUI(() =>
            {
                Mode = AgentStatusMode.Idle;
                StatusTitle = Strings.Get("DashboardTitle");
                StatusSubtitle = Strings.Get("DashboardSubtitle");
                CurrentFolderName = string.Empty;
                CopiedBytes = 0;
                TotalBytes = 0;
                ProgressPercent = 0;
                IsBusy = false;
            });
        }

        public void ReportWaiting(string folderName)
        {
            RunOnUI(() =>
            {
                Mode = AgentStatusMode.Waiting;
                CurrentFolderName = folderName;
                StatusTitle = Strings.Get("StatusWaiting");
                StatusSubtitle = $"«{folderName}»";
                IsBusy = true;
            });
        }

        public void ReportPrompting(string folderName)
        {
            RunOnUI(() =>
            {
                Mode = AgentStatusMode.Prompting;
                CurrentFolderName = folderName;
                StatusTitle = "¿Deseas cambiar el nombre?";
                StatusSubtitle = $"«{folderName}»";
                IsBusy = true;
            });
        }

        public void ReportTransfer(string folderName, long copiedBytes, long totalBytes)
        {
            RunOnUI(() =>
            {
                Mode = AgentStatusMode.Transferring;
                CurrentFolderName = folderName;
                CopiedBytes = copiedBytes;
                TotalBytes = totalBytes;
                ProgressPercent = totalBytes > 0 ? (double)copiedBytes / totalBytes * 100.0 : 0;
                StatusTitle = Strings.Get("StatusWorking");
                StatusSubtitle = $"«{folderName}» ({ProgressPercent:0}%)";
                IsBusy = true;
            });
        }

        public void ReportError(string message)
        {
            RunOnUI(() =>
            {
                Mode = AgentStatusMode.Error;
                StatusTitle = "Atención";
                StatusSubtitle = message;
                IsBusy = false;
            });
        }

        public void Notify(string title, string message)
        {
            NotificationRequested?.Invoke(title, message);
        }

        public void RecordingsChanged()
        {
            RunOnUI(() => RecordingsUpdated?.Invoke());
        }

        private static void RunOnUI(Action action)
        {
            var app = Application.Current;
            if (app != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
