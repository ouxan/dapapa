using System.Drawing;
using System.IO;

namespace dapapa.Services
{
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _statusHeaderItem;
        private readonly ToolStripMenuItem _toggleConnectionItem;
        private readonly ToolStripMenuItem _showHideItem;
        private readonly ToolStripMenuItem _exitItem;
        private bool _isConnected;

        public event Action? ShowRequested;
        public event Action? ConnectRequested;
        public event Action? DisconnectRequested;
        public event Action? ExitRequested;

        public TrayIconManager()
        {
            _statusHeaderItem = new ToolStripMenuItem("")
            {
                Enabled = false,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            _showHideItem = new ToolStripMenuItem("");
            _showHideItem.Click += (_, _) => ShowRequested?.Invoke();

            _toggleConnectionItem = new ToolStripMenuItem("");

            _exitItem = new ToolStripMenuItem("");
            _exitItem.Click += (_, _) => ExitRequested?.Invoke();

            var menu = new ContextMenuStrip();
            menu.Items.Add(_statusHeaderItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_showHideItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_toggleConnectionItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_exitItem);

            _notifyIcon = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Visible = true,
                ContextMenuStrip = menu
            };

            _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke();

            SetConnectedState(false);
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");
                if (File.Exists(iconPath))
                    return new Icon(iconPath, SystemInformation.SmallIconSize);
            }
            catch
            {
            }

            return SystemIcons.Shield;
        }

        public void SetConnectedState(bool connected)
        {
            _isConnected = connected;
            RefreshTexts();

            _toggleConnectionItem.Click -= OnToggleClickConnect;
            _toggleConnectionItem.Click -= OnToggleClickDisconnect;

            if (connected)
                _toggleConnectionItem.Click += OnToggleClickDisconnect;
            else
                _toggleConnectionItem.Click += OnToggleClickConnect;
        }

        public void RefreshTexts() => Dispatcher_RefreshTexts();

        private void Dispatcher_RefreshTexts()
        {
            var appName = Localization.T("AppName");
            var statusWord = _isConnected ? Localization.T("ConnectedStatus") : Localization.T("NotConnected");
            var dot = _isConnected ? "●" : "○";

            _statusHeaderItem.Text = $"{dot}  {appName} - {statusWord}";
            _notifyIcon.Text = $"{appName} - {statusWord}";
            _showHideItem.Text = Localization.T("ShowHide");
            _toggleConnectionItem.Text = _isConnected ? Localization.T("TrayDisconnect") : Localization.T("TrayConnect");
            _exitItem.Text = Localization.T("Exit");
        }

        private void OnToggleClickConnect(object? sender, EventArgs e) => ConnectRequested?.Invoke();
        private void OnToggleClickDisconnect(object? sender, EventArgs e) => DisconnectRequested?.Invoke();

        public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(3000);
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
