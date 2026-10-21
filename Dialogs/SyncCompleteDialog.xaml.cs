using System.Windows;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;

namespace dapapa.Dialogs
{
    public partial class SyncCompleteDialog : Window
    {
        private readonly int _serviceCount;
        private readonly int _ipCount;
        private readonly bool _appliedLive;
        private readonly int _failedDomains;
        private bool _closed;

        public SyncCompleteDialog(int serviceCount, int ipCount, bool appliedLive, int failedDomains = 0)
        {
            _serviceCount = serviceCount;
            _ipCount = ipCount;
            _appliedLive = appliedLive;
            _failedDomains = failedDomains;

            InitializeComponent();
            ApplyLanguage();
            KeyDown += SyncCompleteDialog_KeyDown;
            Services.Localization.Changed += OnLanguageChanged;
            Closed += (_, _) => { _closed = true; Services.Localization.Changed -= OnLanguageChanged; };
        }

        private void OnLanguageChanged()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnLanguageChanged)); return; }
            if (!_closed) ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            Title = Services.Localization.T(_failedDomains > 0 ? "SyncIncompleteHeading" : "SyncCompleteDialogTitle");
            HeadingText.Text = Services.Localization.T("SyncCompleteDialogHeading");
            SubheadingText.Text = Services.Localization.T("SyncCompleteDialogSubheading");
            if (_failedDomains > 0)
            {
                HeadingText.Text = Services.Localization.T("SyncIncompleteHeading");
                SubheadingText.Text = Services.Localization.T(_failedDomains == 1 ? "SyncPartialFailureSingle" : "SyncPartialFailure", _failedDomains);
                var color = (System.Windows.Media.Brush)FindResource("DangerBrush");
                SubheadingText.Foreground = color;
                ResultIconBorder.BorderBrush = color;
                ResultIcon.Stroke = color;
                ResultIcon.Data = System.Windows.Media.Geometry.Parse("M 5,5 L 19,19 M 19,5 L 5,19");
            }

            ServicesUpdatedText.Text = Services.Localization.T("SyncServicesUpdated", _serviceCount, _ipCount);
            DnsFlushedText.Text = Services.Localization.T("SyncDnsFlushed");
            DnsCacheStatusText.Text = Services.Localization.T("SyncDnsCacheStatus");

            if (_appliedLive)
            {
                TargetStatusLiveDot.Visibility = Visibility.Visible;
                TargetStatusSavedPath.Visibility = Visibility.Collapsed;
                TargetStatusText.Text = Services.Localization.T("SyncTargetsAppliedLive");
            }
            else
            {
                TargetStatusLiveDot.Visibility = Visibility.Collapsed;
                TargetStatusSavedPath.Visibility = Visibility.Visible;
                TargetStatusText.Text = Services.Localization.T("SyncTargetsCached");
            }

            OkButton.Content = Services.Localization.T("OK");
        }

        private void SyncCompleteDialog_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                DialogResult = true;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
