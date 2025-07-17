using SaintCoinach;
using SaintCoinach.Xiv;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace map_editor
{
    public class DataLoaderService
    {
        private readonly ARealmReversed? _realm;
        private readonly Action<string> _logDebug;

        public DataLoaderService(ARealmReversed? realm, Action<string> logDebug)
        {
            _realm = realm;
            _logDebug = logDebug;
        }

        public async Task<List<QuestInfo>> LoadQuestsAsync()
        {
            var tempQuests = new List<QuestInfo>();

            try
            {
                if (_realm != null)
                {
                    _logDebug("Loading quests from SaintCoinach...");
                    var questSheet = _realm.GameData.GetSheet<Quest>();

                    int processedCount = 0;
                    int totalCount = questSheet.Count();

                    foreach (var quest in questSheet)
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(quest.Name)) continue;

                            var questInfo = new QuestInfo
                            {
                                Id = (uint)quest.Key,
                                Name = quest.Name?.ToString() ?? "",
                                JournalGenre = quest.JournalGenre?.Name?.ToString() ?? "",
                                ClassJobCategoryId = 0,
                                ClassJobLevelRequired = 0,
                                ClassJobCategoryName = "",
                                IsMainScenarioQuest = quest.JournalGenre?.Name?.ToString().Contains("Main Scenario") == true,
                                IsFeatureQuest = quest.JournalGenre?.Name?.ToString().Contains("Feature") == true,
                                PreviousQuestId = 0,
                                ExpReward = 0,
                                GilReward = 0
                            };

                            // Extract additional quest data...
                            ExtractQuestDetails(quest, questInfo);

                            tempQuests.Add(questInfo);
                            processedCount++;

                            if (processedCount % 500 == 0)
                            {
                                _logDebug($"Processed {processedCount}/{totalCount} quests...");
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log error if needed
                        }
                    }

                    tempQuests.Sort((a, b) => a.Id.CompareTo(b.Id));
                    _logDebug($"Loaded {tempQuests.Count} quests");
                }
            }
            catch (Exception ex)
            {
                _logDebug($"Error loading quests: {ex.Message}");
            }

            return tempQuests;
        }

        public async Task<List<BNpcInfo>> LoadBNpcsAsync()
        {
            return await Task.Run(() =>
            {
                _logDebug("Starting to load BNpcs...");
                var tempBNpcs = new List<BNpcInfo>();

                try
                {
                    if (_realm != null)
                    {
                        _logDebug("Loading BNpcs from SaintCoinach...");

                        SaintCoinach.Xiv.IXivSheet<SaintCoinach.Xiv.BNpcName>? bnpcNameSheet = null;

                        try
                        {
                            bnpcNameSheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.BNpcName>();
                            if (bnpcNameSheet != null)
                            {
                                _logDebug($"Found BNpcName sheet with {bnpcNameSheet.Count} entries");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logDebug($"Failed to load BNpcName sheet: {ex.Message}");
                            return tempBNpcs;
                        }

                        if (bnpcNameSheet == null)
                        {
                            _logDebug("ERROR: BNpcName sheet is null!");
                            return tempBNpcs;
                        }

                        int processedCount = 0;
                        int skippedCount = 0;
                        int totalCount = bnpcNameSheet.Count;
                        _logDebug($"Processing {totalCount} BNpcName entries...");

                        foreach (var bnpcName in bnpcNameSheet)
                        {
                            try
                            {
                                string name = bnpcName.Singular ?? "";

                                if (string.IsNullOrWhiteSpace(name))
                                {
                                    skippedCount++;
                                    continue;
                                }

                                uint nameId = (uint)bnpcName.Key;

                                var bnpcInfo = new BNpcInfo
                                {
                                    BNpcNameId = nameId,
                                    BNpcName = name,
                                    BNpcBaseId = 0,
                                    Title = "",
                                    TribeId = 0,
                                    TribeName = ""
                                };

                                tempBNpcs.Add(bnpcInfo);
                                processedCount++;
                                
                                if (processedCount % 1000 == 0)
                                {
                                    _logDebug($"Processed {processedCount}/{totalCount} BNpc names (skipped {skippedCount} empty)...");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logDebug($"Error processing BNpcName {bnpcName.Key}: {ex.Message}");
                            }
                        }

                        _logDebug($"Finished processing BNpcs: {processedCount} processed, {skippedCount} skipped");
                        tempBNpcs.Sort((a, b) => string.Compare(a.BNpcName, b.BNpcName, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        _logDebug("Realm is null, cannot load BNpcs");
                    }
                }
                catch (Exception ex)
                {
                    _logDebug($"Critical error loading BNpcs: {ex.Message}\n{ex.StackTrace}");
                }

                _logDebug($"Returning {tempBNpcs.Count} BNpcs from LoadBNpcsAsync");
                return tempBNpcs;
            });
        }

        public async Task<List<EventInfo>> LoadEventsAsync()
        {
            return await Task.Run(() =>
            {
                _logDebug("Starting to load events...");
                var tempEvents = new List<EventInfo>();

                try
                {
                    if (_realm != null)
                    {
                        _logDebug("Loading events from SaintCoinach...");
                        var eventSheet = _realm.GameData.GetSheet("Event");

                        if (eventSheet != null)
                        {
                            foreach (var eventRow in eventSheet)
                            {
                                try
                                {
                                    string eventName = eventRow.AsString("Name") ?? "";
                                    if (string.IsNullOrWhiteSpace(eventName)) continue;

                                    var eventInfo = new EventInfo
                                    {
                                        Id = (uint)eventRow.Key,
                                        EventId = (uint)eventRow.Key,
                                        Name = eventName,
                                        EventType = "Event",
                                        Description = eventRow.AsString("Description") ?? "",
                                        TerritoryId = 0,
                                        TerritoryName = ""
                                    };

                                    tempEvents.Add(eventInfo);
                                }
                                catch (Exception ex)
                                {
                                    _logDebug($"Failed to process event with key {eventRow.Key}. Error: {ex.Message}");
                                }
                            }

                            tempEvents.Sort((a, b) => a.EventId.CompareTo(b.EventId));
                            _logDebug($"Loaded {tempEvents.Count} events");
                        }
                    }
                    else
                    {
                        _logDebug("Realm is null, cannot load events");
                    }
                }
                catch (Exception ex)
                {
                    _logDebug($"Critical error loading events: {ex.Message}\n{ex.StackTrace}");
                }

                _logDebug($"Returning {tempEvents.Count} events from LoadEventsAsync");
                return tempEvents;
            });
        }

        public async Task<List<FateInfo>> LoadFatesAsync()
        {
            return await Task.Run(() =>
            {
                var tempFates = new List<FateInfo>();

                try
                {
                    if (_realm != null)
                    {
                        var fateSheet = _realm.GameData.GetSheet("Fate");

                        foreach (var fateRow in fateSheet)
                        {
                            try
                            {
                                string fateName = fateRow.AsString("Name") ?? "";
                                if (string.IsNullOrWhiteSpace(fateName)) continue;

                                // Since we can't reliably look up Level data due to key mismatch,
                                // we'll create FATEs without specific location data for now
                                var fateInfo = new FateInfo
                                {
                                    Id = (uint)fateRow.Key,
                                    FateId = (uint)fateRow.Key,
                                    Name = fateName,
                                    Description = fateRow.AsString("Description") ?? "",
                                    Level = (uint)Math.Max(0, fateRow.AsInt32("ClassJobLevel")),
                                    ClassJobLevel = (uint)Math.Max(0, fateRow.AsInt32("ClassJobLevel")),
                                    TerritoryId = 0, // We don't have reliable territory data without Level lookup
                                    TerritoryName = "",
                                    IconId = (uint)Math.Max(0, fateRow.AsInt32("Icon")),
                                    X = 0, // Default coordinates
                                    Y = 0,
                                    Z = 0
                                };

                                tempFates.Add(fateInfo);
                            }
                            catch (Exception ex)
                            {
                                _logDebug($"Failed to process fate with key {fateRow.Key}. Error: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logDebug($"Critical error loading fates: {ex.Message}");
                }

                return tempFates;
            });
        }

        private uint ConvertToTerritoryId(object territoryValue)
        {
            if (territoryValue is SaintCoinach.Xiv.TerritoryType territoryType)
            {
                return (uint)territoryType.Key;
            }
            else
            {
                try
                {
                    return Convert.ToUInt32(territoryValue);
                }
                catch
                {
                    var keyProp = territoryValue.GetType().GetProperty("Key");
                    if (keyProp != null)
                    {
                        var keyValue = keyProp.GetValue(territoryValue);
                        if (keyValue != null)
                        {
                            return Convert.ToUInt32(keyValue);
                        }
                    }
                }
            }
            return 0;
        }

        public async Task<List<TerritoryInfo>> LoadTerritoriesAsync()
        {
            return await Task.Run(() =>
            {
                _logDebug("Starting to load territories...");
                var tempTerritories = new List<TerritoryInfo>();

                try
                {
                    if (_realm != null)
                    {
                        _logDebug("Loading territories from SaintCoinach...");
                        var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();

                        int processedCount = 0;
                        int totalCount = territorySheet.Count();
                        _logDebug($"Found {totalCount} territories in game data");

                        foreach (var territory in territorySheet)
                        {
                            try
                            {
                                string placeName;
                                uint placeNameId = 0;

                                if (territory.PlaceName != null && !string.IsNullOrWhiteSpace(territory.PlaceName.Name))
                                {
                                    placeName = territory.PlaceName.Name.ToString();
                                    placeNameId = (uint)territory.PlaceName.Key;
                                }
                                else
                                {
                                    placeName = $"[Territory ID: {territory.Key}]";
                                }

                                string territoryNameId = territory.Name?.ToString() ?? string.Empty;
                                string regionName = "Unknown";
                                uint regionId = 0;
                                bool regionFound = false;

                                try
                                {
                                    if (territory.RegionPlaceName != null && territory.RegionPlaceName.Key != 0)
                                    {
                                        string? name = territory.RegionPlaceName.Name?.ToString();
                                        if (!string.IsNullOrEmpty(name))
                                        {
                                            regionName = name;
                                            regionId = (uint)territory.RegionPlaceName.Key;
                                            regionFound = true;
                                        }
                                    }
                                }
                                catch (KeyNotFoundException) { }

                                if (!regionFound)
                                {
                                    try
                                    {
                                        if (territory.ZonePlaceName != null && territory.ZonePlaceName.Key != 0)
                                        {
                                            string? name = territory.ZonePlaceName.Name?.ToString();
                                            if (!string.IsNullOrEmpty(name))
                                            {
                                                regionName = name;
                                                regionId = (uint)territory.ZonePlaceName.Key;
                                            }
                                        }
                                    }
                                    catch (KeyNotFoundException) { }
                                }

                                uint mapId = 0;
                                try
                                {
                                    if (territory.Map != null)
                                    {
                                        mapId = (uint)territory.Map.Key;
                                    }
                                }
                                catch (KeyNotFoundException)
                                {
                                    _logDebug($"Territory {territory.Key} ('{placeName}') has no Map. Defaulting to 0.");
                                }

                                var territoryInfo = new TerritoryInfo
                                {
                                    Id = (uint)territory.Key,
                                    Name = placeName,
                                    TerritoryNameId = territoryNameId,
                                    PlaceNameId = placeNameId,
                                    PlaceName = placeName,
                                    RegionId = regionId,
                                    RegionName = regionName,
                                    Region = regionName,
                                    MapId = mapId,
                                };

                                tempTerritories.Add(territoryInfo);

                                processedCount++;
                                if (processedCount % 100 == 0)
                                {
                                    _logDebug($"Processed {processedCount}/{totalCount} territories...");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logDebug($"Failed to process territory with key {territory.Key}. Error: {ex.Message}");
                            }
                        }

                        _logDebug($"Finished processing {tempTerritories.Count} territories");
                        tempTerritories.Sort((a, b) => a.Id.CompareTo(b.Id));
                    }
                    else
                    {
                        _logDebug("Realm is null, cannot load territories");
                    }
                }
                catch (Exception ex)
                {
                    _logDebug($"Critical error loading territories: {ex.Message}\n{ex.StackTrace}");
                }

                _logDebug($"Returning {tempTerritories.Count} territories from LoadTerritoriesAsync");
                return tempTerritories;
            });
        }

        private void ExtractQuestDetails(Quest quest, QuestInfo questInfo)
        {
            try
            {
                var classJobLevel = quest.AsInt32("ClassJobLevelRequired");
                questInfo.ClassJobLevelRequired = (uint)Math.Max(0, classJobLevel);
            }
            catch { }

            try
            {
                var expReward = quest.AsInt32("ExpReward");
                questInfo.ExpReward = (uint)Math.Max(0, expReward);
            }
            catch { }

            try
            {
                var gilReward = quest.AsInt32("GilReward");
                questInfo.GilReward = (uint)Math.Max(0, gilReward);
            }
            catch { }
        }
    }
}