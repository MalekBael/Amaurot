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
        // ✅ ENHANCED: Cross-platform settings location
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

        // ✅ ADD: Cross-platform settings path logic
        private static string GetCrossPlatformSettingsPath()
        {
            try
            {
                // First priority: Application root directory (cross-platform)
                string appDirectory = GetApplicationDirectory();
                string appRootSettings = Path.Combine(appDirectory, "settings.json");
                
                // If we can write to the app directory, use it
                if (CanWriteToDirectory(appDirectory))
                {
                    return appRootSettings;
                }
                
                // Second priority: User's home directory (cross-platform)
                string userDirectory = GetUserDirectory();
                string userSettings = Path.Combine(userDirectory, ".ffxiv-map-editor", "settings.json");
                
                // If we can write to user directory, use it
                if (CanWriteToDirectory(Path.GetDirectoryName(userSettings)!))
                {
                    return userSettings;
                }
                
                // Final fallback: Temp directory (should always work)
                string tempSettings = Path.Combine(Path.GetTempPath(), "ffxiv-map-editor", "settings.json");
                return tempSettings;
            }
            catch (Exception)
            {
                // Ultimate fallback: current directory
                return Path.Combine(Directory.GetCurrentDirectory(), "settings.json");
            }
        }

        private static string GetApplicationDirectory()
        {
            try
            {
                // Get the directory where the executable is located
                string? executablePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(executablePath))
                {
                    return Path.GetDirectoryName(executablePath) ?? Directory.GetCurrentDirectory();
                }
                
                // Fallback to current directory
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
                // Cross-platform user directory detection
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Windows: Use USERPROFILE or fallback to ApplicationData
                    return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // Linux/macOS: Use HOME environment variable
                    string? homeDir = Environment.GetEnvironmentVariable("HOME");
                    return !string.IsNullOrEmpty(homeDir) ? homeDir : "/tmp";
                }
                else
                {
                    // Unknown platform: use current directory
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
                // Ensure directory exists
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                
                // Test write permissions by creating a temporary file
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
                // ✅ ENHANCED: Try to migrate from old location if new location doesn't exist
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

        // ✅ ADD: Migration from legacy Windows AppData location
        private void TryMigrateFromLegacyLocation()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return; // Only attempt migration on Windows
                }
                
                string legacyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FFXIVMapEditor",
                    "settings.json");
                
                if (File.Exists(legacyPath))
                {
                    _logDebug?.Invoke($"Migrating settings from legacy location: {legacyPath}");
                    
                    // Ensure new directory exists
                    if (!Directory.Exists(SettingsDirectory))
                    {
                        Directory.CreateDirectory(SettingsDirectory);
                    }
                    
                    // Copy the file
                    File.Copy(legacyPath, SettingsFilePath, overwrite: false);
                    _logDebug?.Invoke($"Settings migrated to: {SettingsFilePath}");
                    
                    // Optionally remove old file (commented out for safety)
                    // File.Delete(legacyPath);
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

        // ✅ ADD: Method to get current settings location for debugging
        public string GetSettingsLocation()
        {
            return SettingsFilePath;
        }

        // ✅ EXISTING: All other methods remain the same
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