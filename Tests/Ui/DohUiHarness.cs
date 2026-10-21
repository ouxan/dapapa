using System.IO;
using System.Windows;
using System.Windows.Controls;
using dapapa.Models;
using dapapa.Services;
using Localization = dapapa.Services.Localization;

internal sealed partial class DohUiHarness : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dapapa-doh-ui-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new() { TargetListVersion = 1, Apps = [new() { Domain = "example.com", Name = "Keep" }],
        DpiMode = DpiMode.Custom, CustomDpi = new() { UdpFakeWrongChecksum = true } };
    private SettingsService _settingsService;
    private readonly Engine _winDivert = new();
    private bool _isLoadingSettings, _isBusy, _isConnected, _userWantsConnected;
    private CancellationTokenSource? _operationCts;
    private long _targetSession, _sessionCounter;
    private DateTimeOffset _nextDnsRefresh;
    private long? _lastDnsLatency;
    private readonly TextBlock DohSettingsHeaderText, DohProviderDescriptionText, DohSettingsHintText, PingValueText, PingTargetText;
    private readonly FrameworkElement PingBadge;
    private readonly ComboBox DohProviderComboBox;
    private readonly List<string> Logs = [];
    private Task LastApply = Task.CompletedTask;
    private int Saves;
    private bool RefreshFails;
    private string? ErrorShown;
    private string? BusyKey;
    private int Flushes;
    private void FlushDnsCache() => Flushes++;
    private MessageBoxResult ShowError(string message, string title, MessageBoxButton buttons, MessageBoxImage icon) { ErrorShown = message; return MessageBoxResult.OK; }

    private sealed class Engine
    {
        public bool IsRunning, FailStart;
        public int Starts, Stops;
        public string? Endpoint;
        public DpiOptions? Options;
        public TaskCompletionSource? StopGate;
        public async Task StopAsync()
        {
            Stops++;
            if (StopGate != null) await StopGate.Task;
            IsRunning = false;
        }
        public bool Start(IEnumerable<string> domains, IEnumerable<string> cidrs, string endpoint, DpiOptions options)
        {
            Check(!IsRunning && domains.SequenceEqual(["example.com"]), "Restart preceded stop or lost targets.");
            Starts++; Endpoint = endpoint; Options = options;
            return IsRunning = !FailStart;
        }
    }

    public DohUiHarness(Window window)
    {
        Directory.CreateDirectory(_directory);
        _settingsService = new SettingsService(Path.Combine(_directory, "settings.json"));
        _settingsService.Log += text => { if (text == Localization.T("SettingsSaved")) Saves++; };
        DohSettingsHeaderText = (TextBlock)window.FindName("DohSettingsHeaderText");
        DohProviderDescriptionText = (TextBlock)window.FindName("DohProviderDescriptionText");
        DohSettingsHintText = (TextBlock)window.FindName("DohSettingsHintText");
        PingValueText = (TextBlock)window.FindName("PingValueText");
        PingTargetText = (TextBlock)window.FindName("PingTargetText");
        PingBadge = (FrameworkElement)window.FindName("PingBadge");
        DohProviderComboBox = (ComboBox)window.FindName("DohProviderComboBox");
        DohProviderComboBox.SelectionChanged += DohProvider_SelectionChanged;
        LoadDohSettingsIntoUi();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private long NextTargetSession() => ++_sessionCounter;
    private void AppendLog(string text) => Logs.Add(text);
    private void SetConnectionVisualState(bool active) => _isConnected = active;
    private void SetBusyUi(bool busy, string? key) { DohProviderComboBox.IsEnabled = !busy; BusyKey = key; }
    private void SuppressWatchdog(TimeSpan duration) { }
    private Task<(bool Applied, int Failed)> RefreshTargetsAsync(bool force, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult((!RefreshFails, 0));
    }
    private async Task StopProtectionAsync()
    {
        _userWantsConnected = false;
        _operationCts?.Cancel(); _targetSession = 0; _nextDnsRefresh = DateTimeOffset.MaxValue;
        await _winDivert.StopAsync(); _isConnected = false;
    }
    private Task Select(DohProvider provider)
    {
        DohProviderComboBox.SelectedItem = DohProviderComboBox.Items.Cast<ComboBoxItem>().Single(item => (DohProvider)item.Tag == provider);
        return LastApply;
    }
    public void ShowProvider(DohProvider provider)
    {
        _settings.DohProvider = provider; LoadDohSettingsIntoUi(); UpdatePingTargetLabel();
    }
    public async Task VerifyAsync()
    {
        Check(DohProviderComboBox.Items.Count == 5 && Saves == 0, "Loading saved a provider or preset missing.");
        foreach (var provider in DohProviders.All)
        {
            int before = Saves;
            await Select(provider);
            Check(Saves == before + (provider == DohProvider.Quad9Ecs ? 0 : 1) && _settingsService.Load().DohProvider == provider &&
                _winDivert.Starts == 0, "Offline selection did not save once or started protection.");
        }
        Check(_settings.CustomDpi.UdpFakeWrongChecksum && _settings.Apps[0].Domain == "example.com", "Provider changed targets/DPI.");
        var language = Localization.Current;
        Localization.Current = language == AppLanguage.Turkish ? AppLanguage.English : AppLanguage.Turkish; ApplyDohLanguage();
        Check(DohProviderDescriptionText.Text == Localization.T("DohAdGuardDescription") &&
            ((ComboBoxItem)DohProviderComboBox.SelectedItem).Content.ToString() == Localization.T("DohProviderAdGuard"), "Provider did not retranslate.");
        Localization.Current = language; ApplyDohLanguage();
        var store = _settingsService;
        string blocked = Path.Combine(_directory, "blocked.json"); Directory.CreateDirectory(blocked);
        _settingsService = new SettingsService(blocked);
        await Select(DohProvider.Google);
        Check(_settings.DohProvider == DohProvider.AdGuard && (DohProvider)((ComboBoxItem)DohProviderComboBox.SelectedItem).Tag == DohProvider.AdGuard &&
            ErrorShown != null && _winDivert.Starts == 0, "Save failure changed provider/UI/runtime.");
        _settingsService = store; ErrorShown = null;
        _isBusy = true; await Select(DohProvider.Google); _isBusy = false;
        Check(_settings.DohProvider == DohProvider.AdGuard && (DohProvider)((ComboBoxItem)DohProviderComboBox.SelectedItem).Tag == DohProvider.AdGuard,
            "Busy operation accepted provider change.");
        _userWantsConnected = _winDivert.IsRunning = _isConnected = true;
        _operationCts = new CancellationTokenSource(); var old = _operationCts.Token;
        _targetSession = NextTargetSession(); long oldSession = _targetSession;
        _lastDnsLatency = 123;
        _winDivert.StopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Select(DohProvider.Google);
        Check(!pending.IsCompleted && old.IsCancellationRequested && _isBusy && !DohProviderComboBox.IsEnabled &&
            !_isConnected && _winDivert.Starts == 0 && _targetSession != oldSession && _lastDnsLatency == null &&
            BusyKey == "DohSwitching", "Restart did not cancel/invalidate old session before awaiting stop.");
        _winDivert.StopGate.SetResult(); await pending; _winDivert.StopGate = null;
        Check(_winDivert.Starts == 1 && _winDivert.Endpoint == DohProviders.GetEndpoint(DohProvider.Google) &&
            _winDivert.Options == _settings.GetDpiOptions() && _isConnected && !_isBusy && DohProviderComboBox.IsEnabled &&
            PingTargetText.Text == Localization.T("PingToDoh", new Uri(_settings.GetDohEndpoint()).Authority) && Logs.Contains(Localization.T("DohSelectionApplied", "Google Public DNS")), "Successful switch did not apply provider/restore UI.");
        Check(Flushes > 0, "Provider change failed to clear the Windows DNS cache.");
        _winDivert.FailStart = true; Logs.Clear(); await Select(DohProvider.Cloudflare);
        Check(!_winDivert.IsRunning && !_isConnected && !_userWantsConnected && _targetSession == 0 && ErrorShown != null &&
            _settingsService.Load().DohProvider == DohProvider.Cloudflare && !Logs.Any(l => l == Localization.T("DohSelectionApplied", "Cloudflare")), "Failed restart reported success, fell back or discarded saved preference.");
        _winDivert.FailStart = false; ErrorShown = null; RefreshFails = true;
        _userWantsConnected = _winDivert.IsRunning = true;
        await Select(DohProvider.Quad9);
        Check(!_winDivert.IsRunning && !_isConnected && !_userWantsConnected && ErrorShown != null, "Failed target refresh left new protection active.");
        RefreshFails = false; ErrorShown = null;
        _userWantsConnected = _winDivert.IsRunning = true;
        _winDivert.StopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = _winDivert.Starts;
        pending = Select(DohProvider.Quad9Ecs);
        _userWantsConnected = false; _operationCts!.Cancel();
        _winDivert.StopGate.SetResult(); await pending; _winDivert.StopGate = null;
        Check(_winDivert.Starts == starts && !_isConnected && ErrorShown == null && !_isBusy, "Cancellation during stop restarted protection or showed an error.");
        ShowProvider(DohProvider.Quad9Ecs);
        Console.WriteLine("PASS: real DoH UI saves once, retranslates, rolls back failed saves, blocks busy changes, cancels/invalidate sessions before stop, starts selected provider after drain, clears old latency and retains DPI/targets. Simulated native restart/refresh failures and stop cancellation leave protection off without false success.");
    }
    public void Dispose()
    {
        DohProviderComboBox.SelectionChanged -= DohProvider_SelectionChanged;
        _operationCts?.Cancel(); _operationCts?.Dispose();
        Directory.Delete(_directory, true);
    }
}
