using System.Windows;
using dapapa.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;

namespace dapapa.Dialogs
{
    public partial class AddAppDialog : Window
    {
        public string AppName { get; private set; } = string.Empty;
        public string Domain { get; private set; } = string.Empty;
        public string[] Domains { get; private set; } = [];
        public bool DiscoverRelated => DiscoverRelatedCheckBox.IsChecked == true;
        private readonly bool _editing;
        private bool _closed;

        public AddAppDialog() : this(null) { }

        internal AddAppDialog(ProtectedApp? target)
        {
            InitializeComponent();
            _editing = target != null;
            ApplyLanguage();
            if (target != null)
            {
                AppNameTextBox.Text = target.Name;
                DomainTextBox.Text = target.Domain;
                DomainListTextBox.Text = string.Join(Environment.NewLine, target.GetDomains());
                DomainListRadio.IsChecked = true;
                DiscoverRelatedCheckBox.Visibility = Visibility.Collapsed;
            }
            UpdateInputMode();
            Loaded += (_, _) => { if (DomainListRadio.IsChecked == true) DomainListTextBox.Focus(); else DomainTextBox.Focus(); };
            AppNameTextBox.KeyDown += AppNameTextBox_KeyDown;
            DomainTextBox.KeyDown += AppNameTextBox_KeyDown;
            DomainListTextBox.KeyDown += AppNameTextBox_KeyDown;
            Services.Localization.Changed += OnLanguageChanged;
            Closed += (_, _) => { _closed = true; Services.Localization.Changed -= OnLanguageChanged; };
        }

        private void OnLanguageChanged()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnLanguageChanged)); return; }
            if (_closed) return;
            bool hadError = InputErrorText.Visibility == Visibility.Visible;
            ApplyLanguage();
            UpdateInputMode();
            if (!hadError) return;
            if (DomainListRadio.IsChecked == true)
            {
                if (!Services.DomainList.TryParse(DomainListTextBox.Text, out _, out var error)) ShowError(error);
            }
            else if (!Services.DomainName.TryNormalize(DomainTextBox.Text, out _)) ShowError(Services.Localization.T("InvalidDomain"));
        }

        private void ApplyLanguage()
        {
            Title = HeadingText.Text = Services.Localization.T(_editing ? "EditDomainGroup" : "AddAppDialogHeading");
            DescriptionText.Text = Services.Localization.T("AddAppDialogDescription");
            AppNameLabelText.Text = Services.Localization.T("AppNameLabel");
            DomainLabelText.Text = Services.Localization.T("DomainFieldLabel");
            CancelButtonText.Content = Services.Localization.T("Cancel");
            ContinueButtonText.Content = Services.Localization.T(_editing ? "SaveGroup" : "AddButton");
            DomainScopeText.Text = Services.Localization.T("DomainScope");
            DiscoverRelatedCheckBox.Content = Services.Localization.T("DiscoverRelated");
            SingleDomainRadio.Content = Services.Localization.T("SingleDomain");
            DomainListRadio.Content = Services.Localization.T("DomainList");
            DomainListHintText.Text = Services.Localization.T("DomainListHint", Services.DomainList.MaxDomains);
        }

        private void InputMode_Changed(object sender, RoutedEventArgs e) => UpdateInputMode();

        private void UpdateInputMode()
        {
            if (DomainListTextBox == null || DomainScopeText == null) return;
            bool list = DomainListRadio.IsChecked == true;
            DomainTextBox.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
            DomainListTextBox.Visibility = DomainListHintText.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            DomainLabelText.Text = Services.Localization.T(list ? "DomainList" : "DomainFieldLabel");
            DomainScopeText.Text = Services.Localization.T(list ? "DomainListScope" : "DomainScope");
            InputErrorText.Visibility = Visibility.Collapsed;
        }

        private void AppNameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (sender != DomainListTextBox ||
                (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0))
            {
                ContinueButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
                CancelButton_Click(this, new RoutedEventArgs());
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            string[] domains;
            string error;
            if (DomainListRadio.IsChecked == true)
            {
                if (!Services.DomainList.TryParse(DomainListTextBox.Text, out domains, out error)) { ShowError(error); return; }
            }
            else
            {
                if (!Services.DomainName.TryNormalize(DomainTextBox.Text, out var domain))
                { ShowError(Services.Localization.T("InvalidDomain")); return; }
                domains = [domain];
            }
            AppName = string.IsNullOrWhiteSpace(AppNameTextBox.Text) ? domains[0] : AppNameTextBox.Text.Trim();
            Domains = domains;
            Domain = domains[0];
            DialogResult = true;
        }

        private void ShowError(string error)
        {
            InputErrorText.Text = error;
            InputErrorText.Visibility = Visibility.Visible;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
