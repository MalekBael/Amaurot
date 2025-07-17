using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace map_editor
{
    public class SearchFilterService
    {
        private readonly Action<string> _logDebug;
        private DispatcherTimer? _searchDebounceTimer;

        public SearchFilterService(Action<string> logDebug)
        {
            _logDebug = logDebug;
        }

        public void FilterQuests(string searchText,
            ObservableCollection<QuestInfo> sourceQuests,
            ObservableCollection<QuestInfo> filteredQuests)
        {
            filteredQuests.Clear();

            foreach (var quest in sourceQuests)
            {
                bool matches = string.IsNullOrEmpty(searchText) ||
                              quest.Name.ToLower().Contains(searchText.ToLower()) ||
                              quest.Id.ToString().Contains(searchText) ||
                              quest.JournalGenre.ToLower().Contains(searchText.ToLower());

                if (matches)
                {
                    filteredQuests.Add(quest);
                }
            }
        }

        public void FilterBNpcs(string searchText,
            ObservableCollection<BNpcInfo> sourceBNpcs,
            ObservableCollection<BNpcInfo> filteredBNpcs)
        {
            filteredBNpcs.Clear();

            foreach (var bnpc in sourceBNpcs)
            {
                bool matches = string.IsNullOrEmpty(searchText) ||
                              bnpc.BNpcName.ToLower().Contains(searchText.ToLower()) ||
                              bnpc.BNpcBaseId.ToString().Contains(searchText) ||
                              bnpc.TribeName.ToLower().Contains(searchText.ToLower());

                if (matches)
                {
                    filteredBNpcs.Add(bnpc);
                }
            }
        }

        public void FilterEvents(string searchText,
            ObservableCollection<EventInfo> sourceEvents,
            ObservableCollection<EventInfo> filteredEvents)
        {
            filteredEvents.Clear();

            foreach (var evt in sourceEvents)
            {
                bool matches = string.IsNullOrEmpty(searchText) ||
                              evt.Name.ToLower().Contains(searchText.ToLower()) ||
                              evt.EventId.ToString().Contains(searchText);

                if (matches)
                {
                    filteredEvents.Add(evt);
                }
            }
        }

        public void FilterFates(string searchText,
            ObservableCollection<FateInfo> sourceFates,
            ObservableCollection<FateInfo> filteredFates)
        {
            filteredFates.Clear();

            foreach (var fate in sourceFates)
            {
                bool matches = string.IsNullOrEmpty(searchText) ||
                              fate.Name.ToLower().Contains(searchText.ToLower()) ||
                              fate.FateId.ToString().Contains(searchText);

                if (matches)
                {
                    filteredFates.Add(fate);
                }
            }
        }

        public void FilterTerritoriesWithDebounce(string searchText,
            ObservableCollection<TerritoryInfo> sourceTerritories,
            ObservableCollection<TerritoryInfo> filteredTerritories,
            bool hideDuplicates,
            bool debugLoggingEnabled,
            Action applyFilterCallback)
        {
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };

            _searchDebounceTimer.Tick += (s, args) =>
            {
                _searchDebounceTimer.Stop();
                ApplyTerritoryFilters(searchText, sourceTerritories, filteredTerritories,
                    hideDuplicates, debugLoggingEnabled);
                applyFilterCallback?.Invoke();
            };

            _searchDebounceTimer.Start();
        }

        private void ApplyTerritoryFilters(string searchText,
            ObservableCollection<TerritoryInfo> sourceTerritories,
            ObservableCollection<TerritoryInfo> filteredTerritories,
            bool hideDuplicates,
            bool debugLoggingEnabled)
        {
            if (debugLoggingEnabled)
            {
                _logDebug($"=== ApplyTerritoryFilters START ===");
                _logDebug($"Hide duplicates: {hideDuplicates}");
                _logDebug($"Search text: '{searchText}'");
            }

            Task.Run(() =>
            {
                var filteredTerritoriesTemp = sourceTerritories.AsEnumerable();

                if (!string.IsNullOrEmpty(searchText))
                {
                    filteredTerritoriesTemp = filteredTerritoriesTemp.Where(territory =>
                        territory.PlaceName.ToLower().Contains(searchText.ToLower()) ||
                        territory.Id.ToString().Contains(searchText) ||
                        territory.Region.ToLower().Contains(searchText.ToLower()) ||
                        territory.TerritoryNameId.ToLower().Contains(searchText.ToLower()));
                }

                if (hideDuplicates)
                {
                    var seenPlaceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var territoriesToKeep = new List<TerritoryInfo>();

                    foreach (var territory in filteredTerritoriesTemp)
                    {
                        string placeName = territory.PlaceName ?? territory.Name ?? "";

                        if (placeName.StartsWith("[Territory ID:") || string.IsNullOrEmpty(placeName))
                        {
                            territoriesToKeep.Add(territory);
                            continue;
                        }

                        if (!seenPlaceNames.Contains(placeName))
                        {
                            seenPlaceNames.Add(placeName);
                            territoriesToKeep.Add(territory);
                        }
                    }

                    filteredTerritoriesTemp = territoriesToKeep;
                }

                var finalResults = filteredTerritoriesTemp.ToList();

                WpfApplication.Current.Dispatcher.Invoke(() =>
                {
                    filteredTerritories.Clear();
                    foreach (var territory in finalResults)
                    {
                        filteredTerritories.Add(territory);
                    }
                });

                if (debugLoggingEnabled)
                {
                    _logDebug($"Filtered territories count: {finalResults.Count}");
                    _logDebug($"=== ApplyTerritoryFilters END ===");
                }
            });
        }
    }
}