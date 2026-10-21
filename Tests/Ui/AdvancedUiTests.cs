using System.IO;
using System.Windows;
using dapapa.Models;
using dapapa.Services;
using Localization = dapapa.Services.Localization;

internal sealed partial class DpiUiHarness
{
    public void VerifyAdvanced()
    {
        DpiModeComboBox.SelectedIndex = 1;
        DpiTcpFragmentationCheckBox.IsChecked = true;
        DpiMultiSplitCheckBox.IsChecked = true;
        Check(DpiSplitPositionsTextBox.IsEnabled && !DpiSplitSizeTextBox.IsEnabled && !DpiSplitAtHostnameCheckBox.IsEnabled, "Multisplit dependents incorrect.");
        DpiSplitPositionsTextBox.Text = "1,host,midsld,endhost-1";
        DpiSplitPositionsTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        DpiFakeWrongSequenceCheckBox.IsChecked = true;
        DpiFakeLowTtlCheckBox.IsChecked = true;
        DpiFakeAutoTtlCheckBox.IsChecked = true;
        DpiFakeMd5SignatureCheckBox.IsChecked = true;
        DpiFakeTimestampCheckBox.IsChecked = true;
        DpiFakeDataNoAckCheckBox.IsChecked = true;
        DpiFakeRandomizeTlsCheckBox.IsChecked = true;
        DpiFakeMixSegmentsCheckBox.IsChecked = true;
        Check(!DpiFakeTtlTextBox.IsEnabled, "AutoTTL left fixed TTL active.");
        DpiFakeRepeatsTextBox.Text = "3"; DpiFakeRepeatsTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        DpiFakeHostnameTextBox.Text = "google.com"; DpiFakeHostnameTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        var loaded = _settingsService.Load();
        Check(loaded.CustomDpi == _settings.CustomDpi && loaded.CustomDpi.FakeWrongSequence && loaded.CustomDpi.FakeAutoTtl &&
            loaded.CustomDpi.FakeMd5Signature && loaded.CustomDpi.FakeTimestamp && loaded.CustomDpi.FakeDataNoAck && loaded.CustomDpi.FakeMixSegments &&
            loaded.CustomDpi.FakeRepeats == 3 && loaded.CustomDpi.FakeHostname == "google.com", "Advanced controls did not persist all methods.");
        int writes = Saves, updates = _winDivert.Updates;
        DpiFakeRepeatsTextBox.Text = "0"; DpiFakeRepeatsTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        Check(Saves == writes && _winDivert.Updates == updates && DpiValidationText.Visibility == Visibility.Visible, "Invalid number was saved/applied.");
        DpiModeComboBox.SelectedIndex = 0;
        Check(_settings.GetDpiOptions() == DpiOptions.Standard && DpiValidationText.Visibility == Visibility.Collapsed &&
            DpiFakeRepeatsTextBox.Text == "3", "Invalid input prevented safe return to Standard.");
        DpiModeComboBox.SelectedIndex = 1;
        writes = Saves; updates = _winDivert.Updates;
        DpiSplitPositionsTextBox.Text = "bad-marker"; DpiSplitPositionsTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        Check(Saves == writes && _winDivert.Updates == updates && DpiValidationText.Text == Localization.T("DpiInvalidMarkers"), "Invalid markers reached the engine.");
        var language = Localization.Current;
        Localization.Current = language == AppLanguage.English ? AppLanguage.Turkish : AppLanguage.English;
        ApplyDpiLanguage();
        Check(DpiValidationText.Text == Localization.T("DpiInvalidMarkers") && Saves == writes, "Validation error did not retranslate or saved settings.");
        Localization.Current = language; ApplyDpiLanguage();
        DpiSplitPositionsTextBox.Text = "1,host,midsld,endhost-1"; DpiSplitPositionsTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        DpiFakeHostnameTextBox.Text = "*.invalid"; DpiFakeHostnameTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        Check(DpiValidationText.Visibility == Visibility.Visible && _settings.CustomDpi.FakeHostname == "google.com", "Invalid fake hostname reached settings.");
        DpiModeComboBox.SelectedIndex = 0; DpiModeComboBox.SelectedIndex = 1;
        DpiFakeAdvancedExpander.IsExpanded = true;
        Console.WriteLine("PASS: advanced checkboxes/parameters round-trip, invalid numbers/markers/hostnames never apply, localized errors refresh, and Standard restores valid preferences.");
    }
}
