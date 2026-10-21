using System.Net;
using System.Net.Sockets;

namespace dapapa.Services
{
    internal sealed class DnsPacketRelay(IPEndPoint endpoint, PacketProcessor processor, Func<byte[], WinDivertAddress, bool> send)
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation = new();
        private readonly HashSet<Task> _pending = new();
        private bool _stopping;
        private Task? _stop;
        public event Action<Exception>? Failed;

        public void Queue(byte[] packet, WinDivertAddress address, long targetVersion)
        {
            lock (_gate)
            {
                if (_stopping) return;
                if (_pending.Count >= 72)
                {
                    PacketProcessor.TryReadHeaders(packet, out int ipLength, out _);
                    SendReply(packet, DnsWire.ErrorResponse(packet.AsSpan(ipLength + 8), 2), address, targetVersion);
                    return;
                }
                var task = Task.Run(() => ForwardAsync(packet, address, targetVersion));
                _pending.Add(task);
                _ = task.ContinueWith(completed => { lock (_gate) _pending.Remove(completed); }, TaskScheduler.Default);
            }
        }

        private async Task ForwardAsync(byte[] packet, WinDivertAddress address, long targetVersion)
        {
            PacketProcessor.TryReadHeaders(packet, out int ipLength, out _);
            byte[] query = packet[(ipLength + 8)..];
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                byte[] reply;
                try
                {
                    using var socket = new UdpClient(AddressFamily.InterNetwork);
                    socket.Connect(endpoint);
                    await socket.SendAsync(query, timeout.Token).ConfigureAwait(false);
                    var received = await socket.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    if (!DnsWire.IsResponseFor(query, received.Buffer)) throw new InvalidOperationException("Mismatched local DNS response.");
                    reply = received.Buffer;
                }
                catch (Exception ex) when (!_cancellation.IsCancellationRequested && ex is OperationCanceledException or SocketException or InvalidOperationException)
                {
                    reply = DnsWire.ErrorResponse(query, 2);
                }
                SendReply(packet, reply, address, targetVersion);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
            catch (Exception ex) { Failed?.Invoke(ex); }
        }

        private void SendReply(byte[] original, byte[] reply, WinDivertAddress address, long targetVersion)
        {
            byte[] packet = PacketProcessor.BuildDnsReply(original, reply);
            address.Flags &= ~(1u << 17);
            lock (_gate)
            {
                if (_stopping) return;
                PacketProcessor.TryReadHeaders(original, out int ipLength, out _);
                byte[] query = original[(ipLength + 8)..];
                processor.TrySendDnsResponse(query, targetVersion, () =>
                {
                    if (!send(packet, address)) return false;
                    processor.LearnDnsResponse(query, packet[(ipLength + 8)..], targetVersion);
                    return true;
                });
            }
        }

        public Task StopAsync()
        {
            lock (_gate)
            {
                if (_stop != null) return _stop;
                _stopping = true;
                _cancellation.Cancel();
                var pending = _pending.ToArray();
                return _stop = Task.WhenAll(pending);
            }
        }
    }
}
