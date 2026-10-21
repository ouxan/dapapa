using System.Windows;
using dapapa.Models;
using dapapa.Services;
using Localization = dapapa.Services.Localization;

internal sealed partial class DpiUiHarness
{
    public void VerifyUdp()
    {
        DpiModeComboBox.SelectedIndex = 1;
        DpiUdpFakeWrongChecksumCheckBox.IsChecked = true;
        DpiUdpFakeLowTtlCheckBox.IsChecked = true;
        DpiUdpIpFragmentationCheckBox.IsChecked = true;
        DpiUdpReverseFragmentsCheckBox.IsChecked = true;
        DpiUdpLengthPaddingCheckBox.IsChecked = true;
        DpiUdpStunCheckBox.IsChecked = true;
        DpiUdpWireGuardCheckBox.IsChecked = true;
        DpiUdpDiscordDiscoveryCheckBox.IsChecked = true;
        DpiUdpUnknownCheckBox.IsChecked = true;
        DpiUdpQuicInitialCheckBox.IsChecked = true;
        Check(DpiUdpFragmentPanel.IsEnabled && DpiUdpFakeTtlTextBox.IsEnabled && DpiUdpPaddingSizeTextBox.IsEnabled,
            "UDP dependent controls disabled.");
        DpiUdpFakeTtlTextBox.Text = "3"; DpiUdpFakeTtlTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        DpiUdpFakeRepeatsTextBox.Text = "2"; DpiUdpFakeRepeatsTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        DpiUdpFragmentSizeTextBox.Text = "16"; DpiUdpFragmentSizeTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        DpiUdpPaddingSizeTextBox.Text = "4"; DpiUdpPaddingSizeTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        DpiUdpPacketLimitTextBox.Text = "5"; DpiUdpPacketLimitTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        var options = _settingsService.Load().CustomDpi;
        Check(options == _settings.CustomDpi && options.UdpFakeWrongChecksum && options.UdpFakeLowTtl && options.UdpIpFragmentation &&
            options.UdpLengthPadding && options.UdpReverseFragments && options.UdpStun && options.UdpWireGuard && options.UdpDiscordDiscovery &&
            options.UdpUnknown && options.UdpQuicInitial && options.UdpFakeTtl == 3 && options.UdpFakeRepeats == 2 &&
            options.UdpFragmentSize == 16 && options.UdpPaddingSize == 4 && options.UdpPacketLimit == 5, "UDP controls did not round-trip.");
        DpiBlockQuicCheckBox.IsChecked = true;
        Check(DpiUdpQuicBlockedText.Visibility == Visibility.Visible, "QUIC blocking conflict hidden.");
        DpiBlockQuicCheckBox.IsChecked = false;
        Check(DpiUdpQuicBlockedText.Visibility == Visibility.Collapsed, "QUIC conflict remained after disabling block.");
        int writes = Saves, updates = _winDivert.Updates;
        DpiUdpFragmentSizeTextBox.Text = "9"; DpiUdpFragmentSizeTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        Check(Saves == writes && _winDivert.Updates == updates && DpiValidationText.Text == Localization.T("DpiUdpFragmentSizeError"), "Misaligned fragment size reached the engine.");
        var language = Localization.Current;
        Localization.Current = language == AppLanguage.Turkish ? AppLanguage.English : AppLanguage.Turkish; ApplyDpiLanguage();
        Check(DpiValidationText.Text == Localization.T("DpiUdpFragmentSizeError") && Saves == writes, "UDP validation did not retranslate.");
        Localization.Current = language; ApplyDpiLanguage();
        DpiModeComboBox.SelectedIndex = 0;
        Check(_settings.GetDpiOptions() == DpiOptions.Standard && DpiUdpFragmentSizeTextBox.Text == "16", "Invalid UDP parameter blocked Standard recovery.");
        DpiModeComboBox.SelectedIndex = 1;
        writes = Saves; updates = _winDivert.Updates;
        DpiUdpFakeRepeatsTextBox.Text = "0"; DpiUdpFakeRepeatsTextBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        Check(Saves == writes && _winDivert.Updates == updates && DpiValidationText.Visibility == Visibility.Visible, "Zero UDP repeats saved.");
        DpiModeComboBox.SelectedIndex = 0; DpiModeComboBox.SelectedIndex = 1;
        DpiUdpIpFragmentationCheckBox.IsChecked = false;
        DpiUdpFakeLowTtlCheckBox.IsChecked = false;
        DpiUdpLengthPaddingCheckBox.IsChecked = false;
        Check(!DpiUdpFragmentPanel.IsEnabled && !DpiUdpFakeTtlTextBox.IsEnabled && !DpiUdpPaddingSizeTextBox.IsEnabled, "Inactive UDP dependencies remained enabled.");
        Console.WriteLine("PASS: UDP controls persist/apply, protocol selection survives mode switches, QUIC block conflict is visible, invalid ranges/alignment never apply, validation retranslates and Standard recovery works.");
    }

    public void ShowUdpDemo()
    {
        _isLoadingSettings = true;
        try
        {
            DpiUdpExpander.IsExpanded = true;
            DpiUdpFakeWrongChecksumCheckBox.IsChecked = true;
            DpiUdpFakeLowTtlCheckBox.IsChecked = false;
            DpiUdpIpFragmentationCheckBox.IsChecked = true;
            DpiUdpReverseFragmentsCheckBox.IsChecked = true;
            DpiUdpLengthPaddingCheckBox.IsChecked = false;
            DpiUdpQuicInitialCheckBox.IsChecked = true;
            DpiUdpUnknownCheckBox.IsChecked = false;
            DpiBlockQuicCheckBox.IsChecked = true;
            ApplyDpiLanguage();
        }
        finally { _isLoadingSettings = false; }
    }
}
