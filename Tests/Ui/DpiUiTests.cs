using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using dapapa.Models;
using dapapa.Services;
using Localization = dapapa.Services.Localization;

internal sealed partial class DpiUiHarness
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private PacketDecision ProcessTls()
    {
        var processor = (PacketProcessor)typeof(WinDivertAntiCensorshipService).GetField("_processor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_winDivert.Service)!;
        var method = typeof(dapapa.MainWindow).Assembly.GetType("dapapa.Services.PacketProcessor")!;
        // A complete HTTP request is sufficient to exercise the same live strategy selection.
        byte[] data = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n");
        byte[] packet = new byte[40 + data.Length]; packet[0] = 0x45; packet[8] = 64; packet[9] = 6; packet[32] = 0x50; packet[33] = 0x18;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)packet.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(22), 80); data.CopyTo(packet, 40);
        return processor.Process(packet, true);
    }
    public void Verify()
    {
        var language = Localization.Current;
        Check(Saves == 0 && _winDivert.Updates == 0 && DpiCustomPanel.Visibility == Visibility.Collapsed, "Loading DPI UI saved settings or enabled Custom.");
        _isConnected = true;
        DpiModeComboBox.SelectedIndex = 1;
        Check(Saves == 1 && _winDivert.Updates == 1 && DpiCustomPanel.Visibility == Visibility.Visible &&
            DpiTcpOptionsPanel.IsEnabled && _settings.GetDpiOptions() == DpiOptions.Standard, "Selecting Custom did not save/apply once.");
        DpiTcpFragmentationCheckBox.IsChecked = false;
        Check(!DpiTcpOptionsPanel.IsEnabled && DpiReverseSegmentsCheckBox.IsChecked == true, "TCP dependents remained active or were erased.");
        DpiFakeWrongChecksumCheckBox.IsChecked = true;
        Check(ProcessTls().Action == PacketAction.Modify, "Live fake method did not reach the service.");
        DpiHttpHostHeaderCaseCheckBox.IsChecked = true;
        DpiHttpHostDomainCaseCheckBox.IsChecked = true;
        DpiHttpHostRemoveSpaceCheckBox.IsChecked = true;
        DpiBlockQuicCheckBox.IsChecked = false;
        var custom = _settings.CustomDpi;
        int writes = Saves;
        DpiModeComboBox.SelectedIndex = 0;
        Check(Saves == writes + 1 && _settings.GetDpiOptions() == DpiOptions.Standard && _settings.CustomDpi == custom &&
            DpiCustomPanel.Visibility == Visibility.Collapsed && ProcessTls().Action == PacketAction.Fragment, "Standard did not restore current behavior or lost Custom choices.");
        DpiModeComboBox.SelectedIndex = 1;
        Check(_settings.GetDpiOptions() == custom && DpiTcpFragmentationCheckBox.IsChecked == false && DpiFakeWrongChecksumCheckBox.IsChecked == true,
            "Returning to Custom lost selections.");
        writes = Saves;
        foreach (var current in new[] { AppLanguage.Turkish, AppLanguage.English, language })
        {
            Localization.Current = current; ApplyDpiLanguage();
            Check(DpiCustomModeItem.Content.ToString() == Localization.T("DpiCustomMode") && DpiModeHintText.Text == Localization.T("DpiCustomHint") &&
                DpiFakeWrongChecksumCheckBox.ToolTip.ToString() == Localization.T("DpiFakeWrongChecksumHint") && _settings.CustomDpi == custom && Saves == writes,
                "Language refresh changed options, saved settings or left stale text.");
        }
        foreach (var control in CheckBoxes()) control.IsChecked = false;
        Check(!_settings.GetDpiOptions().HasTrafficMethods && StatusSubText.Text == Localization.T("DnsOnlyHint"), "DNS-only profile falsely claims active DPI methods.");
        Check(ProcessTls().Action == PacketAction.Pass && _settings.Apps.Count == 1 && _settings.Apps[0].Domain == "example.com", "Disabling methods changed target list or retained a method.");
        _isBusy = true; StatusSubText.Text = "Pending operation"; writes = Saves;
        DpiTcpFragmentationCheckBox.IsChecked = true;
        Check(Saves == writes && ProcessTls().Action == PacketAction.Pass && DpiTcpFragmentationCheckBox.IsChecked == false &&
            StatusSubText.Text == "Pending operation", "Busy operation accepted a conflicting DPI change.");
        _isBusy = false; DpiTcpFragmentationCheckBox.IsChecked = false;
        var loaded = _settingsService.Load();
        Check(loaded.DpiMode == DpiMode.Custom && loaded.CustomDpi == _settings.CustomDpi && loaded.Apps.Count == 1, "Real settings save/load changed profile or target.");
        writes = Saves; int updates = _winDivert.Updates;
        DpiTcpFragmentationCheckBox.IsChecked = false;
        Check(Saves == writes && _winDivert.Updates == updates, "Unchanged selection saved or applied twice.");
        var savedStore = _settingsService;
        string blocked = Path.Combine(_directory, "blocked.json"); Directory.CreateDirectory(blocked);
        _settingsService = new SettingsService(blocked);
        var before = _settings.CustomDpi;
        DpiFakeWrongChecksumCheckBox.IsChecked = true;
        Check(ErrorShown && _settings.CustomDpi == before && DpiFakeWrongChecksumCheckBox.IsChecked == false &&
            _winDivert.Updates == updates && !_isLoadingSettings, "Save failure changed live methods or failed to restore the UI.");
        _settingsService = savedStore;
        Console.WriteLine("PASS: real DPI controls save/apply once, retain Custom choices, disable TCP dependents, preserve targets and localize; save failure rolls back without applying.");
    }
    public void ShowStandard() { DpiModeComboBox.SelectedIndex = 0; ApplyDpiLanguage(); }
    public void VerifyRestart()
    {
        ShowStandard();
        _userWantsConnected = _winDivert.IsRunning = true;
        _operationCts = new CancellationTokenSource();
        var previous = _operationCts.Token;
        _winDivert.StopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DpiModeComboBox.SelectedIndex = 1;
        Check(_isBusy && previous.IsCancellationRequested && _winDivert.Starts == 0,
            "Changing capture scope did not cancel and drain the old session first.");
        _winDivert.StopGate.TrySetResult(); Program.Wait(LastRestart); _winDivert.StopGate = null;
        Check(_winDivert.Starts == 1 && _isConnected && !_isBusy && _settingsService.Load().DpiMode == DpiMode.Custom,
            "DPI restart failed to retain saved profile and restore protection status.");
        _winDivert.FailStart = true; ErrorShown = false;
        DpiUdpUnknownCheckBox.IsChecked = !DpiUdpUnknownCheckBox.IsChecked;
        try { Program.Wait(LastRestart); }
        catch (InvalidOperationException) { }
        Program.Wait(Task.Delay(1));
        Check(ErrorShown && !_winDivert.IsRunning && !_isConnected && !_userWantsConnected &&
            _settingsService.Load().CustomDpi == _settings.CustomDpi,
            "Failed DPI restart left protection active or diverged from the saved profile.");
        _winDivert.FailStart = false;
        Console.WriteLine("PASS: actual DPI restart drains old work before capture changes; failed start leaves protection off and retains the saved profile. Native calls are simulated.");
    }
    public void ShowCustomDemo()
    {
        DpiModeComboBox.SelectedIndex = 1;
        DpiTcpFragmentationCheckBox.IsChecked = true;
        DpiReverseSegmentsCheckBox.IsChecked = true;
        DpiSplitAtHostnameCheckBox.IsChecked = true;
        DpiFakeWrongChecksumCheckBox.IsChecked = false;
        DpiHttpHostHeaderCaseCheckBox.IsChecked = true;
        DpiHttpHostDomainCaseCheckBox.IsChecked = true;
        DpiHttpHostRemoveSpaceCheckBox.IsChecked = true;
        DpiBlockQuicCheckBox.IsChecked = true;
        ApplyDpiLanguage();
    }
    public void Dispose()
    {
        DpiModeComboBox.SelectionChanged -= DpiMode_SelectionChanged;
        foreach (var control in CheckBoxes()) { control.Checked -= DpiSetting_Changed; control.Unchecked -= DpiSetting_Changed; }
        _winDivert.Dispose(); Directory.Delete(_directory, true);
    }
}
