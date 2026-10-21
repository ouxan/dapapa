using System.Threading;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Localization = dapapa.Services.Localization;

namespace dapapa
{
    public partial class App : Application
    {
        private const string MutexName = "dapapa_SingleInstance_Mutex";
        private const string ShowEventName = "dapapa_SingleInstance_ShowEvent";
        private const uint ASFW_ANY = unchecked((uint)-1);
        private const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(uint dwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static Mutex? _mutex;
        private static EventWaitHandle? _showEvent;
        private static RegisteredWaitHandle? _registeredWait;

        public static bool StartedFromWindowsStartup { get; private set; }
        internal static Action? ExitCleanup;

        internal static Exception? RunExitCleanup()
        {
            var cleanup = Interlocked.Exchange(ref ExitCleanup, null);
            try { cleanup?.Invoke(); return null; }
            catch (Exception ex) { return ex; }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            StartedFromWindowsStartup = e.Args.Contains("--tray");

            _mutex = new Mutex(true, MutexName, out bool isNew);
            if (!isNew)
            {
                if (StartedFromWindowsStartup) { Shutdown(); return; }
                try
                {
                    AllowSetForegroundWindow(ASFW_ANY);

                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existingEvent))
                    {
                        existingEvent.Set();
                        existingEvent.Dispose();
                    }
                    else
                    {
                        var current = System.Diagnostics.Process.GetCurrentProcess();
                        var existingProcess = System.Diagnostics.Process.GetProcessesByName(current.ProcessName)
                            .FirstOrDefault(p => p.Id != current.Id);
                        if (existingProcess != null && existingProcess.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(existingProcess.MainWindowHandle, SW_RESTORE);
                            SetForegroundWindow(existingProcess.MainWindowHandle);
                        }
                    }
                }
                catch
                {
                }

                Shutdown();
                return;
            }

            try
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                _registeredWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
                {
                    Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (Current.MainWindow is MainWindow mw)
                        {
                            mw.RestoreFromTray();
                        }
                    }));
                }, null, -1, false);
            }
            catch
            {
            }

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var cleanupError = RunExitCleanup();
                string message = Localization.T("UnexpectedErrorMessage", args.ExceptionObject);
                if (cleanupError != null) message += Environment.NewLine + Localization.T("ExitCleanupFailed", cleanupError.Message);
                MessageBox.Show(message,
                    Localization.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (_, args) =>
            {
                MessageBox.Show(Localization.T("UnexpectedErrorMessage", args.Exception.Message),
                    Localization.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                if (MainWindow == null || !MainWindow.IsLoaded)
                {
                    Shutdown(1);
                }
                else
                {
                    args.Handled = true;
                }
            };

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (RunExitCleanup() is Exception cleanupError)
                MessageBox.Show(Localization.T("ExitCleanupFailed", cleanupError.Message), Localization.T("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            try
            {
                _registeredWait?.Unregister(null);
                _showEvent?.Dispose();
                _mutex?.ReleaseMutex();
            }
            catch
            {
            }
            base.OnExit(e);
        }
    }
}
