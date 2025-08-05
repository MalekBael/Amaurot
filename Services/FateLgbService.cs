using SaintCoinach;
using SaintCoinach.Xiv;
using System.Text.Json;
using System.Reflection;
using System.IO;

namespace Amaurot.Services
{
    public class FateLgbService
    {
        private readonly ARealmReversed? _realm;
        private readonly Action<string> _logDebug;
        private readonly Dictionary<uint, List<FateLgbMarker>> _territoryFateCache = new();

        public FateLgbService(ARealmReversed? realm, Action<string> logDebug)
        {
            _realm = realm;
            _logDebug = logDebug;
        }

        public List<MapMarker> LoadFateMarkersFromLgb(uint mapId)
        {
            _logDebug($"🎯 === FATE LGB SERVICE DEBUG START === MapId: {mapId}");

            var markers = new List<MapMarker>();

            if (_realm == null)
            {
                _logDebug("❌ Realm is null, cannot load FATE markers");
                return markers;
            }

            try
            {
                // Get territory ID from map
                var mapSheet = _realm.GameData.GetSheet<Map>();
                var map = mapSheet[(int)mapId];
                if (map == null)
                {
                    _logDebug($"❌ Map {mapId} not found");
                    return markers;
                }

                var territoryId = (uint)map.TerritoryType.Key;
                var territoryName = GetTerritoryName(territoryId);

                _logDebug($"🗺️ Loading FATE markers for map {mapId}, territory {territoryId} ({territoryName})");

                // ✅ ENHANCED: Try external file first, then embedded resource
                var lgbFates = LoadLgbFateDataFromExternalFile(territoryName);
                if (lgbFates.Count == 0)
                {
                    _logDebug($"📁 No external LGB FATE data found, trying embedded resource...");
                    lgbFates = LoadLgbFateDataFromEmbeddedResource(territoryName);
                }

                if (lgbFates.Count == 0)
                {
                    _logDebug($"❌ No LGB FATE data found for territory {territoryName} (neither external nor embedded)");

                    // ✅ ADD: Create a test FATE marker for f1f3 if no data is found
                    if (territoryName == "f1f3")
                    {
                        _logDebug($"🧪 Creating test FATE marker for f1f3 (South Shroud)");
                        var testMarker = CreateTestFateMarker(mapId, map);
                        if (testMarker != null)
                        {
                            markers.Add(testMarker);
                            _logDebug($"✅ Added test FATE marker for f1f3");
                        }
                    }
                    return markers;
                }

                _logDebug($"📊 Found {lgbFates.Count} LGB FATE entries to process");

                // Load Saint Coinach Fate sheet for names/details
                var fateSheet = _realm.GameData.GetSheet("Fate");
                _logDebug($"📋 Loaded Fate sheet with {fateSheet.Count()} entries");

                int processedCount = 0;
                foreach (var lgbFate in lgbFates)
                {
                    try
                    {
                        processedCount++;
                        _logDebug($"🔄 Processing FATE {processedCount}/{lgbFates.Count}: {lgbFate.LayerName} (InstanceId: {lgbFate.InstanceId})");

                        // Link LGB InstanceId to Fate sheet via index 1
                        var fateRow = fateSheet.FirstOrDefault(f =>
                        {
                            try
                            {
                                var instanceIdValue = f[1]; // Index 1 = InstanceId according to your analysis
                                return instanceIdValue != null && Convert.ToUInt32(instanceIdValue) == lgbFate.InstanceId;
                            }
                            catch
                            {
                                return false;
                            }
                        });

                        string fateName = $"FATE_{lgbFate.LayerName}";
                        uint fateId = lgbFate.InstanceId;
                        uint iconId = 60093; // Default FATE icon
                        uint level = 1;

                        if (fateRow != null)
                        {
                            try
                            {
                                // Get FATE name from index 24
                                var nameValue = fateRow[24];
                                if (nameValue != null)
                                {
                                    fateName = nameValue.ToString() ?? fateName;
                                }

                                // Get FATE ID
                                fateId = (uint)fateRow.Key;

                                // Get level from index 2 (ClassJobLevel)
                                var levelValue = fateRow[2];
                                if (levelValue != null)
                                {
                                    level = (uint)Math.Max(1, Convert.ToInt32(levelValue));
                                }

                                // Get icon from index 3
                                var iconValue = fateRow[3];
                                if (iconValue != null)
                                {
                                    iconId = (uint)Math.Max(60093, Convert.ToInt32(iconValue));
                                }

                                _logDebug($"🔗 Linked LGB InstanceId {lgbFate.InstanceId} to Fate '{fateName}' (ID: {fateId}, Level: {level}, Icon: {iconId})");
                            }
                            catch (Exception ex)
                            {
                                _logDebug($"⚠️ Error reading FATE data for InstanceId {lgbFate.InstanceId}: {ex.Message}");
                            }
                        }
                        else
                        {
                            _logDebug($"⚠️ No Fate sheet entry found for InstanceId {lgbFate.InstanceId}, using defaults");
                        }

                        // Convert LGB coordinates to map coordinates using the same logic as MapRenderer
                        var mapMarker = ConvertLgbToMapMarker(lgbFate, map, fateId, fateName, iconId, level);
                        if (mapMarker != null)
                        {
                            markers.Add(mapMarker);
                            _logDebug($"✅ Created marker #{markers.Count}: '{mapMarker.PlaceName}' at ({mapMarker.X:F1}, {mapMarker.Y:F1})");
                        }
                        else
                        {
                            _logDebug($"❌ Failed to create marker for FATE {lgbFate.LayerName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logDebug($"❌ Error processing LGB FATE {lgbFate.LayerName}: {ex.Message}");
                    }
                }

                _logDebug($"🎯 === FATE LGB SERVICE COMPLETE === Created {markers.Count} FATE markers from LGB data");
            }
            catch (Exception ex)
            {
                _logDebug($"❌ CRITICAL ERROR in LoadFateMarkersFromLgb: {ex.Message}");
                _logDebug($"Stack trace: {ex.StackTrace}");
            }

            return markers;
        }

        // ✅ ADD: External file loading for testing
        private List<FateLgbMarker> LoadLgbFateDataFromExternalFile(string territoryName)
        {
            var fates = new List<FateLgbMarker>();

            try
            {
                // Look in multiple possible locations for external JSON files
                var possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", $"{territoryName}_planevent.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", $"{territoryName}.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "LGB-Parser", "bin", "Debug", "net8.0", "output", "bg", "ffxiv", "fst_f1", "fld", territoryName, "level", "planevent.lgb.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{territoryName}_test_fate.json")
                };

                foreach (var jsonPath in possiblePaths)
                {
                    _logDebug($"🔍 Checking for external FATE file: {jsonPath}");

                    if (File.Exists(jsonPath))
                    {
                        _logDebug($"📁 Found external FATE file: {jsonPath}");

                        var jsonContent = File.ReadAllText(jsonPath);
                        _logDebug($"📄 Read {jsonContent.Length} characters from file");

                        var lgbData = JsonSerializer.Deserialize<LgbJsonData>(jsonContent);

                        if (lgbData?.Layers == null)
                        {
                            _logDebug("⚠️ No layers found in external LGB JSON data");
                            continue;
                        }

                        _logDebug($"📊 Found {lgbData.Layers.Count} layers in external JSON");

                        // Find all FATE layers
                        foreach (var layer in lgbData.Layers)
                        {
                            if (layer.Name != null && layer.Name.StartsWith("FATE_"))
                            {
                                _logDebug($"🎯 Found FATE layer: {layer.Name} with {layer.InstanceObjects?.Count ?? 0} objects");

                                // Find EventRange objects within this FATE layer
                                foreach (var instanceObject in layer.InstanceObjects ?? [])
                                {
                                    if (instanceObject.Type == "EventRange" || instanceObject.Type == "FateRange")
                                    {
                                        var lgbFate = new FateLgbMarker
                                        {
                                            InstanceId = instanceObject.InstanceId,
                                            LayerName = layer.Name,
                                            X = instanceObject.Transform?.Position?.X ?? 0,
                                            Y = instanceObject.Transform?.Position?.Y ?? 0,
                                            Z = instanceObject.Transform?.Position?.Z ?? 0,
                                            Type = instanceObject.Type
                                        };

                                        fates.Add(lgbFate);
                                        _logDebug($"✅ Found FATE: {layer.Name} (InstanceId: {instanceObject.InstanceId}) at ({lgbFate.X:F1}, {lgbFate.Y:F1}, {lgbFate.Z:F1})");
                                    }
                                }
                            }
                        }

                        _logDebug($"📊 Loaded {fates.Count} FATE entries from external file: {jsonPath}");
                        return fates; // Return immediately if we found data
                    }
                }

                _logDebug($"📁 No external FATE files found for territory {territoryName}");
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error loading external LGB FATE data: {ex.Message}");
            }

            return fates;
        }

        private List<FateLgbMarker> LoadLgbFateDataFromEmbeddedResource(string territoryName)
        {
            var fates = new List<FateLgbMarker>();

            try
            {
                _logDebug($"🔍 Looking for embedded resource for territory: {territoryName}");

                // Get the assembly containing the embedded resources
                var assembly = Assembly.GetExecutingAssembly();

                // Construct the resource name following the embedded folder structure
                var resourceName = $"Amaurot.Resources.LgbData.bg.ffxiv.fst_f1.fld.{territoryName}.level.planevent.lgb.json";

                _logDebug($"🔍 Looking for embedded resource: {resourceName}");

                // List all embedded resources for debugging
                var resourceNames = assembly.GetManifestResourceNames();
                _logDebug($"📋 Available embedded resources ({resourceNames.Length} total):");
                foreach (var name in resourceNames.Take(10)) // Show first 10 to avoid spam
                {
                    _logDebug($"   - {name}");
                }
                if (resourceNames.Length > 10)
                {
                    _logDebug($"   ... and {resourceNames.Length - 10} more");
                }

                // Try to load the specific resource
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    _logDebug($"❌ Embedded resource not found: {resourceName}");
                    return fates;
                }

                _logDebug($"✅ Found embedded resource, reading content...");

                using var reader = new StreamReader(stream);
                var jsonContent = reader.ReadToEnd();

                _logDebug($"📄 Read {jsonContent.Length} characters from embedded resource");

                var lgbData = JsonSerializer.Deserialize<LgbJsonData>(jsonContent);

                if (lgbData?.Layers == null)
                {
                    _logDebug("❌ No layers found in embedded LGB JSON data");
                    return fates;
                }

                _logDebug($"📊 Found {lgbData.Layers.Count} layers in embedded JSON");

                // Find all FATE layers
                foreach (var layer in lgbData.Layers)
                {
                    if (layer.Name != null && layer.Name.StartsWith("FATE_"))
                    {
                        _logDebug($"🎯 Found FATE layer: {layer.Name} with {layer.InstanceObjects?.Count ?? 0} objects");

                        // Find EventRange objects within this FATE layer
                        foreach (var instanceObject in layer.InstanceObjects ?? [])
                        {
                            if (instanceObject.Type == "EventRange" || instanceObject.Type == "FateRange")
                            {
                                var lgbFate = new FateLgbMarker
                                {
                                    InstanceId = instanceObject.InstanceId,
                                    LayerName = layer.Name,
                                    X = instanceObject.Transform?.Position?.X ?? 0,
                                    Y = instanceObject.Transform?.Position?.Y ?? 0,
                                    Z = instanceObject.Transform?.Position?.Z ?? 0,
                                    Type = instanceObject.Type
                                };

                                fates.Add(lgbFate);
                                _logDebug($"✅ Found FATE: {layer.Name} (InstanceId: {instanceObject.InstanceId}) at ({lgbFate.X:F1}, {lgbFate.Y:F1}, {lgbFate.Z:F1})");
                            }
                        }
                    }
                }

                _logDebug($"📊 Loaded {fates.Count} FATE entries from embedded LGB JSON");
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error loading embedded LGB FATE data: {ex.Message}");
            }

            return fates;
        }

        // ✅ ADD: Create a test FATE marker for debugging
        private MapMarker? CreateTestFateMarker(uint mapId, Map map)
        {
            try
            {
                _logDebug($"🧪 Creating test FATE marker for map {mapId}");

                var testMarker = new MapMarker
                {
                    Id = 999999, // Test ID
                    MapId = mapId,
                    X = 21.0, // Center of map
                    Y = 21.0, // Center of map
                    Z = 0,
                    PlaceName = "TEST FATE MARKER (f1f3)",
                    PlaceNameId = 999999,
                    IconId = 60093, // FATE icon
                    Type = MarkerType.Fate,
                    IsVisible = true
                };

                _logDebug($"✅ Created test marker: '{testMarker.PlaceName}' at ({testMarker.X:F1}, {testMarker.Y:F1})");
                return testMarker;
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error creating test FATE marker: {ex.Message}");
                return null;
            }
        }

        private MapMarker? ConvertLgbToMapMarker(FateLgbMarker lgbFate, Map map, uint fateId, string fateName, uint iconId, uint level)
        {
            try
            {
                _logDebug($"🔄 Converting LGB FATE to map marker: {fateName}");

                // Get map properties for coordinate conversion
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

                    _logDebug($"📏 Map properties: SizeFactor={sizeFactor}, OffsetX={offsetX}, OffsetY={offsetY}");
                }
                catch (Exception ex)
                {
                    _logDebug($"⚠️ Error getting map properties: {ex.Message}");
                }

                // ✅ FIXED: Use the proper coordinate conversion matching MapRenderer's expectations
                // LGB coordinates are in game coordinates that need to be converted to raw map coordinates

                double c = sizeFactor / 100.0;

                // Convert LGB world coordinates (which appear to be in a game-like coordinate system)
                // to the game coordinate system (1-42 range)
                double gameX = (lgbFate.X + 1024.0) / 50.0;  // Approximate conversion from world to game coords
                double gameY = (lgbFate.Z + 1024.0) / 50.0;  // Use Z for Y (height is Y in LGB)

                // ✅ ADJUSTMENT: Fine-tune offset to align perfectly with FATE positions
                // Based on the screenshot, markers need to be moved down by half a marker size
                gameX += 1.0;  // Keep X offset at 1.0
                gameY += 1.5;  // Increased from 1.0 to 1.5 to move markers down

                // Now convert from game coordinates to raw map coordinates
                // This is the reverse of what MapRenderer does: gameCoord = (41.0 / c) * (normalizedCoord) + 1.0
                // So: normalizedCoord = (gameCoord - 1.0) * c / 41.0
                double normalizedX = (gameX - 1.0) * c / 41.0;
                double normalizedY = (gameY - 1.4) * c / 41.0;

                // Convert normalized to raw map coordinates
                double mapX = normalizedX * 2048.0 - offsetX;
                double mapY = normalizedY * 2048.0 - offsetY;

                _logDebug($"🧮 Coordinate conversion (ADJUSTED):");
                _logDebug($"   LGB World: ({lgbFate.X:F1}, {lgbFate.Y:F1}, {lgbFate.Z:F1})");
                _logDebug($"   Game coords (with offset): ({gameX:F1}, {gameY:F1})");
                _logDebug($"   Normalized: ({normalizedX:F3}, {normalizedY:F3})");
                _logDebug($"   Map marker coords: ({mapX:F1}, {mapY:F1})");
                _logDebug($"   Map {map.Key}: SizeFactor={sizeFactor}, OffsetX={offsetX}, OffsetY={offsetY}, c={c:F3}");

                // ✅ VERIFICATION: Show what MapRenderer will calculate
                double verifyNormX = (mapX + offsetX) / 2048.0;
                double verifyNormY = (mapY + offsetY) / 2048.0;
                double verifyGameX = (41.0 / c) * verifyNormX + 1.0;
                double verifyGameY = (41.0 / c) * verifyNormY + 1.0;
                _logDebug($"   Verification: MapRenderer will show: ({verifyGameX:F1}, {verifyGameY:F1}) [1-42 range]");

                var marker = new MapMarker
                {
                    Id = fateId,
                    MapId = (uint)map.Key,
                    X = mapX,
                    Y = mapY,
                    Z = lgbFate.Y, // Keep the original Y (height) value
                    PlaceName = $"{fateName} (Lv.{level})",
                    PlaceNameId = fateId,
                    IconId = iconId,
                    Type = MarkerType.Fate,
                    IsVisible = true
                };

                _logDebug($"✅ Converted FATE '{fateName}': World({lgbFate.X:F1},{lgbFate.Z:F1}) → Game({gameX:F1},{gameY:F1}) → Map({mapX:F1},{mapY:F1})");

                return marker;
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error converting LGB FATE to marker: {ex.Message}");
                return null;
            }
        }

        private string GetTerritoryName(uint territoryId)
        {
            if (_realm == null)
            {
                _logDebug($"⚠️ Realm is null, returning territory ID as string: {territoryId}");
                return territoryId.ToString();
            }

            try
            {
                var territorySheet = _realm.GameData.GetSheet<TerritoryType>();
                var territory = territorySheet[(int)territoryId];

                if (territory != null)
                {
                    var territoryName = territory.Name?.ToString() ?? territoryId.ToString();
                    _logDebug($"🗺️ Territory ID {territoryId} -> Name: '{territoryName}'");
                    return territoryName;
                }
                else
                {
                    _logDebug($"⚠️ Territory not found for ID {territoryId}");
                }
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error getting territory name for ID {territoryId}: {ex.Message}");
            }

            return territoryId.ToString();
        }
    }

    // Data classes for LGB JSON parsing
    public class FateLgbMarker
    {
        public uint InstanceId { get; set; }
        public string LayerName { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string Type { get; set; } = string.Empty;
    }

    // JSON structure classes for parsing LGB output
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