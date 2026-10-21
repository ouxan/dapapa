using System.Windows;
using System.Windows.Controls;
using dapapa.Models;
using MessageBox = System.Windows.MessageBox;
using Localization = dapapa.Services.Localization;

namespace dapapa
{
    public partial class MainWindow
    {
        private void LoadDohSettingsIntoUi()
        {
            bool loading = _isLoadingSettings;
            _isLoadingSettings = true;
            try
            {
                if (DohProviderComboBox.Items.Count == 0)
                    foreach (var provider in DohProviders.All)
                        DohProviderComboBox.Items.Add(new ComboBoxItem { Tag = provider });
                DohProviderComboBox.SelectedItem = DohProviderComboBox.Items.Cast<ComboBoxItem>()
                    .First(item => (DohProvider)item.Tag == _settings.DohProvider);
                ApplyDohLanguage();
            }
            finally { _isLoadingSettings = loading; }
        }

        private void ApplyDohLanguage()
        {
            DohSettingsHeaderText.Text = Localization.T("DohSettingsHeader");
            foreach (ComboBoxItem item in DohProviderComboBox.Items)
                item.Content = Localization.T(DohProviders.GetLabelKey((DohProvider)item.Tag));
            DohProviderDescriptionText.Text = Localization.T(DohProviders.GetDescriptionKey(_settings.DohProvider));
            DohSettingsHintText.Text = Localization.T("DohHint");
        }

        private async void DohProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => await ApplyDohProviderSelectionAsync();

        private async Task ApplyDohProviderSelectionAsync()
        {
            if (_isLoadingSettings || DohProviderComboBox == null) return;
            if (_isBusy) { LoadDohSettingsIntoUi(); return; }
            if (DohProviderComboBox.SelectedItem is not ComboBoxItem { Tag: DohProvider provider } ||
                provider == _settings.DohProvider) return;
            var previous = _settings.DohProvider;
            _settings.DohProvider = provider;
            try { _settingsService.Save(_settings); }
            catch (Exception ex)
            {
                _settings.DohProvider = previous;
                LoadDohSettingsIntoUi();
                MessageBox.Show(Localization.T("SettingsSaveError", ex.Message), Localization.T("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            ApplyDohLanguage();
            _lastDnsLatency = null;
            PingValueText.Text = "-- ms";
            PingBadge.Visibility = Visibility.Collapsed;
            UpdatePingTargetLabel();
            if (!_userWantsConnected && !_winDivert.IsRunning)
            {
                try { dapapa.Services.DnsCacheService.Flush(); }
                catch (Exception ex) { AppendLog(Localization.T("GenericError", ex.Message)); }
                AppendLog(Localization.T("DohSelectionSaved", Localization.T(DohProviders.GetLabelKey(provider))));
                return;
            }
            _isBusy = true;
            SetConnectionVisualState(false);
            SetBusyUi(true, "DohSwitching");
            SuppressWatchdog(TimeSpan.FromSeconds(20));
            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            var ct = _operationCts.Token;
            _targetSession = NextTargetSession();
            _nextDnsRefresh = DateTimeOffset.MaxValue;
            try
            {
                await _winDivert.StopAsync();
                ct.ThrowIfCancellationRequested();
                if (!_userWantsConnected || _settings.GetEffectiveDomains().Count == 0) return;
                if (!_winDivert.Start(_settings.GetEffectiveDomains(), [], _settings.GetDohEndpoint(), _settings.GetDpiOptions()))
                    throw new InvalidOperationException(Localization.T("ProtectionStartFailed"));
                var result = await RefreshTargetsAsync(false, ct);
                if (!result.Applied || !_winDivert.IsRunning)
                    throw new InvalidOperationException(Localization.T("ProtectionStartFailed"));
                dapapa.Services.DnsCacheService.Flush();
                SetConnectionVisualState(true);
                AppendLog(Localization.T("DohSelectionApplied", Localization.T(DohProviders.GetLabelKey(provider))));
                SuppressWatchdog(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                try { await StopProtectionAsync(); }
                catch (Exception cleanupError) { AppendLog(Localization.T("GenericError", cleanupError.Message)); }
                AppendLog(Localization.T("DohSwitchFailed", ex.Message));
                MessageBox.Show(Localization.T("DohSwitchFailed", ex.Message), Localization.T("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isBusy = false;
                SetConnectionVisualState(_winDivert.IsRunning && _userWantsConnected);
                SetBusyUi(false, null);
            }
        }
    }
}
