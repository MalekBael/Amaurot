using SaintCoinach;
using SaintCoinach.Xiv;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Data;
using System.Data.SQLite;
using System.IO;

// ✅ FIX: Use entity types consistently
using QuestInfo = Amaurot.Services.Entities.QuestInfo;
using QuestNpcInfo = Amaurot.Services.Entities.QuestNpcInfo;

namespace Amaurot.Services
{
    /// <summary>
    /// Service for extracting quest locations using Libra Eorzea SQL database
    /// Gets NPC coordinates directly from Libra - no Level sheet parsing needed!
    /// </summary>
    public class QuestLocationService
    {
        private readonly ARealmReversed? _realm;
        private readonly Action<string> _logDebug;
        private readonly Dictionary<uint, QuestLocationData> _questLocationCache = new();
        private readonly Dictionary<uint, uint> _territoryMappingCache = new();
        private bool _verboseDebugMode = false;
        private string? _libraDbPath = null;

        public QuestLocationService(ARealmReversed? realm, Action<string> logDebug)
        {
            _realm = realm;
            _logDebug = logDebug ?? (msg => { });
            InitializeLibraDatabase();
        }

        public void SetVerboseDebugMode(bool enabled)
        {
            _verboseDebugMode = enabled;
            _logDebug($"Quest location service verbose debug mode set to: {enabled}");
        }

        /// <summary>
        /// Find Libra Eorzea database - check for app_data.sqlite in Libs directory
        /// </summary>
        private void InitializeLibraDatabase()
        {
            try
            {
                _logDebug("🔍 Searching for Libra Eorzea database...");

                var possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libs", "app_data.sqlite"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Libs", "app_data.sqlite"),
                    Path.Combine(Environment.CurrentDirectory, "Libs", "app_data.sqlite"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaintCoinach", "ffxiv-datamining.db"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaintCoinach", "ffxiv-datamining-latest.db"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaintCoinach", "app_data.sqlite"),
                    Path.Combine(Directory.GetCurrentDirectory(), "app_data.sqlite"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "app_data.sqlite"),
                };

                foreach (var path in possiblePaths)
                {
                    _logDebug($"  Checking: {path}");
                    if (File.Exists(path))
                    {
                        _libraDbPath = path;
                        var fileInfo = new FileInfo(path);
                        _logDebug($"✅ Found Libra Eorzea database!");
                        _logDebug($"   📍 Location: {path}");
                        _logDebug($"   📏 Size: {fileInfo.Length / (1024 * 1024):F1} MB");
                        _logDebug($"   📅 Modified: {fileInfo.LastWriteTime}");

                        try
                        {
                            using var testConnection = new SQLiteConnection($"Data Source={path};Version=3;Read Only=True;");
                            testConnection.Open();

                            var testQuery = "SELECT name FROM sqlite_master WHERE type='table' LIMIT 5";
                            using var testCommand = new SQLiteCommand(testQuery, testConnection);
                            using var testReader = testCommand.ExecuteReader();

                            var testTables = new List<string>();
                            while (testReader.Read())
                            {
                                testTables.Add(testReader.GetString(0));
                            }

                            _logDebug($"   🔍 Database test successful! Sample tables: {string.Join(", ", testTables)}");
                            break;
                        }
                        catch (Exception testEx)
                        {
                            _logDebug($"   ❌ Database test failed: {testEx.Message}");
                            _libraDbPath = null;
                            continue;
                        }
                    }
                }

                if (_libraDbPath == null)
                {
                    _logDebug("❌ Libra Eorzea database not found in any location!");
                    _logDebug("🔄 Will use Saint Coinach sheet fallback (limited quest markers)");
                }
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error searching for Libra database: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract quest locations - Libra first, then Saint Coinach fallback
        /// </summary>
        public async Task<Dictionary<uint, QuestLocationData>> ExtractQuestLocationsAsync()
        {
            _logDebug("🎯 Starting quest location extraction...");

            if (_libraDbPath != null)
            {
                _logDebug("📊 Using Libra Eorzea database (preferred method)");
                await ExtractFromLibraDatabase();
            }
            else
            {
                _logDebug("📋 Libra database not available, using Saint Coinach Quest[37] fallback...");
                await ExtractFromSaintCoinachQuests();
            }

            _logDebug($"🎯 Quest location extraction complete! Found {_questLocationCache.Count} quest locations");

            // ✅ ADD: Debug MapId distribution
            if (_verboseDebugMode)
            {
                var mapIdCounts = _questLocationCache.Values
                    .GroupBy(q => q.MapId)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .ToList();

                _logDebug($"🗺️ MAP ID DISTRIBUTION (Top 10):");
                foreach (var group in mapIdCounts)
                {
                    _logDebug($"  MapId {group.Key}: {group.Count()} quests");
                }

                var mapId1Count = _questLocationCache.Values.Count(q => q.MapId == 1);
                if (mapId1Count > 0)
                {
                    _logDebug($"⚠️ WARNING: {mapId1Count} quests have MapId=1 (likely incorrect default)");
                }

                // ✅ COORDINATE RANGE DEBUG
                var coordRanges = _questLocationCache.Values.Where(q => q.MapX > 0 && q.MapY > 0).ToList();
                if (coordRanges.Count > 0)
                {
                    var minX = coordRanges.Min(q => q.MapX);
                    var maxX = coordRanges.Max(q => q.MapX);
                    var minY = coordRanges.Min(q => q.MapY);
                    var maxY = coordRanges.Max(q => q.MapY);

                    _logDebug($"📍 COORDINATE RANGES:");
                    _logDebug($"  X: {minX:F1} to {maxX:F1}");
                    _logDebug($"  Y: {minY:F1} to {maxY:F1}");

                    if (maxX > 50 || maxY > 50)
                    {
                        _logDebug($"⚠️ WARNING: Coordinates seem to large (should be 1-42 range)");
                    }
                }
            }

            return _questLocationCache;
        }

        // ✅ ENHANCED: Store quest name in location data for better marker naming
        private async Task ExtractFromLibraDatabase()
        {
            try
            {
                _logDebug("📊 Extracting quest locations from Libra Eorzea database...");

                await Task.Run(() =>
                {
                    using var connection = new SQLiteConnection($"Data Source={_libraDbPath};Version=3;Read Only=True;");
                    connection.Open();

                    // First, let's explore the database structure to understand the column names
                    var schemaQuery = "PRAGMA table_info(ENpcResident)";
                    using var schemaCommand = new SQLiteCommand(schemaQuery, connection);
                    using var schemaReader = schemaCommand.ExecuteReader();

                    var columns = new List<string>();
                    while (schemaReader.Read())
                    {
                        columns.Add(schemaReader.GetString("name"));
                    }

                    if (_verboseDebugMode)
                    {
                        _logDebug($"📊 ENpcResident table columns: {string.Join(", ", columns)}");
                    }

                    // Updated query using correct column names from the CSV structure
                    var query = @"
                        SELECT
                            q.Key as quest_id,
                            COALESCE(q.Name_en, CAST(q.Key AS TEXT)) as quest_name,
                            q.Client as npc_id,
                            COALESCE(n.SGL_en, n.Index_en, CAST(n.Key AS TEXT)) as npc_name,
                            CAST(n.data AS TEXT) as npc_data
                        FROM Quest q
                        INNER JOIN ENpcResident n ON q.Client = n.Key
                        WHERE q.Client > 0
                          AND n.data IS NOT NULL
                          AND CAST(n.data AS TEXT) LIKE '%coordinate%'
                        ORDER BY q.Key
                        LIMIT 1000";

                    using var command = new SQLiteCommand(query, connection);
                    using var reader = command.ExecuteReader();

                    int questCount = 0;
                    int mapId1Count = 0;
                    var territoryMappingFailures = new List<(uint questId, uint libraPlaceNameId, string? placeName)>();

                    while (reader.Read())
                    {
                        try
                        {
                            var questId = Convert.ToUInt32(reader["quest_id"]);
                            var questName = reader["quest_name"]?.ToString() ?? $"Quest_{questId}";
                            var npcId = Convert.ToUInt32(reader["npc_id"]);
                            var npcName = reader["npc_name"]?.ToString() ?? $"NPC_{npcId}";
                            var npcDataJson = reader["npc_data"]?.ToString() ?? "";

                            if (_verboseDebugMode && questCount < 5)
                            {
                                _logDebug($"📊 Processing Quest {questId}: '{questName}' -> NPC {npcId}: '{npcName}'");
                            }

                            var coordinateData = ParseNpcCoordinates(npcDataJson, npcId, connection, questId, territoryMappingFailures);
                            if (coordinateData == null)
                            {
                                if (_verboseDebugMode && questCount < 5)
                                {
                                    _logDebug($"📊 No valid coordinates found for Quest {questId}");
                                }
                                continue;
                            }

                            // ✅ ENHANCED: Track MapId 1 issues
                            if (coordinateData.MapId == 1)
                            {
                                mapId1Count++;
                            }

                            var locationData = new QuestLocationData
                            {
                                QuestId = questId,
                                QuestName = questName, // ✅ STORE QUEST NAME
                                NpcName = npcName,     // ✅ STORE NPC NAME
                                TerritoryId = coordinateData.TerritoryId,
                                MapId = coordinateData.MapId,
                                MapX = coordinateData.MapX,
                                MapY = coordinateData.MapY,
                                MapZ = coordinateData.MapZ,
                                WorldX = coordinateData.WorldX,
                                WorldY = coordinateData.WorldY,
                                WorldZ = coordinateData.WorldZ,
                                ObjectType = "Libra_ENpcResident",
                                ObjectName = $"{npcName}_Territory_{coordinateData.TerritoryId}",
                                ObjectId = npcId,
                                EventId = 0
                            };

                            _questLocationCache[questId] = locationData;
                            questCount++;

                            if (_verboseDebugMode && questCount <= 5)
                            {
                                _logDebug($"✅ Quest {questId} ({questName}) -> Territory {coordinateData.TerritoryId}, Map {coordinateData.MapId}, Coords ({coordinateData.MapX:F1}, {coordinateData.MapY:F1})");
                            }
                        }
                        catch (Exception rowEx)
                        {
                            _logDebug($"❌ Error processing row: {rowEx.Message}");
                        }
                    }

                    _logDebug($"📊 Extracted {questCount} quest locations from Libra database");

                    // ✅ ENHANCED: Report MapId 1 issues
                    if (mapId1Count > 0)
                    {
                        _logDebug($"⚠️ WARNING: {mapId1Count}/{questCount} quests assigned MapId=1 (likely incorrect)");
                    }

                    // ✅ ENHANCED: Report territory mapping failures
                    if (territoryMappingFailures.Count > 0 && _verboseDebugMode)
                    {
                        _logDebug($"🗺️ TERRITORY MAPPING FAILURES ({territoryMappingFailures.Count}):");
                        foreach (var failure in territoryMappingFailures.Take(10))
                        {
                            _logDebug($"  Quest {failure.questId}: Libra PlaceName ID {failure.libraPlaceNameId} ('{failure.placeName}') → No matching SaintCoinach territory");
                        }
                        if (territoryMappingFailures.Count > 10)
                        {
                            _logDebug($"  ... and {territoryMappingFailures.Count - 10} more");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error extracting from Libra database: {ex.Message}");
                await ExtractFromSaintCoinachQuests();
            }
        }

        private async Task ExtractFromSaintCoinachQuests()
        {
            try
            {
                _logDebug("📋 Using Saint Coinach Quest[37] fallback...");
                await Task.Run(() =>
                {
                    if (_realm?.GameData == null)
                    {
                        _logDebug("❌ Realm or GameData is null");
                        return;
                    }

                    var questSheet = _realm.GameData.GetSheet<Quest>();
                    int questCount = 0;

                    foreach (var quest in questSheet.Take(1000))
                    {
                        try
                        {
                            var questId = (uint)quest.Key;
                            var questName = quest.Name?.ToString() ?? $"Quest_{questId}";
                            var npcValue = quest[37];

                            if (npcValue != null && uint.TryParse(npcValue.ToString(), out uint npcId) && npcId > 0)
                            {
                                var locationData = new QuestLocationData
                                {
                                    QuestId = questId,
                                    TerritoryId = 0,
                                    MapId = 0,
                                    MapX = 0,
                                    MapY = 0,
                                    MapZ = 0,
                                    WorldX = 0,
                                    WorldY = 0,
                                    WorldZ = 0,
                                    ObjectType = "Quest_Giver_No_Coords",
                                    ObjectName = $"NPC_{npcId}_NoCoords",
                                    ObjectId = npcId,
                                    EventId = 0
                                };

                                _questLocationCache[questId] = locationData;
                                questCount++;
                            }
                        }
                        catch { }
                    }

                    _logDebug($"📋 Saint Coinach fallback complete: {questCount} quests found (no coordinates)");
                });
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error in Saint Coinach fallback: {ex.Message}");
            }
        }

        private NpcCoordinateData? ParseNpcCoordinates(string npcDataJson, uint npcId, SQLiteConnection connection,
            uint questId = 0, List<(uint questId, uint libraPlaceNameId, string? placeName)>? territoryMappingFailures = null)
        {
            try
            {
                if (string.IsNullOrEmpty(npcDataJson) || !npcDataJson.TrimStart().StartsWith("{"))
                    return null;

                using var jsonDoc = System.Text.Json.JsonDocument.Parse(npcDataJson);
                var root = jsonDoc.RootElement;

                if (!root.TryGetProperty("coordinate", out var coordinateElement))
                    return null;

                foreach (var territoryProperty in coordinateElement.EnumerateObject())
                {
                    if (!uint.TryParse(territoryProperty.Name, out uint libraPlaceNameId))
                        continue;

                    var coordinateArray = territoryProperty.Value;
                    if (coordinateArray.ValueKind != System.Text.Json.JsonValueKind.Array)
                        continue;

                    var firstCoordSet = coordinateArray[0];
                    if (firstCoordSet.ValueKind != System.Text.Json.JsonValueKind.Array || firstCoordSet.GetArrayLength() < 2)
                        continue;

                    var xString = firstCoordSet[0].GetString();
                    var yString = firstCoordSet[1].GetString();

                    if (string.IsNullOrEmpty(xString) || string.IsNullOrEmpty(yString))
                        continue;

                    if (!double.TryParse(xString, out double rawLibraX) || !double.TryParse(yString, out double rawLibraY))
                        continue;

                    // ✅ ENHANCED: Better territory mapping with detailed logging
                    uint saintCoinachTerritoryId = MapLibraToSaintCoinachTerritory(libraPlaceNameId, connection, questId, territoryMappingFailures);
                    uint mapId = GetMapIdForTerritory(saintCoinachTerritoryId);

                    // ✅ ENHANCED: Log problematic MapId assignments
                    if (_verboseDebugMode && mapId == 1 && saintCoinachTerritoryId == 0)
                    {
                        string? placeName = GetLibraPlaceName(libraPlaceNameId, connection);
                        _logDebug($"⚠️ Quest {questId} assigned MapId=1 due to failed territory mapping: Libra PlaceName ID {libraPlaceNameId} ('{placeName}')");
                    }

                    var convertedCoords = ConvertLibraToMapMarkerCoordinates(rawLibraX, rawLibraY, mapId);

                    if (_verboseDebugMode)
                    {
                        _logDebug($"📊 NPC {npcId}: Libra PlaceName {libraPlaceNameId} → Territory {saintCoinachTerritoryId}, Map {mapId}");
                        _logDebug($"📊 Raw Libra coords: ({rawLibraX:F1}, {rawLibraY:F1}) → Map marker coords: ({convertedCoords.X:F1}, {convertedCoords.Y:F1})");
                    }

                    return new NpcCoordinateData
                    {
                        TerritoryId = saintCoinachTerritoryId,
                        MapId = mapId,
                        MapX = convertedCoords.X,
                        MapY = convertedCoords.Y,
                        MapZ = convertedCoords.Z,
                        WorldX = (float)rawLibraX,
                        WorldY = 0,
                        WorldZ = (float)rawLibraY
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error parsing NPC coordinates for NPC {npcId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ✅ COMPLETELY REWRITTEN: Convert Libra coordinates to match exactly what MapRenderer expects
        /// MapRenderer expects raw coordinates that when processed as (coord + offset) / 2048 give correct positions
        /// </summary>
        private (double X, double Y, double Z) ConvertLibraToMapMarkerCoordinates(double rawLibraX, double rawLibraY, uint mapId)
        {
            try
            {
                // ✅ STEP 1: First, correct the Libra coordinates by dividing by 10 to get game coordinates (1-42 range)
                double gameCoordX = rawLibraX / 10.0;
                double gameCoordY = rawLibraY / 10.0;

                if (_realm == null)
                {
                    // ✅ SIMPLE FALLBACK: Just use the game coordinates directly
                    // The MapRenderer will handle the normalization
                    return (gameCoordX, gameCoordY, 0);
                }

                var mapSheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.Map>();
                var map = mapSheet.FirstOrDefault(m => m.Key == mapId);

                if (map != null)
                {
                    // ✅ Get map properties the same way MapRenderer does
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
                        if (_verboseDebugMode)
                        {
                            _logDebug($"    Error getting map properties: {ex.Message}");
                        }
                    }

                    // ✅ KEY INSIGHT: MapRenderer does this calculation:
                    // normalizedX = (marker.X + offsetX) / 2048.0
                    // normalizedY = (marker.Y + offsetY) / 2048.0
                    // gameX = (41.0 / c) * (normalizedX) + 1.0  where c = sizeFactor / 100.0
                    // gameY = (41.0 / c) * (normalizedY) + 1.0

                    // We need to reverse this to find what marker.X should be
                    // From: gameCoord = (41.0 / c) * ((marker.X + offset) / 2048.0) + 1.0
                    // Solve for marker.X:
                    // gameCoord - 1.0 = (41.0 / c) * ((marker.X + offset) / 2048.0)
                    // (gameCoord - 1.0) * c / 41.0 = (marker.X + offset) / 2048.0
                    // ((gameCoord - 1.0) * c / 41.0) * 2048.0 = marker.X + offset
                    // marker.X = ((gameCoord - 1.0) * c / 41.0) * 2048.0 - offset

                    double c = sizeFactor / 100.0;
                    double markerX = ((gameCoordX - 1.0) * c / 41.0) * 2048.0 - offsetX;
                    double markerY = ((gameCoordY - 1.0) * c / 41.0) * 2048.0 - offsetY;

                    if (_verboseDebugMode)
                    {
                        _logDebug($"    ✅ COORDINATE CONVERSION (MATCHED TO MAPRENDERER):");
                        _logDebug($"      Raw Libra: ({rawLibraX:F1}, {rawLibraY:F1})");
                        _logDebug($"      Game Coords: ({gameCoordX:F1}, {gameCoordY:F1}) [1-42 range]");
                        _logDebug($"      Map {mapId}: SizeFactor={sizeFactor}, OffsetX={offsetX}, OffsetY={offsetY}, c={c:F3}");
                        _logDebug($"      Final Marker Coords: ({markerX:F1}, {markerY:F1})");

                        // ✅ VERIFICATION: Show what MapRenderer will calculate
                        double verifyNormX = (markerX + offsetX) / 2048.0;
                        double verifyNormY = (markerY + offsetY) / 2048.0;
                        double verifyGameX = (41.0 / c) * verifyNormX + 1.0;
                        double verifyGameY = (41.0 / c) * verifyNormY + 1.0;
                        _logDebug($"      Verification - MapRenderer will calculate: ({verifyGameX:F1}, {verifyGameY:F1}) [should match game coords]");
                    }

                    return (markerX, markerY, 0);
                }
                else
                {
                    if (_verboseDebugMode)
                    {
                        _logDebug($"    Map {mapId} not found in map sheet, using game coordinates directly");
                    }

                    // ✅ FALLBACK: Use game coordinates directly
                    return (gameCoordX, gameCoordY, 0);
                }
            }
            catch (Exception ex)
            {
                _logDebug($"Error converting coordinates for MapId {mapId}: {ex.Message}");

                // Fallback to simple game coordinate conversion
                double gameCoordX = rawLibraX / 10.0;
                double gameCoordY = rawLibraY / 10.0;
                return (gameCoordX, gameCoordY, 0);
            }
        }

        // ✅ ADD: The missing MapLibraToSaintCoinachTerritory method
        private uint MapLibraToSaintCoinachTerritory(uint libraPlaceNameId, SQLiteConnection connection,
            uint questId = 0, List<(uint questId, uint libraPlaceNameId, string? placeName)>? territoryMappingFailures = null)
        {
            if (_territoryMappingCache.TryGetValue(libraPlaceNameId, out uint cachedMapping))
            {
                return cachedMapping;
            }

            try
            {
                // ✅ FIRST: Try direct ID mapping - Libra PlaceName IDs might match SaintCoinach PlaceName IDs
                uint saintCoinachTerritoryId = FindSaintCoinachTerritoryByPlaceNameId(libraPlaceNameId);

                if (saintCoinachTerritoryId == 0)
                {
                    // If direct ID didn't work, get the place name and try name matching
                    string? libraPlaceName = GetLibraPlaceName(libraPlaceNameId, connection);

                    if (!string.IsNullOrEmpty(libraPlaceName))
                    {
                        // Try exact name match
                        saintCoinachTerritoryId = FindSaintCoinachTerritoryByPlaceName(libraPlaceName);

                        if (saintCoinachTerritoryId == 0)
                        {
                            // Try partial matching with cleaned names
                            saintCoinachTerritoryId = FindSaintCoinachTerritoryByPartialName(libraPlaceName);
                        }

                        if (saintCoinachTerritoryId == 0)
                        {
                            // Try finding by normalized name (remove special characters, articles)
                            saintCoinachTerritoryId = FindSaintCoinachTerritoryByNormalizedName(libraPlaceName);
                        }
                    }

                    if (saintCoinachTerritoryId == 0)
                    {
                        // FALLBACK: Try common known mappings
                        saintCoinachTerritoryId = GetKnownTerritoryMapping(libraPlaceNameId);
                    }

                    if (saintCoinachTerritoryId == 0)
                    {
                        // Track the failure for debugging
                        string? placeName = GetLibraPlaceName(libraPlaceNameId, connection);
                        territoryMappingFailures?.Add((questId, libraPlaceNameId, placeName));

                        if (_verboseDebugMode)
                        {
                            _logDebug($"❌ Failed to map Libra PlaceName ID {libraPlaceNameId} ('{placeName}') → No SaintCoinach territory found");
                        }
                    }
                    else if (_verboseDebugMode)
                    {
                        string? placeName = GetLibraPlaceName(libraPlaceNameId, connection);
                        _logDebug($"✅ Alternative mapping successful: Libra PlaceName ID {libraPlaceNameId} ('{placeName}') → Territory {saintCoinachTerritoryId}");
                    }
                }

                _territoryMappingCache[libraPlaceNameId] = saintCoinachTerritoryId;

                if (_verboseDebugMode && saintCoinachTerritoryId > 0)
                {
                    string? placeName = GetLibraPlaceName(libraPlaceNameId, connection);
                    _logDebug($"📊 Mapped Libra PlaceName ID {libraPlaceNameId} ('{placeName}') → SaintCoinach Territory {saintCoinachTerritoryId}");
                }

                return saintCoinachTerritoryId;
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error mapping Libra PlaceName ID {libraPlaceNameId}: {ex.Message}");
                _territoryMappingCache[libraPlaceNameId] = 0;
                return 0;
            }
        }

        // ✅ ADD: Try finding by normalized name
        private uint FindSaintCoinachTerritoryByNormalizedName(string libraPlaceName)
        {
            try
            {
                if (_realm?.GameData == null || string.IsNullOrEmpty(libraPlaceName))
                    return 0;

                var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();

                // Normalize the Libra place name
                string normalizedLibraName = NormalizePlaceName(libraPlaceName);

                // Try to find a territory with a matching normalized name
                var territory = territorySheet.FirstOrDefault(t =>
                {
                    var territoryPlaceName = t.PlaceName?.ToString();
                    if (string.IsNullOrEmpty(territoryPlaceName))
                        return false;

                    string normalizedTerritoryName = NormalizePlaceName(territoryPlaceName);
                    return normalizedLibraName.Equals(normalizedTerritoryName, StringComparison.OrdinalIgnoreCase);
                });

                return territory != null ? (uint)territory.Key : 0;
            }
            catch (Exception ex)
            {
                if (_verboseDebugMode)
                {
                    _logDebug($"❌ Error in normalized name matching for '{libraPlaceName}': {ex.Message}");
                }
                return 0;
            }
        }

        // ✅ ADD: Normalize place names for better matching
        private string NormalizePlaceName(string placeName)
        {
            // Remove common articles and prepositions
            string normalized = placeName;

            // Remove "The " from the beginning
            if (normalized.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(4);

            // Remove parenthetical content
            int parenIndex = normalized.IndexOf('(');
            if (parenIndex > 0)
                normalized = normalized.Substring(0, parenIndex).Trim();

            // Remove special characters
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^\w\s]", "");

            // Collapse multiple spaces
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
        }

        // ✅ ADD: Known territory mappings for common areas
        private uint GetKnownTerritoryMapping(uint libraPlaceNameId)
        {
            // These are common mappings that might not match by name
            // You can expand this based on debug logs
            var knownMappings = new Dictionary<uint, uint>
            {
                // Add known mappings here as you discover them
                // Example: { libraPlaceNameId, saintCoinachTerritoryId }
            };

            return knownMappings.TryGetValue(libraPlaceNameId, out uint territoryId) ? territoryId : 0;
        }

        // ✅ ADD: Missing method for exact place name matching
        private uint FindSaintCoinachTerritoryByPlaceName(string placeName)
        {
            try
            {
                if (_realm?.GameData == null || string.IsNullOrEmpty(placeName))
                    return 0;

                var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();

                // Try exact match first
                var territory = territorySheet.FirstOrDefault(t =>
                    string.Equals(t.PlaceName?.ToString(), placeName, StringComparison.OrdinalIgnoreCase));

                if (territory != null)
                {
                    if (_verboseDebugMode)
                    {
                        _logDebug($"✅ Exact name match: '{placeName}' → Territory {territory.Key}");
                    }
                    return (uint)territory.Key;
                }

                return 0;
            }
            catch (Exception ex)
            {
                if (_verboseDebugMode)
                {
                    _logDebug($"❌ Error finding SaintCoinach territory for '{placeName}': {ex.Message}");
                }
                return 0;
            }
        }

        // ✅ ADD: Missing method for partial name matching
        private uint FindSaintCoinachTerritoryByPartialName(string libraPlaceName)
        {
            try
            {
                if (_realm?.GameData == null || string.IsNullOrEmpty(libraPlaceName))
                    return 0;

                var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();

                // Clean the place name (remove extra spaces)
                string cleanedLibraName = libraPlaceName.Trim().Replace("  ", " ");

                // Try partial matching (contains)
                var territory = territorySheet.FirstOrDefault(t =>
                {
                    var territoryName = t.PlaceName?.ToString();
                    if (string.IsNullOrEmpty(territoryName))
                        return false;

                    return territoryName.Contains(cleanedLibraName, StringComparison.OrdinalIgnoreCase) ||
                           cleanedLibraName.Contains(territoryName, StringComparison.OrdinalIgnoreCase);
                });

                if (territory != null)
                {
                    if (_verboseDebugMode)
                    {
                        _logDebug($"✅ Partial name match: '{libraPlaceName}' → Territory {territory.Key}");
                    }
                    return (uint)territory.Key;
                }

                return 0;
            }
            catch (Exception ex)
            {
                if (_verboseDebugMode)
                {
                    _logDebug($"❌ Error in partial name matching for '{libraPlaceName}': {ex.Message}");
                }
                return 0;
            }
        }

        // ✅ UPDATE: Fix the PlaceName ID matching method (remove Region check)
        private uint FindSaintCoinachTerritoryByPlaceNameId(uint libraPlaceNameId)
        {
            try
            {
                if (_realm?.GameData == null)
                    return 0;

                var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();

                // First try exact PlaceName.Key match
                var territory = territorySheet.FirstOrDefault(t => t.PlaceName?.Key == libraPlaceNameId);

                if (territory != null)
                {
                    if (_verboseDebugMode)
                    {
                        _logDebug($"✅ Direct PlaceName ID match: {libraPlaceNameId} → Territory {territory.Key}");
                    }
                    return (uint)territory.Key;
                }

                // ✅ FIXED: Check PlaceNameRegion and PlaceNameZone properties
                // Try matching against PlaceNameRegion
                territory = territorySheet.FirstOrDefault(t =>
                {
                    try
                    {
                        // Access PlaceNameRegion using indexer since it's at index 3
                        var placeNameRegion = t[3];
                        if (placeNameRegion != null)
                        {
                            // Check if it's a PlaceName link and compare the key
                            var placeNameRegionObj = placeNameRegion as dynamic;
                            if (placeNameRegionObj?.Key == libraPlaceNameId)
                                return true;
                        }

                        // Also check PlaceNameZone at index 4
                        var placeNameZone = t[4];
                        if (placeNameZone != null)
                        {
                            var placeNameZoneObj = placeNameZone as dynamic;
                            if (placeNameZoneObj?.Key == libraPlaceNameId)
                                return true;
                        }
                    }
                    catch
                    {
                        // Ignore any access errors
                    }
                    return false;
                });

                if (territory != null)
                {
                    if (_verboseDebugMode)
                    {
                        _logDebug($"✅ PlaceNameRegion/Zone ID match: {libraPlaceNameId} → Territory {territory.Key}");
                    }
                    return (uint)territory.Key;
                }

                return 0;
            }
            catch (Exception ex)
            {
                if (_verboseDebugMode)
                {
                    _logDebug($"❌ Error in PlaceName ID matching for ID {libraPlaceNameId}: {ex.Message}");
                }
                return 0;
            }
        }

        // ✅ IMPROVED: Get Libra place name with better error handling
        private string? GetLibraPlaceName(uint placeNameId, SQLiteConnection connection)
        {
            try
            {
                // First try by Key
                var keyQuery = @"SELECT SGL_en FROM PlaceName WHERE Key = ?";
                using var keyCommand = new SQLiteCommand(keyQuery, connection);
                keyCommand.Parameters.AddWithValue("Key", placeNameId);

                var placeName = keyCommand.ExecuteScalar()?.ToString();
                if (!string.IsNullOrEmpty(placeName))
                {
                    return placeName;
                }

                // Then try by rowid
                var placeNameQuery = @"SELECT SGL_en FROM PlaceName WHERE rowid = ?";
                using var placeCommand = new SQLiteCommand(placeNameQuery, connection);
                placeCommand.Parameters.AddWithValue("rowid", placeNameId);

                placeName = placeCommand.ExecuteScalar()?.ToString();

                if (_verboseDebugMode && string.IsNullOrEmpty(placeName))
                {
                    _logDebug($"⚠️ No PlaceName found for ID {placeNameId} in Libra database");
                }

                return placeName;
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error getting PlaceName for ID {placeNameId}: {ex.Message}");
                return null;
            }
        }

        // ✅ IMPROVED: Better fallback for unmapped territories
        private uint GetMapIdForTerritory(uint saintCoinachTerritoryId)
        {
            try
            {
                if (_realm != null && saintCoinachTerritoryId > 0)
                {
                    var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();
                    var territory = territorySheet.FirstOrDefault(t => t.Key == saintCoinachTerritoryId);
                    if (territory?.Map != null)
                    {
                        uint mapId = (uint)territory.Map.Key;

                        if (_verboseDebugMode && mapId == 1)
                        {
                            _logDebug($"⚠️ Territory {saintCoinachTerritoryId} → MapId {mapId} (check if this is correct)");
                        }

                        return mapId;
                    }
                }

                // ✅ IMPORTANT: Don't default to MapId 1 for missing territories
                // Return 0 instead so we can handle it properly upstream
                if (_verboseDebugMode && saintCoinachTerritoryId > 0)
                {
                    _logDebug($"⚠️ No map found for territory {saintCoinachTerritoryId}, returning 0 instead of defaulting to 1");
                }

                return 0; // Return 0 instead of 1 to indicate "no valid map"
            }
            catch (Exception ex)
            {
                _logDebug($"Error getting MapId for Territory {saintCoinachTerritoryId}: {ex.Message}");
                return 0; // Return 0 instead of 1
            }
        }

        public void UpdateQuestLocations(IEnumerable<QuestInfo> quests) // ✅ FIX: Use entity QuestInfo
        {
            int updatedCount = 0;

            try
            {
                foreach (var quest in quests)
                {
                    if (_questLocationCache.TryGetValue(quest.Id, out var locationData))
                    {
                        quest.MapId = locationData.MapId;
                        quest.TerritoryId = locationData.TerritoryId;
                        quest.MapX = locationData.MapX; // ✅ FIX: Use MapX instead of X
                        quest.MapY = locationData.MapY; // ✅ FIX: Use MapY instead of Y
                        quest.MapZ = locationData.MapZ; // ✅ FIX: Use MapZ instead of Z

                        var questGiver = new QuestNpcInfo // ✅ FIX: Use entity QuestNpcInfo
                        {
                            NpcId = locationData.ObjectId,
                            NpcName = GetNpcNameFromId(locationData.ObjectId),
                            TerritoryId = locationData.TerritoryId,
                            TerritoryName = ExtractTerritoryFromObjectName(locationData.ObjectName),
                            MapId = locationData.MapId,
                            MapX = locationData.MapX,
                            MapY = locationData.MapY,
                            MapZ = locationData.MapZ,
                            WorldX = locationData.WorldX,
                            WorldY = locationData.WorldY,
                            WorldZ = locationData.WorldZ,
                            Role = "Quest Giver"
                        };

                        quest.StartNpcs.Clear();
                        quest.StartNpcs.Add(questGiver);

                        updatedCount++;
                    }
                }

                _logDebug($"🎯 Updated {updatedCount} quests with NPC coordinate data from Libra");
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error updating quest locations: {ex.Message}");
            }
        }

        private string GetNpcNameFromId(uint npcId)
        {
            try
            {
                if (_realm != null)
                {
                    var enpcSheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.ENpcResident>();
                    var npc = enpcSheet.FirstOrDefault(n => n.Key == npcId);
                    if (npc != null)
                    {
                        return npc.Singular?.ToString() ?? $"NPC_{npcId}";
                    }
                }
            }
            catch { }

            return $"NPC_{npcId}";
        }

        private string ExtractTerritoryFromObjectName(string objectName)
        {
            try
            {
                if (objectName.Contains("_"))
                {
                    var parts = objectName.Split('_');
                    if (parts.Length > 1)
                    {
                        return parts[1];
                    }
                }
                return objectName;
            }
            catch
            {
                return "Unknown Location";
            }
        }

        public QuestLocationData? GetQuestLocationData(uint questId)
        {
            _questLocationCache.TryGetValue(questId, out var locationData);
            return locationData;
        }

        public Dictionary<uint, QuestLocationData> GetAllQuestLocationData()
        {
            return new Dictionary<uint, QuestLocationData>(_questLocationCache);
        }

        // Data classes
        public class QuestLocationData
        {
            public uint QuestId { get; set; }
            public string QuestName { get; set; } = string.Empty; // ✅ ADD
            public string NpcName { get; set; } = string.Empty;   // ✅ ADD
            public uint TerritoryId { get; set; }
            public uint MapId { get; set; }
            public double MapX { get; set; }
            public double MapY { get; set; }
            public double MapZ { get; set; }
            public float WorldX { get; set; }
            public float WorldY { get; set; }
            public float WorldZ { get; set; }
            public string ObjectType { get; set; } = string.Empty;
            public string ObjectName { get; set; } = string.Empty;
            public uint ObjectId { get; set; } = 0;
            public uint EventId { get; set; } = 0;
        }

        public class QuestNpcData
        {
            public uint QuestId { get; set; }
            public string QuestName { get; set; } = string.Empty;
            public uint NpcId { get; set; }
            public string NpcName { get; set; } = string.Empty;
        }

        public class NpcLocationData
        {
            public uint NpcId { get; set; }
            public uint TerritoryId { get; set; }
            public string TerritoryName { get; set; } = string.Empty;
            public uint MapId { get; set; }
            public float WorldX { get; set; }
            public float WorldY { get; set; }
            public float WorldZ { get; set; }
            public double MapX { get; set; }
            public double MapY { get; set; }
            public double MapZ { get; set; }
        }

        public class NpcCoordinateData
        {
            public uint TerritoryId { get; set; }
            public uint MapId { get; set; }
            public double MapX { get; set; }
            public double MapY { get; set; }
            public double MapZ { get; set; }
            public float WorldX { get; set; }
            public float WorldY { get; set; }
            public float WorldZ { get; set; }
        }
    }
}