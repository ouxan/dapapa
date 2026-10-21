using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using dapapa.Dialogs;
using dapapa.Models;
using dapapa.Services;
using Localization = dapapa.Services.Localization;

internal sealed partial class SelectionHarness : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dapapa-selection-ui-" + Guid.NewGuid());
    private readonly AppSettings _settings = new() { TargetListVersion = 1 };
    private SettingsService _settingsService;
    private bool _isLoadingSettings, _isBusy, _userWantsConnected = true;
    private bool _isConnected = true;
    private List<ProtectedApp> _allApps;
    private Dictionary<string, bool> _savedTargetSelection = new();
    private readonly ObservableCollection<ProtectedApp> _filteredApps = new();
    private readonly Dictionary<string, RelatedDomainsDialog> _discoveryDialogs = new();
    private readonly TextBox CatalogSearchTextBox = new();
    private readonly TextBlock CatalogSearchPlaceholder = new(), NoResultsText = new();
    private readonly Button ClearSearchButton = new();
    private readonly ComboBox LanguageComboBox = new();
    private readonly CheckBox RunOnStartupCheckBox = new(), StartMinimizedCheckBox = new(), MinimizeToTrayCheckBox = new(), AutoConnectCheckBox = new(), ConnectionWatchdogCheckBox = new();
    private readonly DispatcherTimer _statusTimer = new();
    private readonly Engine _winDivert = new();
    private CancellationTokenSource? _operationCts;
    private long _targetSession;
    private DateTimeOffset _nextDnsRefresh;
    private DateTime? _watchdogDropSince { get; set; }
    private WindowState WindowState { get; set; }
    private void Hide() { }
    public int Saves, LanguageApplications;
    private void AppendLog(string value) { }
    private void UpdateActiveServicesCount() { }
    private void ApplyLanguage() => LanguageApplications++;
    private long NextTargetSession() => _targetSession + 1;
    private void SetBusyUi(bool busy, string? key) { }
    private void SetConnectionVisualState(bool active) => _isConnected = active;
    private void SuppressWatchdog(TimeSpan duration) { }
    private Task<(bool Applied, int Failed)> RefreshTargetsAsync(bool force, CancellationToken ct) => Task.FromResult((true, 0));
    private Task StopEmptyProtectionAsync() { _winDivert.IsRunning = false; return Task.CompletedTask; }
    private sealed class Engine
    {
        public bool IsRunning = true;
        public int Updates, Starts;
        public bool FailStop;
        public TaskCompletionSource? StopGate;
        public async Task StopAsync()
        {
            CleanupTrace.Events.Add("stop-begin");
            if (StopGate != null) await StopGate.Task;
            IsRunning = false;
            CleanupTrace.Events.Add("stop-end");
            if (FailStop) throw new IOException("stop failed");
        }
        public void Stop() => StopAsync().GetAwaiter().GetResult();
        public HashSet<string> Domains = ["example.com"];
        public void UpdateTargetDomains(IEnumerable<string> domains, IEnumerable<string> cidrs) { Domains = domains.ToHashSet(); Updates++; }
        public bool Start(IEnumerable<string> domains, IEnumerable<string> cidrs, string endpoint, DpiOptions options)
        {
            if (HostsStub.Clears == 0) throw new Exception("Protection started before legacy hosts cleanup.");
            Starts++; IsRunning = true; return true;
        }
    }
    public SelectionHarness()
    {
        Directory.CreateDirectory(_directory);
        _settingsService = CreateStore();
        _allApps = _settings.Apps;
        LanguageComboBox.Items.Add("English"); LanguageComboBox.Items.Add("Turkish"); LanguageComboBox.SelectedIndex = 0;
    }
    private SettingsService CreateStore()
    {
        var service = new SettingsService(Path.Combine(_directory, "settings.json"));
        service.Log += text => { if (text == Localization.T("SettingsSaved")) Saves++; };
        return service;
    }
    public void Verify()
    {
        var app = new ProtectedApp { Domain = "example.com", Name = "Example", Enabled = true };
        _settings.Apps.Add(app); _settingsService.Save(_settings); Saves = 0;
        _savedTargetSelection[app.Id] = true;
        CheckBox Create()
        {
            var control = new CheckBox { DataContext = app };
            control.Checked += AppEnabledCheckBox_Changed; control.Unchecked += AppEnabledCheckBox_Changed;
            control.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(ProtectedApp.Enabled)) { Mode = BindingMode.OneWay });
            return control;
        }
        var first = Create(); var second = Create();
        if (Saves != 0) throw new Exception("Rebuilding controls saved unchanged targets.");
        second.SetCurrentValue(CheckBox.IsCheckedProperty, false);
        if (Saves != 1 || app.Enabled || _winDivert.Domains.Count != 0) throw new Exception("Disabling target did not commit exactly once.");
        _winDivert.IsRunning = _userWantsConnected = true;
        second.SetCurrentValue(CheckBox.IsCheckedProperty, true);
        if (Saves != 2 || !app.Enabled || first.IsChecked != true) throw new Exception("Enabling target broke bound controls.");
        string blocked = Path.Combine(_directory, "blocked.json"); Directory.CreateDirectory(blocked);
        _settingsService = new SettingsService(blocked);
        int updates = _winDivert.Updates;
        second.SetCurrentValue(CheckBox.IsCheckedProperty, false);
        if (!app.Enabled || second.IsChecked != true || !BindingOperations.IsDataBound(second, CheckBox.IsCheckedProperty) ||
            !CreateStore().Load().Apps[0].Enabled || _winDivert.Updates != updates)
            throw new Exception("Failed toggle changed UI, persisted state or engine.");
        foreach (var action in new[] { "add", "edit", "delete", "discover" })
        {
            var candidate = _allApps.Select(a => a.Copy()).ToList();
            if (action == "add") candidate.Add(new ProtectedApp { Domain = "other.example" });
            if (action == "edit") { candidate[0].Name = "Changed"; candidate[0].SetDomains(["changed.example"]); }
            if (action == "delete") candidate.Clear();
            if (action == "discover") candidate[0].AddRelatedDomains(["cdn.other.net"]);
            if (SaveAppsAndSyncTargets(candidate) || _allApps.Count != 1 || app.Name != "Example" || app.Domain != "example.com" || app.RelatedDomains.Count != 0)
                throw new Exception("Failed " + action + " mutated committed targets.");
        }
        StartMinimizedCheckBox.IsChecked = true;
        SettingCheckBox_Changed(StartMinimizedCheckBox, new RoutedEventArgs()).GetAwaiter().GetResult();
        if (_settings.StartMinimizedToTray || StartMinimizedCheckBox.IsChecked != false) throw new Exception("General setting save failure was not rolled back.");
        Localization.Current = AppLanguage.English; LanguageComboBox.SelectedIndex = 1;
        LanguageComboBox_SelectionChanged(LanguageComboBox, new SelectionChangedEventArgs(ComboBox.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
        if (Localization.Current != AppLanguage.English || LanguageComboBox.SelectedIndex != 0 || LanguageApplications != 0)
            throw new Exception("Language save failure changed localization.");
        _settingsService = CreateStore();
        StartupStub.Fail = true; RunOnStartupCheckBox.IsChecked = true;
        SettingCheckBox_Changed(RunOnStartupCheckBox, new RoutedEventArgs()).GetAwaiter().GetResult();
        StartupStub.Fail = false;
        if (_settings.RunOnWindowsStartup || CreateStore().Load().RunOnWindowsStartup || RunOnStartupCheckBox.IsChecked != false)
            throw new Exception("Startup operation failure left the saved preference enabled.");
        var edited = _allApps.Select(a => a.Copy()).ToList(); edited[0].Name = "Edited"; edited[0].AddRelatedDomains(["cdn.other.net"]);
        if (!SaveAppsAndSyncTargets(edited) || !ReferenceEquals(app, _allApps[0]) || app.Name != "Edited") throw new Exception("Successful group edit lost identity.");
        CatalogSearchTextBox.Text = "cdn.other.net"; FilterCatalog();
        if (!_filteredApps.SequenceEqual([app])) throw new Exception("Search missed a related domain.");
        int changes = 0; _filteredApps.CollectionChanged += (_, _) => changes++;
        FilterCatalog(); if (changes != 0) throw new Exception("Unchanged search rebuilt the collection.");
        _settings.AutoConnectOnLaunch = true; HostsStub.Clears = 0;
        MainWindow_Loaded(this, new RoutedEventArgs()).GetAwaiter().GetResult();
        if (_winDivert.Starts != 1 || HostsStub.Clears != 1) throw new Exception("Auto-connect skipped legacy hosts cleanup.");
        Console.WriteLine("PASS: actual target/general/language handlers roll back failed saves; startup failure restores disk; auto-connect clears hosts; successful edits retain identity and unchanged search does not rebuild.");
    }
    public void Dispose() { _statusTimer.Stop(); _operationCts?.Dispose(); Directory.Delete(_directory, true); }
}
internal static class StartupStub
{
    public static bool Fail;
    public static Task SetEnabledAsync(bool enabled) => Fail ? Task.FromException(new IOException("startup denied")) : Task.CompletedTask;
}
internal static class CleanupTrace { public static readonly List<string> Events = []; }
internal static class HostsStub
{
    public static int Clears;
    public static bool Fail;
    public static void RemoveEntries() { Clears++; CleanupTrace.Events.Add("hosts"); if (Fail) throw new IOException("hosts denied"); }
}
internal static class ResolverStub { public static void ClearCache() => CleanupTrace.Events.Add("doh-cache"); }
internal static class DnsCacheStub
{
    public static bool Fail;
    public static void Flush() { CleanupTrace.Events.Add("windows-cache"); if (Fail) throw new IOException("flush failed"); }
}
