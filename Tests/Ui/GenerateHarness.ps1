param([string]$RootDirectory, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$taskHeader = "using System.IO;`nusing System.Windows;`nusing System.Windows.Controls;`nusing dapapa;`nusing dapapa.Models;`nusing dapapa.Services;`nusing Services = dapapa.Services;`nusing Localization = dapapa.Services.Localization;`n"
foreach ($taskName in @('Dpi', 'Doh')) {
    $taskSource = Get-Content -LiteralPath (Join-Path $RootDirectory "MainWindow.$taskName.cs") -Raw -Encoding UTF8
    $taskStart = $taskSource.IndexOf('        private void Load')
    $taskEnd = $taskSource.LastIndexOf('}', $taskSource.LastIndexOf('}') - 1)
    $taskBody = $taskSource.Substring($taskStart, $taskEnd - $taskStart)
    $taskBody = $taskBody.Replace('System.Windows.MessageBox.Show(', 'ShowError(').Replace('MessageBox.Show(', 'ShowError(')
    if ($taskName -eq 'Doh') {
        $taskBody = $taskBody.Replace('dapapa.Services.DnsCacheService.Flush()', 'FlushDnsCache()')
        $taskBody = $taskBody.Replace('private async void DohProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)' + "`r`n" + '            => await ApplyDohProviderSelectionAsync();', 'private void DohProvider_SelectionChanged(object sender, SelectionChangedEventArgs e) => LastApply = ApplyDohProviderSelectionAsync();')
        $taskBody = $taskBody.Replace('private async void DohProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)' + "`n" + '            => await ApplyDohProviderSelectionAsync();', 'private void DohProvider_SelectionChanged(object sender, SelectionChangedEventArgs e) => LastApply = ApplyDohProviderSelectionAsync();')
    }
    if ($taskName -eq 'Dpi') { $taskBody = $taskBody.Replace('await RestartDpiProtectionAsync()', 'await (LastRestart = RestartDpiProtectionAsync())') }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory "$($taskName)UiHarness.Generated.cs"), $taskHeader + "internal sealed partial class $($taskName)UiHarness {`n" + $taskBody + "`n}")
}
$taskSource = Get-Content -LiteralPath (Join-Path $RootDirectory 'MainWindow.xaml.cs') -Raw -Encoding UTF8
function Get-Method([string]$Signature, [string]$Access = 'private') {
    $taskStart = $taskSource.IndexOf('        ' + $Access + ' ' + $Signature)
    if ($taskStart -lt 0) { throw "UI test extraction failed: $Signature" }
    $taskBrace = $taskSource.IndexOf('{', $taskStart)
    $taskLevel = 1
    $taskPosition = $taskBrace + 1
    while ($taskLevel -gt 0) {
        if ($taskSource[$taskPosition] -eq '{') { $taskLevel++ }
        if ($taskSource[$taskPosition] -eq '}') { $taskLevel-- }
        $taskPosition++
    }
    return $taskSource.Substring($taskStart, $taskPosition - $taskStart)
}
$taskMethods = @('bool SaveAppsAndSyncTargets(', 'void AppEnabledCheckBox_Changed(', 'async void SettingCheckBox_Changed(',
    'void RestoreGeneralSettingsUi(', 'void LanguageComboBox_SelectionChanged(', 'async void MainWindow_Loaded(',
    'async Task StartConnectionFlowAsync(', 'async Task StopProtectionAsync(', 'void CleanupDnsAfterStop(',
    'async Task PollStatusAsync(', 'void FilterCatalog(', 'List<ProtectedApp> GetCurrentMatchingApps(') |
    ForEach-Object { Get-Method $_ }
$taskBody = $taskMethods -join "`n"
$taskBody += "`n" + (Get-Method 'void CleanupProtectionOnExit(' 'internal')
$taskBody = $taskBody.Replace('private async void SettingCheckBox_Changed(', 'private async Task SettingCheckBox_Changed(')
$taskBody = $taskBody.Replace('private async void MainWindow_Loaded(', 'private async Task MainWindow_Loaded(')
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SelectionHarness.Generated.cs'), $taskHeader +
    "using StartupService = StartupStub;`nusing HostsFileService = HostsStub;`nusing DnsCacheService = DnsCacheStub;`nusing DomainDnsResolver = ResolverStub;`ninternal sealed partial class SelectionHarness {`n" + $taskBody + "`n}")
[IO.File]::AppendAllText((Join-Path $OutputDirectory 'DohUiHarness.Generated.cs'), "`ninternal sealed partial class DohUiHarness {`n" + (Get-Method 'void UpdatePingTargetLabel(') + "`n}")
