using System.IO;
using System.Net;
using System.Text;

namespace dapapa.Services
{
    public static class HostsFileService
    {
        private static readonly HostsFileStore Store = new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts"), DnsCacheService.Flush);
        public static event Action<string>? Log;
        public static void RemoveEntries()
        {
            Store.EndSession();
            Log?.Invoke(Localization.T("LogHostsCleared"));
        }

    }

    internal sealed class HostsFileStore(string path, Action flush)
    {
        private const string StartTag = "# [dapapa-start]";
        private const string EndTag = "# [dapapa-end]";
        private readonly object _gate = new();
        private long _session;

        public long BeginSession() { lock (_gate) return ++_session; }

        public bool ApplyEntries(long session, IReadOnlyDictionary<string, string> entries)
        {
            lock (_gate)
            {
                if (session != _session) return false;
                WriteEntries(entries);
                return true;
            }
        }

        public void EndSession()
        {
            lock (_gate)
            {
                ++_session;
                WriteEntries(new Dictionary<string, string>());
            }
        }

        private void WriteEntries(IReadOnlyDictionary<string, string> entries)
        {
            var original = File.ReadAllLines(path).ToList();
            var clean = RemoveDapapaBlock(original);
            if (entries.Count > 0)
            {
                clean.Add(StartTag);
                foreach (var (host, ip) in entries) clean.Add($"{ip,-16} {host}");
                clean.Add(EndTag);
            }
            if (original.SequenceEqual(clean)) return;
            File.Copy(path, path + ".dapapa-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") +
                "-" + Guid.NewGuid().ToString("N")[..8] + ".bak", false);
            var attributes = File.GetAttributes(path);
            try
            {
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                File.WriteAllLines(path, clean, new UTF8Encoding(false));
                flush();
            }
            finally
            {
                if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes);
            }
        }

        private static List<string> RemoveDapapaBlock(List<string> lines)
        {
            var result = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.Equals(StartTag, StringComparison.OrdinalIgnoreCase))
                {
                    int end = lines.FindIndex(i + 1, line => line.Trim().Equals(EndTag, StringComparison.OrdinalIgnoreCase));
                    if (end >= 0) { i = end; continue; }
                }
                if (trimmed.StartsWith("# Discord Ek Domainler (dapapa)", StringComparison.OrdinalIgnoreCase))
                {
                    while (i + 1 < lines.Count)
                    {
                        string next = lines[i + 1].Trim();
                        int comment = next.IndexOf('#');
                        string text = comment >= 0 ? next[..comment] : next;
                        var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (fields.Length < 2 || !IPAddress.TryParse(fields[0], out _) ||
                            !fields.Skip(1).Any(IsLegacyHost)) break;
                        var retained = fields.Skip(1).Where(host => !IsLegacyHost(host)).ToArray();
                        if (retained.Length > 0)
                            result.Add(fields[0] + " " + string.Join(" ", retained) + (comment >= 0 ? " " + next[comment..] : ""));
                        i++;
                    }
                    continue;
                }
                result.Add(lines[i]);
            }
            return result;
        }

        private static bool IsLegacyHost(string host) => host.TrimEnd('.').ToLowerInvariant() is
            "discord.com" or "discord.gg" or "discord.new" or "cdn.discord.com" or "media.discordapp.net" or
            "discordapp.com" or "gateway.discord.gg" or "discord-attachments-uploads-prd.storage.googleapis.com";
    }
}
