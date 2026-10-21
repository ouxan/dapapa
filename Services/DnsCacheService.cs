using System.Runtime.InteropServices;
using System.ComponentModel;

namespace dapapa.Services
{
    public static class DnsCacheService
    {
        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", SetLastError = true)]
        private static extern bool DnsFlushResolverCache();

        public static void Flush()
        {
            if (!DnsFlushResolverCache()) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
