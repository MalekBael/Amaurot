using System;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

namespace map_editor
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
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FFXIVMapEditor",
            "settings.json");

        private static readonly string SettingsDirectory = Path.GetDirectoryName(SettingsFilePath)!;

        private AppSettings _settings;
        private readonly Action<string>? _logDebug;

        public AppSettings Settings => _settings;

        public SettingsService(Action<string>? logDebug = null)
        {
            _logDebug = logDebug;
            _settings = new AppSettings();
            LoadSettings();
        }

        public void LoadSettings()
        {
            try
            {
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

        public void SaveSettings()
        {
            try
            {
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string json = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(SettingsFilePath, json);

                _logDebug?.Invoke($"Settings saved to: {SettingsFilePath}");
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error saving settings: {ex.Message}");
            }
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

        public bool IsValidGamePath()
        {
            return !string.IsNullOrEmpty(_settings.GameInstallationPath) &&
                   Directory.Exists(_settings.GameInstallationPath) &&
                   (Directory.Exists(Path.Combine(_settings.GameInstallationPath, "game")) ||
                    Directory.Exists(Path.Combine(_settings.GameInstallationPath, "boot")));
        }

        public bool IsValidSapphireServerPath()
        {
            if (string.IsNullOrEmpty(_settings.SapphireServerPath))
                return false;

            if (!Directory.Exists(_settings.SapphireServerPath))
                return false;

 
            var sapphireIndicators = new[]
            {
                "src",
                "scripts",
                "CMakeLists.txt",
                "README.md"
            };

            int foundIndicators = 0;
            foreach (var indicator in sapphireIndicators)
            {
                string fullPath = Path.Combine(_settings.SapphireServerPath, indicator);
                if (Directory.Exists(fullPath) || File.Exists(fullPath))
                {
                    foundIndicators++;
                }
            }


            return foundIndicators >= 2;
        }

        public bool IsValidSapphireBuildPath()
        {
            if (string.IsNullOrEmpty(_settings.SapphireBuildPath))
                return false;

            if (!Directory.Exists(_settings.SapphireBuildPath))
                return false;


            var buildIndicators = new[]
            {
                "config",
                "tools",
                "compiledscripts"
            };

            int foundIndicators = 0;
            foreach (var indicator in buildIndicators)
            {
                string fullPath = Path.Combine(_settings.SapphireBuildPath, indicator);
                if (Directory.Exists(fullPath) || File.Exists(fullPath))
                {
                    foundIndicators++;
                }
            }

            return foundIndicators >= 2;
        }

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