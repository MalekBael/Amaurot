using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Amaurot.Services.Entities;
using WpfApplication = System.Windows.Application;

using TerritoryInfo = Amaurot.Services.Entities.TerritoryInfo;
using QuestInfo = Amaurot.Services.Entities.QuestInfo;
using BNpcInfo = Amaurot.Services.Entities.BNpcInfo;
using FateInfo = Amaurot.Services.Entities.FateInfo;
using EventInfo = Amaurot.Services.Entities.EventInfo;

namespace Amaurot.Services
{
    public class FilterService(Action<string> logDebug) : IDisposable    
    {
        private readonly Action<string> _logDebug = logDebug;
        private DispatcherTimer? _searchDebounceTimer;
        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        _searchDebounceTimer?.Stop();
                        _searchDebounceTimer = null;
                        _logDebug?.Invoke("FilterService: Timer disposed");
                    }
                    catch (Exception ex)
                    {
                        _logDebug?.Invoke($"FilterService disposal error: {ex.Message}");
                    }
                }
                _disposed = true;
            }
        }

        ~FilterService()
        {
            Dispose(false);
        }

        #region Generic Filtering Methods

        public void FilterEntities<T>(
            string searchText,
            IEnumerable<T> source,
            ObservableCollection<T> target,
            Func<T, string, bool>? customFilter = null) where T : EntityInfoBase
        {
            if (_disposed) return;

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

        public void FilterEntitiesWithTerritoryFilter<T>(
            string searchText,
            IEnumerable<T> source,
            ObservableCollection<T> target,
            uint? currentTerritoryMapId = null,
            Func<T, string, bool>? customFilter = null) where T : EntityInfoBase
        {
            if (_disposed) return;

            target.Clear();

            var filtered = source.AsEnumerable();

            if (currentTerritoryMapId.HasValue && currentTerritoryMapId.Value > 0)
            {
                filtered = filtered.Where(entity => entity.MapId == currentTerritoryMapId.Value);
            }

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

        public void FilterCollection<T>(
            string searchText,
            IEnumerable<T> source,
            ObservableCollection<T> target,
            Func<T, string, bool> predicate)
        {
            if (_disposed) return;

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

        public void FilterQuests(string searchText,
            ObservableCollection<QuestInfo> sourceQuests,
            ObservableCollection<QuestInfo> filteredQuests)
        {
            FilterEntities(searchText, sourceQuests, filteredQuests, (quest, text) =>
                quest.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                quest.Id.ToString().Contains(text, StringComparison.OrdinalIgnoreCase) ||
                quest.JournalGenre.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        public void FilterBNpcs(string searchText,
            ObservableCollection<BNpcInfo> sourceBNpcs,
            ObservableCollection<BNpcInfo> filteredBNpcs)
        {
            FilterEntities(searchText, sourceBNpcs, filteredBNpcs, (bnpc, text) =>
                bnpc.BNpcName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                bnpc.BNpcBaseId.ToString().Contains(text, StringComparison.OrdinalIgnoreCase) ||
                bnpc.TribeName.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        public void FilterEvents(string searchText,
            ObservableCollection<EventInfo> sourceEvents,
            ObservableCollection<EventInfo> filteredEvents)
        {
            FilterEntities(searchText, sourceEvents, filteredEvents, (evt, text) =>
                evt.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                evt.EventId.ToString().Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        public void FilterFates(string searchText,
            ObservableCollection<FateInfo> sourceFates,
            ObservableCollection<FateInfo> filteredFates)
        {
            FilterEntities(searchText, sourceFates, filteredFates, (fate, text) =>
                fate.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                fate.FateId.ToString().Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        #endregion Type-Specific Filtering Methods (Legacy Support)

        #region Territory Filtering with Debounce (Advanced)

        public void FilterTerritoriesWithDebounce(string searchText,
            ObservableCollection<TerritoryInfo> sourceTerritories,
            ObservableCollection<TerritoryInfo> filteredTerritories,
            bool hideDuplicates,
            bool debugLoggingEnabled,
            Action applyFilterCallback)
        {
            if (_disposed) return;

            _searchDebounceTimer?.Stop();
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };

            _searchDebounceTimer.Tick += (s, args) =>
            {
                if (!_disposed)
                {
                    _searchDebounceTimer.Stop();
                    ApplyTerritoryFilters(searchText, sourceTerritories, filteredTerritories,
                        hideDuplicates, debugLoggingEnabled);
                    applyFilterCallback?.Invoke();
                }
            };

            _searchDebounceTimer.Start();
        }

        private void ApplyTerritoryFilters(string searchText,
            ObservableCollection<TerritoryInfo> sourceTerritories,
            ObservableCollection<TerritoryInfo> filteredTerritories,
            bool hideDuplicates,
            bool debugLoggingEnabled)
        {
            if (_disposed) return;

            if (debugLoggingEnabled)
            {
                _logDebug($"=== ApplyTerritoryFilters START ===");
                _logDebug($"Hide duplicates: {hideDuplicates}");
                _logDebug($"Search text: '{searchText}'");
            }

            Task.Run(() =>
            {
                if (_disposed) return;

                var filteredTerritoriesTemp = sourceTerritories.AsEnumerable();

                if (!string.IsNullOrEmpty(searchText))
                {
                    filteredTerritoriesTemp = filteredTerritoriesTemp.Where(territory =>
                        territory.PlaceName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        territory.Id.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        territory.Region.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        territory.TerritoryNameId.Contains(searchText, StringComparison.OrdinalIgnoreCase));
                }

                if (hideDuplicates)
                {
                    var seenPlaceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var territoriesToKeep = new List<TerritoryInfo>();

                    foreach (var territory in filteredTerritoriesTemp)
                    {
                        string placeName = territory.PlaceName ?? territory.Name ?? "";

                        if (placeName.StartsWith("[Territory ID:", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(placeName))
                        {
                            territoriesToKeep.Add(territory);
                            continue;
                        }

                        if (seenPlaceNames.Add(placeName))
                        {
                            territoriesToKeep.Add(territory);
                        }
                    }

                    filteredTerritoriesTemp = territoriesToKeep;
                }

                var finalResults = filteredTerritoriesTemp.ToList();

                try
                {
                    if (!_disposed && WpfApplication.Current != null)
                    {
                        WpfApplication.Current.Dispatcher.Invoke(() =>
                        {
                            if (!_disposed)
                            {
                                filteredTerritories.Clear();
                                foreach (var territory in finalResults)
                                {
                                    filteredTerritories.Add(territory);
                                }
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logDebug?.Invoke($"Error updating UI in ApplyTerritoryFilters: {ex.Message}");
                }

                if (debugLoggingEnabled && !_disposed)
                {
                    _logDebug($"Filtered territories count: {finalResults.Count}");
                    _logDebug($"=== ApplyTerritoryFilters END ===");
                }
            });
        }

        #endregion Territory Filtering with Debounce (Advanced)

        #region Private Helper Methods

        private static bool DefaultFilter<T>(T entity, string searchText) where T : EntityInfoBase
        {
            return entity.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                   entity.Id.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                   entity.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }

        #endregion Private Helper Methods

        #region Instance Content Filtering

        public void FilterInstanceContents(string searchText, ObservableCollection<InstanceContentInfo> source, ObservableCollection<InstanceContentInfo> target)
        {
            if (_disposed) return;

            try
            {
                var filtered = string.IsNullOrEmpty(searchText)
                    ? [.. source]
                    : source.Where(instance =>
                        instance.InstanceName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        instance.Id.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        instance.InstanceContentTypeName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                    ).ToList();

                target.Clear();
                foreach (var instance in filtered)
                {
                    target.Add(instance);
                }
            }
            catch (Exception ex)
            {
                _logDebug?.Invoke($"Error filtering instance contents: {ex.Message}");
            }
        }

        #endregion Instance Content Filtering

        #region Quest Battle Filtering

        public void FilterQuestBattles(string searchText, ObservableCollection<QuestBattleInfo> source, ObservableCollection<QuestBattleInfo> target)
        {
            try
            {
                FilterEntities(searchText, source, target, (questBattle, text) =>
                    questBattle.QuestBattleName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    questBattle.LayerName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    questBattle.TerritoryName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    questBattle.QuestBattleId.ToString().Contains(text) ||
                    questBattle.AssetType.Contains(text, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logDebug($"Error filtering Quest Battles: {ex.Message}");
            }
        }

        #endregion Quest Battle Filtering
    }

    #region Legacy Classes (for backward compatibility)

    [Obsolete("Use FilterService instead")]
    public class GenericFilterService<T>(Action<string> logDebug) where T : EntityInfoBase
    {
        private readonly FilterService _filterService = new(logDebug);

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

    [Obsolete("Use FilterService instead")]
    public class SearchFilterService(Action<string> logDebug)
    {
        private readonly FilterService _filterService = new(logDebug);

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