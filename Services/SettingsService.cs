using System;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Amaurot.Services
{
    public class AppSettings
    {
        public string GameInstallationPath { get; set; } = string.Empty;
        public bool AutoLoadGameData { get; set; } = false;
        public bool DebugMode { get; set; } = false;
        public bool HideDuplicateTerritories { get; set; } = false;
        public string SapphireServerPath { get; set; } = string.Empty;
        public string SapphireBuildPath { get; set; } = string.Empty;
    }

    public class SettingsService
    {
        private static readonly string SettingsFilePath = GetCrossPlatformSettingsPath();
        private static readonly string SettingsDirectory = Path.GetDirectoryName(SettingsFilePath)!;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        private static readonly string[] GamePathIndicators = ["game", "boot"];
        private static readonly string[] SapphireRepoIndicators = ["src", "scripts", "CMakeLists.txt", "README.md"];
        private static readonly string[] SapphireBuildIndicators = ["compiledscripts", "config", "tools"];

        private AppSettings _settings;
        private readonly Action<string>? _logDebug;

        public AppSettings Settings => _settings;

        public SettingsService(Action<string>? logDebug = null)
        {
            _logDebug = logDebug;
            _settings = new AppSettings();
            LoadSettings();
        }

        private static string GetCrossPlatformSettingsPath()
        {
            try
            {
                string appDirectory = GetApplicationDirectory();
                string appRootSettings = Path.Combine(appDirectory, "settings.json");
                
                if (CanWriteToDirectory(appDirectory))
                {
                    return appRootSettings;
                }
                
                string userDirectory = GetUserDirectory();
                string userSettings = Path.Combine(userDirectory, ".ffxiv-map-editor", "settings.json");
                
                if (CanWriteToDirectory(Path.GetDirectoryName(userSettings)!))
                {
                    return userSettings;
                }
                
                string tempSettings = Path.Combine(Path.GetTempPath(), "ffxiv-map-editor", "settings.json");
                return tempSettings;
            }
            catch (Exception)
            {
                return Path.Combine(Directory.GetCurrentDirectory(), "settings.json");
            }
        }

        private static string GetApplicationDirectory()
        {
            try
            {
                string? executablePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(executablePath))
                {
                    return Path.GetDirectoryName(executablePath) ?? Directory.GetCurrentDirectory();
                }
                
                return Directory.GetCurrentDirectory();
            }
            catch
            {
                return Directory.GetCurrentDirectory();
            }
        }

        private static string GetUserDirectory()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    string? homeDir = Environment.GetEnvironmentVariable("HOME");
                    return !string.IsNullOrEmpty(homeDir) ? homeDir : "/tmp";
                }
                else
                {
                    return Directory.GetCurrentDirectory();
                }
            }
            catch
            {
                return Directory.GetCurrentDirectory();
            }
        }

        private static bool CanWriteToDirectory(string directoryPath)
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                
                string testFile = Path.Combine(directoryPath, $".write_test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    TryMigrateFromLegacyLocation();
                }
                
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);

                    if (loadedSettings != null)
                    {
                        _settings = loadedSettings;
                        _logDebug?.Invoke($"Settings loaded from: {SettingsFilePath}");
                        return;
                    }
                }

                _logDebug?.Invoke("No existing settings found, using defaults");
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error loading settings: {ex.Message}");
            }

            _settings = new AppSettings();
        }

        private void TryMigrateFromLegacyLocation()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return;      
                }
                
                string legacyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FFXIVMapEditor",
                    "settings.json");
                
                if (File.Exists(legacyPath))
                {
                    _logDebug?.Invoke($"Migrating settings from legacy location: {legacyPath}");
                    
                    if (!Directory.Exists(SettingsDirectory))
                    {
                        Directory.CreateDirectory(SettingsDirectory);
                    }
                    
                    File.Copy(legacyPath, SettingsFilePath, overwrite: false);
                    _logDebug?.Invoke($"Settings migrated to: {SettingsFilePath}");
                    
                }
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error migrating settings: {ex.Message}");
            }
        }

        public void SaveSettings()
        {
            try
            {
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                string json = JsonSerializer.Serialize(_settings, SerializerOptions);
                File.WriteAllText(SettingsFilePath, json);

                _logDebug?.Invoke($"Settings saved to: {SettingsFilePath}");
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error saving settings: {ex.Message}");
            }
        }

        public string GetSettingsLocation()
        {
            return SettingsFilePath;
        }

        public void UpdateGamePath(string path)
        {
            _settings.GameInstallationPath = path;
            SaveSettings();
        }

        public void UpdateAutoLoad(bool autoLoad)
        {
            _settings.AutoLoadGameData = autoLoad;
            SaveSettings();
        }

        public void UpdateDebugMode(bool debugMode)
        {
            Settings.DebugMode = debugMode;
            SaveSettings();
        }

        public void UpdateHideDuplicateTerritories(bool hideDuplicates)
        {
            _settings.HideDuplicateTerritories = hideDuplicates;
            SaveSettings();
        }

        public void UpdateSapphireServerPath(string path)
        {
            _settings.SapphireServerPath = path;
            SaveSettings();
        }

        public void UpdateSapphireBuildPath(string path)
        {
            _settings.SapphireBuildPath = path;
            SaveSettings();
        }

        private static bool IsValidPath(string path, string[] indicators, int requiredCount = 2)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return false;

            return indicators.Count(indicator =>
                Directory.Exists(Path.Combine(path, indicator)) || File.Exists(Path.Combine(path, indicator))) >= requiredCount;
        }

        public bool IsValidGamePath() => IsValidPath(_settings.GameInstallationPath, GamePathIndicators);

        public bool IsValidSapphireServerPath() => IsValidPath(_settings.SapphireServerPath, SapphireRepoIndicators);

        public bool IsValidSapphireBuildPath() => IsValidPath(_settings.SapphireBuildPath, SapphireBuildIndicators);

        public void OpenSapphireServerPath()
        {
            if (!IsValidSapphireServerPath())
            {
                _logDebug?.Invoke("Cannot open Sapphire Server path: invalid or not set");
                return;
            }

            try
            {
                _logDebug?.Invoke($"Opening Sapphire Server path: {_settings.SapphireServerPath}");

                Process.Start(new ProcessStartInfo
                {
                    FileName = _settings.SapphireServerPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error opening Sapphire Server path: {ex.Message}");
            }
        }

        public void OpenSapphireBuildPath()
        {
            if (!IsValidSapphireBuildPath())
            {
                _logDebug?.Invoke("Cannot open Sapphire Server build path: invalid or not set");
                return;
            }

            try
            {
                _logDebug?.Invoke($"Opening Sapphire Server build path: {_settings.SapphireBuildPath}");

                Process.Start(new ProcessStartInfo
                {
                    FileName = _settings.SapphireBuildPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error opening Sapphire Server build path: {ex.Message}");
            }
        }
    }
}