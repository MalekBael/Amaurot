using System;
using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;

namespace map_editor
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly Action<string>? _logDebug;

        public SettingsWindow(SettingsService settingsService, Action<string>? logDebug = null)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _logDebug = logDebug;
            
            LoadCurrentSettings();
            UpdatePathStatus();
            UpdateSapphirePathStatus();
            UpdateSapphireBuildPathStatus();
            UpdateSettingsLocationText();
        }

        private void LoadCurrentSettings()
        {
            var settings = _settingsService.Settings;
            
            GamePathTextBox.Text = settings.GameInstallationPath;
            SapphirePathTextBox.Text = settings.SapphireServerPath;
            SapphireBuildPathTextBox.Text = settings.SapphireBuildPath;
            AutoLoadCheckBox.IsChecked = settings.AutoLoadGameData;
            DebugModeCheckBox.IsChecked = settings.DebugMode;
            HideDuplicateTerritoriesCheckBox.IsChecked = settings.HideDuplicateTerritories;
        }

        private void UpdatePathStatus()
        {
            string path = GamePathTextBox.Text.Trim();
            
            if (string.IsNullOrEmpty(path))
            {
                PathStatusText.Text = "No path specified";
                PathStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            }
            else if (Directory.Exists(path))
            {
                bool hasGameFolder = Directory.Exists(Path.Combine(path, "game"));
                bool hasBootFolder = Directory.Exists(Path.Combine(path, "boot"));
                
                if (hasGameFolder || hasBootFolder)
                {
                    PathStatusText.Text = "✓ Valid FFXIV installation path";
                    PathStatusText.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    PathStatusText.Text = "⚠ Path exists but doesn't appear to be FFXIV installation";
                    PathStatusText.Foreground = System.Windows.Media.Brushes.Orange;
                }
            }
            else
            {
                PathStatusText.Text = "✗ Path does not exist";
                PathStatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void UpdateSapphirePathStatus()
        {
            string path = SapphirePathTextBox.Text.Trim();
            
            if (string.IsNullOrEmpty(path))
            {
                SapphirePathStatusText.Text = "No path specified";
                SapphirePathStatusText.Foreground = System.Windows.Media.Brushes.Gray;
                OpenSapphireButton.IsEnabled = false;
            }
            else if (Directory.Exists(path))
            {

                var indicators = new[] { "src", "scripts", "CMakeLists.txt", "README.md" };
                int foundIndicators = 0;
                
                foreach (var indicator in indicators)
                {
                    string fullPath = Path.Combine(path, indicator);
                    if (Directory.Exists(fullPath) || File.Exists(fullPath))
                    {
                        foundIndicators++;
                    }
                }
                
                if (foundIndicators >= 2)
                {
                    SapphirePathStatusText.Text = "✓ Valid Sapphire Server repository";
                    SapphirePathStatusText.Foreground = System.Windows.Media.Brushes.Green;
                    OpenSapphireButton.IsEnabled = true;
                }
                else
                {
                    SapphirePathStatusText.Text = "⚠ Path exists but doesn't appear to be Sapphire Server repository";
                    SapphirePathStatusText.Foreground = System.Windows.Media.Brushes.Orange;
                    OpenSapphireButton.IsEnabled = true; 
                }
            }
            else
            {
                SapphirePathStatusText.Text = "✗ Path does not exist";
                SapphirePathStatusText.Foreground = System.Windows.Media.Brushes.Red;
                OpenSapphireButton.IsEnabled = false;
            }
        }

        private void UpdateSapphireBuildPathStatus()
        {
            string path = SapphireBuildPathTextBox.Text.Trim();
            
            if (string.IsNullOrEmpty(path))
            {
                SapphireBuildPathStatusText.Text = "No path specified";
                SapphireBuildPathStatusText.Foreground = System.Windows.Media.Brushes.Gray;
                OpenSapphireBuildButton.IsEnabled = false;
            }
            else if (Directory.Exists(path))
            {
                var indicators = new[] { "compiledscripts", "config", "tools" };
                int foundIndicators = 0;
                var foundItems = new System.Collections.Generic.List<string>();
                
                foreach (var indicator in indicators)
                {
                    string fullPath = Path.Combine(path, indicator);
                    if (Directory.Exists(fullPath) || File.Exists(fullPath))
                    {
                        foundIndicators++;
                        foundItems.Add(indicator);
                    }
                }
                
                if (foundIndicators >= 2)
                {
                    SapphireBuildPathStatusText.Text = "✓ Valid Sapphire Server build directory";
                    SapphireBuildPathStatusText.Foreground = System.Windows.Media.Brushes.Green;
                    OpenSapphireBuildButton.IsEnabled = true;
                }
                else
                {
                    SapphireBuildPathStatusText.Text = "⚠ Path exists but doesn't appear to be Sapphire Server build directory";
                    SapphireBuildPathStatusText.Foreground = System.Windows.Media.Brushes.Orange;
                    OpenSapphireBuildButton.IsEnabled = true; 
                }
            }
            else
            {
                SapphireBuildPathStatusText.Text = "✗ Path does not exist";
                SapphireBuildPathStatusText.Foreground = System.Windows.Media.Brushes.Red;
                OpenSapphireBuildButton.IsEnabled = false;
            }
        }

        private void UpdateSettingsLocationText()
        {
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FFXIVMapEditor",
                "settings.json");
            
            SettingsLocationText.Text = $"Settings are stored at: {settingsPath}";
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select FFXIV game installation folder",
                UseDescriptionForTitle = true,
                SelectedPath = GamePathTextBox.Text
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                GamePathTextBox.Text = dialog.SelectedPath;
                UpdatePathStatus();
            }
        }

        private void BrowseSapphireButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select Sapphire Server repository folder",
                UseDescriptionForTitle = true,
                SelectedPath = SapphirePathTextBox.Text
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                SapphirePathTextBox.Text = dialog.SelectedPath;
                UpdateSapphirePathStatus();
            }
        }

        private void BrowseSapphireBuildButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select Sapphire Server build directory",
                UseDescriptionForTitle = true,
                SelectedPath = SapphireBuildPathTextBox.Text
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                SapphireBuildPathTextBox.Text = dialog.SelectedPath;
                UpdateSapphireBuildPathStatus();
            }
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            UpdatePathStatus();
            
            string path = GamePathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                WpfMessageBox.Show("Please specify a path first.", "Test Path", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!Directory.Exists(path))
            {
                WpfMessageBox.Show("The specified path does not exist.", "Test Path", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool hasGameFolder = Directory.Exists(Path.Combine(path, "game"));
            bool hasBootFolder = Directory.Exists(Path.Combine(path, "boot"));
            
            if (hasGameFolder || hasBootFolder)
            {
                WpfMessageBox.Show("✓ Valid FFXIV installation detected!", "Test Path", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                WpfMessageBox.Show("The path exists but doesn't appear to contain a valid FFXIV installation.\n\n" +
                               "Make sure you select the main FFXIV folder that contains 'game' and 'boot' subdirectories.", 
                               "Test Path", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void TestSapphireButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateSapphirePathStatus();
            
            string path = SapphirePathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                WpfMessageBox.Show("Please specify a Sapphire Server path first.", "Test Path", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!Directory.Exists(path))
            {
                WpfMessageBox.Show("The specified path does not exist.", "Test Path", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var indicators = new[] { "src", "scripts", "CMakeLists.txt", "README.md" };
            int foundIndicators = 0;
            var foundItems = new System.Collections.Generic.List<string>();
            
            foreach (var indicator in indicators)
            {
                string fullPath = Path.Combine(path, indicator);
                if (Directory.Exists(fullPath) || File.Exists(fullPath))
                {
                    foundIndicators++;
                    foundItems.Add(indicator);
                }
            }
            
            if (foundIndicators >= 2)
            {
                WpfMessageBox.Show($"✓ Valid Sapphire Server repository detected!\n\nFound: {string.Join(", ", foundItems)}", 
                    "Test Path", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                WpfMessageBox.Show($"The path exists but doesn't appear to contain a Sapphire Server repository.\n\n" +
                               $"Expected to find at least 2 of: {string.Join(", ", indicators)}\n" +
                               $"Found: {(foundItems.Count > 0 ? string.Join(", ", foundItems) : "none")}", 
                               "Test Path", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void TestSapphireBuildButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateSapphireBuildPathStatus();
            
            string path = SapphireBuildPathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                WpfMessageBox.Show("Please specify a Sapphire Server build path first.", "Test Path", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!Directory.Exists(path))
            {
                WpfMessageBox.Show("The specified path does not exist.", "Test Path", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var indicators = new[] { "sapphire_datacenter.exe", "sapphire_datacenter", "bin", "lib", "tools", "share" };
            int foundIndicators = 0;
            var foundItems = new System.Collections.Generic.List<string>();
            
            foreach (var indicator in indicators)
            {
                string fullPath = Path.Combine(path, indicator);
                if (Directory.Exists(fullPath) || File.Exists(fullPath))
                {
                    foundIndicators++;
                    foundItems.Add(indicator);
                }
            }
            
            if (foundIndicators >= 2)
            {
                WpfMessageBox.Show($"✓ Valid Sapphire Server build directory detected!\n\nFound: {string.Join(", ", foundItems)}", 
                    "Test Path", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                WpfMessageBox.Show($"The path exists but doesn't appear to contain a Sapphire Server build directory.\n\n" +
                               $"Expected to find at least 2 of: {string.Join(", ", indicators)}\n" +
                               $"Found: {(foundItems.Count > 0 ? string.Join(", ", foundItems) : "none")}", 
                               "Test Path", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenSapphireButton_Click(object sender, RoutedEventArgs e)
        {
            string path = SapphirePathTextBox.Text.Trim();
            
            if (string.IsNullOrEmpty(path))
            {
                WpfMessageBox.Show("Please specify a Sapphire Server path first.", "Open Folder", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!Directory.Exists(path))
            {
                WpfMessageBox.Show("The specified path does not exist.", "Open Folder", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _logDebug?.Invoke($"Opening Sapphire Server folder: {path}");
                
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error opening Sapphire Server folder: {ex.Message}");
                WpfMessageBox.Show($"Failed to open folder: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSapphireBuildButton_Click(object sender, RoutedEventArgs e)
        {
            string path = SapphireBuildPathTextBox.Text.Trim();
            
            if (string.IsNullOrEmpty(path))
            {
                WpfMessageBox.Show("Please specify a Sapphire Server build path first.", "Open Folder", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!Directory.Exists(path))
            {
                WpfMessageBox.Show("The specified path does not exist.", "Open Folder", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _logDebug?.Invoke($"Opening Sapphire Server build folder: {path}");
                
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error opening Sapphire Server build folder: {ex.Message}");
                WpfMessageBox.Show($"Failed to open folder: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AutoLoadCheckBox_Changed(object sender, RoutedEventArgs e)
        {

        }

        private void DebugModeCheckBox_Changed(object sender, RoutedEventArgs e)
        {

        }

        private void HideDuplicateTerritoriesCheckBox_Changed(object sender, RoutedEventArgs e)
        {

        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = WpfMessageBox.Show("Are you sure you want to reset all settings to their defaults?", 
                "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                GamePathTextBox.Text = "";
                SapphirePathTextBox.Text = "";
                SapphireBuildPathTextBox.Text = "";
                AutoLoadCheckBox.IsChecked = false;
                DebugModeCheckBox.IsChecked = false;
                HideDuplicateTerritoriesCheckBox.IsChecked = false;
                UpdatePathStatus();
                UpdateSapphirePathStatus();
                UpdateSapphireBuildPathStatus();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            _settingsService.UpdateGamePath(GamePathTextBox.Text.Trim());
            _settingsService.UpdateSapphireServerPath(SapphirePathTextBox.Text.Trim());
            _settingsService.UpdateSapphireBuildPath(SapphireBuildPathTextBox.Text.Trim());
            _settingsService.UpdateAutoLoad(AutoLoadCheckBox.IsChecked == true);
            _settingsService.UpdateDebugMode(DebugModeCheckBox.IsChecked == true);
            _settingsService.UpdateHideDuplicateTerritories(HideDuplicateTerritoriesCheckBox.IsChecked == true);
            
            _logDebug?.Invoke("Settings saved successfully");
            
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}