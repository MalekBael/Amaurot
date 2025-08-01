using System;
using System.IO;
using System.Linq;
using System.Windows;
using map_editor.Services;  // ✅ Add this using directive
using WinForms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;

namespace map_editor
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly Action<string>? _logDebug;

        // ✅ Updated with C# 12 collection initialization syntax
        private static readonly string[] GamePathIndicators = ["game", "boot"];
        private static readonly string[] SapphireRepoIndicators = ["src", "scripts", "CMakeLists.txt", "README.md"];
        private static readonly string[] SapphireBuildIndicators = ["tools", "bin", "lib"];

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

        private static (string statusText, System.Windows.Media.Brush color, bool isValid) ValidatePath(string path, string[] indicators, int requiredCount = 2)
        {
            if (string.IsNullOrEmpty(path))
                return ("No path specified", System.Windows.Media.Brushes.Gray, false);

            if (!Directory.Exists(path))
                return ("✗ Path does not exist", System.Windows.Media.Brushes.Red, false);

            int foundCount = indicators.Count(indicator =>
                Directory.Exists(Path.Combine(path, indicator)) || File.Exists(Path.Combine(path, indicator)));

            return foundCount >= requiredCount
                ? ("✓ Valid path detected", System.Windows.Media.Brushes.Green, true)
                : ("⚠ Path exists but doesn't appear valid", System.Windows.Media.Brushes.Orange, true);
        }

        private void UpdatePathStatus()
        {
            var (text, color, _) = ValidatePath(GamePathTextBox.Text.Trim(), GamePathIndicators);
            PathStatusText.Text = text.Replace("Valid path", "Valid FFXIV installation path");
            PathStatusText.Foreground = color;
        }

        private void UpdateSapphirePathStatus()
        {
            var (text, color, isValid) = ValidatePath(SapphirePathTextBox.Text.Trim(),
                SapphireRepoIndicators);
            SapphirePathStatusText.Text = text.Replace("Valid path", "Valid Sapphire Server repository");
            SapphirePathStatusText.Foreground = color;
            OpenSapphireButton.IsEnabled = isValid;
        }

        private void UpdateSapphireBuildPathStatus()
        {
            var (text, color, isValid) = ValidatePath(SapphireBuildPathTextBox.Text.Trim(),
                SapphireBuildIndicators);
            SapphireBuildPathStatusText.Text = text.Replace("Valid path", "Valid Sapphire Server build directory");
            SapphireBuildPathStatusText.Foreground = color;
            OpenSapphireBuildButton.IsEnabled = isValid;
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

        // ✅ Made this method static
        private static void TestPath(string path, string[] indicators, string pathType, int requiredCount = 2)
        {
            if (string.IsNullOrEmpty(path))
            {
                WpfMessageBox.Show($"Please specify a {pathType} path first.", "Test Path",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!Directory.Exists(path))
            {
                WpfMessageBox.Show("The specified path does not exist.", "Test Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var foundItems = indicators.Where(indicator =>
                Directory.Exists(Path.Combine(path, indicator)) || File.Exists(Path.Combine(path, indicator))).ToList();

            string message = foundItems.Count >= requiredCount
                ? $"✓ Valid {pathType} detected!\n\nFound: {string.Join(", ", foundItems)}"
                : $"Path exists but doesn't appear to be a valid {pathType}.\n\n" +
                  $"Expected at least {requiredCount} of: {string.Join(", ", indicators)}\n" +
                  $"Found: {(foundItems.Count > 0 ? string.Join(", ", foundItems) : "none")}";

            WpfMessageBox.Show(message, "Test Path", MessageBoxButton.OK,
                foundItems.Count >= requiredCount ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            UpdatePathStatus();
            TestPath(GamePathTextBox.Text.Trim(), GamePathIndicators, "FFXIV installation");
        }

        private void TestSapphireButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateSapphirePathStatus();
            TestPath(SapphirePathTextBox.Text.Trim(), SapphireRepoIndicators, "Sapphire Server repository");
        }

        private void TestSapphireBuildButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateSapphireBuildPathStatus();
            TestPath(SapphireBuildPathTextBox.Text.Trim(), SapphireBuildIndicators, "Sapphire Server build directory");
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