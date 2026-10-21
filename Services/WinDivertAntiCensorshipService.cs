using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Http;
using System.Runtime.InteropServices;
using dapapa.Models;

namespace dapapa.Services
{
    public sealed class WinDivertAntiCensorshipService : IDisposable
    {
        private sealed class WorkerSession(IntPtr handle, PacketProcessor processor, LocalDnsService dns, Uri endpoint, HttpClient? upstream, string filter)
        {
            public readonly IntPtr Handle = handle;
            public readonly PacketProcessor Processor = processor;
            public readonly LocalDnsService Dns = dns;
            public readonly Uri Endpoint = endpoint;
            public readonly HttpClient? Upstream = upstream;
            public readonly string Filter = filter;
            public long LastPacketLogTick;
            public DnsPacketRelay Relay = null!;
            public Thread Thread = null!;
            public volatile bool StopRequested;
            public bool HandleClosed;
        }

        private readonly object _gate = new();
        private WorkerSession? _session;
        private PacketProcessor _processor = new();
        private long _bypassedPacketsCount;
        private long _redirectedDnsCount;
        private string _lastBypassedTarget = "-";
        private DateTime? _lastBypassedTime;

        public event Action<string>? Log;
        public bool IsRunning
        {
            get { lock (_gate) return _session is { StopRequested: false } session && session.Thread.IsAlive && session.Dns.IsRunning; }
        }
        public long BypassedPacketsCount => Interlocked.Read(ref _bypassedPacketsCount);
        public long RedirectedDnsCount => Interlocked.Read(ref _redirectedDnsCount);
        public int ActiveDomainsCount => _processor.DomainsCount;
        public string LastBypassedTarget => _lastBypassedTarget;
        public DateTime? LastBypassedTime { get { lock (_gate) return _lastBypassedTime; } }

        internal static string BuildFilter(DpiOptions options)
        {
            options = options.Normalize();
            var clauses = new List<string> { "(outbound and udp and udp.DstPort == 53)" };
            if (options.TcpFragmentation || options.HasFakeMethods || options.HasHttpMethods)
                clauses.Add("(outbound and tcp and ((tcp.DstPort == 443 and tcp.PayloadLength >= 2 and tcp.Payload[0] == 0x16 and tcp.Payload[1] == 0x03) or " +
                    "(tcp.DstPort == 80 and tcp.PayloadLength >= 4 and (tcp.Payload32[0] == 0x47455420 or tcp.Payload32[0] == 0x504F5354 or tcp.Payload32[0] == 0x48454144))))");
            if (options.FakeAutoTtl)
                clauses.Add("(inbound and tcp and (tcp.SrcPort == 80 or tcp.SrcPort == 443) and tcp.Syn == 1 and tcp.Ack == 1)");
            if (options.BlockQuic) clauses.Add("(outbound and udp and udp.DstPort == 443)");
            if (options.HasUdpMethods)
            {
                var protocols = new List<string>();
                if (options.UdpUnknown) protocols.Add("true");
                else
                {
                    if (options.UdpQuicInitial) protocols.Add("(udp.PayloadLength >= 1200 and udp.Payload[0] >= 0xC0)");
                    if (options.UdpStun) protocols.Add("(udp.PayloadLength >= 20 and udp.Payload32[1] == 0x2112A442)");
                    if (options.UdpWireGuard) protocols.Add("(udp.PayloadLength == 148 and udp.Payload32[0] == 0x01000000)");
                    if (options.UdpDiscordDiscovery) protocols.Add("(udp.PayloadLength == 74 and udp.Payload32[0] == 0x00010046)");
                }
                if (protocols.Count > 0) clauses.Add("(outbound and udp and udp.DstPort != 53 and udp.SrcPort != 53 and length <= 1500 and (" +
                    string.Join(" or ", protocols) + "))");
            }
            return "!impostor and !loopback and ip and (" + string.Join(" or ", clauses) + ")";
        }


        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);
        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern IntPtr WinDivertOpen([MarshalAs(UnmanagedType.LPStr)] string filter, int layer, short priority, ulong flags);
        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertRecv(IntPtr handle, [Out] byte[] packet, uint length, out uint received, out WinDivertAddress address);
        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertSend(IntPtr handle, byte[] packet, uint length, out uint sent, ref WinDivertAddress address);
        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern bool WinDivertHelperCalcChecksums([In, Out] byte[] packet, uint length, ref WinDivertAddress address, ulong flags);
        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertSetParam(IntPtr handle, int parameter, ulong value);
        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertShutdown(IntPtr handle, int how);
        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertClose(IntPtr handle);

        public void ResetCounters()
        {
            Interlocked.Exchange(ref _bypassedPacketsCount, 0);
            Interlocked.Exchange(ref _redirectedDnsCount, 0);
            _lastBypassedTarget = "-";
            lock (_gate) _lastBypassedTime = null;
        }

        public void UpdateTargetDomains(IEnumerable<string> domains, IEnumerable<string>? cidrs = null,
            IEnumerable<ResolvedTargetIp>? resolvedIps = null)
        {
            lock (_gate)
            {
                var names = domains.ToArray();
                _session?.Dns.UpdateTargets(names);
                _processor.UpdateTargets(names, cidrs ?? [], resolvedIps);
            }
            Log?.Invoke(Localization.T("WinDivertUpdated", ActiveDomainsCount));
        }

        public void UpdateDpiOptions(DpiOptions options)
        {
            lock (_gate)
            {
                if (_session != null && _session.Filter != BuildFilter(options))
                    throw new InvalidOperationException("DPI capture filter changes require a protection restart.");
                _processor.UpdateOptions(options);
            }
        }

        public static string? FindWinDivertDirectory()
        {
            string[] candidates = [Path.Combine(AppContext.BaseDirectory, "windivert"), AppContext.BaseDirectory,
                Path.Combine(Directory.GetCurrentDirectory(), "windivert"), Directory.GetCurrentDirectory()];
            return candidates.FirstOrDefault(dir => File.Exists(Path.Combine(dir, "WinDivert.dll")) &&
                File.Exists(Path.Combine(dir, "WinDivert64.sys")));
        }

        public static bool IsDriverAvailable() => FindWinDivertDirectory() != null;

        public bool Start(IEnumerable<string> domains, IEnumerable<string>? cidrs = null, string? dohEndpoint = null, DpiOptions? dpiOptions = null)
        {
            domains = domains.ToArray();
            if (!domains.Any())
            {
                Log?.Invoke(Localization.T("NoActiveTargets"));
                return false;
            }
            lock (_gate)
            {
                Uri endpoint;
                try { endpoint = DohDnsClient.ParseEndpoint(dohEndpoint ?? DohDnsClient.Quad9EcsEndpoint); }
                catch (ArgumentException ex) { Log?.Invoke(ex.Message); return false; }
                if (_session != null)
                {
                    if (_session.StopRequested || !_session.Thread.IsAlive || !_session.Dns.IsRunning) return false;
                    if (_session.Endpoint != endpoint) return false;
                    if (_session.Filter != BuildFilter(dpiOptions ?? DpiOptions.Standard)) return false;
                    var names = domains.ToArray();
                    _session.Dns.UpdateTargets(names);
                    _processor.UpdateTargets(names, cidrs ?? []);
                    _processor.UpdateOptions(dpiOptions ?? DpiOptions.Standard);
                    return true;
                }
                var dir = FindWinDivertDirectory();
                if (dir == null)
                {
                    Log?.Invoke(Localization.T("WinDivertDriverNotFound"));
                    return false;
                }
                IntPtr handle = IntPtr.Zero;
                LocalDnsService? dns = null;
                HttpClient? upstream = null;
                try
                {
                    var names = domains.ToArray();
                    // Query and IP-refresh caches remain separate even when both use Cloudflare.
                    upstream = DomainDnsResolver.CreateHttpClient();
                    var client = new DohDnsClient(upstream, endpoint: endpoint.AbsoluteUri);
                    client.Log += message => Log?.Invoke(message);
                    dns = new LocalDnsService(client);
                    dns.Log += message => Log?.Invoke(message);
                    dns.UpdateTargets(names);
                    dns.StartAsync().GetAwaiter().GetResult();
                    Log?.Invoke(Localization.T("LocalDnsStarted", dns.Endpoint));
                    Log?.Invoke(Localization.T("DohProviderActive", endpoint.GetLeftPart(UriPartial.Path)));
                    if (!SetDllDirectory(dir)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    string filter = BuildFilter(dpiOptions ?? DpiOptions.Standard);
                    handle = WinDivertOpen(filter, 0, 0, 0);
                    if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (!WinDivertSetParam(handle, 0, 16384) || !WinDivertSetParam(handle, 1, 1000))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    _processor = new PacketProcessor(endpoint.Host);
                    _processor.UpdateTargets(names, cidrs ?? []);
                    _processor.UpdateOptions(dpiOptions ?? DpiOptions.Standard);
                    var session = new WorkerSession(handle, _processor, dns, endpoint, upstream, filter);
                    session.Relay = new DnsPacketRelay(dns.Endpoint, _processor, (packet, address) =>
                    {
                        if (session.StopRequested) return false;
                        if (address.InterfaceIndex == 0)
                        {
                            var clientIp = new IPAddress(packet.AsSpan(16, 4));
                            var adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(nic =>
                                nic.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(clientIp)));
                            address.InterfaceIndex = (uint)(adapter?.GetIPProperties().GetIPv4Properties()?.Index ??
                                throw new IOException("DNS client interface not found."));
                        }
                        Send(session.Handle, packet, (uint)packet.Length, address, true);
                        Interlocked.Increment(ref _redirectedDnsCount);
                        return true;
                    });
                    void OnFailure(Exception error)
                    {
                        Log?.Invoke(Localization.T("WinDivertWorkerFailed", error.Message));
                        lock (_gate)
                        {
                            session.StopRequested = true;
                            WinDivertShutdown(session.Handle, 1);
                        }
                    }
                    session.Relay.Failed += OnFailure;
                    dns.Failed += OnFailure;
                    session.Thread = new Thread(() => WorkerLoop(session)) { IsBackground = true, Name = "DpiWorker" };
                    _session = session;
                    session.Thread.Start();
                }
                catch (Exception ex)
                {
                    if (handle != IntPtr.Zero && handle != new IntPtr(-1)) WinDivertClose(handle);
                    _session = null;
                    dns?.StopAsync().GetAwaiter().GetResult();
                    upstream?.Dispose();
                    Log?.Invoke(Localization.T("GenericError", ex.Message));
                    return false;
                }
                Log?.Invoke(Localization.T("WinDivertStarted", ActiveDomainsCount));
                return true;
            }
        }

        public Task StopAsync() => Task.Run(Stop);

        public void Stop()
        {
            WorkerSession? session;
            bool shutdownFailed;
            lock (_gate)
            {
                session = _session;
                if (session == null) return;
                session.StopRequested = true;
                shutdownFailed = !session.HandleClosed && !WinDivertShutdown(session.Handle, 1);
                if (shutdownFailed)
                {
                    Log?.Invoke(Localization.T("GenericError", new Win32Exception(Marshal.GetLastWin32Error()).Message));
                }
            }
            try
            {
                try { session.Relay.StopAsync().GetAwaiter().GetResult(); }
                finally { session.Dns.StopAsync().GetAwaiter().GetResult(); }
            }
            finally
            {
                if (shutdownFailed)
                    lock (_gate)
                    {
                        if (!session.HandleClosed)
                        {
                            WinDivertClose(session.Handle);
                            session.HandleClosed = true;
                        }
                    }
                if (Thread.CurrentThread != session.Thread) session.Thread.Join();
            }
            Log?.Invoke(Localization.T("WinDivertStopped"));
        }

        private void WorkerLoop(WorkerSession session)
        {
            byte[] buffer = new byte[65535];
            try
            {
                while (!session.StopRequested)
                {
                    if (!WinDivertRecv(session.Handle, buffer, (uint)buffer.Length, out uint received, out var address))
                    {
                        if (session.StopRequested) break;
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    if (received == 0 || received > buffer.Length) continue;
                    var decision = session.Processor.Process(buffer.AsSpan(0, (int)received), address.Outbound);
                    if (decision.Action == PacketAction.Drop) continue;
                    if (decision.Action == PacketAction.DnsQuery)
                    {
                        session.Relay.Queue(buffer.AsSpan(0, (int)received).ToArray(), address, decision.TargetVersion);
                        continue;
                    }
                    if (decision.Action is PacketAction.Fragment or PacketAction.Modify)
                    {
                        var injections = PacketProcessor.BuildInjections(buffer.AsSpan(0, (int)received), decision);
                        bool modified = injections.Count > 1 || !injections[0].Packet.AsSpan().SequenceEqual(buffer.AsSpan(0, (int)received));
                        foreach (var injection in injections)
                            Send(session.Handle, injection.Packet, (uint)injection.Packet.Length, address, modified, injection.InvalidTcpChecksum, injection.InvalidUdpChecksum);
                        if (modified)
                        {
                            Interlocked.Increment(ref _bypassedPacketsCount);
                            _lastBypassedTarget = decision.Target;
                            lock (_gate) _lastBypassedTime = DateTime.Now;
                            long tick = Environment.TickCount64;
                            if (tick - session.LastPacketLogTick >= 1000)
                            {
                                session.LastPacketLogTick = tick;
                                Log?.Invoke(Localization.T("WinDivertActivity", BypassedPacketsCount));
                            }
                        }
                    }
                    else
                    {
                        Send(session.Handle, buffer, received, address, decision.Action != PacketAction.Pass);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!session.StopRequested) Log?.Invoke(Localization.T("WinDivertWorkerFailed", ex.Message));
            }
            finally
            {
                session.StopRequested = true;
                try
                {
                    try { session.Relay.StopAsync().GetAwaiter().GetResult(); }
                    finally { session.Dns.StopAsync().GetAwaiter().GetResult(); }
                }
                finally
                {
                    try { session.Upstream?.Dispose(); }
                    finally
                    {
                        lock (_gate)
                        {
                            if (!session.HandleClosed) WinDivertClose(session.Handle);
                            session.HandleClosed = true;
                            if (ReferenceEquals(_session, session)) _session = null;
                        }
                    }
                }
            }
        }

        internal static void PrepareChecksum(byte[] packet, ref WinDivertAddress address, bool invalidTcpChecksum = false, bool invalidUdpChecksum = false)
        {
            bool udpFragment = packet.Length >= 20 && packet[0] >> 4 == 4 && packet[9] == 17 &&
                (BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6)) & 0x3FFF) != 0;
            // Fragment checksums cover the complete UDP datagram, not individual IP fragments.
            if (!WinDivertHelperCalcChecksums(packet, (uint)packet.Length, ref address, udpFragment ? 16u : 0u))
                throw new InvalidOperationException("Packet checksum calculation failed.");
            if (udpFragment) address.Flags |= 1u << 23;
            if (invalidUdpChecksum)
            {
                if (!PacketProcessor.TryReadHeaders(packet, out int udpIpLength, out _) || packet[9] != 17)
                    throw new ArgumentException("Invalid fake UDP packet.", nameof(packet));
                ushort checksum = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(udpIpLength + 6));
                BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(udpIpLength + 6), (ushort)(checksum == 1 ? 2 : checksum ^ 1));
            }
            if (!invalidTcpChecksum) return;
            if (!PacketProcessor.TryReadHeaders(packet, out int ipLength, out _) || packet[9] != 6)
                throw new ArgumentException("Invalid fake TCP packet.", nameof(packet));
            packet[ipLength + 16] ^= 0x01;
        }

        private static void Send(IntPtr handle, byte[] packet, uint length, WinDivertAddress address, bool modified, bool invalidTcpChecksum = false, bool invalidUdpChecksum = false)
        {
            if (modified) PrepareChecksum(packet, ref address, invalidTcpChecksum, invalidUdpChecksum);
            if (!WinDivertSend(handle, packet, length, out uint sent, ref address))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (sent != length) throw new IOException("Incomplete packet injection.");
        }

        public void Dispose() => Stop();
    }
}
