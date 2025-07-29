using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace map_editor
{
    public partial class QuestDetailsWindow : Window
    {
        public QuestDetailsWindow(QuestInfo questInfo)
        {
            InitializeComponent();
            PopulateQuestDetails(questInfo);
        }

        private void PopulateQuestDetails(QuestInfo questInfo)
        {
            // Set the title
            QuestTitleText.Text = questInfo.Name;
            
            // Enhanced subtitle with location if available
            string subtitle = $"Quest ID: {questInfo.Id}";
            if (!string.IsNullOrEmpty(questInfo.PlaceName))
            {
                subtitle += $" • Location: {questInfo.PlaceName}";
            }
            QuestSubtitleText.Text = subtitle;

            // Show appropriate badges
            if (questInfo.IsMainScenarioQuest)
            {
                MSQBadge.Visibility = Visibility.Visible;
            }
            
            if (questInfo.IsFeatureQuest)
            {
                FeatureBadge.Visibility = Visibility.Visible;
            }

            // Set level badge
            if (questInfo.ClassJobLevelRequired > 0)
            {
                LevelText.Text = $"LEVEL {questInfo.ClassJobLevelRequired}";
            }
            else
            {
                LevelBadge.Visibility = Visibility.Collapsed;
            }

            // Clear existing details
            QuestDetailsGrid.RowDefinitions.Clear();
            QuestDetailsGrid.Children.Clear();

            // Add quest details with better organization
            int row = 0;
            
            // Basic Information Section
            AddSectionHeader("Basic Information", row++);
            AddDetailRow("Quest ID:", questInfo.Id.ToString(), row++);
            AddDetailRow("Name:", questInfo.Name, row++);
            AddDetailRow("Quest Type:", GetQuestTypeDescription(questInfo), row++);
            
            // Location Information Section
            if (!string.IsNullOrEmpty(questInfo.PlaceName) || questInfo.MapId > 0)
            {
                AddSectionHeader("Location Information", row++);
                
                if (!string.IsNullOrEmpty(questInfo.PlaceName))
                {
                    AddDetailRow("Location:", questInfo.PlaceName, row++);
                }
                
                if (questInfo.MapId > 0)
                {
                    AddDetailRow("Map ID:", questInfo.MapId.ToString(), row++);
                }
                
                if (questInfo.PlaceNameId > 0)
                {
                    AddDetailRow("Place Name ID:", questInfo.PlaceNameId.ToString(), row++);
                }
            }
            
            // Requirements Section
            if (questInfo.ClassJobLevelRequired > 0 || questInfo.ClassJobCategoryId > 0 || questInfo.PreviousQuestId > 0)
            {
                AddSectionHeader("Requirements", row++);
                
                if (questInfo.ClassJobLevelRequired > 0)
                {
                    AddDetailRow("Required Level:", questInfo.ClassJobLevelRequired.ToString(), row++);
                }
                
                if (questInfo.ClassJobCategoryId > 0)
                {
                    AddDetailRow("Class/Job Category ID:", questInfo.ClassJobCategoryId.ToString(), row++);
                }
                
                if (!string.IsNullOrEmpty(questInfo.ClassJobCategoryName))
                {
                    AddDetailRow("Class/Job Category:", questInfo.ClassJobCategoryName, row++);
                }
                
                if (questInfo.PreviousQuestId > 0)
                {
                    AddDetailRow("Previous Quest ID:", questInfo.PreviousQuestId.ToString(), row++);
                }
            }
            
            // Rewards Section
            if (questInfo.ExpReward > 0 || questInfo.GilReward > 0)
            {
                AddSectionHeader("Rewards", row++);
                
                if (questInfo.ExpReward > 0)
                {
                    AddDetailRow("EXP Reward:", questInfo.ExpReward.ToString(), row++);
                }
                
                if (questInfo.GilReward > 0)
                {
                    AddDetailRow("Gil Reward:", questInfo.GilReward.ToString(), row++);
                }
            }
            
            // Additional Information Section
            AddSectionHeader("Additional Information", row++);
            
            if (!string.IsNullOrEmpty(questInfo.JournalGenre))
            {
                AddDetailRow("Journal Genre:", questInfo.JournalGenre, row++);
            }
            
            if (questInfo.IconId > 0)
            {
                AddDetailRow("Icon ID:", questInfo.IconId.ToString(), row++);
            }
            
            if (questInfo.IsRepeatable)
            {
                AddDetailRow("Repeatable:", "Yes", row++);
            }
        }

        private void AddDetailRow(string label, string value, int row)
        {
            QuestDetailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            QuestDetailsGrid.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = value,
                Margin = new Thickness(0, 3, 0, 3),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);
            QuestDetailsGrid.Children.Add(valueBlock);
        }

        private void AddSectionHeader(string title, int row)
        {
            QuestDetailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
            QuestDetailsGrid.Children.Add(headerBlock);
        }

        private string GetQuestTypeDescription(QuestInfo questInfo)
        {
            if (questInfo.IsMainScenarioQuest)
                return "Main Scenario Quest";
            else if (questInfo.IsFeatureQuest)
                return "Feature Quest";
            else
                return "Side Quest";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}