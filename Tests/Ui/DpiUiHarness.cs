using System.IO;
using System.Windows;
using System.Windows.Controls;
using dapapa.Models;
using dapapa.Services;
using Localization = dapapa.Services.Localization;
using Services = dapapa.Services;

internal sealed partial class DpiUiHarness : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dapapa-dpi-ui-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new() { TargetListVersion = 1, Apps = [new() { Domain = "example.com", Name = "Keep me" }] };
    private SettingsService _settingsService;
    private readonly Engine _winDivert = new();
    private bool _isLoadingSettings;
    private bool _isConnected;
    private bool _isBusy;
    private bool _userWantsConnected;
    private CancellationTokenSource? _operationCts;
    private long _targetSession;
    private Task LastRestart = Task.CompletedTask;
    private DateTimeOffset _nextDnsRefresh;
    private long NextTargetSession() => _targetSession + 1;
    private void SetBusyUi(bool busy, string? key) { }
    private void SetConnectionVisualState(bool active) => _isConnected = active;
    private void SuppressWatchdog(TimeSpan duration) { }
    private Task<(bool Applied, int Failed)> RefreshTargetsAsync(bool force, CancellationToken token) => Task.FromResult((true, 0));
    private Task StopProtectionAsync() { _userWantsConnected = false; return _winDivert.StopAsync(); }
    private MessageBoxResult ShowError(string message, string title, MessageBoxButton buttons, MessageBoxImage icon) { ErrorShown = true; return MessageBoxResult.OK; }
    public int Saves;
    public bool ErrorShown;
    private void AppendLog(string value) { }
    private sealed class Engine : IDisposable
    {
        public readonly WinDivertAntiCensorshipService Service = new();
        public int Updates;
        public bool IsRunning, FailStart;
        public int Starts, Stops;
        public TaskCompletionSource? StopGate;
        public async Task StopAsync() { Stops++; if (StopGate != null) await StopGate.Task; IsRunning = false; }
        public bool Start(IEnumerable<string> domains, IEnumerable<string> cidrs, string endpoint, DpiOptions options)
        {
            Starts++; if (FailStart) return false;
            Service.UpdateTargetDomains(domains, cidrs); Service.UpdateDpiOptions(options); IsRunning = true; return true;
        }
        public void UpdateDpiOptions(DpiOptions options) { Updates++; Service.UpdateDpiOptions(options); }
        public void Dispose() => Service.Dispose();
    }
    private readonly CheckBox DpiBlockQuicCheckBox;
    private readonly TextBlock DpiCustomCompatibilityText;
    private readonly ComboBoxItem DpiCustomModeItem;
    private readonly StackPanel DpiCustomPanel;
    private readonly Expander DpiFakeAdvancedExpander;
    private readonly CheckBox DpiFakeAutoTtlCheckBox;
    private readonly CheckBox DpiFakeDataNoAckCheckBox;
    private readonly TextBlock DpiFakeHostnameLabelText;
    private readonly TextBox DpiFakeHostnameTextBox;
    private readonly CheckBox DpiFakeLowTtlCheckBox;
    private readonly CheckBox DpiFakeMd5SignatureCheckBox;
    private readonly CheckBox DpiFakeMixSegmentsCheckBox;
    private readonly CheckBox DpiFakeRandomizeTlsCheckBox;
    private readonly TextBlock DpiFakeRepeatsLabelText;
    private readonly TextBox DpiFakeRepeatsTextBox;
    private readonly CheckBox DpiFakeTimestampCheckBox;
    private readonly TextBlock DpiFakeTtlLabelText;
    private readonly TextBox DpiFakeTtlTextBox;
    private readonly CheckBox DpiFakeWrongChecksumCheckBox;
    private readonly CheckBox DpiFakeWrongSequenceCheckBox;
    private readonly CheckBox DpiHttpHostDomainCaseCheckBox;
    private readonly CheckBox DpiHttpHostHeaderCaseCheckBox;
    private readonly CheckBox DpiHttpHostRemoveSpaceCheckBox;
    private readonly CheckBox DpiHttpHostTabCheckBox;
    private readonly CheckBox DpiHttpMethodEolCheckBox;
    private readonly CheckBox DpiHttpMethodSpaceCheckBox;
    private readonly TextBlock DpiHttpMethodsLabelText;
    private readonly TextBlock DpiMaxPayloadLabelText;
    private readonly TextBox DpiMaxPayloadTextBox;
    private readonly ComboBox DpiModeComboBox;
    private readonly TextBlock DpiModeHintText;
    private readonly CheckBox DpiMultiSplitCheckBox;
    private readonly CheckBox DpiReverseSegmentsCheckBox;
    private readonly TextBlock DpiSettingsHeaderText;
    private readonly CheckBox DpiSplitAtHostnameCheckBox;
    private readonly TextBlock DpiSplitPositionsLabelText;
    private readonly TextBox DpiSplitPositionsTextBox;
    private readonly TextBlock DpiSplitSizeLabelText;
    private readonly TextBox DpiSplitSizeTextBox;
    private readonly ComboBoxItem DpiStandardModeItem;
    private readonly CheckBox DpiTcpFragmentationCheckBox;
    private readonly StackPanel DpiTcpOptionsPanel;
    private readonly CheckBox DpiUdpDiscordDiscoveryCheckBox;
    private readonly Expander DpiUdpExpander;
    private readonly CheckBox DpiUdpFakeLowTtlCheckBox;
    private readonly TextBlock DpiUdpFakeRepeatsLabelText;
    private readonly TextBox DpiUdpFakeRepeatsTextBox;
    private readonly TextBlock DpiUdpFakeTtlLabelText;
    private readonly TextBox DpiUdpFakeTtlTextBox;
    private readonly CheckBox DpiUdpFakeWrongChecksumCheckBox;
    private readonly StackPanel DpiUdpFragmentPanel;
    private readonly TextBlock DpiUdpFragmentSizeLabelText;
    private readonly TextBox DpiUdpFragmentSizeTextBox;
    private readonly CheckBox DpiUdpIpFragmentationCheckBox;
    private readonly CheckBox DpiUdpLengthPaddingCheckBox;
    private readonly TextBlock DpiUdpPacketLimitLabelText;
    private readonly TextBox DpiUdpPacketLimitTextBox;
    private readonly TextBlock DpiUdpPaddingSizeLabelText;
    private readonly TextBox DpiUdpPaddingSizeTextBox;
    private readonly TextBlock DpiUdpProtocolsLabelText;
    private readonly TextBlock DpiUdpQuicBlockedText;
    private readonly CheckBox DpiUdpQuicInitialCheckBox;
    private readonly CheckBox DpiUdpReverseFragmentsCheckBox;
    private readonly TextBlock DpiUdpScopeText;
    private readonly CheckBox DpiUdpStunCheckBox;
    private readonly CheckBox DpiUdpUnknownCheckBox;
    private readonly CheckBox DpiUdpWireGuardCheckBox;
    private readonly TextBlock DpiValidationText;
    private readonly TextBlock StatusSubText;
    public DpiUiHarness(Window window)
    {
        Directory.CreateDirectory(_directory);
        _settingsService = new SettingsService(Path.Combine(_directory, "settings.json"));
        _settingsService.Log += text => { if (text == Localization.T("SettingsSaved")) Saves++; };
        _winDivert.Service.UpdateTargetDomains(["example.com"], ["203.0.113.20/32"]);
        DpiBlockQuicCheckBox = (CheckBox)window.FindName("DpiBlockQuicCheckBox");
        DpiCustomCompatibilityText = (TextBlock)window.FindName("DpiCustomCompatibilityText");
        DpiCustomModeItem = (ComboBoxItem)window.FindName("DpiCustomModeItem");
        DpiCustomPanel = (StackPanel)window.FindName("DpiCustomPanel");
        DpiFakeAdvancedExpander = (Expander)window.FindName("DpiFakeAdvancedExpander");
        DpiFakeAutoTtlCheckBox = (CheckBox)window.FindName("DpiFakeAutoTtlCheckBox");
        DpiFakeDataNoAckCheckBox = (CheckBox)window.FindName("DpiFakeDataNoAckCheckBox");
        DpiFakeHostnameLabelText = (TextBlock)window.FindName("DpiFakeHostnameLabelText");
        DpiFakeHostnameTextBox = (TextBox)window.FindName("DpiFakeHostnameTextBox");
        DpiFakeLowTtlCheckBox = (CheckBox)window.FindName("DpiFakeLowTtlCheckBox");
        DpiFakeMd5SignatureCheckBox = (CheckBox)window.FindName("DpiFakeMd5SignatureCheckBox");
        DpiFakeMixSegmentsCheckBox = (CheckBox)window.FindName("DpiFakeMixSegmentsCheckBox");
        DpiFakeRandomizeTlsCheckBox = (CheckBox)window.FindName("DpiFakeRandomizeTlsCheckBox");
        DpiFakeRepeatsLabelText = (TextBlock)window.FindName("DpiFakeRepeatsLabelText");
        DpiFakeRepeatsTextBox = (TextBox)window.FindName("DpiFakeRepeatsTextBox");
        DpiFakeTimestampCheckBox = (CheckBox)window.FindName("DpiFakeTimestampCheckBox");
        DpiFakeTtlLabelText = (TextBlock)window.FindName("DpiFakeTtlLabelText");
        DpiFakeTtlTextBox = (TextBox)window.FindName("DpiFakeTtlTextBox");
        DpiFakeWrongChecksumCheckBox = (CheckBox)window.FindName("DpiFakeWrongChecksumCheckBox");
        DpiFakeWrongSequenceCheckBox = (CheckBox)window.FindName("DpiFakeWrongSequenceCheckBox");
        DpiHttpHostDomainCaseCheckBox = (CheckBox)window.FindName("DpiHttpHostDomainCaseCheckBox");
        DpiHttpHostHeaderCaseCheckBox = (CheckBox)window.FindName("DpiHttpHostHeaderCaseCheckBox");
        DpiHttpHostRemoveSpaceCheckBox = (CheckBox)window.FindName("DpiHttpHostRemoveSpaceCheckBox");
        DpiHttpHostTabCheckBox = (CheckBox)window.FindName("DpiHttpHostTabCheckBox");
        DpiHttpMethodEolCheckBox = (CheckBox)window.FindName("DpiHttpMethodEolCheckBox");
        DpiHttpMethodSpaceCheckBox = (CheckBox)window.FindName("DpiHttpMethodSpaceCheckBox");
        DpiHttpMethodsLabelText = (TextBlock)window.FindName("DpiHttpMethodsLabelText");
        DpiMaxPayloadLabelText = (TextBlock)window.FindName("DpiMaxPayloadLabelText");
        DpiMaxPayloadTextBox = (TextBox)window.FindName("DpiMaxPayloadTextBox");
        DpiModeComboBox = (ComboBox)window.FindName("DpiModeComboBox");
        DpiModeHintText = (TextBlock)window.FindName("DpiModeHintText");
        DpiMultiSplitCheckBox = (CheckBox)window.FindName("DpiMultiSplitCheckBox");
        DpiReverseSegmentsCheckBox = (CheckBox)window.FindName("DpiReverseSegmentsCheckBox");
        DpiSettingsHeaderText = (TextBlock)window.FindName("DpiSettingsHeaderText");
        DpiSplitAtHostnameCheckBox = (CheckBox)window.FindName("DpiSplitAtHostnameCheckBox");
        DpiSplitPositionsLabelText = (TextBlock)window.FindName("DpiSplitPositionsLabelText");
        DpiSplitPositionsTextBox = (TextBox)window.FindName("DpiSplitPositionsTextBox");
        DpiSplitSizeLabelText = (TextBlock)window.FindName("DpiSplitSizeLabelText");
        DpiSplitSizeTextBox = (TextBox)window.FindName("DpiSplitSizeTextBox");
        DpiStandardModeItem = (ComboBoxItem)window.FindName("DpiStandardModeItem");
        DpiTcpFragmentationCheckBox = (CheckBox)window.FindName("DpiTcpFragmentationCheckBox");
        DpiTcpOptionsPanel = (StackPanel)window.FindName("DpiTcpOptionsPanel");
        DpiUdpDiscordDiscoveryCheckBox = (CheckBox)window.FindName("DpiUdpDiscordDiscoveryCheckBox");
        DpiUdpExpander = (Expander)window.FindName("DpiUdpExpander");
        DpiUdpFakeLowTtlCheckBox = (CheckBox)window.FindName("DpiUdpFakeLowTtlCheckBox");
        DpiUdpFakeRepeatsLabelText = (TextBlock)window.FindName("DpiUdpFakeRepeatsLabelText");
        DpiUdpFakeRepeatsTextBox = (TextBox)window.FindName("DpiUdpFakeRepeatsTextBox");
        DpiUdpFakeTtlLabelText = (TextBlock)window.FindName("DpiUdpFakeTtlLabelText");
        DpiUdpFakeTtlTextBox = (TextBox)window.FindName("DpiUdpFakeTtlTextBox");
        DpiUdpFakeWrongChecksumCheckBox = (CheckBox)window.FindName("DpiUdpFakeWrongChecksumCheckBox");
        DpiUdpFragmentPanel = (StackPanel)window.FindName("DpiUdpFragmentPanel");
        DpiUdpFragmentSizeLabelText = (TextBlock)window.FindName("DpiUdpFragmentSizeLabelText");
        DpiUdpFragmentSizeTextBox = (TextBox)window.FindName("DpiUdpFragmentSizeTextBox");
        DpiUdpIpFragmentationCheckBox = (CheckBox)window.FindName("DpiUdpIpFragmentationCheckBox");
        DpiUdpLengthPaddingCheckBox = (CheckBox)window.FindName("DpiUdpLengthPaddingCheckBox");
        DpiUdpPacketLimitLabelText = (TextBlock)window.FindName("DpiUdpPacketLimitLabelText");
        DpiUdpPacketLimitTextBox = (TextBox)window.FindName("DpiUdpPacketLimitTextBox");
        DpiUdpPaddingSizeLabelText = (TextBlock)window.FindName("DpiUdpPaddingSizeLabelText");
        DpiUdpPaddingSizeTextBox = (TextBox)window.FindName("DpiUdpPaddingSizeTextBox");
        DpiUdpProtocolsLabelText = (TextBlock)window.FindName("DpiUdpProtocolsLabelText");
        DpiUdpQuicBlockedText = (TextBlock)window.FindName("DpiUdpQuicBlockedText");
        DpiUdpQuicInitialCheckBox = (CheckBox)window.FindName("DpiUdpQuicInitialCheckBox");
        DpiUdpReverseFragmentsCheckBox = (CheckBox)window.FindName("DpiUdpReverseFragmentsCheckBox");
        DpiUdpScopeText = (TextBlock)window.FindName("DpiUdpScopeText");
        DpiUdpStunCheckBox = (CheckBox)window.FindName("DpiUdpStunCheckBox");
        DpiUdpUnknownCheckBox = (CheckBox)window.FindName("DpiUdpUnknownCheckBox");
        DpiUdpWireGuardCheckBox = (CheckBox)window.FindName("DpiUdpWireGuardCheckBox");
        DpiValidationText = (TextBlock)window.FindName("DpiValidationText");
        StatusSubText = (TextBlock)window.FindName("StatusSubText");
        DpiFakeHostnameTextBox.LostFocus += DpiSetting_Changed; DpiFakeHostnameTextBox.KeyDown += DpiInput_KeyDown;
        DpiFakeRepeatsTextBox.LostFocus += DpiSetting_Changed; DpiFakeRepeatsTextBox.KeyDown += DpiInput_KeyDown;
        DpiFakeTtlTextBox.LostFocus += DpiSetting_Changed; DpiFakeTtlTextBox.KeyDown += DpiInput_KeyDown;
        DpiMaxPayloadTextBox.LostFocus += DpiSetting_Changed; DpiMaxPayloadTextBox.KeyDown += DpiInput_KeyDown;
        DpiSplitPositionsTextBox.LostFocus += DpiSetting_Changed; DpiSplitPositionsTextBox.KeyDown += DpiInput_KeyDown;
        DpiSplitSizeTextBox.LostFocus += DpiSetting_Changed; DpiSplitSizeTextBox.KeyDown += DpiInput_KeyDown;
        DpiUdpFakeRepeatsTextBox.LostFocus += DpiSetting_Changed; DpiUdpFakeRepeatsTextBox.KeyDown += DpiInput_KeyDown;
        DpiUdpFakeTtlTextBox.LostFocus += DpiSetting_Changed; DpiUdpFakeTtlTextBox.KeyDown += DpiInput_KeyDown;
        DpiUdpFragmentSizeTextBox.LostFocus += DpiSetting_Changed; DpiUdpFragmentSizeTextBox.KeyDown += DpiInput_KeyDown;
        DpiUdpPacketLimitTextBox.LostFocus += DpiSetting_Changed; DpiUdpPacketLimitTextBox.KeyDown += DpiInput_KeyDown;
        DpiUdpPaddingSizeTextBox.LostFocus += DpiSetting_Changed; DpiUdpPaddingSizeTextBox.KeyDown += DpiInput_KeyDown;
        DpiModeComboBox.SelectionChanged += DpiMode_SelectionChanged;
        foreach (var control in CheckBoxes()) { control.Checked += DpiSetting_Changed; control.Unchecked += DpiSetting_Changed; }
        _isLoadingSettings = true; LoadDpiSettingsIntoUi(); ApplyDpiLanguage(); _isLoadingSettings = false;
    }
    private CheckBox[] CheckBoxes() => [DpiBlockQuicCheckBox, DpiFakeAutoTtlCheckBox, DpiFakeDataNoAckCheckBox, DpiFakeLowTtlCheckBox, DpiFakeMd5SignatureCheckBox, DpiFakeMixSegmentsCheckBox, DpiFakeRandomizeTlsCheckBox, DpiFakeTimestampCheckBox, DpiFakeWrongChecksumCheckBox, DpiFakeWrongSequenceCheckBox, DpiHttpHostDomainCaseCheckBox, DpiHttpHostHeaderCaseCheckBox, DpiHttpHostRemoveSpaceCheckBox, DpiHttpHostTabCheckBox, DpiHttpMethodEolCheckBox, DpiHttpMethodSpaceCheckBox, DpiMultiSplitCheckBox, DpiReverseSegmentsCheckBox, DpiSplitAtHostnameCheckBox, DpiTcpFragmentationCheckBox, DpiUdpDiscordDiscoveryCheckBox, DpiUdpFakeLowTtlCheckBox, DpiUdpFakeWrongChecksumCheckBox, DpiUdpIpFragmentationCheckBox, DpiUdpLengthPaddingCheckBox, DpiUdpQuicInitialCheckBox, DpiUdpReverseFragmentsCheckBox, DpiUdpStunCheckBox, DpiUdpUnknownCheckBox, DpiUdpWireGuardCheckBox];
}
