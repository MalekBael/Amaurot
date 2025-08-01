using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using map_editor.Services;

namespace map_editor
{
    public partial class NpcDetailsWindow : Window
    {
        private NpcInfo _npcInfo;
        private List<NpcQuestInfo> _allQuests;
        private List<NpcQuestInfo> _filteredQuests;

        public NpcDetailsWindow(NpcInfo npcInfo, Window? owner = null)
        {
            InitializeComponent();

            // ✅ FIX: Proper window ownership
            if (owner != null)
            {
                this.Owner = owner;
                this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                if (mainWindow != null && mainWindow != this)
                {
                    this.Owner = mainWindow;
                    this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }

            this.ShowInTaskbar = false;
            this.Topmost = false;
            this.WindowState = WindowState.Normal;

            _npcInfo = npcInfo;
            _allQuests = new List<NpcQuestInfo>(npcInfo.Quests);
            _filteredQuests = new List<NpcQuestInfo>(_allQuests);

            PopulateNpcDetails();
            UpdateQuestList();
        }

        private void PopulateNpcDetails()
        {
            // Set NPC name and subtitle
            NpcNameText.Text = _npcInfo.NpcName;
            NpcSubtitleText.Text = $"NPC ID: {_npcInfo.NpcId} • {_npcInfo.QuestCount} Quest{(_npcInfo.QuestCount != 1 ? "s" : "")}";

            // Clear existing NPC info
            NpcInfoGrid.RowDefinitions.Clear();
            NpcInfoGrid.Children.Clear();

            int row = 0;

            // Add NPC details
            AddDetailRow("NPC ID:", _npcInfo.NpcId.ToString(), row++);
            AddDetailRow("Territory:", $"{_npcInfo.TerritoryName} (ID: {_npcInfo.TerritoryId})", row++);
            AddDetailRow("Map ID:", _npcInfo.MapId.ToString(), row++);
            AddDetailRow("Location:", $"({_npcInfo.MapX:F1}, {_npcInfo.MapY:F1})", row++);
            AddDetailRow("Quest Count:", _npcInfo.QuestCount.ToString(), row++);
        }

        private void AddDetailRow(string label, string value, int row)
        {
            NpcInfoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 10, 2),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            NpcInfoGrid.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = value,
                Margin = new Thickness(0, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);
            NpcInfoGrid.Children.Add(valueBlock);
        }

        private void UpdateQuestList()
        {
            QuestListBox.ItemsSource = _filteredQuests;
        }

        private void QuestSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = QuestSearchBox.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(searchText))
            {
                _filteredQuests = new List<NpcQuestInfo>(_allQuests);
            }
            else
            {
                _filteredQuests = _allQuests.Where(q =>
                    q.QuestName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    q.QuestId.ToString().Contains(searchText) ||
                    q.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            UpdateQuestList();
        }

        private void QuestListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Handle quest selection if needed
        }

        private void QuestListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (QuestListBox.SelectedItem is NpcQuestInfo selectedQuest)
            {
                ShowQuestOnMap(selectedQuest);
            }
        }

        private void ShowQuestOnMap_Click(object sender, RoutedEventArgs e)
        {
            // ✅ FIX: Use explicit WPF Button type
            if (sender is System.Windows.Controls.Button button && button.Tag is NpcQuestInfo quest)
            {
                ShowQuestOnMap(quest);
            }
        }

        private void ShowNpcOnMap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                if (mainWindow == null)
                {
                    // ✅ FIX: Use explicit WPF MessageBox type
                    System.Windows.MessageBox.Show("Could not access main window for map navigation",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Find and switch to the territory
                var targetTerritory = mainWindow.Territories?.FirstOrDefault(t => t.MapId == _npcInfo.MapId);
                if (targetTerritory == null)
                {
                    // ✅ FIX: Use explicit WPF MessageBox type
                    System.Windows.MessageBox.Show($"Could not find territory for Map ID: {_npcInfo.MapId}",
                        "Territory Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Switch to the territory
                mainWindow.TerritoryList.SelectedItem = targetTerritory;

                // Create NPC marker
                var npcMarker = new MapMarker
                {
                    Id = 3000000 + _npcInfo.NpcId, // High ID range for NPC markers
                    MapId = _npcInfo.MapId,
                    PlaceNameId = 0,
                    PlaceName = $"{_npcInfo.NpcName} (NPC)",
                    X = _npcInfo.MapX,
                    Y = _npcInfo.MapY,
                    Z = _npcInfo.MapZ,
                    IconId = 60561, // NPC icon
                    IconPath = "ui/icon/060000/060561.tex",
                    Type = MarkerType.Custom,
                    IsVisible = true
                };

                // Add marker to map
                mainWindow.AddCustomMarker(npcMarker);

                // ✅ FIX: Use explicit WPF MessageBox type
                System.Windows.MessageBox.Show($"Switched to {targetTerritory.PlaceName} and added NPC marker for {_npcInfo.NpcName}",
                    "Navigation Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // ✅ FIX: Use explicit WPF MessageBox type
                System.Windows.MessageBox.Show($"Error navigating to NPC: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowQuestOnMap(NpcQuestInfo quest)
        {
            try
            {
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                if (mainWindow == null)
                {
                    // ✅ FIX: Use explicit WPF MessageBox type
                    System.Windows.MessageBox.Show("Could not access main window for map navigation",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Find and switch to the territory
                var targetTerritory = mainWindow.Territories?.FirstOrDefault(t => t.MapId == quest.MapId);
                if (targetTerritory == null)
                {
                    // ✅ FIX: Use explicit WPF MessageBox type
                    System.Windows.MessageBox.Show($"Could not find territory for Map ID: {quest.MapId}",
                        "Territory Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Switch to the territory
                mainWindow.TerritoryList.SelectedItem = targetTerritory;

                // Create quest marker
                var questMarker = new MapMarker
                {
                    Id = 2000000 + quest.QuestId, // Same ID range as quest markers
                    MapId = quest.MapId,
                    PlaceNameId = 0,
                    PlaceName = quest.ToString(),
                    X = quest.MapX,
                    Y = quest.MapY,
                    Z = quest.MapZ,
                    IconId = 60561, // ✅ CHANGED: Use flag icon for custom quest markers from NPC window
                    IconPath = "ui/icon/060000/060561.tex", // ✅ CHANGED: Updated path for flag icon
                    Type = MarkerType.Quest,
                    IsVisible = true
                };

                // Add marker to map
                mainWindow.AddCustomMarker(questMarker);

                // ✅ FIX: Use explicit WPF MessageBox type
                System.Windows.MessageBox.Show($"Switched to {targetTerritory.PlaceName} and added quest marker for {quest}",
                    "Navigation Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // ✅ FIX: Use explicit WPF MessageBox type
                System.Windows.MessageBox.Show($"Error navigating to quest: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}