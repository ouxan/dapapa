using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace dapapa.Services
{
    public sealed class TrafficStatisticsService
    {
        internal readonly record struct AdapterSample(string Id, string Name, long Received, long Sent, bool Up);
        private readonly Func<AdapterSample?> _read;
        private readonly TimeProvider _clock;
        private AdapterSample? _previous;
        private long _sampleTimestamp;
        private long? _connectionTimestamp;
        private string _adapterName = "";

        public TrafficStatisticsService() : this(ReadAdapter, TimeProvider.System) { }
        internal TrafficStatisticsService(Func<AdapterSample?> read, TimeProvider clock) { _read = read; _clock = clock; }
        public double DownloadSpeedBytesPerSec { get; private set; }
        public double UploadSpeedBytesPerSec { get; private set; }
        public long SessionDownloadedBytes { get; private set; }
        public long SessionUploadedBytes { get; private set; }
        public long SessionTotalBytes => SessionDownloadedBytes + SessionUploadedBytes;
        public bool IsAdapterUp { get; private set; }
        public string AdapterName => string.IsNullOrWhiteSpace(_adapterName) ? Localization.T("NetworkAdapter") : _adapterName;
        public TimeSpan Uptime => _connectionTimestamp is long start ? _clock.GetElapsedTime(start) : TimeSpan.Zero;

        [DllImport("iphlpapi.dll")]
        private static extern uint GetBestInterface(uint destination, out uint index);

        private static AdapterSample? ReadAdapter()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                NetworkInterface? adapter = null;
                if (GetBestInterface(0x01010101, out uint index) == 0)
                    adapter = interfaces.FirstOrDefault(nic => nic.OperationalStatus == OperationalStatus.Up &&
                        nic.GetIPProperties().GetIPv4Properties()?.Index == index);
                adapter ??= interfaces.FirstOrDefault(nic => nic.OperationalStatus == OperationalStatus.Up &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    nic.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork));
                if (adapter == null) return null;
                var stats = adapter.GetIPStatistics();
                return new(adapter.Id, adapter.Name, stats.BytesReceived, stats.BytesSent,
                    adapter.OperationalStatus == OperationalStatus.Up);
            }
            catch { return null; }
        }

        public void StartSession()
        {
            _connectionTimestamp = _clock.GetTimestamp();
            SessionDownloadedBytes = SessionUploadedBytes = 0;
            _previous = null;
            Update(false);
        }

        public void StopSession()
        {
            _connectionTimestamp = null;
            DownloadSpeedBytesPerSec = UploadSpeedBytesPerSec = 0;
        }

        public void Reset()
        {
            SessionDownloadedBytes = SessionUploadedBytes = 0;
            DownloadSpeedBytesPerSec = UploadSpeedBytesPerSec = 0;
            if (_connectionTimestamp != null) StartSession();
            else _previous = null;
        }

        public void Update(bool isConnected)
        {
            var current = _read();
            long timestamp = _clock.GetTimestamp();
            IsAdapterUp = current is { Up: true };
            if (current == null || !current.Value.Up)
            {
                _previous = null;
                DownloadSpeedBytesPerSec = UploadSpeedBytesPerSec = 0;
                return;
            }
            _adapterName = current.Value.Name;
            var sample = current.Value;
            if (_previous is not { } previous || previous.Id != sample.Id ||
                sample.Received < previous.Received || sample.Sent < previous.Sent)
            {
                _previous = sample;
                _sampleTimestamp = timestamp;
                DownloadSpeedBytesPerSec = UploadSpeedBytesPerSec = 0;
                return;
            }
            double elapsed = _clock.GetElapsedTime(_sampleTimestamp, timestamp).TotalSeconds;
            if (elapsed <= 0.4) return;
            long received = sample.Received - previous.Received, sent = sample.Sent - previous.Sent;
            DownloadSpeedBytesPerSec = received / elapsed;
            UploadSpeedBytesPerSec = sent / elapsed;
            if (isConnected)
            {
                SessionDownloadedBytes += received;
                SessionUploadedBytes += sent;
            }
            _previous = sample;
            _sampleTimestamp = timestamp;
        }
        public static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec < 1024) return bytesPerSec.ToString("0", Localization.Culture) + " B/s";
            if (bytesPerSec < 1024 * 1024) return (bytesPerSec / 1024.0).ToString("0.0", Localization.Culture) + " KB/s";
            return (bytesPerSec / (1024.0 * 1024.0)).ToString("0.00", Localization.Culture) + " MB/s";
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(Localization.Culture) + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0", Localization.Culture) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("0.0", Localization.Culture) + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.00", Localization.Culture) + " GB";
        }

        public static string FormatDuration(TimeSpan span)
        {
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}"
                : $"{span.Minutes:D2}:{span.Seconds:D2}";
        }

    }
}
