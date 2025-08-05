using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace Amaurot
{
    public class UIUpdateService
    {
        public void UpdateQuestCount(ObservableCollection<QuestInfo> filteredQuests, TextBlock? questCountText)
        {
            if (questCountText != null)
            {
                questCountText.Text = $"({filteredQuests.Count})";
            }
        }

        public void UpdateBNpcCount(ObservableCollection<BNpcInfo> filteredBNpcs, TextBlock? bnpcCountText)
        {
            if (bnpcCountText != null)
            {
                bnpcCountText.Text = $"({filteredBNpcs.Count})";
            }
        }

        public void UpdateTerritoryCount(ObservableCollection<TerritoryInfo> filteredTerritories, TextBlock? territoryCountText)
        {
            if (territoryCountText != null)
            {
                territoryCountText.Text = $"({filteredTerritories.Count})";
            }
        }

        public void UpdateEventCount(ObservableCollection<EventInfo> filteredEvents, TextBlock? eventCountText)
        {
            if (eventCountText != null)
            {
                eventCountText.Text = $"({filteredEvents.Count})";
            }
        }

        public void UpdateFateCount(ObservableCollection<FateInfo> filteredFates, TextBlock? fateCountText)
        {
            if (fateCountText != null)
            {
                fateCountText.Text = $"({filteredFates.Count})";
            }
        }
    }
}