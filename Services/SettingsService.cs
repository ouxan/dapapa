using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using dapapa.Models;

namespace dapapa.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private readonly bool _userSettings;
        private readonly Action<string, string> _backup;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        public string SettingsFilePath => _settingsPath;
        public event Action<string>? Log;
        private void Emit(string line) => Log?.Invoke(line);

        public SettingsService() : this(Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData), "dapapa", "settings.json"))
        {
            _userSettings = true;
        }

        internal SettingsService(string path, Action<string, string>? backup = null)
        {
            _settingsPath = path;
            _backup = backup ?? ((source, destination) => File.Copy(source, destination, false));
        }

        public static string GetDefaultLanguage()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\dapapa");
                if (key?.GetValue("InstallerLanguage") is string instLang && !string.IsNullOrWhiteSpace(instLang))
                {
                    return instLang.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? "tr" : "en";
                }
                if (key?.GetValue("Language") is string lang && !string.IsNullOrWhiteSpace(lang))
                {
                    return lang.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? "tr" : "en";
                }
            }
            catch { }

            return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
                ? "tr"
                : "en";
        }

        public AppSettings Load()
        {
            string? installerLanguage = null;
            if (_userSettings)
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\dapapa", writable: true);
                    if (key?.GetValue("InstallerLanguage") is string language && !string.IsNullOrWhiteSpace(language))
                        installerLanguage = language.StartsWith("tr", StringComparison.OrdinalIgnoreCase) ? "tr" : "en";
                }
                catch { }
            }
            Emit(Localization.T("SettingsReading", _settingsPath));
            AppSettings settings;
            bool migrated = false;
            if (File.Exists(_settingsPath))
            {
                try
                {
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath))
                        ?? throw new InvalidDataException(Localization.T("SettingsInvalid"));
                    if (settings.TargetListVersion > AppSettings.CurrentTargetListVersion)
                        throw new InvalidDataException(Localization.T("SettingsNewerVersion"));
                    if (settings.TargetListVersion < AppSettings.CurrentTargetListVersion)
                    {
                        string directory = Path.GetDirectoryName(_settingsPath)!;
                        string backupPath = Path.Combine(directory, "settings.pre-manual-targets-" +
                            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".bak");
                        _backup(_settingsPath, backupPath);
                        settings.Apps = [];
                        settings.TargetListVersion = AppSettings.CurrentTargetListVersion;
                        Save(settings);
                        migrated = true;
                        Emit(Localization.T("TargetsMigrated", backupPath));
                    }
                }
                catch (Exception ex)
                {
                    Emit(Localization.T("SettingsReadError", ex.Message));
                    throw;
                }
            }
            else
            {
                settings = new AppSettings { TargetListVersion = AppSettings.CurrentTargetListVersion,
                    Language = _userSettings ? GetDefaultLanguage() : "en" };
                Save(settings);
            }
            settings.NormalizeTargets();
            if (installerLanguage != null)
            {
                settings.Language = installerLanguage;
                Save(settings);
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\dapapa", writable: true);
                    key?.DeleteValue("InstallerLanguage", false);
                }
                catch { }
            }
            else if (string.IsNullOrWhiteSpace(settings.Language)) settings.Language = _userSettings ? GetDefaultLanguage() : "en";
            if (migrated) Emit(Localization.T("NoActiveTargets"));
            return settings;
        }

        public void Save(AppSettings settings)
        {
            string? temporary = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temporary, _settingsPath, true);
                Emit(Localization.T("SettingsSaved"));
            }
            catch (Exception ex)
            {
                Emit(Localization.T("SettingsSaveError", ex.Message));
                throw;
            }
            finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
