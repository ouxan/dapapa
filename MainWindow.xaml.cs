using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using dapapa.Models;
using dapapa.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using MessageBox = System.Windows.MessageBox;
using Localization = dapapa.Services.Localization;

namespace dapapa
{
    public partial class MainWindow : Window
    {

        private readonly WinDivertAntiCensorshipService _winDivert = new();
        private readonly SettingsService _settingsService = new();
        private readonly RelatedDomainDiscoveryService _discoveryService = new();
        private readonly Dictionary<string, Dialogs.RelatedDomainsDialog> _discoveryDialogs = new();
        private readonly TrayIconManager _tray = new();
        private readonly string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dapapa", "dapapa.log");
        private readonly ConcurrentQueue<string> _serviceLogs = new();
        private readonly LogFileWriter _logWriter;
        private readonly DispatcherTimer _logTimer;

        private AppSettings _settings = new();
        private bool _isLoadingSettings = false;

        private List<ProtectedApp> _allApps = new();
        private Dictionary<string, bool> _savedTargetSelection = new();
        private readonly ObservableCollection<ProtectedApp> _filteredApps = new();
        private CancellationTokenSource? _operationCts;
        private bool _isConnected = false;
        private bool _isBusy = false;
        private bool _reallyExit = false;

        private readonly SemaphoreSlim _targetRefreshGate = new(1, 1);
        private long _targetSession;
        private bool _isRefreshingTargets;
        private DateTimeOffset _nextDnsRefresh = DateTimeOffset.MaxValue;
        private readonly DispatcherTimer _statusTimer;

        private readonly LatencyAndEndpointService _latencyService = new();
        private readonly DispatcherTimer _latencyTimer;
        private bool _isMeasuringPing = false;
        private bool _isSyncingIps;
        private long? _lastDnsLatency;
        private string? _busyStatusKey;

        private readonly TrafficStatisticsService _trafficService = new();
        private readonly DispatcherTimer _statsTimer;
        private bool _userWantsConnected = false;
        private DateTime? _watchdogDropSince = null;
        private DateTime _lastWatchdogAttemptTime = DateTime.MinValue;
        private DateTime _watchdogSuppressedUntil = DateTime.MinValue;

        private void SuppressWatchdog(TimeSpan duration)
        {
            _watchdogSuppressedUntil = DateTime.Now + duration;
            _watchdogDropSince = null;
        }

        private readonly List<double> _dlHistory = new();
        private readonly List<double> _ulHistory = new();
        private const int ChartHistoryLimit = 30;

        public MainWindow()
        {
            _logWriter = new LogFileWriter(_logPath);
            App.ExitCleanup = CleanupProtectionOnExit;
            _isLoadingSettings = true;
            try
            {
                InitializeComponent();
            }
            finally
            {
                _isLoadingSettings = false;
            }

            AppsItemsControl.ItemsSource = _filteredApps;

            DomainDnsResolver.Log += OnServiceLog;
            _winDivert.Log += OnServiceLog;
            _settingsService.Log += OnServiceLog;
            HostsFileService.Log += OnServiceLog;
            _tray.ShowRequested += () => Dispatcher.Invoke(RestoreFromTray);
            _tray.ConnectRequested += () => Dispatcher.Invoke(() => _ = StartConnectionFlowAsync());
            _tray.DisconnectRequested += () => Dispatcher.Invoke(() => _ = StopConnectionFlowAsync());
            _tray.ExitRequested += () => Dispatcher.Invoke(() => { _reallyExit = true; Close(); });

            LoadSettingsIntoUi();

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _statusTimer.Tick += async (_, _) =>
            {
                await PollStatusAsync();
                await CheckWatchdogAsync();
                await RefreshExpiredTargetsAsync();
            };

            _latencyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _latencyTimer.Tick += async (_, _) => await UpdatePingAsync();
            _latencyTimer.Start();

            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statsTimer.Tick += (_, _) => UpdateTrafficStats();
            _statsTimer.Start();
            _logTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _logTimer.Tick += (_, _) => DrainServiceLogs();
            _logTimer.Start();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AppendLog(Localization.T("RuntimeVersion", typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown", Environment.ProcessPath ?? "-"));
            if (_settings.StartMinimizedToTray || App.StartedFromWindowsStartup)
            {
                WindowState = WindowState.Minimized;
                Hide();
            }
            try { await StartupService.SetEnabledAsync(_settings.RunOnWindowsStartup); }
            catch (Exception ex) { AppendLog(Localization.T("GenericError", ex.Message)); }
            if (_settings.AutoConnectOnLaunch && _settings.GetEffectiveDomains().Count > 0)
                await StartConnectionFlowAsync();
            else
            {
                try { HostsFileService.RemoveEntries(); }
                catch (Exception ex) { AppendLog(Localization.T("GenericError", ex.Message)); }
                SetConnectionVisualState(false);
            }
            _statusTimer.Start();
        }

        private async Task PollStatusAsync()
        {
            if (!_isBusy && _userWantsConnected && !_winDivert.IsRunning && _targetSession != 0)
            {
                _targetSession = 0;
                _operationCts?.Cancel();
                _nextDnsRefresh = DateTimeOffset.MaxValue;
                _isBusy = true;
                try
                {
                    try { await _winDivert.StopAsync(); }
                    finally { CleanupDnsAfterStop(); }
                }
                catch (Exception ex) { AppendLog(Localization.T("GenericError", ex.Message)); }
                finally { _isBusy = false; }
            }
            if (!_isBusy && _userWantsConnected && _winDivert.IsRunning != _isConnected)
            {
                SetConnectionVisualState(_winDivert.IsRunning);
                AppendLog(Localization.T(_winDivert.IsRunning ? "LogConnectionRestored" : "LogConnectionLost"));
            }
        }

        private bool _closingAfterDisconnect = false;

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_reallyExit && _settings.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                Hide();
                _tray.ShowBalloon(Localization.T("AppName"), Localization.T("StillRunningInTray"));
                return;
            }
            if (!_closingAfterDisconnect)
            {
                e.Cancel = true;
                _closingAfterDisconnect = true;
                _reallyExit = true;
                _statusTimer.Stop();
                var discoveryDialogs = _discoveryDialogs.Values.ToArray();
                foreach (var dialog in discoveryDialogs) dialog.Close();
                await Task.WhenAll(discoveryDialogs.Select(dialog => dialog.Completion));
                try { await StopProtectionAsync(); }
                catch (Exception ex) { AppendLog(Localization.T("GenericError", ex.Message)); }
                Close();
                return;
            }
            _winDivert.Dispose();
            _latencyService.Dispose();
            _discoveryService.Dispose();
            _statusTimer.Stop();
            _latencyTimer.Stop();
            _statsTimer.Stop();
            _tray.Dispose();
            DomainDnsResolver.Log -= OnServiceLog;
            _winDivert.Log -= OnServiceLog;
            _settingsService.Log -= OnServiceLog;
            HostsFileService.Log -= OnServiceLog;
            _logTimer.Stop();
            DrainServiceLogs();
            await _logWriter.DisposeAsync();
            System.Windows.Application.Current.Shutdown();
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, MainTabControl)) return;
            if (e.AddedItems.Count == 0) return;
            if (e.AddedItems[0] is not TabItem tabItem) return;
            if (tabItem.Content is not UIElement content) return;

            if (ReferenceEquals(tabItem, StatsTabItem))
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (ReferenceEquals(MainTabControl.SelectedItem, StatsTabItem))
                        StatsScrollViewer.ScrollToTop();
                }));

            content.Opacity = 0;
            var translate = new TranslateTransform(0, 8);
            content.RenderTransform = translate;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideUp = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            content.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            translate.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            RootBorder.CornerRadius = WindowState == WindowState.Maximized ? new CornerRadius(0) : new CornerRadius(12);

            if (WindowState == WindowState.Minimized && _settings.MinimizeToTrayOnClose)
            {
                Hide();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) != null)
                return;

            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                if (e.ClickCount == 2)
                {
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    return;
                }
                DragMove();
            }
        }

        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private const int SW_RESTORE = 9;
        private const uint WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const int SM_CXICON = 11;
        private const int SM_CYICON = 12;
        private const int SM_CXSMICON = 49;
        private const int SM_CYSMICON = 50;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int smIndex);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private IntPtr _hIconBig = IntPtr.Zero;
        private IntPtr _hIconSmall = IntPtr.Zero;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            SetupWindowAndTaskbarIcons();
        }

        private void SetupWindowAndTaskbarIcons()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");

                var iconUri = new Uri("pack://application:,,,/Resources/app.ico");
                var decoder = BitmapDecoder.Create(iconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var bestFrame = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
                if (bestFrame != null)
                {
                    this.Icon = bestFrame;
                }

                if (File.Exists(iconPath))
                {
                    int cxBig = GetSystemMetrics(SM_CXICON);
                    int cyBig = GetSystemMetrics(SM_CYICON);
                    int cxSmall = GetSystemMetrics(SM_CXSMICON);
                    int cySmall = GetSystemMetrics(SM_CYSMICON);

                    _hIconBig = LoadImage(IntPtr.Zero, iconPath, 1 /* IMAGE_ICON */, cxBig, cyBig, 0x00000010 /* LR_LOADFROMFILE */);
                    _hIconSmall = LoadImage(IntPtr.Zero, iconPath, 1 /* IMAGE_ICON */, cxSmall, cySmall, 0x00000010 /* LR_LOADFROMFILE */);

                    if (_hIconBig != IntPtr.Zero)
                        SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, _hIconBig);
                    if (_hIconSmall != IntPtr.Zero)
                        SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, _hIconSmall);
                }
            }
            catch
            {
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_hIconBig != IntPtr.Zero) { DestroyIcon(_hIconBig); _hIconBig = IntPtr.Zero; }
            if (_hIconSmall != IntPtr.Zero) { DestroyIcon(_hIconSmall); _hIconSmall = IntPtr.Zero; }
        }

        public void RestoreFromTray()
        {
            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();

            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                ShowWindow(helper.Handle, SW_RESTORE);
                SetForegroundWindow(helper.Handle);
            }
        }

        private async void ToggleConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;

            if (_isConnected || _userWantsConnected)
                await StopConnectionFlowAsync();
            else
                await StartConnectionFlowAsync();
        }

        private async Task StartConnectionFlowAsync()
        {
            if (_isBusy) return;
            if (_settings.GetEffectiveDomains().Count == 0)
            {
                AppendLog(Localization.T("NoActiveTargets"));
                return;
            }
            _isBusy = true;
            SetBusyUi(true, "Connecting");
            SuppressWatchdog(TimeSpan.FromSeconds(20));
            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            _userWantsConnected = true;
            _targetSession = NextTargetSession();
            try
            {
                HostsFileService.RemoveEntries();
                if (!_winDivert.Start(_settings.GetEffectiveDomains(), [], _settings.GetDohEndpoint(), _settings.GetDpiOptions()))
                    throw new InvalidOperationException(Localization.T("ProtectionStartFailed"));
                var result = await RefreshTargetsAsync(false, _operationCts.Token);
                if (!result.Applied || !_winDivert.IsRunning)
                    throw new InvalidOperationException(Localization.T("ProtectionStartFailed"));
                SetConnectionVisualState(true);
                SuppressWatchdog(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                AppendLog(Localization.T("GenericError", ex.Message));
                try { await StopProtectionAsync(); }
                catch (Exception cleanupError) { AppendLog(Localization.T("GenericError", cleanupError.Message)); }
            }
            finally
            {
                _isBusy = false;
                SetBusyUi(false, null);
            }
        }

        private async Task StopProtectionAsync()
        {
            _userWantsConnected = false;
            _watchdogDropSince = null;
            _nextDnsRefresh = DateTimeOffset.MaxValue;
            _operationCts?.Cancel();
            _targetSession = 0;
            try { await _winDivert.StopAsync(); }
            finally
            {
                try { CleanupDnsAfterStop(); }
                finally { SetConnectionVisualState(false); }
            }
        }

        private void CleanupDnsAfterStop()
        {
            try { HostsFileService.RemoveEntries(); }
            finally
            {
                try { DomainDnsResolver.ClearCache(); }
                finally { DnsCacheService.Flush(); }
            }
        }

        internal void CleanupProtectionOnExit()
        {
            _userWantsConnected = false;
            _operationCts?.Cancel();
            _targetSession = 0;
            _nextDnsRefresh = DateTimeOffset.MaxValue;
            try { _winDivert.Stop(); }
            finally { CleanupDnsAfterStop(); }
        }

        private async Task StopConnectionFlowAsync()
        {
            if (_isBusy) return;
            _isBusy = true;
            SetBusyUi(true, "Disconnecting");
            try { await StopProtectionAsync(); }
            catch (Exception ex) { AppendLog(Localization.T("GenericError", ex.Message)); }
            finally
            {
                _isBusy = false;
                SetBusyUi(false, null);
            }
        }

        private async Task<(int Services, int Ips, int Failed, bool Applied)> RefreshTargetsAsync(bool force, CancellationToken ct)
        {
            long session = _targetSession;
            var domains = _settings.GetEffectiveDomains();
            await _targetRefreshGate.WaitAsync(ct);
            _isRefreshingTargets = true;
            try
            {
                if (session != _targetSession || !_userWantsConnected || !_winDivert.IsRunning)
                    return (0, 0, 0, false);
                var resolved = await DomainDnsResolver.ResolveAllAsync(domains, force, ct);
                ct.ThrowIfCancellationRequested();
                if (session != _targetSession || !_userWantsConnected || !_winDivert.IsRunning)
                    return (0, 0, 0, false);
                var ips = resolved.SelectMany(pair => pair.Value.Select(ip =>
                    new ResolvedTargetIp(ip, DomainDnsResolver.GetExpiryUtc(pair.Key)))).ToArray();
                _winDivert.UpdateTargetDomains(domains, resolvedIps: ips);
                if (force) DnsCacheService.Flush();
                _nextDnsRefresh = DomainDnsResolver.GetNextRefreshUtc(domains);
                if (_nextDnsRefresh < DateTimeOffset.UtcNow.AddSeconds(5)) _nextDnsRefresh = DateTimeOffset.UtcNow.AddSeconds(5);
                int services = _settings.Apps.Count(a => a.Enabled && a.GetDomains().Any(d =>
                    resolved.TryGetValue(d, out var ips) && ips.Count > 0));
                return (services, resolved.Values.Sum(ips => ips.Count), resolved.Count(pair => pair.Value.Count == 0), true);
            }
            finally
            {
                _isRefreshingTargets = false;
                _targetRefreshGate.Release();
            }
        }

        private async Task RefreshExpiredTargetsAsync()
        {
            if (_isBusy || _isRefreshingTargets || !_userWantsConnected || !_winDivert.IsRunning ||
                DateTimeOffset.UtcNow < _nextDnsRefresh) return;
            try { await RefreshTargetsAsync(false, _operationCts?.Token ?? CancellationToken.None); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _nextDnsRefresh = DateTimeOffset.UtcNow.AddSeconds(30);
                AppendLog(Localization.T("GenericError", ex.Message));
            }
        }

        private void SetBusyUi(bool busy, string? statusKey)
        {
            _busyStatusKey = busy ? statusKey ?? "Preparing" : null;
            ToggleConnectionButton.IsEnabled = !busy;
            DohProviderComboBox.IsEnabled = !busy;
            if (busy)
            {
                StatusText.Text = Localization.T(_busyStatusKey!);
                StatusSubText.Text = Localization.T("PleaseWait");
                AnimateBrushColor(StatusDotBrush, (Color)FindResource("AccentColor"));
                AnimateBrushColor(StatusHaloBrush, (Color)FindResource("AccentColor"));
                AnimateBrushColor(StatusCardBorderBrush, (Color)FindResource("AccentColor"));
                StatusDot.Effect = (System.Windows.Media.Effects.Effect)FindResource("AccentGlowEffect");
                SetDotPulse(DotPulseMode.Busy);
            }
            _tray.ShowBalloon(Localization.T("AppName"), Localization.T(_busyStatusKey ?? "OperationDone"));
        }

        private void SetConnectionVisualState(bool connected)
        {
            bool changed = _isConnected != connected;
            _isConnected = connected;
            var targetColor = (Color)FindResource(connected ? "SuccessColor" : "DangerColor");
            var stateGlow = (System.Windows.Media.Effects.Effect)FindResource(connected ? "SuccessGlowEffect" : "DangerGlowEffect");

            AnimateBrushColor(StatusDotBrush, targetColor);
            AnimateBrushColor(StatusHaloBrush, targetColor);
            AnimateBrushColor(StatusCardBorderBrush, targetColor);
            StatusDot.Effect = stateGlow;
            StatusText.Text = connected ? Localization.T("ConnectedStatus") : Localization.T("NotConnected");
            StatusSubText.Text = connected
                ? Localization.T(_settings.GetDpiOptions().HasTrafficMethods ? "ConnectedHint" : "DnsOnlyHint")
                : Localization.T("StartHint");
            ToggleConnectionButton.Content = connected ? Localization.T("DisconnectButton") : Localization.T("ConnectButton");
            ToggleConnectionButton.Style = connected
                ? (Style)FindResource("DangerButton")
                : (Style)FindResource("AccentButton");

            SetDotPulse(connected ? DotPulseMode.Idle : DotPulseMode.None);
            _tray.SetConnectedState(connected);

            if (connected)
            {
                if (changed) _trafficService.StartSession();
                SessionStatusValueText.Text = Localization.T("ProtectionActive");
                SessionStatusDot.Fill = (SolidColorBrush)FindResource("SuccessBrush");
                StatsLiveDot.Fill = (SolidColorBrush)FindResource("SuccessBrush");
                StatsLiveText.Text = Localization.T("StatsLive");
                UpdateProtectedTargetsText();
                _ = UpdatePingAsync();
            }
            else
            {
                _trafficService.StopSession();
                SessionStatusValueText.Text = Localization.T("ProtectionInactive");
                SessionStatusDot.Fill = (SolidColorBrush)FindResource("DangerBrush");
                StatsLiveDot.Fill = (SolidColorBrush)FindResource("TextMutedBrush");
                StatsLiveText.Text = Localization.T("StatsIdle");
                _lastDnsLatency = null;
                PingValueText.Text = "-- ms";
                PingBadge.Visibility = Visibility.Collapsed;
                LatencyIndicatorDot.Fill = (SolidColorBrush)FindResource("TextMutedBrush");
                DownloadSpeedValueText.Text = Localization.T("IdleSpeed", 0);
                UploadSpeedValueText.Text = Localization.T("IdleSpeed", 0);
            }
        }

        private enum DotPulseMode { None, Idle, Busy }

        private void SetDotPulse(DotPulseMode mode)
        {
            var idle = (Storyboard)FindResource("IdlePulseStoryboard");
            var busy = (Storyboard)FindResource("BusyPulseStoryboard");

            idle.Stop(this);
            busy.Stop(this);
            StatusDot.Opacity = 1.0;
            StatusHalo.Opacity = 0.35;
            StatusHaloScale.ScaleX = 1.0;
            StatusHaloScale.ScaleY = 1.0;

            switch (mode)
            {
                case DotPulseMode.Idle:
                    idle.Begin(this, true);
                    break;
                case DotPulseMode.Busy:
                    busy.Begin(this, true);
                    break;
            }
        }

        private static void AnimateBrushColor(SolidColorBrush brush, Color to)
        {
            var animation = new ColorAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        private void OnServiceLog(string line)
        {
            string entry = _logWriter.Write(line);
            if (_serviceLogs.Count < 512) _serviceLogs.Enqueue(entry);
        }

        private void DrainServiceLogs()
        {
            for (int i = 0; i < 64 && _serviceLogs.TryDequeue(out var entry); i++) AddLogEntry(entry);
        }

        private void AppendLog(string line) => AddLogEntry(_logWriter.Write(line));

        private void AddLogEntry(string entry)
        {
            LogListBox.Items.Add(entry);
            if (LogListBox.Items.Count > 500)
                LogListBox.Items.RemoveAt(0);
        }

        private void LoadSettingsIntoUi()
        {
            _isLoadingSettings = true;
            try
            {
                _settings = _settingsService.Load();

                Localization.Current = string.Equals(_settings.Language, "tr", StringComparison.OrdinalIgnoreCase)
                    ? AppLanguage.Turkish
                    : AppLanguage.English;

                LanguageComboBox.SelectionChanged -= LanguageComboBox_SelectionChanged;
                LanguageComboBox.SelectedIndex = Localization.Current == AppLanguage.Turkish ? 1 : 0;
                LanguageComboBox.SelectionChanged += LanguageComboBox_SelectionChanged;

                RunOnStartupCheckBox.IsChecked = _settings.RunOnWindowsStartup;
                StartMinimizedCheckBox.IsChecked = _settings.StartMinimizedToTray;
                MinimizeToTrayCheckBox.IsChecked = _settings.MinimizeToTrayOnClose;
                AutoConnectCheckBox.IsChecked = _settings.AutoConnectOnLaunch;
                ConnectionWatchdogCheckBox.IsChecked = _settings.EnableConnectionWatchdog;
                LoadDpiSettingsIntoUi();
                LoadDohSettingsIntoUi();

                _allApps = _settings.Apps;
                _savedTargetSelection = _allApps.ToDictionary(a => a.Id, a => a.Enabled);
                ApplyLanguage();
            }
            finally
            {
                _isLoadingSettings = false;
            }
            FilterCatalog();
        }

        private void ApplyLanguage()
        {
            ApplyDohLanguage();
            SubtitleText.Text = Localization.T("Subtitle");
            HomeTabItem.ToolTip = Localization.T("TabHome");
            StatsTabItem.ToolTip = Localization.T("TabStats");
            SettingsTabItem.ToolTip = Localization.T("TabSettings");

            StatsSectionTitleText.Text = Localization.T("StatsSectionTitle");
            StatsDescriptionText.Text = Localization.T("StatsDescription");
            DownloadSpeedLabelText.Text = Localization.T("DownloadSpeed");
            CurrentDownloadSubtext.Text = Localization.T("CurrentDownload");
            UploadSpeedLabelText.Text = Localization.T("UploadSpeed");
            CurrentUploadSubtext.Text = Localization.T("CurrentUpload");
            DataCounterHeaderText.Text = Localization.T("DataCounterSection");
            TotalTransferredLabelText.Text = Localization.T("TotalTransferred");
            ReceivedLabelText.Text = Localization.T("ReceivedLabel");
            SentLabelText.Text = Localization.T("SentLabel");
            SessionInfoHeaderText.Text = Localization.T("SessionInfoSection");
            SessionStatusLabelText.Text = Localization.T("SessionStatusLabel");
            SessionUptimeLabelText.Text = Localization.T("SessionUptimeLabel");
            AdapterLabelText.Text = Localization.T("AdapterLabel");
            ResetStatsButtonText.Text = Localization.T("ResetStatsButton");
            ChartHeaderText.Text = Localization.T("ChartSectionTitle");
            ChartDownloadLegendText.Text = Localization.T("ChartDownloadLegend");
            ChartUploadLegendText.Text = Localization.T("ChartUploadLegend");

            if (DpiStatsHeaderText != null) DpiStatsHeaderText.Text = Localization.T("DpiStatsHeader");
            if (BypassedPacketsLabelText != null) BypassedPacketsLabelText.Text = Localization.T("BypassedPacketsLabel");
            if (BypassedPacketsSubtext != null) BypassedPacketsSubtext.Text = Localization.T("BypassedPacketsSubtext");
            if (SafeDnsLabelText != null) SafeDnsLabelText.Text = Localization.T("SafeDnsLabel");
            if (SafeDnsSubtext != null) SafeDnsSubtext.Text = Localization.T("SafeDnsSubtext");
            if (ProtectedTargetsLabelText != null) ProtectedTargetsLabelText.Text = Localization.T("ProtectedTargetsLabel");
            if (SessionStatusValueText != null)
                SessionStatusValueText.Text = _isConnected ? Localization.T("ProtectionActive") : Localization.T("ProtectionInactive");
            UpdateProtectedTargetsText();

            StatusText.Text = _isConnected ? Localization.T("ConnectedStatus") : Localization.T("NotConnected");
            StatusSubText.Text = _isConnected ? Localization.T(_settings.GetDpiOptions().HasTrafficMethods ? "ConnectedHint" : "DnsOnlyHint") : Localization.T("StartHint");
            ToggleConnectionButton.Content = _isConnected ? Localization.T("DisconnectButton") : Localization.T("ConnectButton");

            LogHeaderText.Text = Localization.T("LogHeader");
            GeneralHeaderText.Text = Localization.T("General");
            RunOnStartupCheckBox.Content = Localization.T("RunOnStartup");
            StartMinimizedCheckBox.Content = Localization.T("StartMinimized");
            MinimizeToTrayCheckBox.Content = Localization.T("MinimizeOnClose");
            AutoConnectCheckBox.Content = Localization.T("AutoConnectOnLaunch");
            ConnectionWatchdogCheckBox.Content = Localization.T("ConnectionWatchdog");
            LanguageLabelText.Text = Localization.T("Language");
            ApplyDpiLanguage();

            TrafficHeaderText.Text = Localization.T("MyTargets");
            CatalogSearchPlaceholder.Text = Localization.T("SearchPlaceholder");
            AddCustomAppButton.Content = Localization.T("AddCustomApp");
            SyncIpsButtonText.Text = Localization.T(_isSyncingIps ? "SyncingIps" : "SyncIps");
            SyncIpsButton.ToolTip = Localization.T("SyncIpsTooltip");
            NoResultsText.Text = Localization.T("NoResultsFound");
            ClearSearchButton.ToolTip = Localization.T("ClearSearch");

            if (_allApps != null)
            {
                foreach (var app in _allApps)
                {
                    app.RefreshLocalization();
                }
            }

            NoteText.Text = Localization.T("Note");
            SaveSettingsButtonText.Content = Localization.T("SaveSettings");

            FilterCatalog();
            UpdateActiveServicesCount();
            UpdateStatisticsLabels();
            _tray.RefreshTexts();

            LatencyHeaderText.Text = Localization.T("LatencySectionTitle");
            UpdatePingTargetLabel();

            if (SessionStatusValueText != null)
                SessionStatusValueText.Text = _isConnected ? Localization.T("ProtectionActive") : Localization.T("ProtectionInactive");
            if (StatsLiveText != null)
                StatsLiveText.Text = _isConnected ? Localization.T("StatsLive") : Localization.T("StatsIdle");
            if (PingLiveBadgeText != null)
                PingLiveBadgeText.Text = Localization.T("StatsLive");
            UpdateWaveformChart();

            if (_isBusy && _busyStatusKey != null)
            {
                StatusText.Text = Localization.T(_busyStatusKey);
                StatusSubText.Text = Localization.T("PleaseWait");
            }
            else if (_watchdogDropSince != null)
            {
                StatusText.Text = Localization.T("WatchdogReconnecting");
                StatusSubText.Text = Localization.T("WatchdogReconnectingHint");
            }

            if (_isConnected)
            {
                if (_lastDnsLatency is long latency)
                {
                    PingValueText.Text = latency >= 0 ? Localization.T("PingUnit", latency) : Localization.T("DohProbeFailed");
                    if (latency >= 0)
                        PingQualityText.Text = Localization.T(latency < 60 ? "PingGood" : latency < 120 ? "PingFair" : "PingPoor");
                }
                _ = UpdatePingAsync();
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings || _settings == null) return;
            if (_isBusy)
            {
                _isLoadingSettings = true;
                try { LanguageComboBox.SelectedIndex = _settings.Language == "tr" ? 1 : 0; }
                finally { _isLoadingSettings = false; }
                return;
            }
            var candidate = _settings.Copy();
            candidate.Language = LanguageComboBox.SelectedIndex == 1 ? "tr" : "en";
            try { _settingsService.Save(candidate); }
            catch (Exception ex)
            {
                _isLoadingSettings = true;
                try { LanguageComboBox.SelectedIndex = _settings.Language == "tr" ? 1 : 0; }
                finally { _isLoadingSettings = false; }
                AppendLog(Localization.T("SettingsSaveError", ex.Message));
                return;
            }
            _settings.Language = candidate.Language;
            Localization.Current = candidate.Language == "tr" ? AppLanguage.Turkish : AppLanguage.English;
            ApplyLanguage();
        }

        private async void SettingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || _settings == null ||
                RunOnStartupCheckBox == null || StartMinimizedCheckBox == null || MinimizeToTrayCheckBox == null ||
                AutoConnectCheckBox == null || ConnectionWatchdogCheckBox == null) return;
            if (_isBusy) { RestoreGeneralSettingsUi(); return; }
            var previous = _settings.Copy();
            var candidate = _settings.Copy();
            candidate.RunOnWindowsStartup = RunOnStartupCheckBox.IsChecked == true;
            candidate.StartMinimizedToTray = StartMinimizedCheckBox.IsChecked == true;
            candidate.MinimizeToTrayOnClose = MinimizeToTrayCheckBox.IsChecked == true;
            candidate.AutoConnectOnLaunch = AutoConnectCheckBox.IsChecked == true;
            candidate.EnableConnectionWatchdog = ConnectionWatchdogCheckBox.IsChecked == true;
            bool startupChanged = previous.RunOnWindowsStartup != candidate.RunOnWindowsStartup;
            bool saved = false;
            _isBusy = true;
            try
            {
                _settingsService.Save(candidate);
                saved = true;
                if (startupChanged) await StartupService.SetEnabledAsync(candidate.RunOnWindowsStartup);
                _settings.RunOnWindowsStartup = candidate.RunOnWindowsStartup;
                _settings.StartMinimizedToTray = candidate.StartMinimizedToTray;
                _settings.MinimizeToTrayOnClose = candidate.MinimizeToTrayOnClose;
                _settings.AutoConnectOnLaunch = candidate.AutoConnectOnLaunch;
                _settings.EnableConnectionWatchdog = candidate.EnableConnectionWatchdog;
                if (!_settings.EnableConnectionWatchdog) _watchdogDropSince = null;
            }
            catch (Exception ex)
            {
                if (saved)
                {
                    try { _settingsService.Save(_settings); }
                    catch (Exception rollback) { AppendLog(Localization.T("SettingsSaveError", rollback.Message)); }
                    if (startupChanged)
                        try { await StartupService.SetEnabledAsync(previous.RunOnWindowsStartup); }
                        catch (Exception rollback) { AppendLog(Localization.T("GenericError", rollback.Message)); }
                }
                AppendLog(Localization.T("GenericError", ex.Message));
            }
            finally
            {
                RestoreGeneralSettingsUi();
                _isBusy = false;
            }
        }

        private void RestoreGeneralSettingsUi()
        {
            bool loading = _isLoadingSettings;
            _isLoadingSettings = true;
            try
            {
                RunOnStartupCheckBox.IsChecked = _settings.RunOnWindowsStartup;
                StartMinimizedCheckBox.IsChecked = _settings.StartMinimizedToTray;
                MinimizeToTrayCheckBox.IsChecked = _settings.MinimizeToTrayOnClose;
                AutoConnectCheckBox.IsChecked = _settings.AutoConnectOnLaunch;
                ConnectionWatchdogCheckBox.IsChecked = _settings.EnableConnectionWatchdog;
            }
            finally { _isLoadingSettings = loading; }
        }



        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy || DpiValidationText.Visibility == Visibility.Visible) return;
            _isBusy = true;
            SaveSettingsButtonText.IsEnabled = false;
            try
            {
                _settings.Apps = _allApps.ToList();
                _settingsService.Save(_settings);
                if (_userWantsConnected)
                {
                    _targetSession = NextTargetSession();
                    var result = await RefreshTargetsAsync(false, _operationCts?.Token ?? CancellationToken.None);
                    if (!result.Applied || !_winDivert.IsRunning) throw new InvalidOperationException(Localization.T("ProtectionStartFailed"));
                    SetConnectionVisualState(true);
                    AppendLog(Localization.T("SettingsSavedAndApplied"));
                }
                else AppendLog(Localization.T("SettingsSaved"));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T("SettingsSaveError", ex.Message), Localization.T("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isBusy = false;
                SaveSettingsButtonText.IsEnabled = true;
                SaveSettingsButtonText.Content = Localization.T("SaveSettings");
            }
        }

        private void UpdateTrafficStats()
        {
            _trafficService.Update(_isConnected);

            double dl = _isConnected ? _trafficService.DownloadSpeedBytesPerSec : 0;
            double ul = _isConnected ? _trafficService.UploadSpeedBytesPerSec : 0;

            _dlHistory.Add(dl);
            _ulHistory.Add(ul);
            while (_dlHistory.Count > ChartHistoryLimit) _dlHistory.RemoveAt(0);
            while (_ulHistory.Count > ChartHistoryLimit) _ulHistory.RemoveAt(0);

            UpdateWaveformChart();

            UpdateStatisticsLabels();
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

        private void ChartContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateWaveformChart();
        }

        private void UpdateWaveformChart()
        {
            double w = ChartContainer.ActualWidth;
            double h = ChartContainer.ActualHeight;
            if (w <= 10 || h <= 10) return;

            double peak = 1024;
            foreach (var val in _dlHistory) if (val > peak) peak = val;
            foreach (var val in _ulHistory) if (val > peak) peak = val;

            ChartPeakText.Text = Localization.T("ChartPeak", TrafficStatisticsService.FormatSpeed(peak));

            double stepX = w / (ChartHistoryLimit - 1);
            int startOffset = ChartHistoryLimit - _dlHistory.Count;

            var dlLinePoints = new PointCollection();
            var dlPolyPoints = new PointCollection();
            var ulLinePoints = new PointCollection();
            var ulPolyPoints = new PointCollection();

            double startX = startOffset * stepX;
            dlPolyPoints.Add(new Point(startX, h));
            ulPolyPoints.Add(new Point(startX, h));

            for (int i = 0; i < _dlHistory.Count; i++)
            {
                double x = (startOffset + i) * stepX;
                double dlRatio = Math.Clamp(_dlHistory[i] / peak, 0, 1);
                double ulRatio = Math.Clamp(_ulHistory[i] / peak, 0, 1);

                double dlY = h - 5 - (dlRatio * (h - 10));
                double ulY = h - 5 - (ulRatio * (h - 10));

                var ptDl = new Point(x, dlY);
                var ptUl = new Point(x, ulY);

                dlLinePoints.Add(ptDl);
                dlPolyPoints.Add(ptDl);

                ulLinePoints.Add(ptUl);
                ulPolyPoints.Add(ptUl);
            }

            double lastX = (startOffset + Math.Max(0, _dlHistory.Count - 1)) * stepX;
            dlPolyPoints.Add(new Point(lastX, h));
            ulPolyPoints.Add(new Point(lastX, h));

            DownloadPolyline.Points = dlLinePoints;
            DownloadPolygon.Points = dlPolyPoints;
            UploadPolyline.Points = ulLinePoints;
            UploadPolygon.Points = ulPolyPoints;
        }

        private void ResetStatsButton_Click(object sender, RoutedEventArgs e)
        {
            _trafficService.Reset();
            _winDivert.ResetCounters();
            _dlHistory.Clear();
            _ulHistory.Clear();
            UpdateWaveformChart();
            SessionUptimeValueText.Text = "00:00";
            UpdateStatisticsLabels();
            AppendLog(Localization.T("StatsResetDone"));
        }

        private async Task CheckWatchdogAsync()
        {
            if (_settings.GetEffectiveDomains().Count == 0 || !_userWantsConnected || _isBusy || _settings.EnableConnectionWatchdog != true ||
                DateTime.Now < _watchdogSuppressedUntil || _winDivert.IsRunning) return;
            if ((DateTime.Now - _lastWatchdogAttemptTime).TotalSeconds < 3) return;
            _lastWatchdogAttemptTime = DateTime.Now;
            _isBusy = true;
            try
            {
                SetConnectionVisualState(false);
                AppendLog(Localization.T("WatchdogDropDetected"));
                _operationCts?.Cancel();
                _operationCts?.Dispose();
                _operationCts = new CancellationTokenSource();
                HostsFileService.RemoveEntries();
                _targetSession = NextTargetSession();
                await _winDivert.StopAsync();
                if (!_winDivert.Start(_settings.GetEffectiveDomains(), [], _settings.GetDohEndpoint(), _settings.GetDpiOptions()))
                    throw new InvalidOperationException(Localization.T("ProtectionStartFailed"));
                var result = await RefreshTargetsAsync(false, _operationCts.Token);
                if (!result.Applied || !_winDivert.IsRunning)
                    throw new InvalidOperationException(Localization.T("ProtectionStartFailed"));
                _watchdogDropSince = null;
                SetConnectionVisualState(true);
                AppendLog(Localization.T("WatchdogRestored"));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppendLog(Localization.T("GenericError", ex.Message));
                _operationCts?.Cancel();
                _targetSession = 0;
                _nextDnsRefresh = DateTimeOffset.MaxValue;
                try { HostsFileService.RemoveEntries(); }
                catch (Exception cleanupError) { AppendLog(Localization.T("GenericError", cleanupError.Message)); }
                try { await _winDivert.StopAsync(); }
                catch (Exception cleanupError) { AppendLog(Localization.T("GenericError", cleanupError.Message)); }
                SetConnectionVisualState(false);
            }
            finally { _isBusy = false; }
        }

        private void UpdatePingTargetLabel()
        {
            string endpoint = _settings.GetDohEndpoint() ?? DohDnsClient.CloudflareEndpoint;
            string target = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Authority : "--";
            PingTargetText.Text = Localization.T("PingToDoh", target);
        }

        private async Task UpdatePingAsync()
        {
            if (_isMeasuringPing || !IsVisible) return;
            UpdatePingTargetLabel();

            if (!_isConnected)
            {
                _lastDnsLatency = null;
                PingValueText.Text = "-- ms";
                PingBadge.Visibility = Visibility.Collapsed;
                LatencyIndicatorDot.Fill = (SolidColorBrush)FindResource("TextMutedBrush");
                return;
            }

            _isMeasuringPing = true;
            try
            {
                string? endpoint = _settings.GetDohEndpoint();
                CancellationToken ct = _operationCts?.Token ?? CancellationToken.None;
                long latency = await _latencyService.MeasureDohAsync(endpoint, ct: ct);
                if (!IsVisible || !_isConnected || ct.IsCancellationRequested || endpoint != _settings.GetDohEndpoint()) return;
                _lastDnsLatency = latency;

                if (latency >= 0)
                {
                    PingValueText.Text = Localization.T("PingUnit", latency);
                    PingBadge.Visibility = Visibility.Visible;

                    if (latency < 60)
                    {
                        PingQualityText.Text = Localization.T("PingGood");
                        PingQualityText.Foreground = (SolidColorBrush)FindResource("SuccessBrush");
                        PingBadge.Background = new SolidColorBrush(Color.FromArgb(0x25, 0x3B, 0xA5, 0x5D));
                        LatencyIndicatorDot.Fill = (SolidColorBrush)FindResource("SuccessBrush");
                    }
                    else if (latency < 120)
                    {
                        PingQualityText.Text = Localization.T("PingFair");
                        PingQualityText.Foreground = (SolidColorBrush)FindResource("AccentBrush");
                        PingBadge.Background = new SolidColorBrush(Color.FromArgb(0x25, 0x58, 0x65, 0xF2));
                        LatencyIndicatorDot.Fill = (SolidColorBrush)FindResource("AccentBrush");
                    }
                    else
                    {
                        PingQualityText.Text = Localization.T("PingPoor");
                        PingQualityText.Foreground = (SolidColorBrush)FindResource("DangerBrush");
                        PingBadge.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xED, 0x42, 0x45));
                        LatencyIndicatorDot.Fill = (SolidColorBrush)FindResource("DangerBrush");
                    }
                }
                else
                {
                    PingValueText.Text = Localization.T("DohProbeFailed");
                    PingBadge.Visibility = Visibility.Collapsed;
                    LatencyIndicatorDot.Fill = (SolidColorBrush)FindResource("DangerBrush");
                }
            }
            catch
            {
            }
            finally
            {
                _isMeasuringPing = false;
            }
        }

        private List<ProtectedApp> GetCurrentMatchingApps()
        {
            var query = CatalogSearchTextBox.Text.Trim();
            return _allApps.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.GetDomains().Any(d => d.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        private void FilterCatalog()
        {
            if (CatalogSearchTextBox == null || _isLoadingSettings) return;
            bool hasQuery = !string.IsNullOrWhiteSpace(CatalogSearchTextBox.Text);
            ClearSearchButton.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
            CatalogSearchPlaceholder.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;
            var matching = GetCurrentMatchingApps();
            var included = matching.ToHashSet();
            for (int i = _filteredApps.Count - 1; i >= 0; i--)
                if (!included.Contains(_filteredApps[i])) _filteredApps.RemoveAt(i);
            for (int i = 0; i < matching.Count; i++)
            {
                if (i < _filteredApps.Count && ReferenceEquals(_filteredApps[i], matching[i])) continue;
                int existing = _filteredApps.IndexOf(matching[i]);
                if (existing >= 0) _filteredApps.Move(existing, i);
                else _filteredApps.Insert(i, matching[i]);
            }
            NoResultsText.Text = Localization.T(_allApps.Count == 0 ? "TargetsEmpty" : "NoResultsFound");
            NoResultsText.Visibility = matching.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateActiveServicesCount();
        }

        private void UpdateActiveServicesCount()
        {
            int activeCount = _allApps.Count(a => a.Enabled);
            ActiveServicesCountText.Text = Localization.T("ActiveServicesCount", activeCount, _allApps.Count);
            UpdateProtectedTargetsText();
        }

        private void UpdateProtectedTargetsText()
        {
            if (ProtectedTargetsValueText == null) return;
            int activeAppsCount = _allApps?.Count(a => a.Enabled) ?? 0;
            int domainsCount = _winDivert.ActiveDomainsCount > 0 
                ? _winDivert.ActiveDomainsCount 
                : (_settings?.GetEffectiveDomains().Count() ?? 0);

            ProtectedTargetsValueText.Text = Localization.T("ProtectedTargetCounts", activeAppsCount, domainsCount);
        }

        private void CatalogSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterCatalog();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            CatalogSearchTextBox.Text = string.Empty;
            CatalogSearchTextBox.Focus();
        }

        private long _targetSessionCounter;
        private long NextTargetSession() => ++_targetSessionCounter;

        private bool SaveAppsAndSyncTargets(List<ProtectedApp> apps)
        {
            if (_isBusy) { AppendLog(Localization.T("PleaseWait")); return false; }
            var candidate = _settings.Copy();
            candidate.Apps = apps;
            try { _settingsService.Save(candidate); }
            catch (Exception ex) { AppendLog(Localization.T("SettingsSaveError", ex.Message)); return false; }
            var previous = _allApps.ToDictionary(a => a.Id);
            _isLoadingSettings = true;
            try
            {
                _allApps = apps.Select(next =>
                {
                    if (!previous.TryGetValue(next.Id, out var app)) return next;
                    app.Apply(next);
                    return app;
                }).ToList();
                _settings.Apps = _allApps;
                _savedTargetSelection = _allApps.ToDictionary(a => a.Id, a => a.Enabled);
                foreach (var removed in previous.Values.Where(a => !_allApps.Contains(a)))
                    if (_discoveryDialogs.TryGetValue(removed.Id, out var dialog)) dialog.Close();
            }
            finally { _isLoadingSettings = false; }
            FilterCatalog();
            UpdateActiveServicesCount();
            if (!_userWantsConnected || !_winDivert.IsRunning) return true;
            _targetSession = NextTargetSession();
            _winDivert.UpdateTargetDomains(_settings.GetEffectiveDomains(), []);
            if (_settings.GetEffectiveDomains().Count == 0)
            {
                _ = StopEmptyProtectionAsync();
                return true;
            }
            _nextDnsRefresh = DateTimeOffset.MinValue;
            return true;
        }

        private async Task StopEmptyProtectionAsync()
        {
            bool wasBusy = _isBusy;
            _isBusy = true;
            SetBusyUi(true, "Disconnecting");
            try { await StopProtectionAsync(); }
            catch (Exception ex) { AppendLog(Localization.T("GenericError", ex.Message)); }
            finally
            {
                if (!wasBusy) { _isBusy = false; SetBusyUi(false, null); }
            }
        }

        private void AppEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || _settings == null) return;
            if (sender is not System.Windows.Controls.CheckBox { DataContext: ProtectedApp app } checkbox ||
                !_savedTargetSelection.TryGetValue(app.Id, out bool saved) || saved == (checkbox.IsChecked == true)) return;
            if (_isBusy) { checkbox.SetCurrentValue(CheckBox.IsCheckedProperty, saved); return; }
            var candidate = _allApps.Select(a => a.Copy()).ToList();
            candidate.First(a => a.Id == app.Id).Enabled = checkbox.IsChecked == true;
            if (!SaveAppsAndSyncTargets(candidate))
            {
                _isLoadingSettings = true;
                try { checkbox.SetCurrentValue(CheckBox.IsCheckedProperty, saved); }
                finally { _isLoadingSettings = false; }
            }
        }

        private async void SyncIpsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            _isBusy = true;
            _isSyncingIps = true;
            SyncIpsButton.IsEnabled = false;
            SyncIpsButtonText.Text = Localization.T("SyncingIps");
            Dialogs.SyncCompleteDialog? completion = null;
            try
            {
                if (_userWantsConnected && _winDivert.IsRunning)
                {
                    _targetSession = NextTargetSession();
                    var result = await RefreshTargetsAsync(true, _operationCts?.Token ?? CancellationToken.None);
                    if (!result.Applied) throw new InvalidOperationException(Localization.T("ProtectionStartFailed"));
                    completion = new Dialogs.SyncCompleteDialog(result.Services, result.Ips, true, result.Failed) { Owner = this };
                }
                else
                {
                    var resolved = await DomainDnsResolver.ResolveAllAsync(_settings.GetEffectiveDomains(), true);
                    DnsCacheService.Flush();
                    int services = _settings.Apps.Count(a => a.Enabled && a.GetDomains().Any(d =>
                        resolved.TryGetValue(d, out var ips) && ips.Count > 0));
                    completion = new Dialogs.SyncCompleteDialog(services, resolved.Values.Sum(ips => ips.Count), false,
                        resolved.Count(pair => pair.Value.Count == 0)) { Owner = this };
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T("SyncError", ex.Message), Localization.T("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _isBusy = false;
                _isSyncingIps = false;
                SyncIpsButton.IsEnabled = true;
                SyncIpsButtonText.Text = Localization.T("SyncIps");
            }
            completion?.ShowDialog();
        }

        private async void AddCustomAppButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            var dialog = new Dialogs.AddAppDialog { Owner = this };
            if (dialog.ShowDialog() != true) return;
            if (_allApps.Any(a => a.Domain.Equals(dialog.Domain, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(Localization.T("AppAlreadyExists", dialog.Domain), Localization.T("Warning"));
                return;
            }
            var app = new ProtectedApp { Name = dialog.AppName, Domain = dialog.Domain, Enabled = true };
            app.SetDomains(dialog.Domains);
            var candidate = _allApps.Select(a => a.Copy()).ToList();
            candidate.Insert(0, app);
            if (!SaveAppsAndSyncTargets(candidate)) return;
            CatalogSearchTextBox.Clear();
            FilterCatalog();
            if (dialog.DiscoverRelated) await ShowDiscoveryAsync(app);
        }

        private void TargetActionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: ProtectedApp app } button) return;
            var menu = new ContextMenu();
            var discover = new MenuItem { Header = Localization.T("DiscoverRelated") };
            discover.Click += async (_, _) => await ShowDiscoveryAsync(app);
            var edit = new MenuItem { Header = Localization.T("EditDomainGroup") };
            edit.Click += (_, _) => EditTarget(app);
            var delete = new MenuItem { Header = Localization.T("Delete") };
            delete.Click += (_, _) => RemoveTarget(app);
            menu.Items.Add(discover);
            menu.Items.Add(edit);
            menu.Items.Add(delete);
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }

        private async Task ShowDiscoveryAsync(ProtectedApp app)
        {
            if (!_allApps.Contains(app) || _closingAfterDisconnect) return;
            if (_discoveryDialogs.TryGetValue(app.Id, out var existing)) { if (existing.IsVisible) existing.Activate(); return; }
            string sourceDomain = app.Domain;
            var dialog = new Dialogs.RelatedDomainsDialog(app.Domain, _discoveryService,
                () => _allApps.Contains(app) && app.Domain == sourceDomain && !_closingAfterDisconnect,
                () => app.GetDomains().ToArray(), selected =>
                {
                    if (!_allApps.Contains(app) || app.Domain != sourceDomain || _closingAfterDisconnect) return;
                    if (_isBusy) throw new InvalidOperationException(Localization.T("PleaseWait"));
                    var candidate = _allApps.Select(a => a.Copy()).ToList();
                    candidate.First(a => a.Id == app.Id).AddRelatedDomains(selected);
                    if (!SaveAppsAndSyncTargets(candidate)) throw new IOException(Localization.T("SettingsInvalid"));
                    FilterCatalog();
                }) { Owner = this };
            _discoveryDialogs.Add(app.Id, dialog);
            try { dialog.Show(); await dialog.Completion; }
            catch (Exception ex) { AppendLog(Localization.T("GenericError", ex.Message)); }
            finally { _discoveryDialogs.Remove(app.Id); }
        }

        private void EditTarget(ProtectedApp app)
        {
            if (_isBusy || !_allApps.Contains(app)) return;
            var dialog = new Dialogs.AddAppDialog(app) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            if (_allApps.Any(a => !ReferenceEquals(a, app) && a.Domain.Equals(dialog.Domain, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(Localization.T("AppAlreadyExists", dialog.Domain), Localization.T("Warning"));
                return;
            }
            var candidate = _allApps.Select(a => a.Copy()).ToList();
            var edited = candidate.First(a => a.Id == app.Id);
            edited.SetDomains(dialog.Domains);
            edited.Name = dialog.AppName;
            if (!SaveAppsAndSyncTargets(candidate)) return;
            if (_discoveryDialogs.TryGetValue(app.Id, out var discovery)) discovery.Close();
            FilterCatalog();
        }

        private void RemoveTarget(ProtectedApp app)
        {
            if (_isBusy || !_allApps.Contains(app)) return;
            if (MessageBox.Show(Localization.T("RemoveGroupConfirm", app.Name, app.GetDomains().Count()), Localization.T("Confirm"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (!SaveAppsAndSyncTargets(_allApps.Where(a => a.Id != app.Id).Select(a => a.Copy()).ToList())) return;
            FilterCatalog();
        }

        private void CatalogScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            AppsItemsControl.ApplyTemplate();
            if (AppsItemsControl.Template.FindName("PART_ScrollViewer", AppsItemsControl) is ScrollViewer scroller)
            {
                bool atTop = scroller.VerticalOffset <= 0;
                bool atBottom = scroller.VerticalOffset >= scroller.ScrollableHeight;

                if ((e.Delta > 0 && atTop) || (e.Delta < 0 && atBottom))
                {
                    SettingsScrollViewer.ScrollToVerticalOffset(SettingsScrollViewer.VerticalOffset - e.Delta / 2.0);
                    e.Handled = true;
                    return;
                }

                scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta / 2.0);
                e.Handled = true;
            }
        }
    }
}
