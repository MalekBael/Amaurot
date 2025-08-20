using SaintCoinach;
using SaintCoinach.Xiv;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Amaurot.Services
{
    public class NpcService
    {
        private readonly ARealmReversed? _realm;
        private readonly Dictionary<uint, NpcInfo> _npcCache = new();
        private string? _libraDbPath = null;

        public NpcService(ARealmReversed? realm)
        {
            _realm = realm;
            InitializeLibraDatabase();
        }

        /// <summary>
        /// Find Libra Eorzea database - same logic as QuestLocationService
        /// </summary>
        private void InitializeLibraDatabase()
        {
            try
            {
                DebugModeManager.LogDebug("Initializing Libra database for NPC positions...");

                var possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libs", "app_data.sqlite"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Libs", "app_data.sqlite"),
                    Path.Combine(Environment.CurrentDirectory, "Libs", "app_data.sqlite"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaintCoinach", "app_data.sqlite"),
                    Path.Combine(Directory.GetCurrentDirectory(), "app_data.sqlite"),
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        _libraDbPath = path;
                        DebugModeManager.LogFileOperation("Found", "Libra database for NPCs", true, path);
                        break;
                    }
                }

                if (_libraDbPath == null)
                {
                    DebugModeManager.LogError("Libra database not found - NPCs will have no position data");
                }
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"Error initializing Libra database: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract NPCs from Saint Coinach with position data and quest data from Libra Eorzea
        /// </summary>
        public async Task<List<NpcInfo>> ExtractNpcsWithPositionsAsync()
        {
            var npcInfoList = new List<NpcInfo>();

            try
            {
                DebugModeManager.LogDebug("Starting enhanced NPC extraction with Libra Eorzea positions and quests...");

                if (_realm?.GameData == null)
                {
                    DebugModeManager.LogError("Realm or GameData is null - cannot extract NPCs");
                    return npcInfoList;
                }

                await Task.Run(() =>
                {
                    var enpcSheet = _realm.GameData.GetSheet<ENpcResident>();
                    var territorySheet = _realm.GameData.GetSheet<TerritoryType>();

                    DebugModeManager.LogDebug($"Found {enpcSheet.Count()} ENpcResident entries in Saint Coinach");

                    var npcPositions = ExtractNpcPositionsFromLibra();
                    DebugModeManager.LogDebug($"Found position data for {npcPositions.Count} NPCs in Libra database");

                    var npcQuests = ExtractNpcQuestsFromLibra();
                    DebugModeManager.LogDebug($"Found quest data for {npcQuests.Count} NPCs in Libra database");

                    foreach (var enpc in enpcSheet)
                    {
                        try
                        {
                            var npcName = enpc.Singular?.ToString()?.Trim();
                            if (string.IsNullOrEmpty(npcName) || npcName == "0" || npcName.Length < 2)
                                continue;

                            var npcId = (uint)enpc.Key;

                            uint territoryId = 0;
                            uint mapId = 0;
                            double x = 0, y = 0, z = 0;
                            float worldX = 0, worldY = 0, worldZ = 0;
                            string territoryName = "Unknown Territory";

                            if (npcPositions.TryGetValue(npcId, out var position))
                            {
                                territoryId = position.TerritoryId;
                                mapId = position.MapId;
                                x = position.MapX;
                                y = position.MapY;
                                z = position.MapZ;
                                worldX = position.WorldX;
                                worldY = position.WorldY;
                                worldZ = position.WorldZ;
                                territoryName = position.TerritoryName;
                            }

                            var questsForNpc = npcQuests.TryGetValue(npcId, out var questList) ? questList : new List<NpcQuestInfo>();

                            if (questsForNpc.Any() && mapId > 0 && territoryId > 0)
                            {
                                foreach (var quest in questsForNpc)
                                {
                                    if (quest.MapId == 0 || quest.TerritoryId == 0)
                                    {
                                        quest.MapId = mapId;
                                        quest.TerritoryId = territoryId;
                                        quest.MapX = x;
                                        quest.MapY = y;
                                        quest.MapZ = z;
                                    }
                                }
                            }

                            var npcInfo = new NpcInfo
                            {
                                NpcId = npcId,
                                NpcName = npcName,
                                TerritoryId = territoryId,
                                TerritoryName = territoryName,
                                MapId = mapId,
                                MapX = x,
                                MapY = y,
                                MapZ = z,
                                WorldX = worldX,
                                WorldY = worldY,
                                WorldZ = worldZ,
                                QuestCount = questsForNpc.Count,
                                Quests = questsForNpc
                            };

                            npcInfoList.Add(npcInfo);
                            _npcCache[npcId] = npcInfo;
                        }
                        catch (Exception ex)
                        {
                            DebugModeManager.LogError($"Error processing ENpc {enpc.Key}: {ex.Message}");
                        }
                    }
                });

                npcInfoList = npcInfoList.OrderBy(n => n.NpcName).ToList();

                var npcsWithPositions = npcInfoList.Count(n => n.MapId > 0);
                var npcsWithQuests = npcInfoList.Count(n => n.QuestCount > 0);
                DebugModeManager.LogDataLoading("NPCs", npcInfoList.Count, $"{npcsWithPositions} with position data, {npcsWithQuests} with quest data");

                var motherMiounne = npcInfoList.FirstOrDefault(n => n.NpcName.Contains("Mother Miounne"));
                if (motherMiounne != null)
                {
                    DebugModeManager.LogDebug($"VERIFIED: Mother Miounne preserved - ID: {motherMiounne.NpcId}, Territory: {motherMiounne.TerritoryId}, Map: {motherMiounne.MapId}, Quests: {motherMiounne.QuestCount}");
                }
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"Error in enhanced NPC extraction: {ex.Message}");
            }

            return npcInfoList;
        }

        /// <summary>
        /// Extract quest data for NPCs from Libra Eorzea database
        /// </summary>
        private Dictionary<uint, List<NpcQuestInfo>> ExtractNpcQuestsFromLibra()
        {
            var npcQuests = new Dictionary<uint, List<NpcQuestInfo>>();

            if (string.IsNullOrEmpty(_libraDbPath))
            {
                DebugModeManager.LogError("No Libra database available for NPC quest data");
                return npcQuests;
            }

            try
            {
                using var connection = new SQLiteConnection($"Data Source={_libraDbPath};Version=3;Read Only=True;");
                connection.Open();

                var query = @"
                    SELECT
                        q.Key as quest_id,
                        COALESCE(q.Name_en, CAST(q.Key AS TEXT)) as quest_name,
                        q.Client as npc_id,
                        COALESCE(n.SGL_en, n.Index_en, CAST(n.Key AS TEXT)) as npc_name,
                        q.Genre as journal_genre_id,
                        COALESCE(jg.Name_en, 'Unknown Genre') as journal_genre_name,
                        q.ClassLevel as level_required,
                        q.Gil as gil_reward,
                        q.Area as place_name_id,
                        COALESCE(pn.SGL_en, 'Unknown Location') as place_name
                    FROM Quest q
                    INNER JOIN ENpcResident n ON q.Client = n.Key
                    LEFT JOIN JournalGenre jg ON q.Genre = jg.Key
                    LEFT JOIN PlaceName pn ON q.Area = pn.Key
                    WHERE q.Client > 0
                      AND q.Key > 0
                      AND COALESCE(q.Name_en, '') != ''
                    ORDER BY q.Client, q.Key";

                using var command = new SQLiteCommand(query, connection);
                using var reader = command.ExecuteReader();

                int questCount = 0;
                var debugNpcIds = new HashSet<uint>();

                while (reader.Read())
                {
                    try
                    {
                        var questId = Convert.ToUInt32(reader["quest_id"]);
                        var questName = reader["quest_name"]?.ToString() ?? $"Quest_{questId}";
                        var npcId = Convert.ToUInt32(reader["npc_id"]);
                        var npcName = reader["npc_name"]?.ToString() ?? $"NPC_{npcId}";
                        var journalGenreName = reader["journal_genre_name"]?.ToString() ?? "Unknown Genre";
                        var levelRequired = Convert.ToUInt32(reader["level_required"] ?? 0);
                        var gilReward = Convert.ToUInt32(reader["gil_reward"] ?? 0);
                        var placeNameId = Convert.ToUInt32(reader["place_name_id"] ?? 0);
                        var placeName = reader["place_name"]?.ToString() ?? "Unknown Location";

                        var questInfo = new NpcQuestInfo
                        {
                            QuestId = questId,
                            QuestName = questName,
                            MapId = 0,
                            TerritoryId = 0,
                            MapX = 0,
                            MapY = 0,
                            MapZ = 0,
                            JournalGenre = journalGenreName,
                            LevelRequired = levelRequired,
                            IsMainScenario = false,
                            IsFeatureQuest = false,
                            ExpReward = 0,
                            GilReward = gilReward,
                            PlaceNameId = placeNameId,
                            PlaceName = placeName
                        };

                        if (!npcQuests.TryGetValue(npcId, out var questList))
                        {
                            questList = new List<NpcQuestInfo>();
                            npcQuests[npcId] = questList;
                        }

                        questList.Add(questInfo);
                        questCount++;

                        if (debugNpcIds.Count < 5 && debugNpcIds.Add(npcId))
                        {
                            DebugModeManager.LogDebug($"NPC {npcId} ({npcName}) offers quest: '{questName}' (Level {levelRequired}, {journalGenreName})");
                        }

                        if (npcName.Contains("Mother Miounne"))
                        {
                            DebugModeManager.LogDebug($"Found quest for Mother Miounne (NPC {npcId}): '{questName}' (ID: {questId})");
                        }
                    }
                    catch (Exception rowEx)
                    {
                        DebugModeManager.LogError($"Error processing quest row: {rowEx.Message}");
                    }
                }

                DebugModeManager.LogDebug($"Processed {questCount} quests for {npcQuests.Count} NPCs from Libra database");

                if (npcQuests.Count > 0)
                {
                    var topQuestGivers = npcQuests.OrderByDescending(kvp => kvp.Value.Count).Take(5).ToList();
                    DebugModeManager.LogDebug($"Top quest givers:");
                    foreach (var (npcId, quests) in topQuestGivers)
                    {
                        var firstQuest = quests.FirstOrDefault();
                        var npcName = firstQuest?.PlaceName ?? $"NPC_{npcId}";
                        DebugModeManager.LogDebug($"  NPC {npcId}: {quests.Count} quests");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"Error extracting NPC quest data from Libra: {ex.Message}");
            }

            return npcQuests;
        }

        /// <summary>
        /// Extract NPC positions from Libra Eorzea database using the same logic as QuestLocationService
        /// </summary>
        private Dictionary<uint, NpcLocationData> ExtractNpcPositionsFromLibra()
        {
            var npcPositions = new Dictionary<uint, NpcLocationData>();

            if (string.IsNullOrEmpty(_libraDbPath))
            {
                DebugModeManager.LogError("No Libra database available for NPC positions");
                return npcPositions;
            }

            try
            {
                using var connection = new SQLiteConnection($"Data Source={_libraDbPath};Version=3;Read Only=True;");
                connection.Open();

                var query = @"
                    SELECT
                        n.Key as npc_id,
                        COALESCE(n.SGL_en, n.Index_en, CAST(n.Key AS TEXT)) as npc_name,
                        CAST(n.data AS TEXT) as npc_data
                    FROM ENpcResident n
                    WHERE n.data IS NOT NULL
                      AND CAST(n.data AS TEXT) LIKE '%coordinate%'
                      AND n.Key > 0
                    ORDER BY n.Key";

                using var command = new SQLiteCommand(query, connection);
                using var reader = command.ExecuteReader();

                int processedCount = 0;
                int validPositionsFound = 0;
                int invalidMapIdCount = 0;

                while (reader.Read())
                {
                    try
                    {
                        var npcId = Convert.ToUInt32(reader["npc_id"]);
                        var npcName = reader["npc_name"]?.ToString() ?? $"NPC_{npcId}";
                        var npcDataJson = reader["npc_data"]?.ToString() ?? "";

                        processedCount++;

                        if (processedCount <= 5)
                        {
                            DebugModeManager.LogDebug($"Processing NPC {npcId}: '{npcName}'");
                        }

                        var coordinateData = ParseNpcCoordinates(npcDataJson, npcId, connection);
                        if (coordinateData != null)
                        {
                            if (coordinateData.MapId == 0)
                            {
                                invalidMapIdCount++;
                                if (npcName.Contains("Mother Miounne"))
                                {
                                    DebugModeManager.LogWarning("Mother Miounne has invalid Map ID 0, skipped coordinate data");
                                }
                                continue;
                            }

                            var locationData = new NpcLocationData
                            {
                                NpcId = npcId,
                                NpcName = npcName,
                                TerritoryId = coordinateData.TerritoryId,
                                TerritoryName = GetTerritoryName(coordinateData.TerritoryId),
                                MapId = coordinateData.MapId,
                                MapX = coordinateData.MapX,
                                MapY = coordinateData.MapY,
                                MapZ = coordinateData.MapZ,
                                WorldX = coordinateData.WorldX,
                                WorldY = coordinateData.WorldY,
                                WorldZ = coordinateData.WorldZ
                            };

                            npcPositions[npcId] = locationData;
                            validPositionsFound++;

                            if (processedCount <= 5 || npcName.Contains("Mother Miounne"))
                            {
                                DebugModeManager.LogDebug($"NPC {npcId} ({npcName}) -> Territory {coordinateData.TerritoryId}, Map {coordinateData.MapId}, Coords ({coordinateData.MapX:F1}, {coordinateData.MapY:F1})");
                            }
                        }
                    }
                    catch (Exception rowEx)
                    {
                        DebugModeManager.LogError($"Error processing NPC row: {rowEx.Message}");
                    }
                }

                DebugModeManager.LogDebug($"Processed {processedCount} NPCs from Libra, found {validPositionsFound} with valid positions, skipped {invalidMapIdCount} with invalid Map ID 0");
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"Error extracting NPC positions from Libra: {ex.Message}");
            }

            return npcPositions;
        }

        /// <summary>
        /// Parse NPC coordinates using the same logic as QuestLocationService
        /// </summary>
        private NpcCoordinateData? ParseNpcCoordinates(string npcDataJson, uint npcId, SQLiteConnection connection)
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

                    uint saintCoinachTerritoryId = MapLibraToSaintCoinachTerritory(libraPlaceNameId, connection);
                    uint mapId = GetMapIdForTerritory(saintCoinachTerritoryId);

                    if (saintCoinachTerritoryId == 0 || mapId == 0)
                    {
                        continue;
                    }

                    var convertedCoords = ConvertLibraToMapMarkerCoordinates(rawLibraX, rawLibraY, mapId);

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
                DebugModeManager.LogError($"Error parsing NPC coordinates for NPC {npcId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Convert Libra coordinates to map marker coordinates (same as QuestLocationService)
        /// </summary>
        private (double X, double Y, double Z) ConvertLibraToMapMarkerCoordinates(double rawLibraX, double rawLibraY, uint mapId)
        {
            try
            {
                double gameCoordX = rawLibraX / 10.0;
                double gameCoordY = rawLibraY / 10.0;

                if (_realm == null)
                {
                    return (gameCoordX, gameCoordY, 0);
                }

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
                    catch { }

                    double c = sizeFactor / 100.0;
                    double markerX = ((gameCoordX - 1.0) * c / 41.0) * 2048.0 - offsetX;
                    double markerY = ((gameCoordY - 1.0) * c / 41.0) * 2048.0 - offsetY;

                    return (markerX, markerY, 0);
                }
                else
                {
                    return (gameCoordX, gameCoordY, 0);
                }
            }
            catch (Exception ex)
            {
                DebugModeManager.LogDebug($"Error converting coordinates for MapId {mapId}: {ex.Message}");

                double gameCoordX = rawLibraX / 10.0;
                double gameCoordY = rawLibraY / 10.0;
                return (gameCoordX, gameCoordY, 0);
            }
        }

        /// <summary>
        /// Map Libra PlaceName ID to Saint Coinach Territory (simplified version)
        /// </summary>
        private uint MapLibraToSaintCoinachTerritory(uint libraPlaceNameId, SQLiteConnection connection)
        {
            try
            {
                if (_realm == null) return 0;

                var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();

                var territory = territorySheet.FirstOrDefault(t => t.PlaceName?.Key == libraPlaceNameId);
                if (territory != null)
                {
                    return (uint)territory.Key;
                }

                var placeNameQuery = "SELECT SGL_en FROM PlaceName WHERE Key = ?";
                using var command = new SQLiteCommand(placeNameQuery, connection);
                command.Parameters.AddWithValue("Key", libraPlaceNameId);

                var placeName = command.ExecuteScalar()?.ToString();
                if (!string.IsNullOrEmpty(placeName))
                {
                    territory = territorySheet.FirstOrDefault(t =>
                        string.Equals(t.PlaceName?.ToString(), placeName, StringComparison.OrdinalIgnoreCase));

                    if (territory != null)
                    {
                        return (uint)territory.Key;
                    }
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Get map ID for territory
        /// </summary>
        private uint GetMapIdForTerritory(uint territoryId)
        {
            try
            {
                if (_realm != null && territoryId > 0)
                {
                    var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();
                    var territory = territorySheet.FirstOrDefault(t => t.Key == territoryId);
                    if (territory?.Map != null)
                    {
                        return (uint)territory.Map.Key;
                    }
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Get territory name
        /// </summary>
        private string GetTerritoryName(uint territoryId)
        {
            try
            {
                if (_realm != null && territoryId > 0)
                {
                    var territorySheet = _realm.GameData.GetSheet<SaintCoinach.Xiv.TerritoryType>();
                    var territory = territorySheet.FirstOrDefault(t => t.Key == territoryId);
                    return territory?.PlaceName?.ToString() ?? $"Territory_{territoryId}";
                }

                return "Unknown Territory";
            }
            catch
            {
                return "Unknown Territory";
            }
        }

        public async Task<List<NpcInfo>> ExtractNpcsFromSaintCoinachAsync()
        {
            var npcInfoList = new List<NpcInfo>();

            try
            {
                DebugModeManager.LogDebug("Starting basic NPC extraction from Saint Coinach ENpcResident sheet...");

                if (_realm?.GameData == null)
                {
                    DebugModeManager.LogError("Realm or GameData is null - cannot extract NPCs");
                    return npcInfoList;
                }

                await Task.Run(() =>
                {
                    var enpcSheet = _realm.GameData.GetSheet<ENpcResident>();
                    DebugModeManager.LogDebug($"Found {enpcSheet.Count()} ENpcResident entries in Saint Coinach");

                    foreach (var enpc in enpcSheet)
                    {
                        try
                        {
                            var npcName = enpc.Singular?.ToString()?.Trim();
                            if (string.IsNullOrEmpty(npcName) || npcName == "0" || npcName.Length < 2)
                                continue;

                            var npcInfo = new NpcInfo
                            {
                                NpcId = (uint)enpc.Key,
                                NpcName = npcName,
                                TerritoryId = 0,
                                TerritoryName = "Unknown Territory",
                                MapId = 0,
                                MapX = 0,
                                MapY = 0,
                                MapZ = 0,
                                WorldX = 0,
                                WorldY = 0,
                                WorldZ = 0,
                                QuestCount = 0,
                                Quests = new List<NpcQuestInfo>()
                            };

                            npcInfoList.Add(npcInfo);
                            _npcCache[(uint)enpc.Key] = npcInfo;
                        }
                        catch (Exception ex)
                        {
                            DebugModeManager.LogError($"Error processing ENpc {enpc.Key}: {ex.Message}");
                        }
                    }
                });

                npcInfoList = npcInfoList.OrderBy(n => n.NpcName).ToList();
                DebugModeManager.LogDataLoading("NPCs", npcInfoList.Count, "basic extraction complete");
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"Error extracting NPCs from Saint Coinach: {ex.Message}");
            }

            return npcInfoList;
        }

        public NpcInfo? GetNpcInfo(uint npcId)
        {
            _npcCache.TryGetValue(npcId, out var npcInfo);
            return npcInfo;
        }

        public List<NpcInfo> FilterNpcsByTerritory(List<NpcInfo> allNpcs, uint currentTerritoryId)
        {
            return allNpcs.Where(n => n.TerritoryId == currentTerritoryId).ToList();
        }

        public List<NpcInfo> FilterNpcsByMap(List<NpcInfo> allNpcs, uint currentMapId)
        {
            return allNpcs.Where(n => n.MapId == currentMapId).ToList();
        }
    }

    // Data classes
    public class NpcInfo
    {
        public uint NpcId { get; set; }
        public string NpcName { get; set; } = string.Empty;
        public uint TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;
        public uint MapId { get; set; }
        public double MapX { get; set; }
        public double MapY { get; set; }
        public double MapZ { get; set; }
        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public float WorldZ { get; set; }
        public int QuestCount { get; set; }
        public List<NpcQuestInfo> Quests { get; set; } = new();
    }

    public class NpcQuestInfo
    {
        public uint QuestId { get; set; }
        public string QuestName { get; set; } = string.Empty;
        public uint MapId { get; set; }
        public uint TerritoryId { get; set; }
        public double MapX { get; set; }
        public double MapY { get; set; }
        public double MapZ { get; set; }
        public string JournalGenre { get; set; } = string.Empty;
        public uint LevelRequired { get; set; }
        public bool IsMainScenario { get; set; }
        public bool IsFeatureQuest { get; set; }
        public uint ExpReward { get; set; }
        public uint GilReward { get; set; }
        public uint PlaceNameId { get; set; }
        public string PlaceName { get; set; } = string.Empty;
    }

    public class NpcLocationData
    {
        public uint NpcId { get; set; }
        public string NpcName { get; set; } = string.Empty;
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