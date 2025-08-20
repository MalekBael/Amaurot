using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Lumina;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using SaintCoinach;
using SaintCoinach.Xiv;
// ✅ ADD: Missing using directive for Entities namespace
using Amaurot.Services.Entities;

namespace Amaurot.Services
{
    public class QuestBattleLgbService
    {
        private readonly ARealmReversed? _realm;
        private readonly GameData? _luminaGameData;
        private readonly Dictionary<uint, List<QuestBattleLgbMarker>> _territoryQuestBattleCache = new();

        public QuestBattleLgbService(ARealmReversed? realm)
        {
            _realm = realm;

            if (_realm?.GameData != null)
            {
                try
                {
                    DebugModeManager.LogServiceInitialization("QuestBattleLgbService", false, "Initializing Lumina GameData");

                    string? coinachPath = null;

                    var gameDataType = _realm.GameData.GetType();
                    var directoryProperty = gameDataType.GetProperty("Directory", BindingFlags.Public | BindingFlags.Instance);

                    if (directoryProperty != null)
                    {
                        var directory = directoryProperty.GetValue(_realm.GameData);
                        if (directory != null)
                        {
                            coinachPath = directory.ToString();
                        }
                    }

                    if (string.IsNullOrEmpty(coinachPath))
                    {
                        var knownPath = @"D:\Final Fantasy XIV - Sapphire\3.35\FINAL FANTASY XIV - A Realm Reborn";
                        if (Directory.Exists(knownPath))
                        {
                            coinachPath = knownPath;
                        }
                    }

                    if (!string.IsNullOrEmpty(coinachPath))
                    {
                        var luminaPath = Path.Combine(coinachPath, "game", "sqpack");
                        if (Directory.Exists(luminaPath))
                        {
                            _luminaGameData = new GameData(luminaPath);
                            DebugModeManager.LogServiceInitialization("QuestBattleLgbService", true, "Lumina GameData initialized successfully");
                        }
                        else
                        {
                            DebugModeManager.LogError($"Lumina path does not exist: {luminaPath}");
                        }
                    }
                    else
                    {
                        DebugModeManager.LogError("Could not determine path for Lumina initialization");
                    }
                }
                catch (Exception ex)
                {
                    DebugModeManager.LogError($"Error initializing Lumina GameData for Quest Battles: {ex.Message}");
                }
            }

            DebugModeManager.LogServiceInitialization(
                "QuestBattleLgbService",
                _realm != null && _luminaGameData != null,
                $"realm: {_realm != null}, lumina: {_luminaGameData != null}"
            );
        }

        public async Task<List<MapMarker>> LoadQuestBattleMarkersFromLgbAsync(uint mapId)
        {
            return await Task.Run(() => LoadQuestBattleMarkersFromLgb(mapId));
        }

        public List<MapMarker> LoadQuestBattleMarkersFromLgb(uint mapId)
        {
            var markers = new List<MapMarker>();

            if (_realm == null || _luminaGameData == null)
            {
                DebugModeManager.LogError($"Quest Battle service not properly initialized (realm: {_realm != null}, lumina: {_luminaGameData != null})");
                return markers;
            }

            try
            {
                var mapSheet = _realm.GameData.GetSheet<Map>();
                var map = mapSheet[(int)mapId];
                if (map == null)
                {
                    DebugModeManager.LogWarning($"Map not found for ID: {mapId}");
                    return markers;
                }

                var territoryId = (uint)map.TerritoryType.Key;
                var territoryFolderName = GetTerritoryFolderName(territoryId);

                DebugModeManager.LogDebug($"Loading Quest Battle markers for Map {mapId}, Territory {territoryId}");

                if (_territoryQuestBattleCache.TryGetValue(territoryId, out var cachedQuestBattles))
                {
                    DebugModeManager.LogCacheOperation("Using", "Quest Battle", cachedQuestBattles.Count, territoryId);
                    return ProcessCachedQuestBattleMarkers(cachedQuestBattles, map);
                }

                var lgbQuestBattles = LoadLgbQuestBattleDataFromLumina(territoryFolderName, territoryId);
                if (lgbQuestBattles.Count == 0)
                {
                    DebugModeManager.LogDebug($"No Quest Battle data found for territory {territoryId}");
                    return markers;
                }

                foreach (var lgbQuestBattle in lgbQuestBattles)
                {
                    try
                    {
                        var marker = ProcessLgbQuestBattle(lgbQuestBattle, map);
                        if (marker != null)
                        {
                            markers.Add(marker);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugModeManager.LogError($"Error processing Quest Battle {lgbQuestBattle.LayerName}: {ex.Message}");
                    }
                }

                DebugModeManager.LogMarkerCreation("Quest Battle", markers.Count, mapId);
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"Error loading Quest Battle markers for map {mapId}: {ex.Message}");
            }

            return markers;
        }

        private List<MapMarker> ProcessCachedQuestBattleMarkers(List<QuestBattleLgbMarker> cachedQuestBattles, Map map)
        {
            var markers = new List<MapMarker>();

            foreach (var lgbQuestBattle in cachedQuestBattles)
            {
                var marker = ProcessLgbQuestBattle(lgbQuestBattle, map);
                if (marker != null)
                {
                    markers.Add(marker);
                }
            }

            return markers;
        }

        private MapMarker? ProcessLgbQuestBattle(QuestBattleLgbMarker lgbQuestBattle, Map map)
        {
            string questBattleName = lgbQuestBattle.LayerName;

            if (questBattleName.StartsWith("QB_", StringComparison.OrdinalIgnoreCase))
            {
                questBattleName = questBattleName.Substring(3);
            }

            uint questBattleId = (uint)Math.Abs((lgbQuestBattle.LayerName + map.Key).GetHashCode());

            uint iconId = 61806;

            DebugModeManager.LogDebug($"Processing QB {lgbQuestBattle.Type}: {lgbQuestBattle.LayerName} -> {questBattleName} at ({lgbQuestBattle.X:F1}, {lgbQuestBattle.Y:F1}, {lgbQuestBattle.Z:F1})");

            return ConvertLgbToMapMarkerFast(lgbQuestBattle, map, questBattleId, questBattleName, iconId);
        }

        private MapMarker? ConvertLgbToMapMarkerFast(QuestBattleLgbMarker lgbQuestBattle, Map map, uint questBattleId, string questBattleName, uint iconId)
        {
            try
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
                        var sizeFactorValue = indexer.GetValue(map, new object[] { "SizeFactor" });
                        var offsetXValue = indexer.GetValue(map, new object[] { "OffsetX" });
                        var offsetYValue = indexer.GetValue(map, new object[] { "OffsetY" });

                        sizeFactor = sizeFactorValue != null ? (float)Convert.ChangeType(sizeFactorValue, typeof(float)) : 200.0f;
                        offsetX = offsetXValue != null ? (float)Convert.ChangeType(offsetXValue, typeof(float)) : 0f;
                        offsetY = offsetYValue != null ? (float)Convert.ChangeType(offsetYValue, typeof(float)) : 0f;
                    }
                }
                catch { }

                double c = sizeFactor / 100.0;
                double gameX = (lgbQuestBattle.X + 1024.0) / 50.0 + 1.0;
                double gameY = (lgbQuestBattle.Z + 1024.0) / 50.0 + 1.5;
                double normalizedX = (gameX - 1.0) * c / 41.0;
                double normalizedY = (gameY - 1.4) * c / 41.0;
                double mapX = normalizedX * 2048.0 - offsetX;
                double mapY = normalizedY * 2048.0 - offsetY;

                DebugModeManager.LogCoordinateConversion("LGB", "Map", mapX, mapY, lgbQuestBattle.Y, (uint)map.Key);

                return new MapMarker
                {
                    Id = questBattleId,
                    MapId = (uint)map.Key,
                    X = mapX,
                    Y = mapY,
                    Z = lgbQuestBattle.Y,
                    PlaceName = $"Quest Battle: {questBattleName}",
                    PlaceNameId = questBattleId,
                    IconId = 061418,
                    IconPath = "ui/icon/061000/061418.tex",
                    Type = MarkerType.QuestBattle,
                    IsVisible = true
                };
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"Error converting Quest Battle {lgbQuestBattle.LayerName}: {ex.Message}");
                return null;
            }
        }

        private List<QuestBattleLgbMarker> LoadLgbQuestBattleDataFromLumina(string territoryFolderName, uint territoryId)
        {
            var questBattles = new List<QuestBattleLgbMarker>();

            if (_luminaGameData == null) return questBattles;

            try
            {
                if (_territoryQuestBattleCache.TryGetValue(territoryId, out var cachedMarkers))
                {
                    return cachedMarkers;
                }

                var lgbFiles = new[]
                {
                    ("planner.lgb", "Planner"),
                    ("planevent.lgb", "PlanEvent")
                };

                var possiblePaths = new[]
                {
                    "bg/ffxiv/fst_f1/fld/{0}/level/{1}",
                    "bg/ffxiv/sea_s1/fld/{0}/level/{1}",
                    "bg/ffxiv/wil_w1/fld/{0}/level/{1}",
                    "bg/ffxiv/roc_r1/fld/{0}/level/{1}",
                    "bg/ffxiv/lak_l1/fld/{0}/level/{1}",
                    "bg/ffxiv/air_a1/fld/{0}/level/{1}",

                    "bg/ffxiv/fst_f1/twn/{0}/level/{1}",
                    "bg/ffxiv/sea_s1/twn/{0}/level/{1}",
                    "bg/ffxiv/wil_w1/twn/{0}/level/{1}",
                    "bg/ffxiv/roc_r1/twn/{0}/level/{1}",
                    "bg/ffxiv/lak_l1/twn/{0}/level/{1}",

                    "bg/ffxiv/fst_f1/dun/{0}/level/{1}",
                    "bg/ffxiv/sea_s1/dun/{0}/level/{1}",
                    "bg/ffxiv/wil_w1/dun/{0}/level/{1}",
                    "bg/ffxiv/roc_r1/dun/{0}/level/{1}",
                    "bg/ffxiv/lak_l1/dun/{0}/level/{1}",
                    "bg/ffxiv/air_a1/dun/{0}/level/{1}",

                    "bg/ffxiv/fst_f1/evt/{0}/level/{1}",
                    "bg/ffxiv/sea_s1/evt/{0}/level/{1}",
                    "bg/ffxiv/wil_w1/evt/{0}/level/{1}",
                    "bg/ffxiv/roc_r1/evt/{0}/level/{1}",
                    "bg/ffxiv/lak_l1/evt/{0}/level/{1}",
                    "bg/ffxiv/air_a1/evt/{0}/level/{1}",
                };

                foreach (var (lgbFileName, lgbType) in lgbFiles)
                {
                    LgbFile? lgbFile = null;
                    string loadedPath = "";

                    foreach (var pathTemplate in possiblePaths)
                    {
                        var path = string.Format(pathTemplate, territoryFolderName, lgbFileName);
                        try
                        {
                            lgbFile = _luminaGameData.GetFile<LgbFile>(path);
                            if (lgbFile != null)
                            {
                                loadedPath = path;
                                DebugModeManager.LogFileOperation("Load", $"{lgbType} LGB", true, path);
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                        }
                    }

                    if (lgbFile == null)
                    {
                        DebugModeManager.LogWarning($"No {lgbType} LGB found for territory {territoryId}");
                        continue;
                    }

                    var layerCount = lgbFile.Layers?.Count() ?? 0;
                    DebugModeManager.LogLgbProcessing(lgbType, territoryFolderName, territoryId, layerCount);

                    foreach (var layer in lgbFile.Layers)
                    {
                        if (layer.Name.StartsWith("QB_", StringComparison.OrdinalIgnoreCase))
                        {
                            DebugModeManager.LogDebug($"Found QB_ layer: {layer.Name} in {lgbType}");

                            foreach (var instanceObject in layer.InstanceObjects)
                            {
                                if (instanceObject.AssetType == LayerEntryType.PopRange ||
                                    instanceObject.AssetType == LayerEntryType.CollisionBox)
                                {
                                    questBattles.Add(new QuestBattleLgbMarker
                                    {
                                        LayerName = layer.Name,
                                        X = instanceObject.Transform.Translation.X,
                                        Y = instanceObject.Transform.Translation.Y,
                                        Z = instanceObject.Transform.Translation.Z,
                                        Type = instanceObject.AssetType.ToString(),
                                        Source = $"{lgbType}.lgb"
                                    });

                                    DebugModeManager.LogDebug($"Added QB {instanceObject.AssetType} marker: {layer.Name} at ({instanceObject.Transform.Translation.X:F1}, {instanceObject.Transform.Translation.Y:F1}, {instanceObject.Transform.Translation.Z:F1})");

                                    break;
                                }
                            }
                        }
                        else if (layer.Name.IndexOf("QuestBattle", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            DebugModeManager.LogDebug($"Found QuestBattle layer: {layer.Name} in {lgbType}");

                            foreach (var instanceObject in layer.InstanceObjects)
                            {
                                if (instanceObject.AssetType == LayerEntryType.PopRange ||
                                    instanceObject.AssetType == LayerEntryType.CollisionBox)
                                {
                                    questBattles.Add(new QuestBattleLgbMarker
                                    {
                                        LayerName = layer.Name,
                                        X = instanceObject.Transform.Translation.X,
                                        Y = instanceObject.Transform.Translation.Y,
                                        Z = instanceObject.Transform.Translation.Z,
                                        Type = instanceObject.AssetType.ToString(),
                                        Source = $"{lgbType}.lgb"
                                    });

                                    DebugModeManager.LogDebug($"Added QuestBattle {instanceObject.AssetType} marker: {layer.Name} at ({instanceObject.Transform.Translation.X:F1}, {instanceObject.Transform.Translation.Y:F1}, {instanceObject.Transform.Translation.Z:F1})");
                                    break;
                                }
                            }
                        }
                    }
                }

                _territoryQuestBattleCache[territoryId] = questBattles;
                DebugModeManager.LogCacheOperation("Cached", "QB markers", questBattles.Count, territoryId);
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"Error loading Quest Battle LGB data: {ex.Message}");
            }

            return questBattles;
        }

        private string GetTerritoryFolderName(uint territoryId)
        {
            var territoryFolderMap = new Dictionary<uint, string>
            {
                { 128, "s1t1" }, { 129, "s1t2" }, { 134, "s1f1" }, { 135, "s1f2" },
                { 137, "s1f3" }, { 138, "s1f4" }, { 139, "s1f5" }, { 180, "s1f6" },
                { 130, "w1t1" }, { 131, "w1t2" }, { 140, "w1f1" }, { 141, "w1f2" },
                { 145, "w1f3" }, { 146, "w1f4" }, { 147, "w1f5" },
                { 132, "f1t1" }, { 133, "f1t2" }, { 148, "f1f1" }, { 152, "f1f2" },
                { 153, "f1f3" }, { 154, "f1f4" },
                { 155, "r1f1" },
                { 156, "l1f1" },
            };

            return territoryFolderMap.TryGetValue(territoryId, out var folderName)
                ? folderName
                : $"unknown_{territoryId}";
        }

        // ✅ NEW: Method to load QuestBattleInfo entities
        public async Task<List<QuestBattleInfo>> LoadQuestBattlesAsync()
        {
            return await Task.Run(() => LoadAllQuestBattles());
        }

        private List<QuestBattleInfo> LoadAllQuestBattles()
        {
            var questBattles = new List<QuestBattleInfo>();

            if (_realm?.GameData == null)
            {
                DebugModeManager.LogError("Quest Battle service not properly initialized");
                return questBattles;
            }

            try
            {
                DebugModeManager.LogDebug("Loading all Quest Battles from all territories...");

                var territorySheet = _realm.GameData.GetSheet<TerritoryType>();
                var mapSheet = _realm.GameData.GetSheet<Map>();

                foreach (var territory in territorySheet)
                {
                    try
                    {
                        var territoryId = (uint)territory.Key;
                        var territoryFolderName = GetTerritoryFolderName(territoryId);

                        // Skip unknown territories
                        if (territoryFolderName.StartsWith("unknown_"))
                            continue;

                        var territoryName = territory.PlaceName?.Name?.ToString() ?? $"Territory_{territoryId}";
                        var map = mapSheet.FirstOrDefault(m => m.TerritoryType.Key == territory.Key);
                        var mapId = map != null ? (uint)map.Key : 0;

                        var lgbQuestBattles = LoadLgbQuestBattleDataFromLumina(territoryFolderName, territoryId);

                        foreach (var lgbQuestBattle in lgbQuestBattles)
                        {
                            try
                            {
                                string questBattleName = lgbQuestBattle.LayerName;
                                if (questBattleName.StartsWith("QB_", StringComparison.OrdinalIgnoreCase))
                                {
                                    questBattleName = questBattleName.Substring(3);
                                }

                                // ✅ FIXED: Use QuestBattleInfo without Entities prefix since we have the using directive
                                var questBattleInfo = new QuestBattleInfo
                                {
                                    Id = (uint)Math.Abs((lgbQuestBattle.LayerName + territoryId).GetHashCode()),
                                    Name = questBattleName,
                                    QuestBattleName = questBattleName,
                                    TerritoryId = territoryId,
                                    TerritoryName = territoryName,
                                    MapId = mapId,
                                    MapX = lgbQuestBattle.X,
                                    MapY = lgbQuestBattle.Y,
                                    MapZ = lgbQuestBattle.Z,
                                    LayerName = lgbQuestBattle.LayerName,
                                    AssetType = lgbQuestBattle.Type,
                                    Source = lgbQuestBattle.Source,
                                    IconId = 61806,
                                    IconPath = "ui/icon/061000/061806.tex"
                                };

                                questBattles.Add(questBattleInfo);
                            }
                            catch (Exception ex)
                            {
                                DebugModeManager.LogError($"Error processing Quest Battle {lgbQuestBattle.LayerName}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugModeManager.LogError($"Error processing territory {territory.Key}: {ex.Message}");
                    }
                }

                DebugModeManager.LogDataLoading("Quest Battles", questBattles.Count, "from LGB files");
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"Error loading Quest Battles: {ex.Message}");
            }

            return questBattles.OrderBy(qb => qb.QuestBattleName).ToList();
        }

        public void Dispose()
        {
            try
            {
                _luminaGameData?.Dispose();
            }
            catch { }
        }
    }

    public class QuestBattleLgbMarker
    {
        public string LayerName { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }
}