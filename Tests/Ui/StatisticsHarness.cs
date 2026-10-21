using System.Windows;
using System.Windows.Controls;
using dapapa.Services;
using Localization = dapapa.Services.Localization;
using dapapa.Models;

internal sealed class StatisticsHarness
{
    private bool _isConnected;
    private readonly TrafficStatisticsService _trafficService = new();
    private readonly FakeCounters _winDivert = new();
    private sealed class FakeCounters { public long BypassedPacketsCount; public long RedirectedDnsCount; public int ActiveDomainsCount; }
    private readonly List<ProtectedApp> _allApps = [];
    private readonly AppSettings _settings = new();
    private readonly TextBlock ProtectedTargetsValueText;
    private void UpdateProtectedTargetsText()
    {
        if (ProtectedTargetsValueText == null) return;
        int activeAppsCount = _allApps?.Count(a => a.Enabled) ?? 0;
        int domainsCount = _winDivert.ActiveDomainsCount > 0
            ? _winDivert.ActiveDomainsCount
            : (_settings?.GetEffectiveDomains().Count() ?? 0);
        ProtectedTargetsValueText.Text = Localization.T("ProtectedTargetCounts", activeAppsCount, domainsCount);
    }
    private readonly TextBlock AdapterValueText;
    private readonly TextBlock BypassedPacketsValueText;
    private readonly TextBlock DownloadSpeedValueText;
    private readonly TextBlock SafeDnsValueText;
    private readonly TextBlock SessionReceivedValueText;
    private readonly TextBlock SessionSentValueText;
    private readonly TextBlock SessionUptimeValueText;
    private readonly TextBlock TotalTransferredValueText;
    private readonly TextBlock UploadSpeedValueText;
    public StatisticsHarness(Window window)
    {
        ProtectedTargetsValueText = (TextBlock)window.FindName("ProtectedTargetsValueText");
        AdapterValueText = (TextBlock)window.FindName("AdapterValueText");
        BypassedPacketsValueText = (TextBlock)window.FindName("BypassedPacketsValueText");
        DownloadSpeedValueText = (TextBlock)window.FindName("DownloadSpeedValueText");
        SafeDnsValueText = (TextBlock)window.FindName("SafeDnsValueText");
        SessionReceivedValueText = (TextBlock)window.FindName("SessionReceivedValueText");
        SessionSentValueText = (TextBlock)window.FindName("SessionSentValueText");
        SessionUptimeValueText = (TextBlock)window.FindName("SessionUptimeValueText");
        TotalTransferredValueText = (TextBlock)window.FindName("TotalTransferredValueText");
        UploadSpeedValueText = (TextBlock)window.FindName("UploadSpeedValueText");
    }
        private void UpdateStatisticsLabels()
        {
            DownloadSpeedValueText.Text = _isConnected ? TrafficStatisticsService.FormatSpeed(_trafficService.DownloadSpeedBytesPerSec) : Localization.T("IdleSpeed", 0);
            UploadSpeedValueText.Text = _isConnected ? TrafficStatisticsService.FormatSpeed(_trafficService.UploadSpeedBytesPerSec) : Localization.T("IdleSpeed", 0);
            TotalTransferredValueText.Text = TrafficStatisticsService.FormatBytes(_trafficService.SessionTotalBytes);
            SessionReceivedValueText.Text = TrafficStatisticsService.FormatBytes(_trafficService.SessionDownloadedBytes);
            SessionSentValueText.Text = TrafficStatisticsService.FormatBytes(_trafficService.SessionUploadedBytes);
            AdapterValueText.Text = _trafficService.AdapterName;
            if (_isConnected) SessionUptimeValueText.Text = TrafficStatisticsService.FormatDuration(_trafficService.Uptime);
            long packets = _winDivert.BypassedPacketsCount;
            long queries = _winDivert.RedirectedDnsCount;
            BypassedPacketsValueText.Text = Localization.T(packets == 1 ? "PacketCount" : "PacketsCount", packets);
            SafeDnsValueText.Text = Localization.T(queries == 1 ? "QueryCount" : "QueriesCount", queries);
            UpdateProtectedTargetsText();
        }

    public void Verify()
    {
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            Localization.Current = language;
            _winDivert.BypassedPacketsCount = _winDivert.RedirectedDnsCount = 0;
            UpdateStatisticsLabels();
            Check(BypassedPacketsValueText.Text == Localization.T("PacketsCount", 0), "Disconnected packet label stale.");
            Check(SafeDnsValueText.Text == Localization.T("QueriesCount", 0), "Disconnected DNS label stale.");
            Check(DownloadSpeedValueText.Text == Localization.T("IdleSpeed", 0), "Disconnected speed culture stale.");
            Check(ProtectedTargetsValueText.Text == Localization.T("ProtectedTargetCounts", 0, 0), "Empty target counts stale.");
            _allApps.Add(new ProtectedApp { Domain = "example.com", Enabled = true });
            _winDivert.ActiveDomainsCount = 2;
            UpdateStatisticsLabels();
            Check(ProtectedTargetsValueText.Text == Localization.T("ProtectedTargetCounts", 1, 2), "Active target counts stale.");
            _allApps.Clear(); _winDivert.ActiveDomainsCount = 0;
            _winDivert.BypassedPacketsCount = 1234; _winDivert.RedirectedDnsCount = 1;
            UpdateStatisticsLabels();
            Check(BypassedPacketsValueText.Text == Localization.T("PacketsCount", 1234), "Retained packet count stale.");
            Check(SafeDnsValueText.Text == Localization.T("QueryCount", 1), "Singular retained DNS count stale.");
            Check(_winDivert.BypassedPacketsCount == 1234, "Language update reset counters.");
            _isConnected = true; UpdateStatisticsLabels(); _isConnected = false;
        }
    }
    public void Update() => UpdateStatisticsLabels();
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
