using System;
using System.Drawing;
using System.Windows.Forms;

namespace DisableWindowsUpdates
{
    internal sealed class TrayNotifier : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private bool _disposed;

        public TrayNotifier(string applicationName)
        {
            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Icon = SystemIcons.Information,
                Text = applicationName
            };
        }

        public void ShowInfo(string message)
        {
            Logger.Debug("Tray info: " + message);
            ShowBalloon(message, ToolTipIcon.Info);
        }

        public void ShowWarning(string message)
        {
            Logger.Warning("Tray warning: " + message);
            ShowBalloon(message, ToolTipIcon.Warning);
        }

        public void ShowError(string message)
        {
            Logger.Error("Tray error notification displayed: " + message);
            ShowBalloon(message, ToolTipIcon.Error);
        }

        private void ShowBalloon(string message, ToolTipIcon icon)
        {
            if (_disposed)
            {
                return;
            }

            _notifyIcon.BalloonTipTitle = _notifyIcon.Text;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(5000);
        }

        public void FlushAndDispose(int delayMilliseconds)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(delayMilliseconds);
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _disposed = true;
        }

        public static void ShowError(string title, string message)
        {
            using (var notifier = new TrayNotifier(title))
            {
                notifier.ShowError(message);
                notifier.FlushAndDispose(5000);
            }
        }
    }
}
