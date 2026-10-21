using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using dapapa.Dialogs;
using dapapa.Models;
using dapapa.Services;
using Localization = dapapa.Services.Localization;

internal static partial class Program
{
    private static int _images;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Flush()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    internal static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow > deadline) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Check(task.IsCompleted, "WPF async action did not finish.");
        task.GetAwaiter().GetResult();
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) yield return found;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Render(FrameworkElement content, int width, int height, string name)
    {
        var owner = Window.GetWindow(content); if (owner != null) { owner.Width = width; owner.Height = height; }
        content.Width = width; content.Height = height;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        Wait(Task.Delay(260)); content.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.GetFullPath("artifacts/ui-tests/" + name + ".png"));
        encoder.Save(stream); _images++;
    }

    private static void RenderDialog(Window window, string name)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        int width = (int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right);
        int height = (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.GetFullPath("artifacts/ui-tests/" + name + ".png"));
        encoder.Save(stream); _images++;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        if (args.Contains("--live-discovery"))
        {
            using var service = new RelatedDomainDiscoveryService();
            foreach (string domain in new[] { "example.com", "github.com" })
            {
                var pending = service.DiscoverAsync(domain, CancellationToken.None);
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                timer.Tick += (_, _) => { if (pending.IsCompleted) frame.Continue = false; };
                timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
                var result = pending.GetAwaiter().GetResult();
                Console.WriteLine($"LIVE: {domain}: completed={result.Completed}, candidates={result.Candidates.Count}, error={result.Error ?? "none"}");
                Check(result.Completed, "Live discovery failed: " + domain);
            }
            app.Shutdown(); return;
        }
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var resources = new XElement(p + "ResourceDictionary", new XAttribute("xmlns", p), new XAttribute(XNamespace.Xmlns + "x", x),
            XDocument.Load("App.xaml").Root!.Element(p + "Application.Resources")!.Elements().Select(e => new XElement(e)));
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        using (var selection = new SelectionHarness()) selection.Verify();
        using (var shutdown = new SelectionHarness()) shutdown.VerifyShutdown();
        Directory.CreateDirectory("artifacts/ui-tests");
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            Localization.Current = language;
            var source = XDocument.Load("MainWindow.xaml");
            source.Root!.Attribute(x + "Class")!.Remove();
            source.Root.Element(p + "Window.Triggers")?.Remove();
            var events = new HashSet<string> { "Click", "Checked", "Unchecked", "SelectionChanged", "TextChanged", "MouseLeftButtonDown", "PreviewMouseWheel", "SizeChanged", "LostFocus", "KeyDown" };
            foreach (var attribute in source.Descendants().Attributes().ToList())
            {
                if (events.Contains(attribute.Name.LocalName)) attribute.Remove();
                else if (attribute.Value.StartsWith("pack://application:,,,/Resources/"))
                    attribute.Value = attribute.Value.Replace("pack://application:,,,/Resources/", "pack://application:,,,/dapapa;component/Resources/");
            }
            var root = source.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "RootBorder");
            root.SetAttributeValue("Opacity", "1"); root.Element(p + "Border.RenderTransform")?.Remove();
            var window = (Window)XamlReader.Parse(source.ToString());
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000; window.Top = -10000; window.ShowInTaskbar = false;
            window.Show(); Flush();
            foreach (Match match in Regex.Matches(File.ReadAllText("MainWindow.xaml.cs"), "(\\w+)\\.(Text|Content) = Localization\\.T\\(\"([^\"]+)\"\\)"))
            {
                if (window.FindName(match.Groups[1].Value) is TextBlock text) text.Text = Localization.T(match.Groups[3].Value);
                else if (window.FindName(match.Groups[1].Value) is ContentControl control) control.Content = Localization.T(match.Groups[3].Value);
            }
            foreach (string removed in new[] { "RegionalCatalogHeaderText", "RegionalCatalogStatusText", "CategoryFilterPanel", "RefreshCatalogButton", "ResetAppsDefaultsButtonText", "ShowMoreAppsButton", "LastBypassedLabelText" })
                Check(window.FindName(removed) == null, "Removed control is present: " + removed);
            var tabs = (TabControl)window.FindName("MainTabControl");
            var list = (ItemsControl)window.FindName("AppsItemsControl");
            var scroll = (ScrollViewer)window.FindName("SettingsScrollViewer");
            var empty = (TextBlock)window.FindName("NoResultsText");
            tabs.SelectedIndex = 2;
            var content = (FrameworkElement)window.Content;
            ((ComboBox)window.FindName("LanguageComboBox")).SelectedIndex = language == AppLanguage.Turkish ? 1 : 0;
            ((TextBlock)window.FindName("PingValueText")).Text = "-- ms";
            ((TextBlock)window.FindName("PingTargetText")).Text = Localization.T("PingToDoh", "dns11.quad9.net");
            using var dpiHarness = new DpiUiHarness(window);
            Check(((CheckBox)window.FindName("DpiBlockQuicCheckBox")).FocusVisualStyle != null,
                "Keyboard focus indicator was removed from checkboxes.");
            var scrollbar = new System.Windows.Controls.Primitives.ScrollBar { Orientation = Orientation.Vertical,
                Style = (Style)app.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] };
            scrollbar.Measure(new Size(8, 100));
            Check(scrollbar.Visibility == Visibility.Visible && scrollbar.Width >= 8,
                "Global scrollbar style hides long-list scrolling.");
            dpiHarness.Verify(); dpiHarness.VerifyAdvanced(); dpiHarness.VerifyUdp(); dpiHarness.VerifyRestart();
            using var dohHarness = new DohUiHarness(window); Wait(dohHarness.VerifyAsync());
            foreach (int width in new[] { 520, 460 })
            {
                dpiHarness.ShowStandard(); Render(content, width, 640, $"{language}-dpi-standard-top-{width}");
                var dpiHeader = (FrameworkElement)window.FindName("DpiSettingsHeaderText");
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + dpiHeader.TransformToAncestor(scroll).Transform(new Point()).Y - 12); Flush();
                Render(content, width, 640, $"{language}-dpi-standard-{width}");
                dpiHarness.ShowCustomDemo(); dpiHarness.ShowUdpDemo(); content.UpdateLayout();
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + dpiHeader.TransformToAncestor(scroll).Transform(new Point()).Y - 12); Flush();
                Render(content, width, 640, $"{language}-dpi-custom-top-{width}");
                var fakeControl = (CheckBox)window.FindName("DpiFakeWrongChecksumCheckBox");
                Check(fakeControl.IsChecked == false && ((System.Windows.Shapes.Path)fakeControl.Template.FindName("CheckMark", fakeControl)).Visibility == Visibility.Collapsed,
                    "Unchecked fake method is visually checked after animations finish.");
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + dpiHeader.TransformToAncestor(scroll).Transform(new Point()).Y - 12); Flush();
                Render(content, width, 640, $"{language}-dpi-custom-bottom-{width}");
                foreach (var section in new[] { "DpiFakeAdvancedExpander", "DpiFakeHostnameLabelText", "DpiHttpMethodsLabelText", "DpiUdpExpander", "DpiUdpIpFragmentationCheckBox", "DpiUdpPacketLimitLabelText" })
                {
                    var anchor = (FrameworkElement)window.FindName(section);
                    scroll.ScrollToVerticalOffset(scroll.VerticalOffset + anchor.TransformToAncestor(scroll).Transform(new Point()).Y - 12); Flush();
                    Render(content, width, 640, $"{language}-{section}-{width}");
                }
            }
            
            dpiHarness.ShowStandard();
            foreach (var provider in new[] { DohProvider.Google, DohProvider.AdGuard })
            {
                dohHarness.ShowProvider(provider);
                foreach (int width in new[] { 520, 460 })
                {
                    var anchor = (FrameworkElement)window.FindName("DohSettingsHeaderText");
                    scroll.ScrollToVerticalOffset(scroll.VerticalOffset + anchor.TransformToAncestor(scroll).Transform(new Point()).Y - 12); Flush();
                    Render(content, width, 640, $"{language}-doh-{provider}-{width}");
                }
            }
            scroll.ScrollToTop(); content.UpdateLayout();
            foreach (int width in new[] { 520, 460 })
            {
                int height = width == 520 ? 640 : 560;
                list.ItemsSource = Array.Empty<ProtectedApp>();
                empty.Text = Localization.T("TargetsEmpty"); empty.Visibility = Visibility.Visible;
                ((TextBlock)window.FindName("ActiveServicesCountText")).Text = Localization.T("ActiveServicesCount", 0, 0);
                Render(content, width, height, $"{language}-empty-top-{width}");
                scroll.ScrollToEnd(); content.UpdateLayout();
                Render(content, width, height, $"{language}-empty-targets-{width}");
                var targets = Enumerable.Range(1, 8).Select(i => new ProtectedApp { Name = "Target " + i, Domain = $"target{i}.example", Enabled = i % 2 == 0 }).ToList();
                targets[0].AddRelatedDomains(["cdn.other.net", "api.other.net"]);
                list.ItemsSource = targets; empty.Visibility = Visibility.Collapsed;
                ((TextBlock)window.FindName("ActiveServicesCountText")).Text = Localization.T("ActiveServicesCount", 4, 8);
                content.UpdateLayout(); scroll.ScrollToEnd(); content.UpdateLayout();
                Render(content, width, height, $"{language}-populated-{width}");
                list.ItemsSource = targets.Where(a => a.Domain.Contains("target3")).ToList();
                Check(list.Items.Count == 1, "Search fixture incorrect.");
                Render(content, width, height, $"{language}-search-{width}");
                scroll.ScrollToTop(); content.UpdateLayout();
            }
            var tabHarness = new TabSelectionHarness(window);
            tabs.SelectedIndex = 1; Flush();
            var stats = (ScrollViewer)window.FindName("StatsScrollViewer");
            content.UpdateLayout(); stats.ScrollToEnd(); content.UpdateLayout();
            Check(stats.VerticalOffset > 0, "Statistics fixture did not scroll.");
            tabs.SelectedIndex = 0; tabs.SelectedIndex = 1; Flush(); content.UpdateLayout();
            Check(stats.VerticalOffset == 0, "Statistics did not reset to top.");
            var statsHarness = new StatisticsHarness(window);
            statsHarness.Verify(); Localization.Current = language; statsHarness.Update();
            var statsContent = (UIElement)((TabItem)tabs.SelectedItem).Content;
            statsContent.BeginAnimation(UIElement.OpacityProperty, null);
            statsContent.Opacity = 1;
            statsContent.RenderTransform = Transform.Identity;
            ((TextBlock)window.FindName("ChartPeakText")).Text = Localization.T("ChartPeak", TrafficStatisticsService.FormatSpeed(1024));
            Render(content, 520, 640, $"{language}-statistics-top");
            stats.ScrollToEnd(); content.UpdateLayout(); Render(content, 520, 640, $"{language}-statistics-counters");
                        if (language == AppLanguage.English)
            {
                tabs.SelectedIndex = 2; Flush(); content.UpdateLayout();
                var auditTimer = System.Diagnostics.Stopwatch.StartNew();
                var auditAllocated = GC.GetAllocatedBytesForCurrentThread();
                list.ItemsSource = Enumerable.Range(0, 500).Select(i => new ProtectedApp { Domain = $"audit{i}.example", Name = $"Audit {i}" }).ToArray();
                content.UpdateLayout(); Flush(); content.UpdateLayout();
                int auditContainers = Enumerable.Range(0, 500).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null);
                Check(auditContainers > 0 && auditContainers < 50, "Target list virtualization did not limit realized cards.");
                Console.WriteLine($"PERF: 500 targets: realized={auditContainers}, layout={auditTimer.Elapsed.TotalMilliseconds:F1} ms, UI-thread allocated={GC.GetAllocatedBytesForCurrentThread() - auditAllocated} bytes; no native driver.");
                var inner = Visuals<ScrollViewer>(list).First();
                inner.ScrollToTop(); Flush(); content.UpdateLayout();
                var vertical = Visuals<System.Windows.Controls.Primitives.ScrollBar>(inner).First(b => b.Orientation == Orientation.Vertical);
                Check(vertical.ActualWidth == 8 && vertical.Visibility == Visibility.Visible, "Real target scrollbar did not use the visible thin style.");
                var thumb = Visuals<System.Windows.Controls.Primitives.Thumb>(vertical).Single();
                thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0, 20)
                    { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
                Flush(); content.UpdateLayout();
                Check(inner.VerticalOffset > 0, "Dragging the target scrollbar did not move the list.");
                ((ListBox)list).ScrollIntoView(list.Items[499]); Flush(); content.UpdateLayout();
                Check(list.ItemContainerGenerator.ContainerFromIndex(499) != null, "Virtualized list could not reach its last target.");
            }
            tabHarness.Close(); window.Close();

            var add = new AddAppDialog();
            Check(!add.DiscoverRelated, "Discovery enabled by default.");
            Check(((TextBlock)add.FindName("AppNameLabelText")).Text == Localization.T("AppNameLabel"), "Name label is not optional.");
            add.Show(); Flush(); RenderDialog(add, $"{language}-add-domain");
            add.Close();
            var submit = new AddAppDialog();
            submit.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                ((TextBox)submit.FindName("DomainTextBox")).Text = "https://EXAMPLE.COM/path";
                ((CheckBox)submit.FindName("DiscoverRelatedCheckBox")).IsChecked = true;
                ((Button)submit.FindName("ContinueButtonText")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            Check(submit.ShowDialog() == true && submit.AppName == "example.com" && submit.Domain == "example.com" && submit.DiscoverRelated,
                "Blank display name or discovery option failed in the actual add dialog.");

            var listPreview = new AddAppDialog();
            ((RadioButton)listPreview.FindName("DomainListRadio")).IsChecked = true;
            ((TextBox)listPreview.FindName("AppNameTextBox")).Text = "My group";
            ((TextBox)listPreview.FindName("DomainListTextBox")).Text = "example.com\ncdn.other.net\napi.other.net";
            listPreview.Show(); Flush(); RenderDialog(listPreview, $"{language}-add-list"); listPreview.Close();
            var bulk = new AddAppDialog();
            bulk.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                ((RadioButton)bulk.FindName("DomainListRadio")).IsChecked = true;
                var input = (TextBox)bulk.FindName("DomainListTextBox");
                var save = (Button)bulk.FindName("ContinueButtonText");
                input.Text = "group.example\n*.invalid.net";
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(((TextBlock)bulk.FindName("InputErrorText")).Visibility == Visibility.Visible && bulk.Domains.Length == 0,
                    "Invalid list was partially submitted or missing inline error.");
                input.Text = "GROUP.EXAMPLE\nhttps://cdn.other.net/path\ngroup.example";
                ((TextBox)bulk.FindName("AppNameTextBox")).Text = "My group";
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            Check(bulk.ShowDialog() == true && bulk.Domains.SequenceEqual(["group.example", "cdn.other.net"]) && bulk.AppName == "My group",
                "Bulk add did not produce one normalized group.");
            var editedGroup = new ProtectedApp { Name = bulk.AppName };
            editedGroup.SetDomains(bulk.Domains);
            var edit = new AddAppDialog(editedGroup);
            Check(((CheckBox)edit.FindName("DiscoverRelatedCheckBox")).Visibility == Visibility.Collapsed &&
                ((TextBox)edit.FindName("DomainListTextBox")).Text.Contains("cdn.other.net"), "Group editor did not show existing members.");
            edit.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                RenderDialog(edit, $"{language}-edit-group");
                ((TextBox)edit.FindName("DomainListTextBox")).Text = "group.example\napi.new.net";
                ((Button)edit.FindName("ContinueButtonText")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            Check(edit.ShowDialog() == true && edit.Domains.SequenceEqual(["group.example", "api.new.net"]), "Group editor did not replace a member.");

            var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("<script src='https://api.other.net/a'></script><img src='https://cdn.other.net/i'>", Encoding.UTF8, "text/html") }));
            using var service = new RelatedDomainDiscoveryService(new HttpClient(handler));
            string[]? added = null;
            var rootGroup = new ProtectedApp { Name = "Example", Domain = "example.com" };
            var groupedSettings = new AppSettings { TargetListVersion = 1, Apps = [rootGroup] };
            var dialog = new RelatedDomainsDialog("example.com", service, () => true, () => rootGroup.GetDomains().ToArray(), values =>
            {
                added = values;
                rootGroup.AddRelatedDomains(values);
            });
            dialog.Show(); Flush();
            Wait((Task)typeof(RelatedDomainsDialog).GetField("_load", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!);
            dialog.UpdateLayout();
            var choices = (ItemsControl)dialog.FindName("CandidatesItemsControl");
            Check(choices.Items.Count == 2 && !((Button)dialog.FindName("AddSelectedButton")).IsEnabled && added == null, "Unselected discoveries activated targets.");
            RenderDialog(dialog, $"{language}-discovery-results");
            var boxes = Visuals<CheckBox>(choices).ToArray();
            Check(boxes.Length == 2 && boxes.All(b => b.IsChecked == false), "Candidate checkboxes are not clear by default.");
            boxes[0].IsChecked = true; Flush();
            var selectedDomain = ((DiscoveryChoice)choices.Items[0]).Domain;
            ((Button)dialog.FindName("AddSelectedButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(dialog.Completion);
            Check(added?.SequenceEqual([selectedDomain]) == true, "WPF selection did not add exactly the selected domain.");
            Check(groupedSettings.Apps.Count == 1 && rootGroup.RelatedDomains.SequenceEqual([selectedDomain]),
                "WPF discovery created separate target rows instead of a group member.");

            var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var waitingService = new RelatedDomainDiscoveryService(new HttpClient(new Handler((_, _) => pending.Task)));
            bool exists = true;
            bool lateAdd = false;
            var waiting = new RelatedDomainsDialog("example.com", waitingService, () => exists, () => ["example.com"], _ => lateAdd = true);
            waiting.Show(); Flush(); exists = false; waiting.Close();
            pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<script src='https://late.other.net/a'></script>", Encoding.UTF8, "text/html") });
            Wait(waiting.Completion);
            Check(!lateAdd && !((ItemsControl)waiting.FindName("CandidatesItemsControl")).HasItems, "Closed/deleted target received late discovery results.");
        }
        VerifyLiveLanguageChanges();
        app.Shutdown();
        Console.WriteLine($"PASS: {_images} WPF renders; Turkish/English single/list add, atomic invalid-list validation, group editing, discovery grouping, checkbox selection, cancellation and statistics top reset. Rebuilding the list does not save unchanged targets; user toggles save once. MainWindow and native protection were not started.");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
