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

        // Fixed: Make the method actually async by wrapping the work in Task.Run
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
                        int totalCount = questSheet.Count();
                        
                        _logDebug($"Found {totalCount} quests in sheet");

                        foreach (var quest in questSheet)
                        {
                            try
                            {
                                // More defensive checks for quest data
                                if (quest == null)
                                {
                                    errorCount++;
                                    continue;
                                }

                                // Use the row key as ID and try to get the name safely
                                uint questId = (uint)quest.Key;
                                string questName = "";
                                
                                try
                                {
                                    // Quest.json shows Name is at index 0 (default column)
                                    questName = quest.Name?.ToString() ?? "";
                                }
                                catch (Exception ex)
                                {
                                    // If we can't get the name, try to get it by index
                                    try
                                    {
                                        if (quest is SaintCoinach.Xiv.XivRow xivRow)
                                        {
                                            questName = xivRow.AsString("Name") ?? "";
                                        }
                                        else
                                        {
                                            // Fallback: try to access as generic row
                                            var nameValue = quest[0]; // Index 0 according to Quest.json
                                            questName = nameValue?.ToString() ?? "";
                                        }
                                    }
                                    catch
                                    {
                                        // If all else fails, use a placeholder name
                                        questName = $"Quest {questId}";
                                    }
                                }

                                if (string.IsNullOrWhiteSpace(questName))
                                {
                                    questName = $"Quest {questId}"; // Fallback name
                                }

                                string journalGenre = "";
                                bool isMainScenario = false;
                                bool isFeature = false;
                                
                                try
                                {
                                    // JournalGenre is at index 1506 according to Quest.json
                                    journalGenre = quest.JournalGenre?.Name?.ToString() ?? "";
                                    isMainScenario = journalGenre.Contains("Main Scenario", StringComparison.OrdinalIgnoreCase);
                                    isFeature = journalGenre.Contains("Feature", StringComparison.OrdinalIgnoreCase);
                                }
                                catch
                                {
                                    // JournalGenre access failed, continue with defaults
                                }

                                var questInfo = new QuestInfo
                                {
                                    Id = questId,
                                    Name = questName,
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

                                // Extract additional quest data with better error handling
                                ExtractQuestDetailsSafely(quest, questInfo);

                                tempQuests.Add(questInfo);
                                processedCount++;

                                // Log some examples for verification
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
                                if (errorCount <= 10) // Only log first 10 errors to avoid spam
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

        private void ExtractQuestDetailsSafely(Quest quest, QuestInfo questInfo)
        {
            // Add debug flag to see what's happening
            bool isDebugQuest = questInfo.Id <= 10; // Debug first 10 quests

            if (isDebugQuest)
            {
                _logDebug($"=== Extracting details for Quest {questInfo.Id}: '{questInfo.Name}' ===");
            }

            try
            {
                // ClassJobLevel[0] is at index 4 according to Quest.json
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
                // GilReward is at index 1441 according to Quest.json
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

            // Try to get location information from multiple sources
            bool locationFound = false;

            try
            {
                // First try: PlaceName is at index 1505 according to Quest.json
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
                        
                        // Try to find the corresponding MapId by looking up territories with this PlaceName
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

            // Second try: Get location from ToDoMainLocation (Level data)
            if (!locationFound)
            {
                try
                {
                    if (isDebugQuest)
                    {
                        _logDebug($"  Trying ToDoMainLocation extraction...");
                    }

                    // ToDoMainLocation is at index 1222+ (24 entries)
                    // Let's try the first ToDoMainLocation entry
                    var todoLocationValue = quest[1222]; // First ToDoMainLocation
                    
                    if (todoLocationValue != null)
                    {
                        if (isDebugQuest)
                        {
                            _logDebug($"  ToDoMainLocation found: {todoLocationValue.GetType().Name}");
                        }

                        // If it's a Level object, we can get Map and Territory info
                        if (todoLocationValue is SaintCoinach.Ex.Relational.IRelationalRow levelRow)
                        {
                            try
                            {
                                // Get Map from Level (index 8 in Level.json)
                                var mapValue = levelRow[8];
                                if (mapValue != null && mapValue is SaintCoinach.Xiv.Map mapObj)
                                {
                                    questInfo.MapId = (uint)mapObj.Key;
                                    
                                    // Try to get PlaceName from the map
                                    if (mapObj.PlaceName != null)
                                    {
                                        questInfo.PlaceName = mapObj.PlaceName.Name?.ToString() ?? "";
                                        questInfo.PlaceNameId = (uint)mapObj.PlaceName.Key;
                                    }
                                    
                                    locationFound = true;
                                    
                                    if (isDebugQuest)
                                    {
                                        _logDebug($"  Level->Map found: MapId={questInfo.MapId}, PlaceName='{questInfo.PlaceName}'");
                                    }
                                }
                                
                                // If no map, try Territory (index 9 in Level.json)
                                if (!locationFound)
                                {
                                    var territoryValue = levelRow[9];
                                    if (territoryValue != null && territoryValue is SaintCoinach.Xiv.TerritoryType territoryObj)
                                    {
                                        // Get map from territory
                                        if (territoryObj.Map != null)
                                        {
                                            questInfo.MapId = (uint)territoryObj.Map.Key;
                                        }
                                        
                                        // Get place name from territory
                                        if (territoryObj.PlaceName != null)
                                        {
                                            questInfo.PlaceName = territoryObj.PlaceName.Name?.ToString() ?? "";
                                            questInfo.PlaceNameId = (uint)territoryObj.PlaceName.Key;
                                        }
                                        
                                        locationFound = true;
                                        
                                        if (isDebugQuest)
                                        {
                                            _logDebug($"  Level->Territory found: MapId={questInfo.MapId}, PlaceName='{questInfo.PlaceName}'");
                                        }
                                    }
                                }
                            }
                            catch (Exception levelEx)
                            {
                                if (isDebugQuest)
                                {
                                    _logDebug($"  Error processing Level data: {levelEx.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (isDebugQuest)
                    {
                        _logDebug($"  ToDoMainLocation extraction failed: {ex.Message}");
                    }
                }
            }

            // Third try: Alternative PlaceName access methods
            if (!locationFound)
            {
                try
                {
                    if (isDebugQuest)
                    {
                        _logDebug($"  Trying alternative PlaceName extraction methods...");
                    }

                    // Try accessing by index directly
                    var placeNameValue = quest[1505]; // Index 1505 from Quest.json
                    if (placeNameValue != null)
                    {
                        if (placeNameValue is SaintCoinach.Xiv.PlaceName altPlaceName)
                        {
                            questInfo.PlaceName = altPlaceName.Name?.ToString() ?? "";
                            questInfo.PlaceNameId = (uint)altPlaceName.Key;
                            questInfo.MapId = FindMapIdForPlaceName(questInfo.PlaceNameId);
                            
                            locationFound = true;
                            
                            if (isDebugQuest)
                            {
                                _logDebug($"  Alternative PlaceName found: '{questInfo.PlaceName}' (ID: {questInfo.PlaceNameId}) -> MapId: {questInfo.MapId}");
                            }
                        }
                    }
                }
                catch (Exception altEx)
                {
                    if (isDebugQuest)
                    {
                        _logDebug($"  Alternative PlaceName extraction failed: {altEx.Message}");
                    }
                }
            }

            try
            {
                // Icon is at index 1508 according to Quest.json
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
                // IsRepeatable is at index 43 according to Quest.json
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
                _logDebug($"  PlaceName: '{questInfo.PlaceName}' (ID: {questInfo.PlaceNameId})");
                _logDebug($"  MapId: {questInfo.MapId}");
                _logDebug($"  ClassJobLevel: {questInfo.ClassJobLevelRequired}");
                _logDebug($"  GilReward: {questInfo.GilReward}");
                _logDebug($"  IconId: {questInfo.IconId}");
                _logDebug($"  IsRepeatable: {questInfo.IsRepeatable}");
                _logDebug($"  Location Found: {locationFound}");
                _logDebug($"===============================");
            }
        }

        private uint FindMapIdForPlaceName(uint placeNameId)
        {
            try
            {
                if (_realm != null && placeNameId > 0)
                {
                    // Use a more efficient lookup with debugging
                    var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();
                    
                    int checkedTerritories = 0;
                    int maxCheck = 100; // Limit checks for performance
                    
                    foreach (var territory in territorySheet)
                    {
                        checkedTerritories++;
                        if (checkedTerritories > maxCheck) break; // Prevent long searches
                        
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
                            // Continue searching if this territory has issues
                        }
                    }
                    
                    _logDebug($"  No MapId found for PlaceNameId {placeNameId} (checked {checkedTerritories} territories)");
                }
            }
            catch (Exception ex)
            {
                _logDebug($"  Error in FindMapIdForPlaceName for PlaceNameId {placeNameId}: {ex.Message}");
            }
            
            return 0; // No map found
        }

        public async Task<List<BNpcInfo>> LoadBNpcsAsync()
        {
            return await Task.Run(() =>
            {
                _logDebug("Starting to load BNpcs from CSV...");
                var tempBNpcs = new List<BNpcInfo>();

                try
                {
                    // Load from CSV file instead of SaintCoinach
                    string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libs", "MonsterData.csv");

                    _logDebug($"Looking for CSV at: {csvPath}");
                    _logDebug($"Base directory: {AppDomain.CurrentDomain.BaseDirectory}");
                    _logDebug($"File exists: {File.Exists(csvPath)}");

                    if (!File.Exists(csvPath))
                    {
                        _logDebug($"ERROR: MonsterData.csv not found at {csvPath}");

                        // Try alternative paths
                        string altPath1 = Path.Combine(Directory.GetCurrentDirectory(), "Libs", "MonsterData.csv");
                        string altPath2 = Path.Combine(Environment.CurrentDirectory, "Libs", "MonsterData.csv");

                        _logDebug($"Alternative path 1: {altPath1} - Exists: {File.Exists(altPath1)}");
                        _logDebug($"Alternative path 2: {altPath2} - Exists: {File.Exists(altPath2)}");

                        if (File.Exists(altPath1))
                        {
                            csvPath = altPath1;
                            _logDebug($"Using alternative path 1: {csvPath}");
                        }
                        else if (File.Exists(altPath2))
                        {
                            csvPath = altPath2;
                            _logDebug($"Using alternative path 2: {csvPath}");
                        }
                        else
                        {
                            return tempBNpcs;
                        }
                    }

                    _logDebug($"Loading BNpcs from CSV file: {csvPath}");

                    var lines = File.ReadAllLines(csvPath);
                    _logDebug($"Read {lines.Length} lines from CSV");

                    if (lines.Length <= 1)
                    {
                        _logDebug("ERROR: CSV file is empty or has no data rows");
                        return tempBNpcs;
                    }

                    // Log the header for verification
                    _logDebug($"CSV Header: {lines[0]}");

                    // Skip header line
                    var dataLines = lines.Skip(1);
                    var processedEntries = new HashSet<string>(); // To avoid duplicates

                    int processedCount = 0;
                    int skippedCount = 0;
                    int totalLines = dataLines.Count();

                    _logDebug($"Processing {totalLines} CSV lines...");

                    foreach (var line in dataLines)
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(line))
                            {
                                skippedCount++;
                                continue;
                            }

                            var parts = line.Split(',');
                            if (parts.Length < 3)
                            {
                                _logDebug($"Skipping line with insufficient columns ({parts.Length}): {line}");
                                skippedCount++;
                                continue;
                            }

                            string bnpcName = parts[0].Trim();
                            string bnpcNameIdStr = parts[1].Trim();
                            string bnpcBaseIdStr = parts[2].Trim();

                            // Log first few entries for debugging
                            if (processedCount < 5)
                            {
                                _logDebug($"Processing line: Name='{bnpcName}', NameId='{bnpcNameIdStr}', BaseId='{bnpcBaseIdStr}'");
                            }

                            // Skip entries with empty names
                            if (string.IsNullOrEmpty(bnpcName))
                            {
                                skippedCount++;
                                continue;
                            }

                            // Parse IDs
                            if (!uint.TryParse(bnpcNameIdStr, out uint bnpcNameId) || bnpcNameId == 0)
                            {
                                if (processedCount < 5)
                                {
                                    _logDebug($"Failed to parse NameId: '{bnpcNameIdStr}'");
                                }
                                skippedCount++;
                                continue;
                            }

                            if (!uint.TryParse(bnpcBaseIdStr, out uint bnpcBaseId))
                            {
                                bnpcBaseId = 0; // Default to 0 if parsing fails
                            }

                            // Create unique key to avoid duplicates
                            string uniqueKey = $"{bnpcNameId}_{bnpcName}";
                            if (processedEntries.Contains(uniqueKey))
                            {
                                skippedCount++;
                                continue;
                            }

                            processedEntries.Add(uniqueKey);

                            var bnpcInfo = new BNpcInfo
                            {
                                BNpcNameId = bnpcNameId,
                                BNpcName = bnpcName,
                                BNpcBaseId = bnpcBaseId, // Now using actual base ID from CSV
                                Title = "",
                                TribeId = 0,
                                TribeName = ""
                            };

                            tempBNpcs.Add(bnpcInfo);
                            processedCount++;

                            // Log some examples for verification
                            if (processedCount <= 10)
                            {
                                _logDebug($"  Added: '{bnpcName}' (NameId: {bnpcNameId}) -> BaseId: {bnpcBaseId}");
                            }

                            if (processedCount % 500 == 0)
                            {
                                _logDebug($"Processed {processedCount}/{totalLines} CSV lines (skipped {skippedCount})...");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logDebug($"Error processing CSV line: {line} - {ex.Message}");
                            skippedCount++;
                        }
                    }

                    _logDebug($"Finished processing CSV:");
                    _logDebug($"  - Total processed: {processedCount}");
                    _logDebug($"  - Skipped: {skippedCount}");
                    _logDebug($"  - Unique entries: {tempBNpcs.Count}");

                    // Sort by name for better user experience
                    tempBNpcs.Sort((a, b) => string.Compare(a.BNpcName, b.BNpcName, StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex)
                {
                    _logDebug($"Critical error loading BNpcs from CSV: {ex.Message}\n{ex.StackTrace}");
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