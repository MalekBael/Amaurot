using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using map_editor.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace map_editor
{
    public partial class NpcQuestPopupWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly NpcInfo _npcInfo;

        public NpcQuestPopupWindow(NpcInfo npcInfo, MainWindow mainWindow)
        {
            InitializeComponent();
            _npcInfo = npcInfo;
            _mainWindow = mainWindow;

            InitializeWindow();
            PopulateNpcQuests();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void InitializeWindow()
        {
            this.Title = $"Quests - {_npcInfo.NpcName}";
            this.Width = 500;
            this.Height = 400;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.Owner = _mainWindow;
            this.ShowInTaskbar = false;
        }

        private async void NavigateToQuest(NpcQuestInfo questInfo)
        {
            try
            {
                var targetTerritory = _mainWindow.Territories.FirstOrDefault(t => t.MapId == questInfo.MapId);

                if (targetTerritory != null)
                {
                    this.Close();

                    _mainWindow.TerritoryList.SelectedItem = targetTerritory;

                    _mainWindow.LogDebug($"Navigated to quest '{questInfo.QuestName}' in {targetTerritory.PlaceName}");
                }
                else
                {
                    WpfMessageBox.Show($"Could not find territory for quest '{questInfo.QuestName}'",
                        "Territory Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _mainWindow.LogDebug($"Error navigating to quest: {ex.Message}");
                WpfMessageBox.Show($"Error navigating to quest: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PopulateNpcQuests()
        {
            NpcNameText.Text = _npcInfo.NpcName;
            NpcLocationText.Text = $"{_npcInfo.TerritoryName} ({_npcInfo.MapX:F1}, {_npcInfo.MapY:F1})";
            QuestCountText.Text = $"{_npcInfo.QuestCount} Quest{(_npcInfo.QuestCount != 1 ? "s" : "")}";

            QuestListBox.ItemsSource = _npcInfo.Quests.OrderBy(q => q.LevelRequired).ThenBy(q => q.QuestName);
        }

        private void QuestListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (QuestListBox.SelectedItem is NpcQuestInfo selectedQuest)
            {
                NavigateToQuest(selectedQuest);
            }
        }
    }
}