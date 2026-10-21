using System.Windows;
using System.Windows.Controls;
using dapapa.Models;
using Localization = dapapa.Services.Localization;

namespace dapapa
{
    public partial class MainWindow
    {
        private void LoadDpiSettingsIntoUi()
        {
            var options = _settings.CustomDpi;
            DpiModeComboBox.SelectedIndex = _settings.DpiMode == DpiMode.Custom ? 1 : 0;
            DpiTcpFragmentationCheckBox.IsChecked = options.TcpFragmentation;
            DpiReverseSegmentsCheckBox.IsChecked = options.ReverseSegments;
            DpiSplitAtHostnameCheckBox.IsChecked = options.SplitAtHostname;
            DpiFakeWrongChecksumCheckBox.IsChecked = options.FakeWrongChecksum;
            DpiHttpHostHeaderCaseCheckBox.IsChecked = options.HttpHostHeaderCase;
            DpiHttpHostDomainCaseCheckBox.IsChecked = options.HttpHostDomainCase;
            DpiHttpHostRemoveSpaceCheckBox.IsChecked = options.HttpHostRemoveSpace;
            DpiBlockQuicCheckBox.IsChecked = options.BlockQuic;
            DpiMultiSplitCheckBox.IsChecked = options.MultiSplit;
            DpiFakeWrongSequenceCheckBox.IsChecked = options.FakeWrongSequence;
            DpiFakeLowTtlCheckBox.IsChecked = options.FakeLowTtl;
            DpiFakeAutoTtlCheckBox.IsChecked = options.FakeAutoTtl;
            DpiFakeMd5SignatureCheckBox.IsChecked = options.FakeMd5Signature;
            DpiFakeTimestampCheckBox.IsChecked = options.FakeTimestamp;
            DpiFakeDataNoAckCheckBox.IsChecked = options.FakeDataNoAck;
            DpiFakeRandomizeTlsCheckBox.IsChecked = options.FakeRandomizeTls;
            DpiFakeMixSegmentsCheckBox.IsChecked = options.FakeMixSegments;
            DpiHttpMethodSpaceCheckBox.IsChecked = options.HttpMethodSpace;
            DpiHttpMethodEolCheckBox.IsChecked = options.HttpMethodEol;
            DpiHttpHostTabCheckBox.IsChecked = options.HttpHostTab;
            DpiUdpFakeWrongChecksumCheckBox.IsChecked = options.UdpFakeWrongChecksum;
            DpiUdpFakeLowTtlCheckBox.IsChecked = options.UdpFakeLowTtl;
            DpiUdpIpFragmentationCheckBox.IsChecked = options.UdpIpFragmentation;
            DpiUdpReverseFragmentsCheckBox.IsChecked = options.UdpReverseFragments;
            DpiUdpLengthPaddingCheckBox.IsChecked = options.UdpLengthPadding;
            DpiUdpQuicInitialCheckBox.IsChecked = options.UdpQuicInitial;
            DpiUdpStunCheckBox.IsChecked = options.UdpStun;
            DpiUdpWireGuardCheckBox.IsChecked = options.UdpWireGuard;
            DpiUdpDiscordDiscoveryCheckBox.IsChecked = options.UdpDiscordDiscovery;
            DpiUdpUnknownCheckBox.IsChecked = options.UdpUnknown;
            DpiUdpFakeTtlTextBox.Text = options.UdpFakeTtl.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DpiUdpFakeRepeatsTextBox.Text = options.UdpFakeRepeats.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DpiUdpFragmentSizeTextBox.Text = options.UdpFragmentSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DpiUdpPaddingSizeTextBox.Text = options.UdpPaddingSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DpiUdpPacketLimitTextBox.Text = options.UdpPacketLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DpiSplitSizeTextBox.Text = options.TcpSplitSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DpiSplitPositionsTextBox.Text = options.SplitPositions;
            DpiFakeTtlTextBox.Text = options.FakeTtl.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DpiFakeRepeatsTextBox.Text = options.FakeRepeats.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DpiFakeHostnameTextBox.Text = options.FakeHostname;
            DpiMaxPayloadTextBox.Text = options.MaxPayloadSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            UpdateDpiUi();
        }

        private void ApplyDpiLanguage()
        {
            DpiSettingsHeaderText.Text = Localization.T("DpiSettingsHeader");
            DpiStandardModeItem.Content = Localization.T("DpiStandardMode");
            DpiCustomModeItem.Content = Localization.T("DpiCustomMode");
            DpiTcpFragmentationCheckBox.Content = Localization.T("DpiTcpFragmentation");
            DpiTcpFragmentationCheckBox.ToolTip = Localization.T("DpiTcpFragmentationHint");
            DpiReverseSegmentsCheckBox.Content = Localization.T("DpiReverseSegments");
            DpiReverseSegmentsCheckBox.ToolTip = Localization.T("DpiReverseSegmentsHint");
            DpiSplitAtHostnameCheckBox.Content = Localization.T("DpiSplitAtHostname");
            DpiSplitAtHostnameCheckBox.ToolTip = Localization.T("DpiSplitAtHostnameHint");
            DpiFakeWrongChecksumCheckBox.Content = Localization.T("DpiFakeWrongChecksum");
            DpiFakeWrongChecksumCheckBox.ToolTip = Localization.T("DpiFakeWrongChecksumHint");
            DpiHttpMethodsLabelText.Text = Localization.T("DpiHttpMethodsLabel");
            DpiHttpHostHeaderCaseCheckBox.Content = Localization.T("DpiHttpHostHeaderCase");
            DpiHttpHostHeaderCaseCheckBox.ToolTip = Localization.T("DpiHttpHostHeaderCaseHint");
            DpiHttpHostDomainCaseCheckBox.Content = Localization.T("DpiHttpHostDomainCase");
            DpiHttpHostDomainCaseCheckBox.ToolTip = Localization.T("DpiHttpHostDomainCaseHint");
            DpiHttpHostRemoveSpaceCheckBox.Content = Localization.T("DpiHttpHostRemoveSpace");
            DpiHttpHostRemoveSpaceCheckBox.ToolTip = Localization.T("DpiHttpHostRemoveSpaceHint");
            DpiBlockQuicCheckBox.Content = Localization.T("DpiBlockQuic");
            DpiBlockQuicCheckBox.ToolTip = Localization.T("DpiBlockQuicHint");
            DpiCustomCompatibilityText.Text = Localization.T("DpiCustomCompatibility");
            DpiMultiSplitCheckBox.Content = Localization.T("DpiMultiSplit");
            DpiMultiSplitCheckBox.ToolTip = Localization.T("DpiMultiSplitHint");
            DpiFakeWrongSequenceCheckBox.Content = Localization.T("DpiFakeWrongSequence");
            DpiFakeWrongSequenceCheckBox.ToolTip = Localization.T("DpiFakeWrongSequenceHint");
            DpiFakeLowTtlCheckBox.Content = Localization.T("DpiFakeLowTtl");
            DpiFakeLowTtlCheckBox.ToolTip = Localization.T("DpiFakeLowTtlHint");
            DpiFakeAutoTtlCheckBox.Content = Localization.T("DpiFakeAutoTtl");
            DpiFakeAutoTtlCheckBox.ToolTip = Localization.T("DpiFakeAutoTtlHint");
            DpiFakeMd5SignatureCheckBox.Content = Localization.T("DpiFakeMd5Signature");
            DpiFakeMd5SignatureCheckBox.ToolTip = Localization.T("DpiFakeMd5SignatureHint");
            DpiFakeTimestampCheckBox.Content = Localization.T("DpiFakeTimestamp");
            DpiFakeTimestampCheckBox.ToolTip = Localization.T("DpiFakeTimestampHint");
            DpiFakeDataNoAckCheckBox.Content = Localization.T("DpiFakeDataNoAck");
            DpiFakeDataNoAckCheckBox.ToolTip = Localization.T("DpiFakeDataNoAckHint");
            DpiFakeRandomizeTlsCheckBox.Content = Localization.T("DpiFakeRandomizeTls");
            DpiFakeRandomizeTlsCheckBox.ToolTip = Localization.T("DpiFakeRandomizeTlsHint");
            DpiHttpMethodSpaceCheckBox.Content = Localization.T("DpiHttpMethodSpace");
            DpiHttpMethodSpaceCheckBox.ToolTip = Localization.T("DpiHttpMethodSpaceHint");
            DpiHttpMethodEolCheckBox.Content = Localization.T("DpiHttpMethodEol");
            DpiHttpMethodEolCheckBox.ToolTip = Localization.T("DpiHttpMethodEolHint");
            DpiHttpHostTabCheckBox.Content = Localization.T("DpiHttpHostTab");
            DpiHttpHostTabCheckBox.ToolTip = Localization.T("DpiHttpHostTabHint");
            DpiSplitSizeLabelText.Text = Localization.T("DpiSplitSize");
            DpiSplitPositionsLabelText.Text = Localization.T("DpiSplitPositions");
            DpiFakeTtlLabelText.Text = Localization.T("DpiFakeTtl");
            DpiFakeRepeatsLabelText.Text = Localization.T("DpiFakeRepeats");
            DpiFakeHostnameLabelText.Text = Localization.T("DpiFakeHostname");
            DpiMaxPayloadLabelText.Text = Localization.T("DpiMaxPayload");
            DpiFakeAdvancedExpander.Header = Localization.T("DpiFakeAdvanced");
            DpiFakeMixSegmentsCheckBox.Content = Localization.T("DpiFakeMixSegments");
            DpiFakeMixSegmentsCheckBox.ToolTip = Localization.T("DpiFakeMixSegmentsHint");
            DpiUdpExpander.Header = Localization.T("DpiUdpHeader");
            DpiUdpScopeText.Text = Localization.T("DpiUdpScope");
            DpiUdpFakeWrongChecksumCheckBox.Content = Localization.T("DpiUdpFakeWrongChecksum");
            DpiUdpFakeWrongChecksumCheckBox.ToolTip = Localization.T("DpiUdpFakeWrongChecksumHint");
            DpiUdpFakeLowTtlCheckBox.Content = Localization.T("DpiUdpFakeLowTtl");
            DpiUdpFakeLowTtlCheckBox.ToolTip = Localization.T("DpiUdpFakeLowTtlHint");
            DpiUdpIpFragmentationCheckBox.Content = Localization.T("DpiUdpIpFragmentation");
            DpiUdpIpFragmentationCheckBox.ToolTip = Localization.T("DpiUdpIpFragmentationHint");
            DpiUdpReverseFragmentsCheckBox.Content = Localization.T("DpiUdpReverseFragments");
            DpiUdpLengthPaddingCheckBox.Content = Localization.T("DpiUdpLengthPadding");
            DpiUdpLengthPaddingCheckBox.ToolTip = Localization.T("DpiUdpLengthPaddingHint");
            DpiUdpQuicInitialCheckBox.Content = Localization.T("DpiUdpQuicInitial");
            DpiUdpStunCheckBox.Content = Localization.T("DpiUdpStun");
            DpiUdpWireGuardCheckBox.Content = Localization.T("DpiUdpWireGuard");
            DpiUdpDiscordDiscoveryCheckBox.Content = Localization.T("DpiUdpDiscordDiscovery");
            DpiUdpUnknownCheckBox.Content = Localization.T("DpiUdpUnknown");
            DpiUdpFakeTtlLabelText.Text = Localization.T("DpiUdpFakeTtl");
            DpiUdpFakeRepeatsLabelText.Text = Localization.T("DpiUdpFakeRepeats");
            DpiUdpFragmentSizeLabelText.Text = Localization.T("DpiUdpFragmentSize");
            DpiUdpPaddingSizeLabelText.Text = Localization.T("DpiUdpPaddingSize");
            DpiUdpPacketLimitLabelText.Text = Localization.T("DpiUdpPacketLimit");
            DpiUdpProtocolsLabelText.Text = Localization.T("DpiUdpProtocols");
            DpiUdpQuicBlockedText.Text = Localization.T("DpiUdpQuicBlocked");
            if (DpiValidationText.Visibility == Visibility.Visible) TryReadDpiParameters(_settings.CustomDpi, out _);
            UpdateDpiUi();
        }

        private void UpdateDpiUi()
        {
            if (DpiCustomPanel == null || DpiBlockQuicCheckBox == null) return;
            bool custom = DpiModeComboBox.SelectedIndex == 1;
            DpiCustomPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            DpiTcpOptionsPanel.IsEnabled = custom && DpiTcpFragmentationCheckBox.IsChecked == true;
            DpiModeHintText.Text = Localization.T(custom ? "DpiCustomHint" : "DpiStandardHint");
            DpiSplitSizeTextBox.IsEnabled = custom && DpiTcpFragmentationCheckBox.IsChecked == true && DpiMultiSplitCheckBox.IsChecked != true && DpiSplitAtHostnameCheckBox.IsChecked != true;
            DpiSplitPositionsTextBox.IsEnabled = custom && DpiTcpFragmentationCheckBox.IsChecked == true && DpiMultiSplitCheckBox.IsChecked == true;
            DpiSplitAtHostnameCheckBox.IsEnabled = custom && DpiTcpFragmentationCheckBox.IsChecked == true && DpiMultiSplitCheckBox.IsChecked != true;
            DpiFakeTtlTextBox.IsEnabled = custom && DpiFakeLowTtlCheckBox.IsChecked == true && DpiFakeAutoTtlCheckBox.IsChecked != true;
            DpiFakeMixSegmentsCheckBox.IsEnabled = custom && DpiTcpFragmentationCheckBox.IsChecked == true;
            DpiUdpFragmentPanel.IsEnabled = custom && DpiUdpIpFragmentationCheckBox.IsChecked == true;
            DpiUdpFakeTtlTextBox.IsEnabled = custom && DpiUdpFakeLowTtlCheckBox.IsChecked == true;
            DpiUdpFakeRepeatsTextBox.IsEnabled = custom && (DpiUdpFakeLowTtlCheckBox.IsChecked == true || DpiUdpFakeWrongChecksumCheckBox.IsChecked == true);
            DpiUdpPaddingSizeTextBox.IsEnabled = custom && DpiUdpLengthPaddingCheckBox.IsChecked == true;
            DpiUdpQuicBlockedText.Visibility = DpiBlockQuicCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DpiMode_SelectionChanged(object sender, SelectionChangedEventArgs e) => DpiSetting_Changed(sender, e);

        private async void DpiSetting_Changed(object sender, RoutedEventArgs e)
        {
            UpdateDpiUi();
            if (_isLoadingSettings || DpiBlockQuicCheckBox == null) return;
            if (_isBusy) { LoadDpiSettingsIntoUi(); return; }
            var previousMode = _settings.DpiMode;
            var previousOptions = _settings.CustomDpi;
            var basicOptions = new DpiOptions
            {
                TcpFragmentation = DpiTcpFragmentationCheckBox.IsChecked == true,
                ReverseSegments = DpiReverseSegmentsCheckBox.IsChecked == true,
                SplitAtHostname = DpiSplitAtHostnameCheckBox.IsChecked == true,
                FakeWrongChecksum = DpiFakeWrongChecksumCheckBox.IsChecked == true,
                HttpHostHeaderCase = DpiHttpHostHeaderCaseCheckBox.IsChecked == true,
                HttpHostDomainCase = DpiHttpHostDomainCaseCheckBox.IsChecked == true,
                HttpHostRemoveSpace = DpiHttpHostRemoveSpaceCheckBox.IsChecked == true,
                BlockQuic = DpiBlockQuicCheckBox.IsChecked == true
            };
            DpiOptions customOptions = previousOptions;
            if (DpiModeComboBox.SelectedIndex == 1 && !TryReadDpiParameters(basicOptions, out customOptions)) return;
            bool standard = DpiModeComboBox.SelectedIndex != 1;
            if (standard) DpiValidationText.Visibility = Visibility.Collapsed;
            _settings.DpiMode = DpiModeComboBox.SelectedIndex == 1 ? DpiMode.Custom : DpiMode.Standard;
            _settings.CustomDpi = customOptions;
            if (previousMode == _settings.DpiMode && previousOptions == _settings.CustomDpi) return;
            bool saved = false;
            try
            {
                _settingsService.Save(_settings);
                saved = true;
                var oldEffective = previousMode == DpiMode.Custom ? previousOptions : DpiOptions.Standard;
                if (_winDivert.IsRunning && dapapa.Services.WinDivertAntiCensorshipService.BuildFilter(oldEffective) !=
                    dapapa.Services.WinDivertAntiCensorshipService.BuildFilter(_settings.GetDpiOptions()))
                    await RestartDpiProtectionAsync();
                else _winDivert.UpdateDpiOptions(_settings.GetDpiOptions());
                if (standard)
                {
                    _isLoadingSettings = true;
                    try { LoadDpiSettingsIntoUi(); }
                    finally { _isLoadingSettings = false; }
                }
                if (_isConnected && !_isBusy)
                    StatusSubText.Text = Localization.T(_settings.GetDpiOptions().HasTrafficMethods ? "ConnectedHint" : "DnsOnlyHint");
                AppendLog(Localization.T("DpiSettingsApplied", Localization.T(_settings.DpiMode == DpiMode.Custom ? "DpiCustomMode" : "DpiStandardMode")));
            }
            catch (Exception ex)
            {
                if (!saved)
                {
                    _settings.DpiMode = previousMode;
                    _settings.CustomDpi = previousOptions;
                }
                _isLoadingSettings = true;
                try { LoadDpiSettingsIntoUi(); }
                finally { _isLoadingSettings = false; }
                System.Windows.MessageBox.Show(Localization.T(saved ? "GenericError" : "SettingsSaveError", ex.Message), Localization.T("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RestartDpiProtectionAsync()
        {
            _isBusy = true;
            SetBusyUi(true, "Connecting");
            SuppressWatchdog(TimeSpan.FromSeconds(20));
            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            var ct = _operationCts.Token;
            _targetSession = NextTargetSession();
            _nextDnsRefresh = DateTimeOffset.MaxValue;
            try
            {
                await _winDivert.StopAsync();
                ct.ThrowIfCancellationRequested();
                if (!_userWantsConnected) return;
                if (!_winDivert.Start(_settings.GetEffectiveDomains(), [], _settings.GetDohEndpoint(), _settings.GetDpiOptions()))
                    throw new InvalidOperationException(Localization.T("ProtectionStartFailed"));
                var result = await RefreshTargetsAsync(false, ct);
                if (!result.Applied || !_winDivert.IsRunning)
                    throw new InvalidOperationException(Localization.T("ProtectionStartFailed"));
            }
            catch
            {
                await StopProtectionAsync();
                throw;
            }
            finally
            {
                _isBusy = false;
                SetConnectionVisualState(_winDivert.IsRunning && _userWantsConnected);
                SetBusyUi(false, null);
            }
        }

        private void DpiInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            DpiSetting_Changed(sender, e); e.Handled = true;
        }

        private bool TryReadDpiParameters(DpiOptions basic, out DpiOptions options)
        {
            options = basic;
            bool Number(System.Windows.Controls.TextBox input, string label, int min, int max, out int value)
            {
                if (int.TryParse(input.Text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value) && value >= min && value <= max) return true;
                DpiValidationText.Text = Localization.T("DpiNumberError", Localization.T(label), min, max);
                DpiValidationText.Visibility = Visibility.Visible; return false;
            }
            if (!Number(DpiSplitSizeTextBox, "DpiSplitSize", 1, 1024, out int split) ||
                !Number(DpiFakeTtlTextBox, "DpiFakeTtl", 1, 64, out int ttl) ||
                !Number(DpiFakeRepeatsTextBox, "DpiFakeRepeats", 1, 6, out int repeats) ||
                !Number(DpiMaxPayloadTextBox, "DpiMaxPayload", 0, 65535, out int maxPayload) ||
                !Number(DpiUdpFakeTtlTextBox, "DpiUdpFakeTtl", 1, 64, out int udpTtl) ||
                !Number(DpiUdpFakeRepeatsTextBox, "DpiUdpFakeRepeats", 1, 6, out int udpRepeats) ||
                !Number(DpiUdpFragmentSizeTextBox, "DpiUdpFragmentSize", 8, 1440, out int udpFragment) ||
                !Number(DpiUdpPaddingSizeTextBox, "DpiUdpPaddingSize", 1, 64, out int udpPadding) ||
                !Number(DpiUdpPacketLimitTextBox, "DpiUdpPacketLimit", 1, 20, out int udpLimit)) return false;
            if (udpFragment % 8 != 0)
            {
                DpiValidationText.Text = Localization.T("DpiUdpFragmentSizeError");
                DpiValidationText.Visibility = Visibility.Visible; return false;
            }
            string markers = DpiSplitPositionsTextBox.Text.Trim();
            if (!Services.TcpSplitPositions.IsValid(markers))
            {
                DpiValidationText.Text = Localization.T("DpiInvalidMarkers"); DpiValidationText.Visibility = Visibility.Visible; return false;
            }
            string hostname = DpiFakeHostnameTextBox.Text.Trim();
            if (hostname.Length > 0 && !Services.DomainName.TryNormalize(hostname, out hostname))
            {
                DpiValidationText.Text = Localization.T("InvalidDomain"); DpiValidationText.Visibility = Visibility.Visible; return false;
            }
            options = basic with
            {
                TcpSplitSize = split, SplitPositions = markers, FakeTtl = ttl, FakeRepeats = repeats,
                FakeHostname = hostname, MaxPayloadSize = maxPayload,
                MultiSplit = DpiMultiSplitCheckBox.IsChecked == true,
                FakeWrongSequence = DpiFakeWrongSequenceCheckBox.IsChecked == true,
                FakeLowTtl = DpiFakeLowTtlCheckBox.IsChecked == true,
                FakeAutoTtl = DpiFakeAutoTtlCheckBox.IsChecked == true,
                FakeMd5Signature = DpiFakeMd5SignatureCheckBox.IsChecked == true,
                FakeTimestamp = DpiFakeTimestampCheckBox.IsChecked == true,
                FakeDataNoAck = DpiFakeDataNoAckCheckBox.IsChecked == true,
                FakeRandomizeTls = DpiFakeRandomizeTlsCheckBox.IsChecked == true,
                FakeMixSegments = DpiFakeMixSegmentsCheckBox.IsChecked == true,
                HttpMethodSpace = DpiHttpMethodSpaceCheckBox.IsChecked == true,
                HttpMethodEol = DpiHttpMethodEolCheckBox.IsChecked == true,
                HttpHostTab = DpiHttpHostTabCheckBox.IsChecked == true,
                UdpFakeWrongChecksum = DpiUdpFakeWrongChecksumCheckBox.IsChecked == true,
                UdpFakeLowTtl = DpiUdpFakeLowTtlCheckBox.IsChecked == true,
                UdpIpFragmentation = DpiUdpIpFragmentationCheckBox.IsChecked == true,
                UdpReverseFragments = DpiUdpReverseFragmentsCheckBox.IsChecked == true,
                UdpLengthPadding = DpiUdpLengthPaddingCheckBox.IsChecked == true,
                UdpQuicInitial = DpiUdpQuicInitialCheckBox.IsChecked == true,
                UdpStun = DpiUdpStunCheckBox.IsChecked == true,
                UdpWireGuard = DpiUdpWireGuardCheckBox.IsChecked == true,
                UdpDiscordDiscovery = DpiUdpDiscordDiscoveryCheckBox.IsChecked == true,
                UdpUnknown = DpiUdpUnknownCheckBox.IsChecked == true,
                UdpFakeTtl = udpTtl, UdpFakeRepeats = udpRepeats, UdpFragmentSize = udpFragment,
                UdpPaddingSize = udpPadding, UdpPacketLimit = udpLimit
            };
            DpiValidationText.Visibility = Visibility.Collapsed;
            return true;
        }
    }
}
