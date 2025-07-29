using SaintCoinach;
using SaintCoinach.Xiv;
using SaintCoinach.Graphics.Lgb;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

namespace map_editor
{
    public class LgbLocationParsing
    {
        private readonly ARealmReversed? _realm;
        private readonly Action<string> _logDebug;
        private readonly Dictionary<uint, QuestLocationData> _questLocationCache = new();
        private readonly Dictionary<uint, List<uint>> _npcToQuestMap = new(); // Map NPC IDs to Quest IDs
        private readonly Dictionary<uint, List<uint>> _eventToQuestMap = new(); // Map Event IDs to Quest IDs
        private bool _verboseDebugMode = false;

        public LgbLocationParsing(ARealmReversed? realm, Action<string> logDebug)
        {
            _realm = realm;
            _logDebug = logDebug ?? (msg => { });
        }

        public void SetVerboseDebugMode(bool enabled)
        {
            _verboseDebugMode = enabled;
            _logDebug($"Location parsing verbose debug mode set to: {enabled}");
        }

        /// <summary>
        /// Parse quest locations from multiple sources (Level sheet, Quest references, and LGB files)
        /// </summary>
        public async Task<Dictionary<uint, QuestLocationData>> ParseQuestLocationsFromLgbAsync()
        {
            if (_realm == null)
            {
                _logDebug("Realm is null, cannot parse quest locations");
                return _questLocationCache;
            }

            _logDebug("*** FORCED LOG: Starting quest location parsing ***");

            try
            {
                // Step 1: Build quest-to-object mappings from Quest sheet
                _logDebug("*** FORCED LOG: About to call BuildQuestMappingsAsync ***");
                await BuildQuestMappingsAsync();

                // Step 2: Parse Level sheet for direct quest location references
                _logDebug("*** FORCED LOG: About to call ParseQuestLocationsFromLevelSheetAsync ***");
                await ParseQuestLocationsFromLevelSheetAsync();

                // Step 3: Parse LGB files to find additional object locations
                _logDebug("*** FORCED LOG: About to call ParseQuestLocationsFromLgbFilesAsync ***");
                await ParseQuestLocationsFromLgbFilesAsync();

                _logDebug($"*** FORCED LOG: Quest location parsing complete. Found {_questLocationCache.Count} quest locations ***");
            }
            catch (Exception ex)
            {
                _logDebug($"*** FORCED LOG: Error in ParseQuestLocationsFromLgbAsync: {ex.Message} ***");
            }

            return _questLocationCache;
        }

        /// <summary>
        /// Build mappings between NPCs/Events and Quests
        /// </summary>
        private async Task BuildQuestMappingsAsync()
        {
            _logDebug("Building quest-to-object mappings...");

            try
            {
                var questSheet = _realm.GameData.GetSheet<Quest>();
                int mappedQuests = 0;
                int directLocations = 0;
                int totalQuests = 0;

                foreach (var quest in questSheet)
                {
                    try
                    {
                        totalQuests++;
                        var questId = (uint)quest.Key;

                        // Debug specific quest from your research
                        bool isResearchQuest = questId == 65564; // "To the Bannock"
                        
                        if (totalQuests <= 5 || isResearchQuest)
                        {
                            _logDebug($"=== Quest {questId} Debug ===");
                            _logDebug($"  Name: {quest.Name}");
                            
                            // Test your discovery: index 37 should contain NPC ID
                            try
                            {
                                var npcAtIndex37 = quest[37];
                                _logDebug($"  quest[37]: {npcAtIndex37?.GetType().Name ?? "null"} = {npcAtIndex37}");
                        
                                if (npcAtIndex37 != null)
                                {
                                    // If it's a direct NPC ID (uint), use it
                                    if (npcAtIndex37 is uint debugNpcId)
                                    {
                                        _logDebug($"    Found NPC ID directly: {debugNpcId}");
                                        if (!_npcToQuestMap.ContainsKey(debugNpcId))
                                            _npcToQuestMap[debugNpcId] = new List<uint>();
                                        _npcToQuestMap[debugNpcId].Add(questId);
                                        mappedQuests++;
                                    }
                                    // If it's an ENpcResident object, get its key
                                    else if (npcAtIndex37 is ENpcResident npcResident)
                                    {
                                        var debugNpcResidentId = (uint)npcResident.Key;
                                        _logDebug($"    Found NPC Resident: ID {debugNpcResidentId}, Name: '{npcResident.Singular}'");
                                        if (!_npcToQuestMap.ContainsKey(debugNpcResidentId))
                                            _npcToQuestMap[debugNpcResidentId] = new List<uint>();
                                        _npcToQuestMap[debugNpcResidentId].Add(questId);
                                        mappedQuests++;
                                    }
                                    // Try to convert whatever it is to uint
                                    else
                                    {
                                        try
                                        {
                                            var debugConvertedNpcId = Convert.ToUInt32(npcAtIndex37);
                                            if (debugConvertedNpcId > 0)
                                            {
                                                _logDebug($"    Converted to NPC ID: {debugConvertedNpcId}");
                                                if (!_npcToQuestMap.ContainsKey(debugConvertedNpcId))
                                                    _npcToQuestMap[debugConvertedNpcId] = new List<uint>();
                                                _npcToQuestMap[debugConvertedNpcId].Add(questId);
                                                mappedQuests++;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logDebug($"    Failed to convert to uint: {ex.Message}");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logDebug($"  Error accessing quest[37]: {ex.Message}");
                            }
                        }
                        else
                        {
                            // For non-debug quests, just do the extraction without logging
                            try
                            {
                                var npcAtIndex37 = quest[37];
                                if (npcAtIndex37 != null)
                                {
                                    uint extractedNpcId = 0;
                                    
                                    if (npcAtIndex37 is uint directNpcId)
                                    {
                                        extractedNpcId = directNpcId;
                                    }
                                    else if (npcAtIndex37 is ENpcResident npcResident)
                                    {
                                        extractedNpcId = (uint)npcResident.Key;
                                    }
                                    else
                                    {
                                        try
                                        {
                                            extractedNpcId = Convert.ToUInt32(npcAtIndex37);
                                        }
                                        catch { }
                                    }
                                    
                                    if (extractedNpcId > 0)
                                    {
                                        if (!_npcToQuestMap.ContainsKey(extractedNpcId))
                                            _npcToQuestMap[extractedNpcId] = new List<uint>();
                                        _npcToQuestMap[extractedNpcId].Add(questId);
                                        mappedQuests++;
                                    }
                                }
                            }
                            catch { }
                        }

                        // Check ToDoMainLocation entries (index 1222-1245) - keep this as fallback
                        for (int i = 1222; i < 1246; i++)
                        {
                            try
                            {
                                var levelRef = quest[i] as Level;
                                if (levelRef != null && levelRef.Map != null)
                                {
                                    var mapId = (uint)levelRef.Map.Key;
                                    var territoryId = levelRef.Map.TerritoryType != null ? (uint)levelRef.Map.TerritoryType.Key : 0;

                                    var locationData = new QuestLocationData
                                    {
                                        QuestId = questId,
                                        TerritoryId = territoryId,
                                        MapId = mapId,
                                        MapX = levelRef.MapX,
                                        MapY = levelRef.MapY,
                                        MapZ = 0,
                                        WorldX = levelRef.X,
                                        WorldY = levelRef.Y,
                                        WorldZ = levelRef.Z,
                                        ObjectType = $"Level_{levelRef.Type}",
                                        ObjectName = $"Level_{levelRef.Key}",
                                        ObjectId = (uint)(levelRef.Object?.Key ?? 0),
                                        EventId = 0
                                    };

                                    _questLocationCache[questId] = locationData;
                                    directLocations++;

                                    if (directLocations <= 10 || isResearchQuest)
                                    {
                                        _logDebug($"Found quest {questId} direct location from Level {levelRef.Key}: Map {mapId} at ({locationData.MapX:F1}, {locationData.MapY:F1})");
                                    }

                                    break; // Use first valid location
                                }
                            }
                            catch { }
                        }

                        // Map Target NPC (index 39)
                        var targetRef = quest[39];
                        if (targetRef is ENpcResident targetNpc)
                        {
                            var targetNpcId = (uint)targetNpc.Key;
                            if (!_npcToQuestMap.ContainsKey(targetNpcId))
                                _npcToQuestMap[targetNpcId] = new List<uint>();
                            _npcToQuestMap[targetNpcId].Add(questId);
                        }
                        
                    }
                    catch (Exception ex)
                    {
                        _logDebug($"Error processing quest {quest.Key}: {ex.Message}");
                    }
                }

                _logDebug($"Built mappings from {totalQuests} quests: {_npcToQuestMap.Count} NPCs linked to quests, {directLocations} quests with direct Level locations");
                
                // Debug the specific NPC from your research
                if (_npcToQuestMap.ContainsKey(1000100))
                {
                    var motherMiounneQuests = _npcToQuestMap[1000100];
                    _logDebug($"Mother Miounne (NPC 1000100) is linked to {motherMiounneQuests.Count} quests: [{string.Join(", ", motherMiounneQuests)}]");
                    
                    // Verify if quest 65564 is in there
                    if (motherMiounneQuests.Contains(65564))
                    {
                        _logDebug($"SUCCESS: Quest 65564 'To the Bannock' is correctly linked to Mother Miounne!");
                    }
                }
                
                // Debug first few NPCs
                if (_npcToQuestMap.Count > 0)
                {
                    var firstNpcs = _npcToQuestMap.Take(5);
                    foreach (var kvp in firstNpcs)
                    {
                        _logDebug($"NPC {kvp.Key} linked to {kvp.Value.Count} quests: [{string.Join(", ", kvp.Value)}]");
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug($"Error building quest mappings: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse Level sheet for quest locations via NPCs using Territory data from Level entries
        /// </summary>
        private async Task ParseQuestLocationsFromLevelSheetAsync()
        {
            _logDebug("Parsing Level sheet for quest locations using Level TerritoryType data...");

            try
            {
                var levelSheet = _realm.GameData.GetSheet<Level>();
                if (levelSheet == null)
                {
                    _logDebug("Level sheet not found");
                    return;
                }

                int processedEntries = 0;
                int foundLocations = 0;
                int npcObjectsFound = 0;
                int questLinkedNpcsFound = 0;

                foreach (var level in levelSheet)
                {
                    try
                    {
                        processedEntries++;

                        // Based on your research:
                        // Level index 6 = Object (NPC reference)
                        // Level indices 0,1,2 = X,Y,Z coordinates  
                        // Level index 9 = TerritoryType (THE CORRECT ONE!)
                        
                        // Get the object referenced by this Level entry (index 6)
                        var objectRef = level[6];
                        if (objectRef != null)
                        {
                            // Try to get NPC ID from the object reference
                            uint npcId = 0;
                            
                            if (objectRef is ENpcResident npc)
                            {
                                npcId = (uint)npc.Key;
                                npcObjectsFound++;
                            }
                            else
                            {
                                // Try to convert directly to uint
                                try
                                {
                                    npcId = Convert.ToUInt32(objectRef);
                                    if (npcId > 0) npcObjectsFound++;
                                }
                                catch { }
                            }
                            
                            if (npcId > 0 && _npcToQuestMap.TryGetValue(npcId, out var questIds))
                            {
                                questLinkedNpcsFound++;
                                
                                // Debug important NPCs
                                bool isImportantNpc = (npcId == 1000100); // Mother Miounne
                                
                                if (isImportantNpc)
                                {
                                    _logDebug($"*** FOUND Mother Miounne (NPC {npcId}) in Level {level.Key} ***");
                                }

                                // Extract coordinates from Level (indices 0,1,2)
                                var x = Convert.ToSingle(level[0]); // X at index 0
                                var y = Convert.ToSingle(level[1]); // Y at index 1  
                                var z = Convert.ToSingle(level[2]); // Z at index 2

                                // Get TerritoryType from Level index 9 (your research!)
                                uint territoryId = 0;
                                uint mapId = 0;
                                string territoryName = "Unknown";
                                
                                try
                                {
                                    // Get TerritoryType from Level index 9
                                    var territoryValue = level[9];
                                    if (territoryValue is SaintCoinach.Xiv.TerritoryType territoryObj)
                                    {
                                        territoryId = (uint)territoryObj.Key;
                                        territoryName = territoryObj.PlaceName?.Name?.ToString() ?? $"Territory_{territoryId}";
                                        
                                        // Get Map from Territory (the correct way!)
                                        if (territoryObj.Map != null)
                                        {
                                            mapId = (uint)territoryObj.Map.Key;
                                        }
                                        
                                        if (isImportantNpc)
                                        {
                                            _logDebug($"  Got TerritoryType {territoryId} '{territoryName}' from Level[9]");
                                            _logDebug($"  Territory → Map {mapId}");
                                        }
                                    }
                                    else if (territoryValue != null)
                                    {
                                        try
                                        {
                                            territoryId = Convert.ToUInt32(territoryValue);
                                            
                                            // Look up the territory to get its map and name
                                            if (_realm != null && territoryId > 0)
                                            {
                                                var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();
                                                var territory = territorySheet[(int)territoryId];
                                                if (territory != null)
                                                {
                                                    territoryName = territory.PlaceName?.Name?.ToString() ?? $"Territory_{territoryId}";
                                                    if (territory.Map != null)
                                                    {
                                                        mapId = (uint)territory.Map.Key;
                                                    }
                                                }
                                                
                                                if (isImportantNpc)
                                                {
                                                    _logDebug($"  Converted TerritoryType {territoryId} '{territoryName}' and found Map {mapId}");
                                                    
                                                    // Special check for Mother Miounne
                                                    if (territoryId == 132)
                                                    {
                                                        _logDebug($"  *** SUCCESS: Mother Miounne is correctly in Territory 132 (New Gridania)! ***");
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            if (isImportantNpc)
                                            {
                                                _logDebug($"  Error converting TerritoryType: {ex.Message}");
                                            }
                                        }
                                    }
                                    else if (isImportantNpc)
                                    {
                                        _logDebug($"  *** WARNING: Level[9] is null for Mother Miounne! ***");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (isImportantNpc)
                                    {
                                        _logDebug($"  Error getting TerritoryType from Level[9]: {ex.Message}");
                                    }
                                }

                                // Convert world coordinates to map coordinates
                                var mapCoords = ConvertWorldToMapCoordinates(x, y, z, mapId);

                                foreach (var questId in questIds)
                                {
                                    // Only update if we don't already have a location for this quest
                                    if (!_questLocationCache.ContainsKey(questId))
                                    {
                                        var locationData = new QuestLocationData
                                        {
                                            QuestId = questId,
                                            TerritoryId = territoryId, // Using correct Territory ID from Level[9]
                                            MapId = mapId,             // Map derived from Territory
                                            MapX = mapCoords.X,
                                            MapY = mapCoords.Y,
                                            MapZ = mapCoords.Z,
                                            WorldX = x,
                                            WorldY = y,
                                            WorldZ = z,
                                            ObjectType = $"Level_NPC",
                                            ObjectName = $"NPC_{npcId}_{territoryName}",
                                            ObjectId = npcId,
                                            EventId = 0
                                        };

                                        _questLocationCache[questId] = locationData;
                                        foundLocations++;

                                        if (foundLocations <= 10 || isImportantNpc || questId == 65564)
                                        {
                                            _logDebug($"*** CREATED QUEST LOCATION: Quest {questId} via NPC {npcId} in Territory {territoryId} '{territoryName}' → Map {mapId} at world({x:F1},{y:F1},{z:F1}) -> map({mapCoords.X:F1}, {mapCoords.Y:F1}) ***");
                                        }
                                    }
                                }
                            }
                        }

                        if (processedEntries % 5000 == 0)
                        {
                            _logDebug($"Processed {processedEntries} Level entries, found {npcObjectsFound} NPC objects, {questLinkedNpcsFound} quest-linked NPCs, {foundLocations} quest locations...");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_verboseDebugMode)
                        {
                            _logDebug($"Error processing Level entry {processedEntries}: {ex.Message}");
                        }
                    }
                }

                _logDebug($"Level sheet parsing complete: processed {processedEntries} entries, found {npcObjectsFound} NPC objects, {questLinkedNpcsFound} quest-linked NPCs, {foundLocations} quest locations");

                // Check if we found the specific quest from your research
                if (_questLocationCache.ContainsKey(65564))
                {
                    var locationData = _questLocationCache[65564];
                    _logDebug($"SUCCESS: Found location for quest 65564 'To the Bannock': Territory {locationData.TerritoryId} → Map {locationData.MapId}, coords ({locationData.MapX:F1}, {locationData.MapY:F1})");
                    
                    // Special verification for your research
                    if (locationData.TerritoryId == 132)
                    {
                        _logDebug($"*** RESEARCH VERIFIED: Quest 65564 is correctly placed in Territory 132 (New Gridania)! ***");
                    }
                    else
                    {
                        _logDebug($"*** RESEARCH ISSUE: Quest 65564 is in Territory {locationData.TerritoryId}, expected 132 (New Gridania) ***");
                    }
                }
                else
                {
                    _logDebug($"*** FAILED: Quest 65564 'To the Bannock' was NOT found in Level sheet parsing ***");
                }
            }
            catch (Exception ex)
            {
                _logDebug($"Error parsing Level sheet: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse LGB files for additional object locations (DISABLED - using sheet data instead)
        /// </summary>
        private async Task ParseQuestLocationsFromLgbFilesAsync()
        {
            _logDebug("Skipping LGB file parsing - using sheet data instead per user research");
            
            // LGB parsing is disabled because we have all the data we need from sheets:
            // Quest -> NPC (Quest sheet index 37)
            // NPC -> Map (ENpcResident sheet index 9) 
            // NPC -> Coordinates (Level sheet indices 0,1,2 where Level.Object matches NPC)
            
            return;
        }

        private List<string> GetLgbPathsForTerritory(string bgName)
        {
            var paths = new List<string>();

            if (_verboseDebugMode)
            {
                _logDebug($"Getting LGB paths for bgName: '{bgName}'");
            }

            // Handle empty/null bgName
            if (string.IsNullOrEmpty(bgName))
            {
                return paths;
            }

            // Test with a known working path first
            if (bgName == "ffxiv/fst_f1")
            {
                paths.Add("bg/ffxiv/fst_f1/fld/f1f1/level/planmap.lgb");
                paths.Add("bg/ffxiv/fst_f1/level/planevent.lgb");
                paths.Add("bg/ffxiv/fst_f1/level/planmap.lgb");
                paths.Add("bg/ffxiv/fst_f1/level/bg.lgb");
            }
            else
            {
                // Based on FFXIV's standard LGB file structure
                var basePath = bgName.StartsWith("bg/") ? bgName : $"bg/{bgName}";
                
                paths.Add($"{basePath}/level/planevent.lgb");
                paths.Add($"{basePath}/level/planmap.lgb");
                paths.Add($"{basePath}/level/bg.lgb");
            }

            if (_verboseDebugMode)
            {
                _logDebug($"Generated {paths.Count} LGB paths for '{bgName}'");
                foreach (var path in paths.Take(3))
                {
                    _logDebug($"  Path: {path}");
                }
            }

            return paths;
        }

        private uint GetNpcIdFromEntry(LgbENpcEntry npcEntry)
        {
            try
            {
                // Based on SaintCoinach's LgbENpcEntry structure
                // The NPC ID should be in the header
                var headerProp = npcEntry.GetType().GetProperty("Header");
                if (headerProp != null)
                {
                    var header = headerProp.GetValue(npcEntry);
                    if (header != null)
                    {
                        // Try to get ENpcId from header
                        var npcIdProp = header.GetType().GetProperty("ENpcId")
                                     ?? header.GetType().GetProperty("NpcId")
                                     ?? header.GetType().GetProperty("Id");

                        if (npcIdProp != null)
                        {
                            var value = npcIdProp.GetValue(header);
                            if (value != null)
                            {
                                return Convert.ToUInt32(value);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_verboseDebugMode)
                {
                    _logDebug($"Error getting NPC ID from LgbENpcEntry: {ex.Message}");
                }
            }

            return 0;
        }

        private (float X, float Y, float Z) GetEntryCoordinates(ILgbEntry entry)
        {
            try
            {
                // Most LGB entries have Header.Translation
                var headerProp = entry.GetType().GetProperty("Header");
                if (headerProp != null)
                {
                    var header = headerProp.GetValue(entry);
                    if (header != null)
                    {
                        var translationProp = header.GetType().GetProperty("Translation");
                        if (translationProp != null)
                        {
                            var translation = translationProp.GetValue(header);
                            if (translation != null)
                            {
                                // Translation is typically a Vector3 with X, Y, Z properties
                                var xProp = translation.GetType().GetProperty("X");
                                var yProp = translation.GetType().GetProperty("Y");
                                var zProp = translation.GetType().GetProperty("Z");

                                if (xProp != null && yProp != null && zProp != null)
                                {
                                    return (
                                        Convert.ToSingle(xProp.GetValue(translation)),
                                        Convert.ToSingle(yProp.GetValue(translation)),
                                        Convert.ToSingle(zProp.GetValue(translation))
                                    );
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_verboseDebugMode)
                {
                    _logDebug($"Error getting entry coordinates: {ex.Message}");
                }
            }

            return (0f, 0f, 0f);
        }

        private (double X, double Y, double Z) ConvertWorldToMapCoordinates(float worldX, float worldY, float worldZ, uint mapId)
        {
            try
            {
                if (_realm == null) return (21, 21, 0);

                var mapSheet = _realm.GameData.GetSheet<Map>();
                var map = mapSheet[(int)mapId];

                if (map == null) return (21, 21, 0);

                // Use SaintCoinach's built-in conversion method
                double mapX = map.ToMapCoordinate3d(worldX, map.OffsetX);
                double mapY = map.ToMapCoordinate3d(worldZ, map.OffsetY); // Note: Y in map = Z in world

                // Clamp to valid map bounds (1-42)
                mapX = Math.Max(1, Math.Min(42, mapX));
                mapY = Math.Max(1, Math.Min(42, mapY));

                return (mapX, mapY, worldY); // Store world Y as map Z
            }
            catch (Exception ex)
            {
                if (_verboseDebugMode)
                {
                    _logDebug($"Error converting world coordinates: {ex.Message}");
                }
                return (21, 21, 0);
            }
        }

        /// <summary>
        /// Update quest objects with location data
        /// </summary>
        public void UpdateQuestLocations(IEnumerable<QuestInfo> quests)
        {
            int updatedCount = 0;

            try
            {
                foreach (var quest in quests)
                {
                    if (_questLocationCache.TryGetValue(quest.Id, out var locationData))
                    {
                        // Update quest with location data
                        quest.MapId = locationData.MapId;
                        quest.TerritoryId = locationData.TerritoryId;
                        quest.X = locationData.MapX;
                        quest.Y = locationData.MapY;
                        quest.Z = locationData.MapZ;

                        // Also try to get the place name for the quest
                        if (_realm != null && locationData.MapId > 0)
                        {
                            try
                            {
                                var mapSheet = _realm.GameData.GetSheet<Map>();
                                var map = mapSheet[(int)locationData.MapId];
                                if (map?.PlaceName?.Name != null)
                                {
                                    quest.PlaceName = map.PlaceName.Name.ToString();
                                    quest.PlaceNameId = (uint)map.PlaceName.Key;
                                }
                            }
                            catch { }
                        }

                        updatedCount++;

                        if (_verboseDebugMode && updatedCount <= 10)
                        {
                            _logDebug($"Updated quest {quest.Id} '{quest.Name}' with location: Map {locationData.MapId}, coords ({locationData.MapX:F1}, {locationData.MapY:F1})");
                        }
                    }
                }

                _logDebug($"Updated {updatedCount} quests with location data");
            }
            catch (Exception ex)
            {
                _logDebug($"Error updating quest locations: {ex.Message}");
            }
        }

        /// <summary>
        /// Get location data for a specific quest
        /// </summary>
        public QuestLocationData? GetQuestLocationData(uint questId)
        {
            _questLocationCache.TryGetValue(questId, out var locationData);
            return locationData;
        }

        /// <summary>
        /// Get all cached quest location data
        /// </summary>
        public Dictionary<uint, QuestLocationData> GetAllQuestLocationData()
        {
            return new Dictionary<uint, QuestLocationData>(_questLocationCache);
        }
    }

    /// <summary>
    /// Data structure to hold quest location information
    /// </summary>
    public class QuestLocationData
    {
        public uint QuestId { get; set; }
        public uint TerritoryId { get; set; }
        public uint MapId { get; set; }

        // Map coordinates (1-42 system)
        public double MapX { get; set; }
        public double MapY { get; set; }
        public double MapZ { get; set; }

        // World coordinates (raw)
        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public float WorldZ { get; set; }

        // Object information
        public string ObjectType { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public uint ObjectId { get; set; } = 0;
        public uint EventId { get; set; } = 0;
    }
}