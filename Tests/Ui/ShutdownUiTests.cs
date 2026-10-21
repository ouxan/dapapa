using System.IO;
using dapapa;

internal sealed partial class SelectionHarness
{
    public void VerifyShutdown()
    {
        _operationCts = new CancellationTokenSource();
        _winDivert.IsRunning = _userWantsConnected = _isConnected = true;
        _targetSession = 10;
        _winDivert.StopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CleanupTrace.Events.Clear();
        var stopping = StopProtectionAsync();
        if (stopping.IsCompleted || _userWantsConnected || !_operationCts.IsCancellationRequested || _targetSession != 0 ||
            !CleanupTrace.Events.SequenceEqual(["stop-begin"])) throw new Exception("DNS cleanup ran before the native session drained.");
        _winDivert.StopGate.TrySetResult(); Program.Wait(stopping); _winDivert.StopGate = null;
        if (_isConnected || !CleanupTrace.Events.SequenceEqual(["stop-begin", "stop-end", "hosts", "doh-cache", "windows-cache"]))
            throw new Exception("Normal disconnect did not flush both caches after stopping protection.");
        foreach (string failure in new[] { "hosts", "native", "flush" })
        {
            CleanupTrace.Events.Clear();
            HostsStub.Fail = failure == "hosts"; _winDivert.FailStop = failure == "native"; DnsCacheStub.Fail = failure == "flush";
            _userWantsConnected = _winDivert.IsRunning = _isConnected = true;
            try { Program.Wait(StopProtectionAsync()); throw new Exception("Cleanup failure was hidden."); }
            catch (IOException) { }
            if (_isConnected || !CleanupTrace.Events.Contains("doh-cache") || !CleanupTrace.Events.Contains("windows-cache"))
                throw new Exception("Cleanup error skipped cache cleanup or retained active UI state.");
        }
        HostsStub.Fail = _winDivert.FailStop = DnsCacheStub.Fail = false;
        CleanupTrace.Events.Clear();
        _userWantsConnected = true; _isConnected = true; _targetSession = 11; _winDivert.IsRunning = false;
        Program.Wait(PollStatusAsync());
        if (!_userWantsConnected || _isBusy || _isConnected || !CleanupTrace.Events.Contains("windows-cache"))
            throw new Exception("Unexpected worker exit skipped cleanup or disabled watchdog intent.");
        CleanupTrace.Events.Clear(); _winDivert.IsRunning = _userWantsConnected = true;
        App.ExitCleanup = CleanupProtectionOnExit;
        if (App.RunExitCleanup() != null || _winDivert.IsRunning || _userWantsConnected ||
            !CleanupTrace.Events.SequenceEqual(["stop-begin", "stop-end", "hosts", "doh-cache", "windows-cache"]))
            throw new Exception("Application exit fallback did not stop and clean protection.");
        App.RunExitCleanup();
        if (CleanupTrace.Events.Count != 5) throw new Exception("Exit fallback ran more than once.");
        Console.WriteLine("PASS: actual disconnect, worker-exit and application-exit cleanup drains native work before clearing hosts/DoH/Windows caches; failures remain visible and watchdog intent is preserved. OS calls are simulated.");
    }
}
