using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Amaurot.Services.Entities;
using WpfApplication = System.Windows.Application;

// Add proper using statements for entity types
using TerritoryInfo = Amaurot.Services.Entities.TerritoryInfo;
using QuestInfo = Amaurot.Services.Entities.QuestInfo;
using BNpcInfo = Amaurot.Services.Entities.BNpcInfo;
using FateInfo = Amaurot.Services.Entities.FateInfo;
using EventInfo = Amaurot.Services.Entities.EventInfo;

namespace Amaurot.Services
{
    /// <summary>
    /// Unified filter service that combines generic and specific filtering capabilities
    /// </summary>
    public class FilterService
    {
        private readonly Action<string> _logDebug;
        private DispatcherTimer? _searchDebounceTimer;

        public FilterService(Action<string> logDebug)
        {
            _logDebug = logDebug;
        }

        #region Generic Filtering Methods

        /// <summary>
        /// Generic filter method for entities inheriting from EntityInfoBase
        /// </summary>
        public void FilterEntities<T>(
            string searchText,
            IEnumerable<T> source,
            ObservableCollection<T> target,
            Func<T, string, bool>? customFilter = null) where T : EntityInfoBase
        {
            target.Clear();

            var filtered = string.IsNullOrEmpty(searchText)
                ? source
                : source.Where(entity =>
                    customFilter?.Invoke(entity, searchText) ??
                    DefaultFilter(entity, searchText));

            foreach (var item in filtered)
            {
                target.Add(item);
            }

            _logDebug($"Filtered {typeof(T).Name}: {target.Count} items");
        }

        /// <summary>
        /// Generic filter method with territory filtering capability
        /// </summary>
        public void FilterEntitiesWithTerritoryFilter<T>(
            string searchText,
            IEnumerable<T> source,
            ObservableCollection<T> target,
            uint? currentTerritoryMapId = null,
            Func<T, string, bool>? customFilter = null) where T : EntityInfoBase
        {
            target.Clear();

            var filtered = source.AsEnumerable();

            // Apply territory filter if specified
            if (currentTerritoryMapId.HasValue && currentTerritoryMapId.Value > 0)
            {
                filtered = filtered.Where(entity => entity.MapId == currentTerritoryMapId.Value);
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered.Where(entity =>
                    customFilter?.Invoke(entity, searchText) ??
                    DefaultFilter(entity, searchText));
            }

            foreach (var item in filtered)
            {
                target.Add(item);
            }

            _logDebug($"Filtered {typeof(T).Name}: {target.Count} items (Territory: {currentTerritoryMapId?.ToString() ?? "All"})");
        }

        /// <summary>
        /// Non-generic filter method for collections that don't inherit from EntityInfoBase
        /// </summary>
        public void FilterCollection<T>(
            string searchText,
            IEnumerable<T> source,
            ObservableCollection<T> target,
            Func<T, string, bool> predicate)
        {
            target.Clear();

            var filtered = string.IsNullOrEmpty(searchText)
                ? source
                : source.Where(item => predicate(item, searchText));

            foreach (var item in filtered)
            {
                target.Add(item);
            }

            _logDebug($"Filtered {typeof(T).Name}: {target.Count} items");
        }

        #endregion Generic Filtering Methods

        #region Type-Specific Filtering Methods (Legacy Support)

        /// <summary>
        /// Filter quests using original SearchFilterService logic
        /// </summary>
        public void FilterQuests(string searchText,
            ObservableCollection<QuestInfo> sourceQuests,
            ObservableCollection<QuestInfo> filteredQuests)
        {
            FilterEntities(searchText, sourceQuests, filteredQuests, (quest, text) =>
                quest.Name.ToLower().Contains(text.ToLower()) ||
                quest.Id.ToString().Contains(text) ||
                quest.JournalGenre.ToLower().Contains(text.ToLower()));
        }

        /// <summary>
        /// Filter BNpcs using original SearchFilterService logic
        /// </summary>
        public void FilterBNpcs(string searchText,
            ObservableCollection<BNpcInfo> sourceBNpcs,
            ObservableCollection<BNpcInfo> filteredBNpcs)
        {
            FilterEntities(searchText, sourceBNpcs, filteredBNpcs, (bnpc, text) =>
                bnpc.BNpcName.ToLower().Contains(text.ToLower()) ||
                bnpc.BNpcBaseId.ToString().Contains(text) ||
                bnpc.TribeName.ToLower().Contains(text.ToLower()));
        }

        /// <summary>
        /// Filter events using original SearchFilterService logic
        /// </summary>
        public void FilterEvents(string searchText,
            ObservableCollection<EventInfo> sourceEvents,
            ObservableCollection<EventInfo> filteredEvents)
        {
            FilterEntities(searchText, sourceEvents, filteredEvents, (evt, text) =>
                evt.Name.ToLower().Contains(text.ToLower()) ||
                evt.EventId.ToString().Contains(text));
        }

        /// <summary>
        /// Filter fates using original SearchFilterService logic
        /// </summary>
        public void FilterFates(string searchText,
            ObservableCollection<FateInfo> sourceFates,
            ObservableCollection<FateInfo> filteredFates)
        {
            FilterEntities(searchText, sourceFates, filteredFates, (fate, text) =>
                fate.Name.ToLower().Contains(text.ToLower()) ||
                fate.FateId.ToString().Contains(text));
        }

        #endregion Type-Specific Filtering Methods (Legacy Support)

        #region Territory Filtering with Debounce (Advanced)

        /// <summary>
        /// Filter territories with debounce and async processing (original SearchFilterService logic)
        /// </summary>
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

        #endregion Territory Filtering with Debounce (Advanced)

        #region Private Helper Methods

        /// <summary>
        /// Default filter logic for entities inheriting from EntityInfoBase
        /// </summary>
        private bool DefaultFilter<T>(T entity, string searchText) where T : EntityInfoBase
        {
            return entity.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                   entity.Id.ToString().Contains(searchText) ||
                   entity.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }

        #endregion Private Helper Methods
    }

    #region Legacy Classes (for backward compatibility)

    /// <summary>
    /// Legacy generic filter service - redirects to FilterService
    /// </summary>
    [Obsolete("Use FilterService instead")]
    public class GenericFilterService<T> where T : EntityInfoBase
    {
        private readonly FilterService _filterService;

        public GenericFilterService(Action<string> logDebug)
        {
            _filterService = new FilterService(logDebug);
        }

        public void FilterEntities(
            string searchText,
            IEnumerable<T> source,
            ObservableCollection<T> target,
            Func<T, string, bool>? customFilter = null)
        {
            _filterService.FilterEntities(searchText, source, target, customFilter);
        }

        public void FilterEntitiesWithTerritoryFilter(
            string searchText,
            IEnumerable<T> source,
            ObservableCollection<T> target,
            uint? currentTerritoryMapId = null,
            Func<T, string, bool>? customFilter = null)
        {
            _filterService.FilterEntitiesWithTerritoryFilter(searchText, source, target, currentTerritoryMapId, customFilter);
        }
    }

    /// <summary>
    /// Legacy search filter service - redirects to FilterService
    /// </summary>
    [Obsolete("Use FilterService instead")]
    public class SearchFilterService
    {
        private readonly FilterService _filterService;

        public SearchFilterService(Action<string> logDebug)
        {
            _filterService = new FilterService(logDebug);
        }

        public void FilterQuests(string searchText,
            ObservableCollection<QuestInfo> sourceQuests,
            ObservableCollection<QuestInfo> filteredQuests)
        {
            _filterService.FilterQuests(searchText, sourceQuests, filteredQuests);
        }

        public void FilterBNpcs(string searchText,
            ObservableCollection<BNpcInfo> sourceBNpcs,
            ObservableCollection<BNpcInfo> filteredBNpcs)
        {
            _filterService.FilterBNpcs(searchText, sourceBNpcs, filteredBNpcs);
        }

        public void FilterEvents(string searchText,
            ObservableCollection<EventInfo> sourceEvents,
            ObservableCollection<EventInfo> filteredEvents)
        {
            _filterService.FilterEvents(searchText, sourceEvents, filteredEvents);
        }

        public void FilterFates(string searchText,
            ObservableCollection<FateInfo> sourceFates,
            ObservableCollection<FateInfo> filteredFates)
        {
            _filterService.FilterFates(searchText, sourceFates, filteredFates);
        }

        public void FilterTerritoriesWithDebounce(string searchText,
            ObservableCollection<TerritoryInfo> sourceTerritories,
            ObservableCollection<TerritoryInfo> filteredTerritories,
            bool hideDuplicates,
            bool debugLoggingEnabled,
            Action applyFilterCallback)
        {
            _filterService.FilterTerritoriesWithDebounce(searchText, sourceTerritories, filteredTerritories,
                hideDuplicates, debugLoggingEnabled, applyFilterCallback);
        }
    }

    #endregion Legacy Classes (for backward compatibility)
}