using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls; // ✅ ADD: Missing using for TextBlock

// Add using statements for entity types
using TerritoryInfo = Amaurot.Services.Entities.TerritoryInfo;
using QuestInfo = Amaurot.Services.Entities.QuestInfo;
using BNpcInfo = Amaurot.Services.Entities.BNpcInfo;
using FateInfo = Amaurot.Services.Entities.FateInfo;
using NpcInfo = Amaurot.Services.Entities.NpcInfo; // ✅ ADD: Missing using for NpcInfo

namespace Amaurot.Services
{
    public class UIUpdateService
    {
        public void UpdateTerritoryCount(ObservableCollection<TerritoryInfo> territories, TextBlock? countTextBlock)
        {
            if (countTextBlock != null)
            {
                countTextBlock.Text = $"({territories.Count})";
            }
        }

        public void UpdateQuestCount(ObservableCollection<QuestInfo> quests, TextBlock? countTextBlock)
        {
            if (countTextBlock != null)
            {
                countTextBlock.Text = $"({quests.Count})";
            }
        }

        public void UpdateBNpcCount(ObservableCollection<BNpcInfo> bnpcs, TextBlock? countTextBlock)
        {
            if (countTextBlock != null)
            {
                countTextBlock.Text = $"({bnpcs.Count})";
            }
        }

        public void UpdateFateCount(ObservableCollection<FateInfo> fates, TextBlock? countTextBlock)
        {
            if (countTextBlock != null)
            {
                countTextBlock.Text = $"({fates.Count})";
            }
        }

        public void UpdateNpcCount(ObservableCollection<NpcInfo> npcs, TextBlock? countTextBlock)
        {
            if (countTextBlock != null)
            {
                countTextBlock.Text = $"({npcs.Count})";
            }
        }
    }
}