using SaintCoinach;
using SaintCoinach.Xiv;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace map_editor.Services
{
    public class DataLoaderService
    {
        private readonly ARealmReversed? _realm;
        private readonly Action<string> _logDebug;
        private readonly QuestLocationService? _questLocationService;

        public DataLoaderService(ARealmReversed? realm, Action<string> logDebug)
        {
            _realm = realm;
            _logDebug = logDebug;
            _questLocationService = new QuestLocationService(_realm, _logDebug);
        }

        public async Task<List<QuestInfo>> LoadQuestsAsync()
        {
            return await Task.Run(() =>
            {
                var tempQuests = new List<QuestInfo>();

                try
                {
                    if (_realm != null)
                    {
                        _logDebug("Loading quests from SaintCoinach...");
                        var questSheet = _realm.GameData.GetSheet<Quest>();

                        int processedCount = 0;
                        int errorCount = 0;
                        int totalCount = questSheet.Count;

                        _logDebug($"Found {totalCount} quests in sheet");

                        foreach (var quest in questSheet
                        .OrderBy(q => q.Key))
                        {
                            try
                            {
                                if (quest == null)
                                {
                                    errorCount++;
                                    continue;
                                }

                                uint questId = (uint)quest.Key;
                                string questName = "";

                                try
                                {
                                    questName = quest.Name?.ToString() ?? "";
                                }
                                catch (Exception)
                                {
                                    try
                                    {
                                        if (quest is SaintCoinach.Xiv.XivRow xivRow)
                                        {
                                            questName = xivRow.AsString("Name") ?? "";
                                        }
                                        else
                                        {
                                            var nameValue = quest[0];
                                            questName = nameValue?.ToString() ?? "";
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        questName = $"Quest {questId}";
                                    }
                                }

                                if (string.IsNullOrWhiteSpace(questName))
                                {
                                    questName = $"Quest {questId}";
                                }

                                string journalGenre = "";
                                bool isMainScenario = false;
                                bool isFeature = false;

                                try
                                {
                                    journalGenre = quest.JournalGenre?.Name?.ToString() ?? "";
                                    isMainScenario = journalGenre.Contains("Main Scenario", StringComparison.OrdinalIgnoreCase);
                                    isFeature = journalGenre.Contains("Feature", StringComparison.OrdinalIgnoreCase);
                                }
                                catch
                                {
                                }

                                var questInfo = new QuestInfo
                                {
                                    Id = questId,
                                    Name = questName,
                                    QuestIdString = "",
                                    JournalGenre = journalGenre,
                                    ClassJobCategoryId = 0,
                                    ClassJobLevelRequired = 0,
                                    ClassJobCategoryName = "",
                                    IsMainScenarioQuest = isMainScenario,
                                    IsFeatureQuest = isFeature,
                                    PreviousQuestId = 0,
                                    ExpReward = 0,
                                    GilReward = 0,
                                    PlaceName = "",
                                    PlaceNameId = 0,
                                    MapId = 0,
                                    IconId = 0,
                                    Description = "",
                                    IsRepeatable = false
                                };

                                ExtractQuestDetailsSafely(quest, questInfo);

                                tempQuests.Add(questInfo);
                                processedCount++;

                                if (processedCount <= 10)
                                {
                                    _logDebug($"  Added quest: '{questName}' (ID: {questId}) Location: '{questInfo.PlaceName}' MapId: {questInfo.MapId}");
                                }

                                if (processedCount % 1000 == 0)
                                {
                                    _logDebug($"Processed {processedCount}/{totalCount} quests (errors: {errorCount})...");
                                }
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                if (errorCount <= 10)
                                {
                                    _logDebug($"Error processing quest {quest?.Key}: {ex.Message}");
                                }
                            }
                        }

                        tempQuests.Sort((a, b) => a.Id.CompareTo(b.Id));
                        _logDebug($"Loaded {tempQuests.Count} quests successfully (skipped {errorCount} problematic quests)");
                    }
                }
                catch (Exception ex)
                {
                    _logDebug($"Error loading quests: {ex.Message}");
                }

                return tempQuests;
            });
        }

        private QuestNpcInfo? ExtractNpcFromRow(SaintCoinach.Ex.Relational.IRelationalRow npcRow, string role, uint questId)
        {
            try
            {
                if (_realm == null) return null;

                uint npcId = (uint)npcRow.Key;

                string npcName;
                try
                {
                    var nameValue = npcRow[2];
                    npcName = nameValue?.ToString() ?? $"NPC_{npcId}";
                }
                catch
                {
                    npcName = $"NPC_{npcId}";
                }

                var levelSheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.Level>();
                foreach (var level in levelSheet)
                {
                    try
                    {
                        // Check if this Level entry references our NPC (Object field = NPC ID)
                        var objectValue = level[6]; // Object field at index 6
                        if (objectValue != null && Convert.ToUInt32(objectValue) == npcId)
                        {
                            float worldX = Convert.ToSingle(level[0]); // X at index 0
                            float worldY = Convert.ToSingle(level[1]); // Y at index 1
                            float worldZ = Convert.ToSingle(level[2]); // Z at index 2

                            var territoryValue = level[9];
                            var mapValue = level[8];

                            if (territoryValue is SaintCoinach.Xiv.TerritoryType territory &&
                                mapValue is SaintCoinach.Xiv.Map map)
                            {
                                uint territoryId = (uint)territory.Key;
                                uint mapId = (uint)map.Key;
                                string territoryName = territory.PlaceName?.Name?.ToString() ?? $"Territory_{territoryId}";

                                // Convert world coordinates to map coordinates
                                var mapCoords = ConvertWorldToMapCoordinates(worldX, worldY, worldZ, mapId);

                                return new QuestNpcInfo
                                {
                                    NpcId = npcId,
                                    NpcName = npcName,
                                    TerritoryId = territoryId,
                                    TerritoryName = territoryName,
                                    MapId = mapId,
                                    MapX = mapCoords.X,
                                    MapY = mapCoords.Y,
                                    MapZ = mapCoords.Z,
                                    WorldX = worldX,
                                    WorldY = worldY,
                                    WorldZ = worldZ,
                                    Role = role
                                };
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                return new QuestNpcInfo
                {
                    NpcId = npcId,
                    NpcName = npcName,
                    Role = role,
                    TerritoryName = "Unknown Location"
                };
            }
            catch (Exception ex)
            {
                _logDebug($"Error extracting NPC data: {ex.Message}");
                return null;
            }
        }

        private void ExtractQuestDetailsSafely(Quest quest, QuestInfo questInfo)
        {
            bool isDebugQuest = questInfo.Id <= 10;

            if (isDebugQuest)
            {
                _logDebug($"=== Extracting details for Quest {questInfo.Id}: '{questInfo.Name}' ===");
            }

            try
            {
                var questIdStringValue = quest[1];
                if (questIdStringValue != null)
                {
                    questInfo.QuestIdString = questIdStringValue.ToString() ?? "";

                    if (isDebugQuest)
                    {
                        _logDebug($"  QuestIdString: '{questInfo.QuestIdString}'");
                    }
                }
            }
            catch (Exception ex)
            {
                if (isDebugQuest)
                {
                    _logDebug($"  QuestIdString extraction failed: {ex.Message}");
                }
            }

            try
            {
                if (isDebugQuest)
                {
                    _logDebug($"  Extracting Quest Giver from index 37 using research method...");
                }

                var questGiverValue = quest[37];
                if (questGiverValue != null)
                {
                    if (isDebugQuest)
                    {
                        _logDebug($"  Quest Giver NPC ID from index 37: {questGiverValue}");
                    }

                    if (uint.TryParse(questGiverValue.ToString(), out uint npcId) && npcId > 0)
                    {
                        var questGiver = ExtractQuestGiverByResearch(npcId, questInfo.Id);
                        if (questGiver != null)
                        {
                            questInfo.StartNpcs.Add(questGiver);

                            if (isDebugQuest)
                            {
                                _logDebug($"  ✅ Research-based Quest Giver: {questGiver.NpcName} at {questGiver.TerritoryName} ({questGiver.MapX:F1}, {questGiver.MapY:F1})");
                            }
                        }
                        else if (isDebugQuest)
                        {
                            _logDebug($"  ⚠️ Could not extract coordinates for NPC ID: {npcId}");
                        }
                    }
                    else
                    {
                        var fallbackQuestGiver = new QuestNpcInfo
                        {
                            NpcId = (uint)Math.Abs(questGiverValue.ToString()?.GetHashCode() ?? (int)questInfo.Id),
                            NpcName = $"Quest Giver ({questGiverValue})",
                            Role = "Quest Giver",
                            TerritoryName = questInfo.PlaceName ?? "Location TBD",
                            MapId = questInfo.MapId,
                            MapX = 0,
                            MapY = 0,
                            MapZ = 0
                        };

                        questInfo.StartNpcs.Add(fallbackQuestGiver);

                        if (isDebugQuest)
                        {
                            _logDebug($"  ✅ Fallback Quest Giver: {fallbackQuestGiver.NpcName}");
                        }
                    }
                }
            }
            catch (Exception npcEx)
            {
                if (isDebugQuest)
                {
                    _logDebug($"  ❌ Quest Giver extraction failed: {npcEx.Message}");
                }
            }

            try
            {
                var classJobLevel = quest.AsInt32("ClassJobLevel[0]");
                questInfo.ClassJobLevelRequired = (uint)Math.Max(0, classJobLevel);

                if (isDebugQuest)
                {
                    _logDebug($"  ClassJobLevel: {questInfo.ClassJobLevelRequired}");
                }
            }
            catch (Exception ex)
            {
                if (isDebugQuest)
                {
                    _logDebug($"  ClassJobLevel extraction failed: {ex.Message}");
                }
            }

            try
            {
                var gilReward = quest.AsInt32("GilReward");
                questInfo.GilReward = (uint)Math.Max(0, gilReward);

                if (isDebugQuest)
                {
                    _logDebug($"  GilReward: {questInfo.GilReward}");
                }
            }
            catch (Exception ex)
            {
                if (isDebugQuest)
                {
                    _logDebug($"  GilReward extraction failed: {ex.Message}");
                }
            }

            bool locationFound = false;

            try
            {
                var placeNameObj = quest.PlaceName;

                if (isDebugQuest)
                {
                    _logDebug($"  PlaceName object: {(placeNameObj != null ? "Found" : "NULL")}");
                }

                if (placeNameObj != null)
                {
                    var placeName = placeNameObj.Name?.ToString() ?? "";
                    var placeNameKey = (uint)placeNameObj.Key;

                    if (!string.IsNullOrEmpty(placeName))
                    {
                        questInfo.PlaceName = placeName;
                        questInfo.PlaceNameId = placeNameKey;

                        if (isDebugQuest)
                        {
                            _logDebug($"  PlaceName: '{placeName}' (ID: {placeNameKey})");
                        }

                        if (placeNameKey > 0)
                        {
                            questInfo.MapId = FindMapIdForPlaceName(questInfo.PlaceNameId);

                            if (isDebugQuest)
                            {
                                _logDebug($"  MapId lookup result: {questInfo.MapId}");
                            }
                        }

                        locationFound = true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (isDebugQuest)
                {
                    _logDebug($"  PlaceName extraction failed: {ex.Message}");
                }
            }

            try
            {
                var iconId = quest.AsInt32("Icon");
                questInfo.IconId = (uint)Math.Max(0, iconId);

                if (isDebugQuest)
                {
                    _logDebug($"  IconId: {questInfo.IconId}");
                }
            }
            catch (Exception ex)
            {
                if (isDebugQuest)
                {
                    _logDebug($"  IconId extraction failed: {ex.Message}");
                }
            }

            try
            {
                var isRepeatable = quest.AsBoolean("IsRepeatable");
                questInfo.IsRepeatable = isRepeatable;

                if (isDebugQuest)
                {
                    _logDebug($"  IsRepeatable: {questInfo.IsRepeatable}");
                }
            }
            catch (Exception ex)
            {
                if (isDebugQuest)
                {
                    _logDebug($"  IsRepeatable extraction failed: {ex.Message}");
                }
            }

            if (isDebugQuest)
            {
                _logDebug($"=== Final Quest {questInfo.Id} Details ===");
                _logDebug($"  Name: '{questInfo.Name}'");
                _logDebug($"  QuestIdString: '{questInfo.QuestIdString}'");
                _logDebug($"  Start NPCs: {questInfo.StartNpcs.Count}");
                foreach (var npc in questInfo.StartNpcs)
                {
                    _logDebug($"    - {npc.NpcName}: MapId={npc.MapId}, Coords=({npc.MapX:F1}, {npc.MapY:F1}), Territory={npc.TerritoryName}");
                }
                _logDebug($"  PlaceName: '{questInfo.PlaceName}' (ID: {questInfo.PlaceNameId})");
                _logDebug($"  MapId: {questInfo.MapId}");
                _logDebug($"===============================");
            }
        }

        private (double X, double Y, double Z) ConvertWorldToMapCoordinates(float worldX, float worldY, float worldZ, uint mapId)
        {
            try
            {
                if (_realm == null) return (0, 0, 0);

                var mapSheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.Map>();
                var map = mapSheet.FirstOrDefault(m => m.Key == mapId);

                if (map != null)
                {
                    float sizeFactor = 200.0f;
                    float offsetX = 0;
                    float offsetY = 0;

                    try
                    {
                        var type = map.GetType();
                        var indexer = type.GetProperty("Item", new[] { typeof(string) });

                        if (indexer != null)
                        {
                            sizeFactor = (float)Convert.ChangeType(indexer.GetValue(map, new object[] { "SizeFactor" }), typeof(float));
                            offsetX = (float)Convert.ChangeType(indexer.GetValue(map, new object[] { "OffsetX" }), typeof(float));
                            offsetY = (float)Convert.ChangeType(indexer.GetValue(map, new object[] { "OffsetY" }), typeof(float));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logDebug($"    Error getting map properties: {ex.Message}");
                    }

                    // REVERSE the MapRenderer coordinate conversion to get the raw marker coordinates
                    // MapRenderer does: gameX = (41.0 / c) * (normalizedX) + 1.0
                    // Where: normalizedX = (marker.X + offsetX) / 2048.0
                    // And: c = sizeFactor / 100.0

                    double c = sizeFactor / 100.0;

                    // From MapRenderer: gameCoord = (41.0 / c) * ((rawCoord + offset) / 2048.0) + 1.0
                    // Solving for rawCoord: rawCoord = ((gameCoord - 1.0) * c * 2048.0 / 41.0) - offset

                    double rawMarkerX = worldX * sizeFactor / 100.0;
                    double rawMarkerY = worldZ * sizeFactor / 100.0; // Note: Y in map = Z in world

                    double normalizedX = (rawMarkerX + offsetX) / 2048.0;
                    double normalizedY = (rawMarkerY + offsetY) / 2048.0;
                    double gameX = (41.0 / c) * (normalizedX) + 1.0;
                    double gameY = (41.0 / c) * (normalizedY) + 1.0;

                    _logDebug($"    COORDINATE CONVERSION DEBUG:");
                    _logDebug($"      World: ({worldX:F1}, {worldY:F1}, {worldZ:F1})");
                    _logDebug($"      Map {mapId}: SizeFactor={sizeFactor}, OffsetX={offsetX}, OffsetY={offsetY}");
                    _logDebug($"      Raw Marker: ({rawMarkerX:F1}, {rawMarkerY:F1})");
                    _logDebug($"      Normalized: ({normalizedX:F3}, {normalizedY:F3})");
                    _logDebug($"      Game Coordinates: ({gameX:F1}, {gameY:F1}) - should be (11.7, 13.5) for Mother Miounne");

                    return (gameX, gameY, worldY);
                }
                else
                {
                    _logDebug($"    Map {mapId} not found in map sheet");
                }
            }
            catch (Exception ex)
            {
                _logDebug($"Error converting coordinates for MapId {mapId}: {ex.Message}");
            }

            return (0, 0, 0);
        }

        private uint FindMapIdForPlaceName(uint placeNameId)
        {
            try
            {
                if (_realm != null && placeNameId > 0)
                {
                    var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();

                    int checkedTerritories = 0;
                    int maxCheck = 100;

                    foreach (var territory in territorySheet)
                    {
                        checkedTerritories++;
                        if (checkedTerritories > maxCheck) break;

                        try
                        {
                            if (territory.PlaceName != null && territory.PlaceName.Key == placeNameId)
                            {
                                if (territory.Map != null)
                                {
                                    uint mapId = (uint)territory.Map.Key;
                                    _logDebug($"  Found MapId {mapId} for PlaceNameId {placeNameId} (checked {checkedTerritories} territories)");
                                    return mapId;
                                }
                            }
                        }
                        catch
                        {
                        }
                    }

                    _logDebug($"  No MapId found for PlaceNameId {placeNameId} (checked {checkedTerritories} territories)");
                }
            }
            catch (Exception ex)
            {
                _logDebug($"  Error in FindMapIdForPlaceName for PlaceNameId {placeNameId}: {ex.Message}");
            }

            return 0;
        }

        public async Task<List<BNpcInfo>> LoadBNpcsAsync()
        {
            return await Task.Run(() =>
            {
                _logDebug("Loading BNpcs from CSV...");
                var tempBNpcs = new List<BNpcInfo>();

                try
                {
                    var csvPath = FindCsvFile("MonsterData.csv");
                    if (csvPath == null) return tempBNpcs;

                    var lines = File.ReadAllLines(csvPath).Skip(1);
                    var processedEntries = new HashSet<string>();

                    foreach (var line in lines)
                    {
                        if (TryParseBNpcLine(line, processedEntries, out var bnpcInfo) && bnpcInfo != null)
                        {
                            tempBNpcs.Add(bnpcInfo);
                        }
                    }

                    tempBNpcs.Sort((a, b) => string.Compare(a.BNpcName, b.BNpcName, StringComparison.OrdinalIgnoreCase));
                    _logDebug($"Loaded {tempBNpcs.Count} BNpcs from CSV");
                }
                catch (Exception ex)
                {
                    _logDebug($"Error loading BNpcs: {ex.Message}");
                }

                return tempBNpcs;
            });
        }

        private static string? FindCsvFile(string filename)
        {
            var paths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libs", filename),
                Path.Combine(Directory.GetCurrentDirectory(), "Libs", filename),
                Path.Combine(Environment.CurrentDirectory, "Libs", filename)
            };

            return paths.FirstOrDefault(File.Exists);
        }

        private static bool TryParseBNpcLine(string line, HashSet<string> processedEntries, out BNpcInfo? bnpcInfo)
        {
            bnpcInfo = null;

            if (string.IsNullOrWhiteSpace(line)) return false;

            var parts = line.Split(',');
            if (parts.Length < 3) return false;

            var name = parts[0].Trim();
            if (string.IsNullOrEmpty(name) || !uint.TryParse(parts[1].Trim(), out uint nameId) || nameId == 0)
                return false;

            var uniqueKey = $"{nameId}_{name}";
            if (processedEntries.Contains(uniqueKey)) return false;

            processedEntries.Add(uniqueKey);
            _ = uint.TryParse(parts[2].Trim(), out uint baseId);

            bnpcInfo = new BNpcInfo
            {
                BNpcNameId = nameId,
                BNpcName = name,
                BNpcBaseId = baseId,
                Title = "",
                TribeId = 0,
                TribeName = ""
            };

            return true;
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

                                var fateInfo = new FateInfo
                                {
                                    Id = (uint)fateRow.Key,
                                    FateId = (uint)fateRow.Key,
                                    Name = fateName,
                                    Description = fateRow.AsString("Description") ?? "",
                                    Level = (uint)Math.Max(0, fateRow.AsInt32("ClassJobLevel")),
                                    ClassJobLevel = (uint)Math.Max(0, fateRow.AsInt32("ClassJobLevel")),
                                    TerritoryId = 0,
                                    TerritoryName = "",
                                    IconId = (uint)Math.Max(0, fateRow.AsInt32("Icon")),
                                    X = 0,
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
                        int totalCount = territorySheet.Count;
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

        public async Task LoadQuestLocationsAsync(List<QuestInfo> quests)
        {
            if (_questLocationService == null)
            {
                _logDebug("Quest location service is null");
                return;
            }

            try
            {
                _logDebug("🎯 Starting quest location extraction using Libra Eorzea...");

                await _questLocationService.ExtractQuestLocationsAsync();

                _questLocationService.UpdateQuestLocations(quests);

                _logDebug("🎯 Quest location extraction completed using Libra Eorzea");
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error in quest location extraction: {ex.Message}");
            }
        }

        public void SetVerboseDebugMode(bool enabled)
        {
            _questLocationService?.SetVerboseDebugMode(enabled);
        }

        private QuestNpcInfo? ExtractQuestGiverByResearch(uint npcId, uint questId)
        {
            try
            {
                if (_realm == null) return null;

                _logDebug($"    Research method: Looking up NPC ID {npcId} for Quest {questId}...");

                if (_questLocationService != null)
                {
                    var questLocationData = _questLocationService.GetQuestLocationData(questId);
                    if (questLocationData != null)
                    {
                        _logDebug($"    ✅ Using quest location service data for Quest {questId}");
                        _logDebug($"    Coordinates: ({questLocationData.MapX:F1}, {questLocationData.MapY:F1}) - should be (11.7, 13.5) for Mother Miounne");

                        string npcName = $"NPC_{npcId}";
                        try
                        {
                            var enpcSheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.ENpcResident>();
                            var npcData = enpcSheet.FirstOrDefault(npc => npc.Key == npcId);
                            if (npcData != null)
                            {
                                npcName = npcData.Singular?.ToString() ?? $"NPC_{npcId}";
                            }
                        }
                        catch (Exception ex)
                        {
                            _logDebug($"    Warning: Could not get NPC name: {ex.Message}");
                        }

                        var questGiver = new QuestNpcInfo
                        {
                            NpcId = npcId,
                            NpcName = npcName,
                            TerritoryId = questLocationData.TerritoryId,
                            TerritoryName = questLocationData.ObjectName.Replace("NPC_", "").Replace($"_{questLocationData.TerritoryId}", ""), // Clean up territory name
                            MapId = questLocationData.MapId,
                            MapX = questLocationData.MapX,
                            MapY = questLocationData.MapY,
                            MapZ = questLocationData.MapZ,
                            WorldX = questLocationData.WorldX,
                            WorldY = questLocationData.WorldY,
                            WorldZ = questLocationData.WorldZ,
                            Role = "Quest Giver"
                        };

                        _logDebug($"    ✅ Created Quest Giver from location service data: {questGiver.NpcName} at {questGiver.TerritoryName} ({questGiver.MapX:F1}, {questGiver.MapY:F1})");
                        return questGiver;
                    }
                    else
                    {
                        _logDebug($"    ⚠️ Quest location service has no location data for Quest {questId}");
                    }
                }

                var enpcSheet2 = _realm.GameData.GetSheet<SaintCoinach.Xiv.ENpcResident>();
                var npcData2 = enpcSheet2.FirstOrDefault(npc => npc.Key == npcId);

                if (npcData2 == null)
                {
                    _logDebug($"    NPC ID {npcId} not found in ENpcResident sheet");
                    return null;
                }

                string npcName2 = npcData2.Singular?.ToString() ?? $"NPC_{npcId}";
                _logDebug($"    Found NPC: {npcName2} but no location data from quest location service");

                return new QuestNpcInfo
                {
                    NpcId = npcId,
                    NpcName = npcName2,
                    Role = "Quest Giver",
                    TerritoryName = "Coordinates from quest location service not found",
                    MapId = 0,
                    MapX = 0,
                    MapY = 0,
                    MapZ = 0
                };
            }
            catch (Exception ex)
            {
                _logDebug($"    Error in research method for NPC {npcId}: {ex.Message}");
                return null;
            }
        }
    }
}