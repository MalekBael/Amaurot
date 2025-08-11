using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Amaurot.Services.Entities;
using Amaurot.Services;

namespace Amaurot
{
    public partial class InstanceContentDetailsWindow : Window
    {
        private readonly InstanceContentInfo _instanceContent;
        private readonly MainWindow _mainWindow;
        private readonly InstanceScriptService? _instanceScriptService;  // ✅ Use InstanceScriptService

        public InstanceContentDetailsWindow(InstanceContentInfo instanceContentInfo, MainWindow? mainWindow, InstanceScriptService? instanceScriptService = null)
        {
            InitializeComponent();

            _instanceContent = instanceContentInfo;
            _mainWindow = mainWindow!;
            _instanceScriptService = instanceScriptService;  // ✅ Store InstanceScriptService

            // ✅ FIX: Manual positioning without owner relationship (like QuestDetailsWindow)
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (mainWindow != null)
            {
                // Position relative to owner without setting Owner property
                this.Left = mainWindow.Left + (mainWindow.Width - this.Width) / 2;
                this.Top = mainWindow.Top + (mainWindow.Height - this.Height) / 2;
            }

            // ✅ FIX: Critical properties to prevent app minimization
            this.ShowInTaskbar = false;
            this.Topmost = false;
            this.WindowState = WindowState.Normal;

            PopulateInstanceContentDetails(instanceContentInfo);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void PopulateInstanceContentDetails(InstanceContentInfo instanceContent)
        {
            InstanceContentTitleText.Text = instanceContent.InstanceName;

            string subtitle = $"Instance Content ID: {instanceContent.Id}";
            if (instanceContent.LevelRequired > 0)
            {
                subtitle += $" • Level: {instanceContent.LevelRequired}";
            }
            if (!string.IsNullOrEmpty(instanceContent.InstanceContentTypeName))
            {
                subtitle += $" • Type: {instanceContent.InstanceContentTypeName}";
            }
            InstanceContentSubtitleText.Text = subtitle;

            InstanceContentDetailsGrid.RowDefinitions.Clear();
            InstanceContentDetailsGrid.Children.Clear();

            int row = 0;

            AddSectionHeader(InstanceContentDetailsGrid, "Basic Information", row++);
            AddDetailRow(InstanceContentDetailsGrid, "Instance Content ID:", instanceContent.Id.ToString(), row++);
            AddDetailRow(InstanceContentDetailsGrid, "Name:", instanceContent.InstanceName, row++);
            if (instanceContent.LevelRequired > 0)
            {
                AddDetailRow(InstanceContentDetailsGrid, "Level Requirement:", instanceContent.LevelRequired.ToString(), row++);
            }
            AddDetailRow(InstanceContentDetailsGrid, "Type:", instanceContent.InstanceContentTypeName, row++);

            if (instanceContent.ContentFinderConditionId > 0 || instanceContent.SortKey > 0)
            {
                AddSectionHeader(InstanceContentDetailsGrid, "Content Information", row++);

                if (instanceContent.ContentFinderConditionId > 0)
                {
                    AddDetailRow(InstanceContentDetailsGrid, "Content Finder Condition ID:", instanceContent.ContentFinderConditionId.ToString(), row++);
                }

                if (instanceContent.SortKey > 0)
                {
                    AddDetailRow(InstanceContentDetailsGrid, "Sort Key:", instanceContent.SortKey.ToString(), row++);
                }
            }

            if (instanceContent.TimeLimit > 0)
            {
                AddSectionHeader(InstanceContentDetailsGrid, "Time and Difficulty", row++);
                AddDetailRow(InstanceContentDetailsGrid, "Time Limit:", $"{instanceContent.TimeLimit} minutes", row++);
            }

            if (instanceContent.NewPlayerBonusExp > 0 || instanceContent.NewPlayerBonusGil > 0 || instanceContent.InstanceClearGil > 0)
            {
                AddSectionHeader(InstanceContentDetailsGrid, "Rewards", row++);

                if (instanceContent.NewPlayerBonusExp > 0)
                {
                    AddDetailRow(InstanceContentDetailsGrid, "New Player Bonus EXP:", instanceContent.NewPlayerBonusExp.ToString(), row++);
                }

                if (instanceContent.NewPlayerBonusGil > 0)
                {
                    AddDetailRow(InstanceContentDetailsGrid, "New Player Bonus Gil:", instanceContent.NewPlayerBonusGil.ToString(), row++);
                }

                if (instanceContent.InstanceClearGil > 0)
                {
                    AddDetailRow(InstanceContentDetailsGrid, "Instance Clear Gil:", instanceContent.InstanceClearGil.ToString(), row++);
                }
            }

            AddSectionHeader(InstanceContentDetailsGrid, "Development", row++);
            AddScriptFileRowWithButtons(instanceContent, row++);

            AddSectionHeader(InstanceContentDetailsGrid, "Additional Information", row++);
            AddDetailRow(InstanceContentDetailsGrid, "Repeatable:", instanceContent.IsRepeatable ? "Yes" : "No", row++);
        }

        private void CloseButton_Click_1(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private List<string> FindScriptFiles(InstanceContentInfo instanceContent)
        {
            if (_instanceScriptService == null)
            {
                return new List<string>();
            }

            try
            {
                // Determine if this is a Hard mode dungeon
                bool isHardMode = instanceContent.InstanceName.Contains("(Hard)", StringComparison.OrdinalIgnoreCase);
                
                // Use InstanceScriptService to find the script
                var scriptPath = _instanceScriptService.FindInstanceScript(
                    instanceContent.InstanceName, 
                    instanceContent.Id, 
                    isHardMode);

                if (!string.IsNullOrEmpty(scriptPath))
                {
                    return new List<string> { scriptPath };
                }

                return new List<string>();
            }
            catch (Exception ex)
            {
                // Log error if needed, but return empty list
                return new List<string>();
            }
        }

        private string[] GetDefaultSearchPaths()
        {
            var paths = new List<string>();

            try
            {
                var settingsService = GetSettingsService();
                if (settingsService?.IsValidSapphireServerPath() == true)
                {
                    var serverPath = settingsService.Settings.SapphireServerPath;
                    
                    // ✅ CORRECT: Only instance script directories
                    var instanceScriptPaths = new[]
                    {
                        Path.Combine(serverPath, "src", "scripts", "instances"),
                        Path.Combine(serverPath, "src", "scripts", "instances", "dungeons"),
                        Path.Combine(serverPath, "src", "scripts", "instances", "raids"),
                        Path.Combine(serverPath, "src", "scripts", "instances", "trials"),
                        Path.Combine(serverPath, "src", "scripts", "instances", "guildhests"),
                        Path.Combine(serverPath, "src", "scripts", "instances", "pvp"),
                        Path.Combine(serverPath, "src", "scripts", "instances", "questbattles")
                    };

                    paths.AddRange(instanceScriptPaths);
                }
            }
            catch { }

            return paths.ToArray();
        }

        private void AddScriptFileRowWithButtons(InstanceContentInfo instanceContent, int row)
        {
            InstanceContentDetailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = "Instance Scripts:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            InstanceContentDetailsGrid.Children.Add(labelBlock);

            var valuePanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 3, 0, 3)
            };

            var scriptFiles = FindScriptFiles(instanceContent);

            if (scriptFiles.Count > 0)
            {
                // VS Code button
                var vscodeButton = new System.Windows.Controls.Button
                {
                    Content = "VSCode",
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 215)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 90, 158)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = $"Open {scriptFiles.Count} script file(s) in VS Code"
                };
                vscodeButton.Click += (s, e) => OpenScriptFiles(scriptFiles, useVSCode: true);
                valuePanel.Children.Add(vscodeButton);

                // Visual Studio button
                var vsButton = new System.Windows.Controls.Button
                {
                    Content = "Visual Studio",
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 33, 122)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(84, 23, 102)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = $"Open {scriptFiles.Count} script file(s) in Visual Studio"
                };
                vsButton.Click += (s, e) => OpenScriptFiles(scriptFiles, useVSCode: false);
                valuePanel.Children.Add(vsButton);
                // Status info
                var infoText = new TextBlock
                {
                    Text = $"✓ {scriptFiles.Count} file(s) found",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.Green),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0)
                };
                valuePanel.Children.Add(infoText);
            }
            else
            {
                // Configure path button
                var configButton = new System.Windows.Controls.Button
                {
                    Content = "⚙️ Configure Sapphire Path",
                    Padding = new Thickness(8, 4, 8, 4),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 120, 0)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = "Configure Sapphire Server path in settings"
                };
                configButton.Click += (s, e) => OpenSettings();
                valuePanel.Children.Add(configButton);

                // Status info
                var infoText = new TextBlock
                {
                    Text = "✗ Configure Sapphire path to find scripts",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = $"Searches for '{instanceContent.InstanceName}' in src/scripts/instances/ subdirectories"
                };
                valuePanel.Children.Add(infoText);
            }

            Grid.SetRow(valuePanel, row);
            Grid.SetColumn(valuePanel, 1);
            InstanceContentDetailsGrid.Children.Add(valuePanel);
        }

        private void OpenSettings()
        {
            try
            {
                var settingsService = GetSettingsService();
                if (settingsService != null)
                {
                    // ✅ FIX: No Owner relationship
                    var settingsWindow = new SettingsWindow(settingsService);
                    settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    settingsWindow.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not open settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddDetailRow(Grid grid, string label, string value, int row)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = value,
                Margin = new Thickness(0, 3, 0, 3),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);
        }

        private void AddSectionHeader(Grid grid, string title, int row)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerBlock = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.DarkBlue),
                Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 5),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(headerBlock, row);
            Grid.SetColumn(headerBlock, 0);
            Grid.SetColumnSpan(headerBlock, 2);
            grid.Children.Add(headerBlock);
        }

        private void OpenScriptFiles(List<string> files, bool useVSCode)
        {
            try
            {
                if (files.Count == 1)
                {
                    OpenFileInEditor(files[0], useVSCode);
                }
                else
                {
                    ShowFileSelectionDialog(files, _instanceContent.InstanceName, useVSCode);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening script files: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowFileSelectionDialog(List<string> files, string instanceName, bool useVSCode)
        {
            var dialog = new Window
            {
                Title = $"Script Files for {instanceName}",
                Width = 600,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, // ✅ CHANGE
                ShowInTaskbar = false,
                Topmost = false
                // ✅ FIX: No Owner property
            };

            var listBox = new System.Windows.Controls.ListBox
            {
                ItemsSource = files.Select(f => new { FullPath = f, FileName = Path.GetFileName(f), Directory = Path.GetDirectoryName(f) }),
                Margin = new Thickness(10)
            };

            var template = new DataTemplate();
            var stackPanel = new FrameworkElementFactory(typeof(StackPanel));

            var fileName = new FrameworkElementFactory(typeof(TextBlock));
            fileName.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("FileName"));
            fileName.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);

            var directory = new FrameworkElementFactory(typeof(TextBlock));
            directory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Directory"));
            directory.SetValue(TextBlock.FontSizeProperty, 10.0);
            directory.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Gray);

            stackPanel.AppendChild(fileName);
            stackPanel.AppendChild(directory);
            template.VisualTree = stackPanel;
            listBox.ItemTemplate = template;

            listBox.MouseDoubleClick += (s, e) =>
            {
                if (listBox.SelectedItem != null)
                {
                    var selected = (dynamic)listBox.SelectedItem;
                    OpenFileInEditor(selected.FullPath, useVSCode);
                    dialog.Close();
                }
            };

            dialog.Content = listBox;
            dialog.ShowDialog();
        }

        private void OpenFileInEditor(string filePath, bool useVSCode)
        {
            try
            {
                // ✅ Use the injected InstanceScriptService directly (like QuestDetailsWindow)
                if (_instanceScriptService != null)
                {
                    bool success = useVSCode 
                        ? _instanceScriptService.OpenInVSCode(filePath)
                        : _instanceScriptService.OpenInVisualStudio(filePath);
                        
                    if (!success)
                    {
                        string editorName = useVSCode ? "Visual Studio Code" : "Visual Studio";
                        System.Windows.MessageBox.Show($"Failed to open file in {editorName}.\n\nPlease ensure {editorName} is installed and accessible.",
                            "Error Opening Script", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    // Fallback if service not available
                    if (useVSCode)
                    {
                        var process = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "code",
                                Arguments = $"\"{filePath}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                            }
                        };
                        process.Start();
                    }
                    else
                    {
                        var process = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "devenv",
                                Arguments = $"\"{filePath}\"",
                                UseShellExecute = true
                            }
                        };
                        process.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback to default application
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) 
                    { 
                        UseShellExecute = true 
                    });
                }
                catch
                {
                    System.Windows.MessageBox.Show($"Could not open file: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ✅ KEEP: The GetSettingsService method for settings access
        private SettingsService? GetSettingsService()
        {
            try
            {
                var servicesField = typeof(MainWindow).GetField("_services",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (servicesField?.GetValue(_mainWindow) is var services && services != null)
                {
                    var getMethod = services.GetType().GetMethod("Get")?.MakeGenericMethod(typeof(SettingsService));
                    return getMethod?.Invoke(services, null) as SettingsService;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}