using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using dapapa.Dialogs;
using dapapa.Services;
using Localization = dapapa.Services.Localization;

internal static partial class Program
{
    private static int LanguageSubscribers() => ((Delegate?)typeof(Localization)
        .GetField("Changed", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null))?.GetInvocationList().Length ?? 0;
    private static string Text(Window window, string name) => ((TextBlock)window.FindName(name)).Text;
    private static string Content(Window window, string name) => ((ContentControl)window.FindName(name)).Content?.ToString() ?? "";
    private static void VerifyLiveLanguageChanges()
    {
        int baseline = LanguageSubscribers();
        Localization.Current = AppLanguage.English;
        var add = new AddAppDialog(); add.Show(); Flush();
        ((RadioButton)add.FindName("DomainListRadio")).IsChecked = true;
        ((TextBox)add.FindName("AppNameTextBox")).Text = "Preserved name";
        ((TextBox)add.FindName("DomainListTextBox")).Text = "example.com\n*.invalid.net";
        ((Button)add.FindName("ContinueButtonText")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        foreach (var language in new[] { AppLanguage.Turkish, AppLanguage.English })
        {
            Localization.Current = language; Flush();
            Check(add.Title == Localization.T("AddAppDialogHeading"), "Open add dialog title stale.");
            Check(Text(add, "DomainScopeText") == Localization.T("DomainListScope"), "Open add dialog scope stale.");
            Check(Content(add, "DomainListRadio") == Localization.T("DomainList"), "Open add mode stale.");
            Check(Text(add, "InputErrorText") == Localization.T("InvalidDomainLine", 2), "Inline validation error stale.");
            Check(((TextBlock)add.FindName("InputErrorText")).Visibility == Visibility.Visible, "Language change lost validation error.");
            Check(((RadioButton)add.FindName("DomainListRadio")).IsChecked == true &&
                ((TextBox)add.FindName("AppNameTextBox")).Text == "Preserved name" &&
                ((TextBox)add.FindName("DomainListTextBox")).Text == "example.com\n*.invalid.net", "Language change lost user input.");
            RenderDialog(add, $"{language}-switch-add-invalid");
        }
        add.Close(); Flush(); Check(LanguageSubscribers() == baseline, "Closed add dialog leaks language subscription.");

        using var service = new RelatedDomainDiscoveryService(new HttpClient(new Handler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<script src='https://cdn.other.net/a'></script>", Encoding.UTF8, "text/html") }))));
        var discovery = new RelatedDomainsDialog("example.com", service, () => true, () => ["example.com"], _ => throw new Exception("Language switch unexpectedly added domains."));
        discovery.Show(); Flush();
        Wait((Task)typeof(RelatedDomainsDialog).GetField("_load", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(discovery)!);
        var items = (ItemsControl)discovery.FindName("CandidatesItemsControl");
        var checkbox = Visuals<CheckBox>(items).Single(); checkbox.IsChecked = true; Flush();
        foreach (var language in new[] { AppLanguage.Turkish, AppLanguage.English })
        {
            Localization.Current = language; Flush(); discovery.UpdateLayout();
            Check(discovery.Title == Localization.T("DiscoveryTitle", "example.com"), "Open discovery title stale.");
            Check(Text(discovery, "StatusText") == Localization.T("DiscoveryFoundSingle", 1), "Open discovery count stale.");
            Check(Content(discovery, "AddSelectedButton") == Localization.T("DiscoveryAddSelected"), "Open discovery button stale.");
            Check(Visuals<TextBlock>(checkbox).Any(t => t.Text == Localization.T("DiscoveryReasonResource")), "Bound candidate reason stale.");
            Check(checkbox.IsChecked == true && ((DiscoveryChoice)items.Items[0]).Selected &&
                ((Button)discovery.FindName("AddSelectedButton")).IsEnabled, "Language update lost candidate selection.");
            RenderDialog(discovery, $"{language}-switch-discovery-selected");
        }
        discovery.Close(); Wait(discovery.Completion); Flush();
        Check(LanguageSubscribers() == baseline, "Closed discovery leaks language subscription.");

        using var failedService = new RelatedDomainDiscoveryService(new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))));
        var failed = new RelatedDomainsDialog("example.com", failedService, () => true, () => ["example.com"], _ => { });
        failed.Show(); Flush(); Wait((Task)typeof(RelatedDomainsDialog).GetField("_load", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(failed)!);
        foreach (var language in new[] { AppLanguage.Turkish, AppLanguage.English })
        {
            Localization.Current = language; Flush();
            Check(Text(failed, "StatusText") == Localization.T("DiscoveryFailed", Localization.T("DiscoveryHttpFailure", 403)), "Open HTTP 403 failure stale.");
            Check(!((Button)failed.FindName("AddSelectedButton")).IsEnabled, "403 enabled candidate addition.");
            RenderDialog(failed, $"{language}-switch-discovery-403");
        }
        failed.Close(); Wait(failed.Completion); Flush(); Check(LanguageSubscribers() == baseline, "Closed error dialog leaks subscription.");

        var sync = new SyncCompleteDialog(2, 1234, true, 1); sync.Show(); Flush();
        foreach (var language in new[] { AppLanguage.Turkish, AppLanguage.English })
        {
            Localization.Current = language; Flush();
            Check(sync.Title == Localization.T("SyncIncompleteHeading") && Text(sync, "HeadingText") == sync.Title, "Partial sync claims success or stale title.");
            Check(Text(sync, "SubheadingText") == Localization.T("SyncPartialFailureSingle", 1), "Partial failure translation stale.");
            Check(Text(sync, "ServicesUpdatedText") == Localization.T("SyncServicesUpdated", 2, 1234), "Sync counters ignore selected culture.");
            Check(Text(sync, "TargetStatusText") == Localization.T("SyncTargetsAppliedLive"), "Sync target status stale.");
            RenderDialog(sync, $"{language}-switch-sync-partial");
        }
        sync.Close(); Flush(); Check(LanguageSubscribers() == baseline, "Closed sync dialog leaks subscription.");
        Console.WriteLine("PASS: live language changes retain inputs, validation, checkbox selection and counters; bound reasons and stored errors retranslate; closed dialogs unsubscribe.");
    }
}
