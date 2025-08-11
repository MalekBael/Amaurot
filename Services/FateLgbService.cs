using SaintCoinach;
using SaintCoinach.Xiv;
using System.Text.Json;
using System.Reflection;
using System.IO;
using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;

namespace Amaurot.Services
{
    public class FateLgbService
    {
        private readonly ARealmReversed? _realm;
        private readonly GameData? _luminaGameData;
        private readonly Action<string> _logDebug;
        private readonly Dictionary<uint, List<FateLgbMarker>> _territoryFateCache = new();

        // ✅ NEW: Cache for Fate sheet lookup to avoid repeated linear searches
        private readonly Dictionary<uint, object?> _fateSheetCache = new();

        private bool _fateSheetCacheInitialized = false;

        public FateLgbService(ARealmReversed? realm, Action<string> logDebug)
        {
            _realm = realm;
            _logDebug = logDebug;

            // ✅ SIMPLIFIED: Initialize Lumina GameData for direct LGB access
            if (_realm?.GameData != null)
            {
                try
                {
                    _logDebug($"🔍 Initializing Lumina GameData...");

                    string? coinachPath = null;

                    // Try to get the Directory property from Saint Coinach's GameData
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

                    // Fallback to hardcoded path if needed
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
                            _logDebug($"✅ Lumina GameData initialized successfully");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logDebug($"❌ Error initializing Lumina GameData: {ex.Message}");
                }
            }
        }

        // ✅ NEW: Async method for non-blocking FATE loading
        public async Task<List<MapMarker>> LoadFateMarkersFromLgbAsync(uint mapId)
        {
            return await Task.Run(() => LoadFateMarkersFromLgb(mapId));
        }

        // ✅ OPTIMIZED: Reduced logging, faster processing
        public List<MapMarker> LoadFateMarkersFromLgb(uint mapId)
        {
            var markers = new List<MapMarker>();

            if (_realm == null || _luminaGameData == null)
            {
                return markers;
            }

            try
            {
                // Get territory ID from map
                var mapSheet = _realm.GameData.GetSheet<Map>();
                var map = mapSheet[(int)mapId];
                if (map == null) return markers;

                var territoryId = (uint)map.TerritoryType.Key;
                var territoryFolderName = GetTerritoryFolderName(territoryId);

                _logDebug($"🎯 Loading FATE markers for territory {territoryId} (folder: {territoryFolderName})");

                // ✅ OPTIMIZED: Fast cache check first
                if (_territoryFateCache.TryGetValue(territoryId, out var cachedFates))
                {
                    _logDebug($"📋 Using cached {cachedFates.Count} FATE markers for territory {territoryId}");
                    return ProcessCachedFateMarkers(cachedFates, map);
                }

                // Load FATE data from LGB
                var lgbFates = LoadLgbFateDataFromLumina(territoryFolderName, territoryId);
                if (lgbFates.Count == 0)
                {
                    _logDebug($"❌ No FATE data found for territory {territoryId}");
                    return markers;
                }

                // ✅ OPTIMIZED: Initialize Fate sheet cache once
                if (!_fateSheetCacheInitialized)
                {
                    InitializeFateSheetCache();
                }

                // Process FATE markers efficiently
                foreach (var lgbFate in lgbFates)
                {
                    try
                    {
                        var marker = ProcessLgbFate(lgbFate, map);
                        if (marker != null)
                        {
                            markers.Add(marker);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logDebug($"❌ Error processing FATE {lgbFate.InstanceId}: {ex.Message}");
                    }
                }

                _logDebug($"✅ Created {markers.Count} FATE markers for territory {territoryId}");
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error in LoadFateMarkersFromLgb: {ex.Message}");
            }

            return markers;
        }

        // ✅ NEW: Fast processing of cached FATE markers
        private List<MapMarker> ProcessCachedFateMarkers(List<FateLgbMarker> cachedFates, Map map)
        {
            var markers = new List<MapMarker>();

            foreach (var lgbFate in cachedFates)
            {
                var marker = ProcessLgbFate(lgbFate, map);
                if (marker != null)
                {
                    markers.Add(marker);
                }
            }

            return markers;
        }

        // ✅ NEW: Initialize Fate sheet cache for fast lookups
        private void InitializeFateSheetCache()
        {
            try
            {
                var fateSheet = _realm?.GameData?.GetSheet("Fate");
                if (fateSheet != null)
                {
                    foreach (var fateRow in fateSheet)
                    {
                        try
                        {
                            var instanceIdValue = fateRow[1]; // Index 1 = InstanceId
                            if (instanceIdValue != null)
                            {
                                var instanceId = Convert.ToUInt32(instanceIdValue);
                                _fateSheetCache[instanceId] = fateRow;
                            }
                        }
                        catch { }
                    }
                }
                _fateSheetCacheInitialized = true;
                _logDebug($"📋 Initialized Fate sheet cache with {_fateSheetCache.Count} entries");
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error initializing Fate sheet cache: {ex.Message}");
            }
        }

        // ✅ OPTIMIZED: Fast FATE processing with cached lookups
        // ✅ FIXED: Proper FATE name extraction from Fate sheet using direct indexer access
        private MapMarker? ProcessLgbFate(FateLgbMarker lgbFate, Map map)
        {
            // Fast cache lookup instead of linear search
            _fateSheetCache.TryGetValue(lgbFate.InstanceId, out var fateRow);

            string fateName = $"FATE_{lgbFate.LayerName}"; // Fallback name
            uint fateId = lgbFate.InstanceId;
            uint iconId = 60093;
            uint level = 1;

            if (fateRow != null)
            {
                try
                {
                    // ✅ FIXED: Use direct indexer access instead of reflection to avoid ambiguous match
                    // Cast to dynamic to access the indexer directly
                    dynamic dynamicRow = fateRow;

                    // Get FATE name from index 24
                    var nameValue = dynamicRow[24];
                    if (nameValue != null)
                    {
                        var nameString = nameValue.ToString();
                        if (!string.IsNullOrEmpty(nameString) && nameString != "")
                        {
                            fateName = nameString;
                            _logDebug($"🔗 Found FATE name: '{fateName}' for InstanceId {lgbFate.InstanceId}");
                        }
                    }

                    // ✅ FIXED: Get FATE ID (Key/RowId) - this is the actual FATE ID, not InstanceId
                    var keyProperty = fateRow.GetType().GetProperty("Key");
                    if (keyProperty != null)
                    {
                        var keyValue = keyProperty.GetValue(fateRow);
                        if (keyValue != null)
                        {
                            fateId = (uint)(int)keyValue;
                            _logDebug($"🔗 Found FATE ID: {fateId} for InstanceId {lgbFate.InstanceId}");
                        }
                    }

                    // ✅ FIXED: Get level from index 2 (ClassJobLevel) using direct indexer
                    var levelValue = dynamicRow[2];
                    if (levelValue != null)
                    {
                        level = (uint)Math.Max(1, Convert.ToInt32(levelValue));
                    }

                    // ✅ FIXED: Get icon from index 3 using direct indexer
                    var iconValue = dynamicRow[3];
                    if (iconValue != null)
                    {
                        iconId = (uint)Math.Max(60093, Convert.ToInt32(iconValue));
                    }
                }
                catch (Exception ex)
                {
                    _logDebug($"⚠️ Error extracting FATE data for InstanceId {lgbFate.InstanceId}: {ex.Message}");
                }
            }
            else
            {
                _logDebug($"⚠️ No Fate sheet entry found for InstanceId {lgbFate.InstanceId}");
            }

            // ✅ OPTIMIZED: Simplified coordinate conversion
            return ConvertLgbToMapMarkerFast(lgbFate, map, fateId, fateName, iconId, level);
        }

        // ✅ OPTIMIZED: Fast coordinate conversion without extensive logging
        private MapMarker? ConvertLgbToMapMarkerFast(FateLgbMarker lgbFate, Map map, uint fateId, string fateName, uint iconId, uint level)
        {
            try
            {
                // Get map properties
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

                // Fast coordinate conversion
                double c = sizeFactor / 100.0;
                double gameX = (lgbFate.X + 1024.0) / 50.0 + 1.0;
                double gameY = (lgbFate.Z + 1024.0) / 50.0 + 1.5;
                double normalizedX = (gameX - 1.0) * c / 41.0;
                double normalizedY = (gameY - 1.4) * c / 41.0;
                double mapX = normalizedX * 2048.0 - offsetX;
                double mapY = normalizedY * 2048.0 - offsetY;

                return new MapMarker
                {
                    Id = fateId,
                    MapId = (uint)map.Key,
                    X = mapX,
                    Y = mapY,
                    Z = lgbFate.Y,
                    PlaceName = $"{fateName} (Lv.{level})",
                    PlaceNameId = fateId,
                    IconId = iconId,
                    Type = MarkerType.Fate,
                    IsVisible = true
                };
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error converting FATE {lgbFate.InstanceId}: {ex.Message}");
                return null;
            }
        }

        // ✅ OPTIMIZED: Streamlined LGB loading with minimal logging
        private List<FateLgbMarker> LoadLgbFateDataFromLumina(string territoryFolderName, uint territoryId)
        {
            var fates = new List<FateLgbMarker>();

            if (_luminaGameData == null) return fates;

            try
            {
                // Check cache first
                if (_territoryFateCache.TryGetValue(territoryId, out var cachedMarkers))
                {
                    return cachedMarkers;
                }

                // Try to load LGB file
                var possiblePaths = new[]
                {
                    $"bg/ffxiv/roc_r1/fld/{territoryFolderName}/level/planevent.lgb",
                    $"bg/ffxiv/lak_l1/fld/{territoryFolderName}/level/planevent.lgb",
                    $"bg/ffxiv/sea_s1/fld/{territoryFolderName}/level/planevent.lgb",
                    $"bg/ffxiv/wil_w1/fld/{territoryFolderName}/level/planevent.lgb",
                    $"bg/ffxiv/fst_f1/fld/{territoryFolderName}/level/planevent.lgb",
                    $"bg/ffxiv/air_a1/fld/{territoryFolderName}/level/planevent.lgb"
                };

                LgbFile? lgbFile = null;

                foreach (var path in possiblePaths)
                {
                    try
                    {
                        lgbFile = _luminaGameData.GetFile<LgbFile>(path);
                        if (lgbFile != null)
                        {
                            _logDebug($"✅ Loaded LGB: {path}");
                            break;
                        }
                    }
                    catch { }
                }

                if (lgbFile == null) return fates;

                // Fast processing of FATE layers
                foreach (var layer in lgbFile.Layers)
                {
                    if (layer.Name.StartsWith("FATE_", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var instanceObject in layer.InstanceObjects)
                        {
                            if (instanceObject.AssetType == LayerEntryType.EventRange ||
                                instanceObject.AssetType == LayerEntryType.FateRange)
                            {
                                fates.Add(new FateLgbMarker
                                {
                                    InstanceId = instanceObject.InstanceId,
                                    LayerName = layer.Name,
                                    X = instanceObject.Transform.Translation.X,
                                    Y = instanceObject.Transform.Translation.Y,
                                    Z = instanceObject.Transform.Translation.Z,
                                    Type = instanceObject.AssetType.ToString(),
                                    Source = "Lumina"
                                });
                            }
                        }
                    }
                }

                // Cache the results
                _territoryFateCache[territoryId] = fates;
                _logDebug($"📊 Cached {fates.Count} FATE markers for territory {territoryId}");
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error loading LGB data: {ex.Message}");
            }

            return fates;
        }

        // ✅ CORRECTED: Territory mapping
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
                { 155, "r1f1" }, // Coerthas Central Highlands
                { 156, "l1f1" }, // ✅ FIXED: Mor Dhona
            };

            return territoryFolderMap.TryGetValue(territoryId, out var folderName)
                ? folderName
                : $"unknown_{territoryId}";
        }

        // ✅ SIMPLIFIED: Other methods with minimal changes
        private List<FateLgbMarker> LoadLgbFateDataFromExternalFile(string territoryName) => new();

        private List<FateLgbMarker> LoadLgbFateDataFromEmbeddedResource(string territoryName) => new();

        private MapMarker? CreateTestFateMarker(uint mapId, Map map) => null;

        private string GetTerritoryName(uint territoryId)
        {
            try
            {
                var territorySheet = _realm?.GameData?.GetSheet<TerritoryType>();
                var territory = territorySheet?[(int)territoryId];
                return territory?.PlaceName?.Name?.ToString() ?? territoryId.ToString();
            }
            catch
            {
                return territoryId.ToString();
            }
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

    // ✅ KEEP: Data classes unchanged
    public class FateLgbMarker
    {
        public uint InstanceId { get; set; }
        public string LayerName { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    // ✅ KEEP: All existing JSON structure classes unchanged
    public class LgbJsonData
    {
        public string? FilePath { get; set; }
        public LgbMetadata? Metadata { get; set; }
        public List<LgbLayer>? Layers { get; set; }
    }

    public class LgbMetadata
    {
        public int LayerCount { get; set; }
        public string? ParsedAt { get; set; }
        public string? ProcessingMode { get; set; }
        public int TotalObjectsProcessed { get; set; }
        public int EnhancedObjectDataCount { get; set; }
    }

    public class LgbLayer
    {
        public uint LayerId { get; set; }
        public string? Name { get; set; }
        public LgbLayerProperties? Properties { get; set; }
        public List<LgbInstanceObject>? InstanceObjects { get; set; }
        public List<LgbLayerSetReference>? LayerSetReferences { get; set; }
    }

    public class LgbLayerProperties
    {
        public int InstanceObjectCount { get; set; }
        public bool ToolModeVisible { get; set; }
        public bool ToolModeReadOnly { get; set; }
        public bool IsBushLayer { get; set; }
        public bool PS3Visible { get; set; }
        public bool IsTemporary { get; set; }
        public bool IsHousing { get; set; }
        public int FestivalID { get; set; }
        public int FestivalPhaseID { get; set; }
        public int VersionMask { get; set; }
    }

    public class LgbInstanceObject
    {
        public uint InstanceId { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public LgbTransform? Transform { get; set; }
        public object? EnhancedData { get; set; }
    }

    public class LgbTransform
    {
        public LgbPosition? Position { get; set; }
        public LgbRotation? Rotation { get; set; }
        public LgbScale? Scale { get; set; }
    }

    public class LgbPosition
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class LgbRotation
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class LgbScale
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class LgbLayerSetReference
    {
        public uint LayerSetId { get; set; }
    }
}