using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using Microsoft.Win32;

namespace dapapa.Services
{
    public static class StartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string TaskName = "dapapa";
        private static readonly SemaphoreSlim Gate = new(1, 1);
        internal readonly record struct CommandResult(int ExitCode, string Output, string Error);

        public static async Task SetEnabledAsync(bool enabled, CancellationToken ct = default)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await SetEnabledAsync(enabled, Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "dapapa.exe"),
                ExecuteAsync, () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                    key?.DeleteValue(TaskName, false);
                }, ct).ConfigureAwait(false);
            }
            finally { Gate.Release(); }
        }

        internal static async Task SetEnabledAsync(bool enabled, string exePath,
            Func<IReadOnlyList<string>, CancellationToken, Task<CommandResult>> execute, Action removeLegacy,
            CancellationToken ct = default)
        {
            var query = await execute(["/query", "/tn", TaskName, "/xml"], ct).ConfigureAwait(false);
            bool exists = query.ExitCode == 0;
            if (!exists)
            {
                var listing = await execute(["/query", "/fo", "CSV", "/nh"], ct).ConfigureAwait(false);
                RequireSuccess(listing);
                if (listing.Output.Split('\n').Any(line => line.TrimStart().StartsWith("\"\\" + TaskName + "\",", StringComparison.OrdinalIgnoreCase)))
                    RequireSuccess(query);
            }
            if (enabled)
            {
                if (!exists || !IsCurrentTask(query.Output, exePath))
                    RequireSuccess(await execute(["/create", "/tn", TaskName, "/tr", $"\"{exePath}\" --tray",
                        "/sc", "onlogon", "/rl", "highest", "/f"], ct).ConfigureAwait(false));
            }
            else if (exists)
                RequireSuccess(await execute(["/delete", "/tn", TaskName, "/f"], ct).ConfigureAwait(false));
            removeLegacy();
        }

        internal static bool IsCurrentTask(string xml, string exePath)
        {
            try
            {
                var task = XDocument.Parse(xml);
                XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
                return task.Descendants(ns + "Enabled").All(e => !e.Value.Equals("false", StringComparison.OrdinalIgnoreCase)) &&
                    task.Descendants(ns + "RunLevel").Any(e => e.Value == "HighestAvailable") &&
                    task.Descendants(ns + "LogonTrigger").Any() &&
                    task.Descendants(ns + "Exec").Any(e =>
                        string.Equals(e.Element(ns + "Command")?.Value.Trim('"'), exePath, StringComparison.OrdinalIgnoreCase) &&
                        e.Element(ns + "Arguments")?.Value.Trim() == "--tray");
            }
            catch (System.Xml.XmlException) { return false; }
        }

        private static void RequireSuccess(CommandResult result)
        {
            if (result.ExitCode != 0)
                throw new IOException($"Windows startup task failed ({result.ExitCode}): {result.Error.Trim()}");
        }

        private static async Task<CommandResult> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var info = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw new IOException("Unable to start schtasks.exe.");
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                try { await Task.WhenAll(output, error).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                if (ct.IsCancellationRequested) throw;
                throw new TimeoutException("Windows startup task timed out.");
            }
        }
    }
}
