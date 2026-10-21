using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using dapapa.Models;
using dapapa.Services;

internal static partial class Program
{
    private static async Task AuditRegressionTests()
    {
        Run("legacy hosts cleanup preserves unrelated aliases and backs up before writing", LegacyHostsBoundaries);
        Run("resolved IP TTL expires without a UI refresh and exact matches allocate nothing", ResolvedIpExpiry);
        Run("native capture filters exclude unrelated UDP and track enabled methods", NativeCaptureScope);
        Run("adapter switches, disconnects and counter resets retain accurate totals", AdapterSwitches);
        await RunAsync("target removal rejects pending loopback and native DNS replies", RemovedTargetReply);
        await RunAsync("IP refresh preserves pending selected DNS and learned subdomain IPs", UnchangedDomainsReply);
        await RunAsync("startup task errors propagate and only one registration is used", StartupRegistration);
        await RunAsync("background logs drain, rotate and remain bounded", LogRotation);
        Run("application exit cleanup runs once and reports failures", ExitCleanup);
    }

    private static void ExitCleanup()
    {
        int calls = 0;
        dapapa.App.ExitCleanup = () => calls++;
        Check(dapapa.App.RunExitCleanup() == null && calls == 1, "Exit cleanup was skipped.");
        Check(dapapa.App.RunExitCleanup() == null && calls == 1, "Exit cleanup was repeated.");
        var failure = new IOException("cleanup denied");
        dapapa.App.ExitCleanup = () => throw failure;
        Check(ReferenceEquals(dapapa.App.RunExitCleanup(), failure), "Exit cleanup failure was hidden.");
        Check(dapapa.App.RunExitCleanup() == null, "Failed exit callback was retained.");
    }

    private static void LegacyHostsBoundaries()
    {
        string directory = Path.Combine(Path.GetTempPath(), "dapapa-hosts-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "hosts");
        const string original = "127.0.0.1 localhost\n# Discord Ek Domainler (dapapa)\n203.0.113.1 DISCORD.COM. keep.example # retain\n203.0.113.2 private-notdiscord.com\n192.0.2.3 unrelated.example\n";
        try
        {
            File.WriteAllText(path, original);
            int flushes = 0;
            var store = new HostsFileStore(path, () => flushes++);
            store.EndSession();
            string cleaned = File.ReadAllText(path);
            Check(cleaned.Contains("private-notdiscord.com") && cleaned.Contains("keep.example # retain") &&
                cleaned.Contains("unrelated.example") && !cleaned.Contains("DISCORD.COM"), "Legacy cleanup deleted an unrelated hostname or missed case normalization.");
            string backup = Directory.GetFiles(directory, "*.bak").Single();
            Check(File.ReadAllText(backup) == original && flushes == 1, "Backup was not the unchanged original.");
            store.EndSession();
            Check(Directory.GetFiles(directory, "*.bak").Length == 1 && flushes == 1, "Unchanged hosts produced another write/backup.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void ResolvedIpExpiry()
    {
        var clock = new Clock();
        var processor = new PacketProcessor(timeProvider: clock);
        processor.UpdateTargets(["example.com"], [], [new("203.0.113.20", clock.Now.AddSeconds(10))]);
        byte[] selected = UdpDpiPacket([1]);
        Check(processor.Process(selected, true).Action == PacketAction.Drop, "Unexpired resolved IP was not targeted.");
        clock.Now = clock.Now.AddSeconds(11);
        Check(processor.Process(selected, true).Action == PacketAction.Pass, "Expired resolved IP survived without a UI refresh.");
        processor.UpdateTargets(["example.com"], Enumerable.Range(0, 2000).Select(i => $"10.0.{i / 256}.{i % 256}/32"));
        for (int i = 0; i < 10000; i++) processor.Process(selected, true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < 100000; i++) Check(processor.Process(selected, true).Action == PacketAction.Pass, "Unselected IP changed.");
        timer.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 1024, "Unmatched UDP hot path allocates per packet.");
        Console.WriteLine($"PERF: 100000 unmatched packets, 2000 exact targets: {timer.Elapsed.TotalMilliseconds:F1} ms, {allocated} allocated bytes; managed processor only.");
        processor.UpdateTargets(["example.com"], ["203.0.113.0/24"]);
        Check(processor.Process(selected, true).Action == PacketAction.Drop, "CIDR support regressed.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool EvalFilter([MarshalAs(UnmanagedType.LPStr)] string filter, byte[] packet, uint length, ref WinDivertAddress address);
    private static void NativeCaptureScope()
    {
        IntPtr library = NativeLibrary.Load(Path.GetFullPath(Path.Combine("windivert", "WinDivert.dll")));
        try
        {
            var eval = Marshal.GetDelegateForFunctionPointer<EvalFilter>(NativeLibrary.GetExport(library, "WinDivertHelperEvalFilter"));
            var address = new WinDivertAddress { Flags = 1u << 17, InterfaceIndex = 12 };
            bool Matches(DpiOptions options, byte[] packet) => eval(WinDivertAntiCensorshipService.BuildFilter(options), packet, (uint)packet.Length, ref address);
            Check(!Matches(DpiOptions.Standard, UdpDpiPacket([1, 2, 3], 50000)), "Standard captures unrelated UDP ports.");
            Check(Matches(DpiOptions.Standard, UdpDpiPacket([1], 443)) && Matches(DpiOptions.Standard, UdpDpiPacket(DnsWire.CreateQuery("example.com"), 53)), "Standard missed QUIC/DNS.");
            Check(!Matches(DpiOptions.Standard with { BlockQuic = false }, UdpDpiPacket([1], 443)), "Disabled QUIC blocking retained UDP/443 capture.");
            Check(Matches(UdpOptions with { UdpStun = true }, UdpDpiPacket(StunRequest(), 3478)), "Custom STUN capture missed a valid request.");
            Check(!Matches(UdpOptions, UdpDpiPacket(StunRequest(), 3478)), "Unselected STUN method expanded capture.");
            Check(Matches(UdpOptions with { UdpUnknown = true }, UdpDpiPacket([1, 2, 3], 50000)), "Explicit unknown UDP capture was disabled.");
            address.Flags = 0;
            byte[] synAck = TcpPacket(ClientHello("example.com"));
            Write16(synAck, 20, 443); synAck[33] = 0x12;
            Check(!Matches(DpiOptions.Standard, synAck) && Matches(DpiOptions.Standard with { FakeAutoTtl = true }, synAck), "AutoTTL capture did not follow the option.");
        }
        finally { NativeLibrary.Free(library); }
    }

    private sealed class TrafficClock : TimeProvider
    {
        public long Timestamp;
        public override long GetTimestamp() => Timestamp;
        public override long TimestampFrequency => 1000;
        public void Advance() => Timestamp += 1000;
    }
    private static void AdapterSwitches()
    {
        var clock = new TrafficClock();
        TrafficStatisticsService.AdapterSample? sample = new("wifi", "Wi-Fi", 1000, 100, true);
        var stats = new TrafficStatisticsService(() => sample, clock);
        stats.StartSession(); clock.Advance(); sample = new("wifi", "Wi-Fi", 1200, 150, true); stats.Update(true);
        Check(stats.SessionDownloadedBytes == 200 && stats.DownloadSpeedBytesPerSec == 200, "Initial adapter sample wrong.");
        clock.Advance(); sample = new("ethernet", "Ethernet", 900000, 800000, true); stats.Update(true);
        Check(stats.SessionDownloadedBytes == 200 && stats.DownloadSpeedBytesPerSec == 0, "Adapter switch created a traffic spike.");
        clock.Advance(); sample = new("ethernet", "Ethernet", 900300, 800020, true); stats.Update(true);
        Check(stats.SessionDownloadedBytes == 500 && stats.SessionUploadedBytes == 70, "Completed adapter totals were lost.");
        clock.Advance(); sample = null; stats.Update(true); clock.Advance(); sample = new("ethernet", "Ethernet", 999999, 999999, true); stats.Update(true);
        Check(stats.SessionDownloadedBytes == 500 && stats.DownloadSpeedBytesPerSec == 0, "Reconnect compared different baselines.");
        clock.Advance(); sample = new("ethernet", "Ethernet", 1, 1, true); stats.Update(true);
        Check(stats.SessionDownloadedBytes == 500 && stats.Uptime.TotalSeconds == 6, "Counter reset or monotonic uptime failed.");
        stats.Reset(); Check(stats.SessionTotalBytes == 0 && stats.Uptime == TimeSpan.Zero, "Session reset failed.");
    }

    private static async Task RemovedTargetReply()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new WireHandler(async (query, _) => { started.TrySetResult(); await release.Task; return WireResponse(Answer(query)); });
        using var http = new HttpClient(handler);
        var client = new DohDnsClient(http);
        await using var dns = new LocalDnsService(client, 0);
        dns.UpdateTargets([UploadDomain, "keep.example"]); await dns.StartAsync();
        var processor = Processor(UploadDomain, "keep.example");
        int sends = 0;
        var relay = new DnsPacketRelay(dns.Endpoint, processor, (_, _) => { Interlocked.Increment(ref sends); return true; });
        byte[] packet = UdpPacket(DnsWire.CreateQuery(UploadDomain), "192.0.2.1", "1.1.1.1", 52000, 53);
        long oldVersion = processor.Process(packet, true).TargetVersion;
        relay.Queue(packet, default, oldVersion);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        processor.UpdateTargets(["keep.example"], []); dns.UpdateTargets(["keep.example"]);
        release.TrySetResult();
        Check(!processor.TrySendDnsResponse(packet[28..], oldVersion, () => { sends++; return true; }), "Old DNS version was allowed to send.");
        await Task.Delay(80); await relay.StopAsync();
        Check(sends == 0 && processor.DomainsCount == 1 && processor.Process(packet, true).Action == PacketAction.Pass,
            "A removed target sent an old reply or restored its selection.");
        using var socket = new UdpClient(); socket.Connect(dns.Endpoint);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await socket.SendAsync(DnsWire.CreateQuery("keep.example"), timeout.Token);
        Check(DnsWire.TryReadQuestion((await socket.ReceiveAsync(timeout.Token)).Buffer, out var question) && question.Name == "keep.example", "Remaining target stopped working.");
    }

    private static async Task StartupRegistration()
    {
        const string exe = @"C:\Program Files\dapapa\dapapa.exe";
        string xml = $"<Task xmlns='http://schemas.microsoft.com/windows/2004/02/mit/task'><Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers><Principals><Principal><RunLevel>HighestAvailable</RunLevel></Principal></Principals><Settings><Enabled>true</Enabled></Settings><Actions><Exec><Command>{exe}</Command><Arguments>--tray</Arguments></Exec></Actions></Task>";
        Check(StartupService.IsCurrentTask(xml, exe) && !StartupService.IsCurrentTask(xml.Replace("<Enabled>true", "<Enabled>false"), exe) &&
            !StartupService.IsCurrentTask(xml, "wrong.exe"), "Disabled/wrong startup task was considered current.");
        int creates = 0, deletes = 0, legacyRemovals = 0; bool exists = false, fail = false;
        Task<StartupService.CommandResult> Execute(IReadOnlyList<string> args, CancellationToken _)
        {
            if (args.Contains("/create")) { creates++; if (fail) return Task.FromResult(new StartupService.CommandResult(1, "", "denied")); exists = true; }
            if (args.Contains("/delete")) { deletes++; exists = false; }
            if (args.Contains("/xml")) return Task.FromResult(new StartupService.CommandResult(exists ? 0 : 1, exists ? xml : "", "missing"));
            return Task.FromResult(new StartupService.CommandResult(0, "", ""));
        }
        await StartupService.SetEnabledAsync(true, exe, Execute, () => legacyRemovals++);
        await StartupService.SetEnabledAsync(true, exe, Execute, () => legacyRemovals++);
        Check(creates == 1 && legacyRemovals == 2, "Startup duplicated a valid registration or retained legacy Run entry.");
        await StartupService.SetEnabledAsync(false, exe, Execute, () => legacyRemovals++);
        Check(deletes == 1 && !exists, "Disabling startup did not remove the task.");
        fail = true;
        try { await StartupService.SetEnabledAsync(true, exe, Execute, () => legacyRemovals++); throw new Exception("Failed startup task reported success."); }
        catch (IOException) { }
        Check(legacyRemovals == 3, "Task failure mutated legacy registration.");
    }

    private static async Task UnchangedDomainsReply()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new WireHandler(async (query, _) => { started.TrySetResult(); await release.Task; return WireResponse(Answer(query)); });
        using var http = new HttpClient(handler);
        await using var dns = new LocalDnsService(new DohDnsClient(http), 0);
        dns.UpdateTargets(["example.com"]); await dns.StartAsync();
        var processor = Processor("example.com");
        byte[] query = DnsWire.CreateQuery("api.example.com");
        byte[] packet = UdpPacket(query, "192.0.2.1", "1.1.1.1", 52000, 53);
        long version = processor.Process(packet, true).TargetVersion;
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var relay = new DnsPacketRelay(dns.Endpoint, processor, (_, _) => { received.TrySetResult(); return true; });
        relay.Queue(packet, default, version);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        processor.UpdateTargets(["example.com"], [], [new("192.0.2.44", DateTimeOffset.UtcNow.AddSeconds(30))]);
        dns.UpdateTargets(["example.com"]);
        release.TrySetResult();
        await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await relay.StopAsync();
        processor.UpdateTargets(["example.com"], [], [new("192.0.2.45", DateTimeOffset.UtcNow.AddSeconds(30))]);
        Check(processor.Process(UdpDpiPacket([1]), true).Action == PacketAction.Drop,
            "IP-only refresh erased an unexpired subdomain address learned from selected DNS.");
    }

    private static async Task LogRotation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "dapapa-log-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "app.log");
        try
        {
            for (int batch = 0; batch < 7; batch++)
            {
                await using var log = new LogFileWriter(path, 4096);
                for (int i = 0; i < 12; i++) log.Write("batch=" + batch + " " + new string('x', 80));
            }
            var files = Directory.GetFiles(directory);
            Check(files.Length <= 4 && files.Length > 1 && files.All(f => new FileInfo(f).Length <= 4096), "Log rotation did not bound file count and sizes.");
            Check(File.ReadAllText(path).Contains("batch=6"), "Shutdown failed to drain the latest entries.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
