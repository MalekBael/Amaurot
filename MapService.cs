using SaintCoinach;
using SaintCoinach.Xiv;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace map_editor
{
    public class MapService
    {
        private readonly ARealmReversed? _realm;
        private readonly Dictionary<uint, List<MapMarker>> _mapMarkerCache = new();
        private readonly Dictionary<uint, Dictionary<uint, string>> _placeNameCache = new();
        private bool _csvDataLoaded = false;

        // Use structured data for symbol categories instead of hardcoded strings
        private class MapSymbolCategory
        {
            public string Name { get; set; } = string.Empty;
            public uint DefaultIconId { get; set; }
            public HashSet<string> Keywords { get; set; } = new HashSet<string>();
        }

        // Fields to store symbol data
        private Dictionary<string, uint> _mapSymbols = new();
        private Dictionary<uint, uint> _placeNameToSymbol = new();
        private Dictionary<string, uint> _keywordToSymbol = new();
        private List<MapSymbolCategory> _symbolCategories = new();
        private Dictionary<uint, uint> _placeNameToIconMap = new(); // PlaceNameId -> IconId
        private Dictionary<string, uint> _placeNameStringToIconMap = new(); // PlaceName string -> IconId
        private Dictionary<uint, List<uint>> _iconToPlaceNameMap = new(); // IconId -> List<PlaceNameId>
        private bool _symbolsLoaded = false;

        public MapService(ARealmReversed? realm)
        {
            _realm = realm;
        }

        // Load map markers for a specific map using SaintCoinach directly
        public List<MapMarker> LoadMapMarkers(uint mapId)
        {
            if (_mapMarkerCache.TryGetValue(mapId, out var cachedMarkers))
            {
                Debug.WriteLine($"Using cached {cachedMarkers.Count} markers for map {mapId}");
                return cachedMarkers;
            }

            var markers = new List<MapMarker>();

            try
            {
                if (_realm == null)
                {
                    Debug.WriteLine("SaintCoinach realm is null. Returning sample markers.");
                    return CreateSampleMarkers(mapId);
                }

                Debug.WriteLine($"Loading markers for map ID: {mapId}");

                var mapSheet = _realm.GameData.GetSheet<Map>();
                var map = mapSheet.FirstOrDefault(m => m.Key == mapId);

                if (map == null)
                {
                    Debug.WriteLine($"Map with ID {mapId} not found in SaintCoinach data.");
                    return CreateSampleMarkers(mapId);
                }

                Debug.WriteLine($"Map belongs to territory ID: {map.TerritoryType.Key}");

                LoadMapMarkersFromSheet(markers, map);

                if (markers.Count == 0)
                {
                    Debug.WriteLine($"No markers found in SaintCoinach data, trying to load from CSV for map {mapId}");
                    markers = LoadMapMarkersFromCsv(mapId);
                    if (markers.Count == 0)
                    {
                        Debug.WriteLine($"No markers found in CSV, creating sample markers for map {mapId}");
                        markers = CreateSampleMarkers(mapId);
                    }
                }
                _mapMarkerCache[mapId] = markers;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading map markers: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                markers = CreateSampleMarkers(mapId);
            }

            return markers;
        }

        // Updated: Pass the Map object so we can access MapMarkerRange
        private void LoadMapMarkersFromSheet(List<MapMarker> markers, Map map)
        {
            try
            {
                Debug.WriteLine("Loading map markers from sheet...");
                int count = 0;
                const int MAX_MARKERS = 100; // Limit the number of markers to prevent freezing

                var mapMarkerSheet = _realm?.GameData?.GetSheet("MapMarker");
                if (mapMarkerSheet == null)
                {
                    Debug.WriteLine("MapMarker sheet not found in SaintCoinach");
                    return;
                }

                // Get the MapMarkerRange value for this map
                uint markerRange = 0;
                try
                {
                    markerRange = Convert.ToUInt32(map.MapMarkerRange);
                    Debug.WriteLine($"Map has MapMarkerRange: {markerRange}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting MapMarkerRange: {ex.Message}");
                    return;
                }
                
                if (markerRange == 0)
                {
                    Debug.WriteLine("MapMarkerRange is 0, cannot find markers");
                    return;
                }

                // Find the row for this markerRange
                var variantRow = mapMarkerSheet.FirstOrDefault(r => Convert.ToUInt32(r.Key) == markerRange);
                if (variantRow == null)
                {
                    Debug.WriteLine($"No row found for markerRange {markerRange}");
                    return;
                }

                Debug.WriteLine($"Found row for markerRange {markerRange}, type: {variantRow.GetType().FullName}");

                try
                {
                    // Get the SourceRow property (XivRow contains a SourceRow)
                    var sourceRowProp = variantRow.GetType().GetProperty("SourceRow");
                    if (sourceRowProp != null)
                    {
                        var sourceRow = sourceRowProp.GetValue(variantRow);
                        if (sourceRow != null)
                        {
                            Debug.WriteLine($"Found SourceRow of type: {sourceRow.GetType().FullName}");
                            
                            // Get the SubRows property
                            var subRowsProp = sourceRow.GetType().GetProperty("SubRows");
                            if (subRowsProp != null)
                            {
                                var subRowsCollection = subRowsProp.GetValue(sourceRow) as System.Collections.IEnumerable;
                                if (subRowsCollection != null)
                                {
                                    Debug.WriteLine("Successfully retrieved SubRows collection");
                                    
                                    // CRITICAL FIX: Add performance monitoring and limits
                                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                                    const int MAX_PROCESSING_TIME_MS = 2000; // Maximum 2 seconds processing
                                    
                                    foreach (var subRow in subRowsCollection)
                                    {
                                        // Check if we've hit our limits to prevent freezing
                                        if (count >= MAX_MARKERS)
                                        {
                                            Debug.WriteLine($"Reached maximum marker limit of {MAX_MARKERS}, stopping processing");
                                            break;
                                        }
                                        
                                        if (stopwatch.ElapsedMilliseconds > MAX_PROCESSING_TIME_MS)
                                        {
                                            Debug.WriteLine($"Processing time exceeded {MAX_PROCESSING_TIME_MS}ms, stopping to prevent freeze");
                                            break;
                                        }
                                        
                                        try
                                        {
                                            var subRowType = subRow.GetType();
                                            var indexerMethod = subRowType.GetMethod("get_Item", new[] { typeof(string) });
                                            
                                            if (indexerMethod != null)
                                            {
                                                // Try to get values safely (with type checking)
                                                float x = 0;
                                                float y = 0;
                                                uint iconId = 0;
                                                string placeName = "Marker";
                                                uint placeNameId = 0;
                                                
                                                try
                                                {
                                                    var xValue = indexerMethod.Invoke(subRow, new object[] { "X" });
                                                    if (xValue != null && !(xValue is SaintCoinach.Imaging.ImageFile))
                                                        x = Convert.ToSingle(xValue);
                                                }
                                                catch { /* Ignore conversion errors */ }
                                                
                                                try
                                                {
                                                    var yValue = indexerMethod.Invoke(subRow, new object[] { "Y" });
                                                    if (yValue != null && !(yValue is SaintCoinach.Imaging.ImageFile))
                                                        y = Convert.ToSingle(yValue);
                                                }
                                                catch { /* Ignore conversion errors */ }
                                                
                                                try
                                                {
                                                    var iconValue = indexerMethod.Invoke(subRow, new object[] { "Icon" });
                                                    if (iconValue != null && !(iconValue is SaintCoinach.Imaging.ImageFile))
                                                        iconId = Convert.ToUInt32(iconValue);
                                                }
                                                catch { /* Ignore conversion errors */ }
                                                
                                                try
                                                {
                                                    var placeNameValue = indexerMethod.Invoke(subRow, new object[] { "PlaceNameSubtext" });
                                                    if (placeNameValue != null && !(placeNameValue is SaintCoinach.Imaging.ImageFile))
                                                        placeName = placeNameValue.ToString() ?? "Marker";
                                                    else
                                                    {
                                                        // Try alternate column name for place name
                                                        placeNameValue = indexerMethod.Invoke(subRow, new object[] { "PlaceName" });
                                                        if (placeNameValue != null && !(placeNameValue is SaintCoinach.Imaging.ImageFile))
                                                            placeName = placeNameValue.ToString() ?? "Marker";
                                                    }
                                                }
                                                catch { /* Ignore conversion errors */ }
                                                
                                                // Also try to get place name ID if available
                                                try
                                                {
                                                    var placeNameIdValue = indexerMethod.Invoke(subRow, new object[] { "PlaceNameId" });
                                                    if (placeNameIdValue != null && !(placeNameIdValue is SaintCoinach.Imaging.ImageFile))
                                                        placeNameId = Convert.ToUInt32(placeNameIdValue);
                                                }
                                                catch { /* Ignore conversion errors */ }
                                                
                                                // Get key of subrow
                                                uint subRowKey = 0;
                                                var keyProp = subRowType.GetProperty("Key");
                                                if (keyProp != null)
                                                {
                                                    var keyValue = keyProp.GetValue(subRow);
                                                    if (keyValue != null)
                                                        subRowKey = Convert.ToUInt32(keyValue);
                                                }
                                                
                                                // Only add markers with valid coordinates
                                                if (x > 0 && y > 0)
                                                {
                                                    // Assign appropriate icon based on name and/or position
                                                    if (iconId == 0)
                                                    {
                                                        iconId = GetIconForPlaceName(placeName);
                                                    }
                                                    
                                                    if (count < 10) // Only log first 10 markers to avoid spam
                                                    {
                                                        Debug.WriteLine($"SubRow data: Key={subRowKey}, X={x}, Y={y}, Icon={iconId}, Name={placeName}");
                                                    }
                                                    
                                                    var marker = new MapMarker
                                                    {
                                                        Id = subRowKey,
                                                        MapId = (uint)map.Key,
                                                        X = x,
                                                        Y = y,
                                                        Z = 0,
                                                        PlaceNameId = placeNameId,
                                                        PlaceName = placeName,
                                                        IconId = iconId,
                                                        Type = DetermineMarkerType(iconId),
                                                        IsVisible = true,
                                                        IconPath = GetIconPath(iconId)
                                                    };
                                                    
                                                    markers.Add(marker);
                                                    count++;
                                                }
                                            }
                                            else
                                            {
                                                Debug.WriteLine($"SubRow does not have string indexer method");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"Error processing subrow: {ex.Message}");
                                            // Continue processing other subrows
                                        }
                                    }
                                    
                                    stopwatch.Stop();
                                    Debug.WriteLine($"Processed {count} markers in {stopwatch.ElapsedMilliseconds}ms");
                                }
                            }
                        }
                    }

                    Debug.WriteLine($"Loaded {count} markers from MapMarker sheet");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading markers from sheet: {ex.Message}");
                    Debug.WriteLine($"Exception details: {ex}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading markers from sheet: {ex.Message}");
                Debug.WriteLine($"Exception details: {ex}");
            }
        }

        // Helper to safely get column values
        private T TryGetColumnValue<T>(SaintCoinach.Ex.Relational.IRelationalRow row, string columnName)
        {
            try
            {
                object? value = null;
                if (row is SaintCoinach.Xiv.XivRow xivRow)
                {
                    if (typeof(T) == typeof(int))
                        value = xivRow.AsInt32(columnName);
                    else if (typeof(T) == typeof(float))
                        value = xivRow.AsSingle(columnName);
                    else if (typeof(T) == typeof(uint))
                        value = Convert.ToUInt32(xivRow.AsInt32(columnName));
                    else if (typeof(T) == typeof(string))
                        value = xivRow.AsString(columnName)?.ToString() ?? "";
                    else
                        value = xivRow[columnName];
                }
                else
                {
                    value = row[columnName];
                }
                return (T)value!;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception getting {columnName} for row {row?.Key}: {ex.Message}");
                return default!;
            }
        }

        private List<MapMarker> CreateSampleMarkers(uint mapId)
        {
            var markers = new List<MapMarker>();

            // The center of the map in FFXIV coordinates is (50, 50)
            float centerX = 50f;
            float centerY = 50f;

            markers.Add(new MapMarker
            {
                Id = 1,
                MapId = mapId,
                X = centerX,
                Y = centerY,
                Z = 0.0,
                PlaceName = "Center Aetheryte",
                IconId = 60441,
                Type = MarkerType.Aetheryte,
                IsVisible = true,
                IconPath = GetIconPath(60441)
            });

            // Add cardinal direction markers
            markers.Add(CreateDirectionalMarker(mapId, 2, centerX + 20, centerY, "East Quest", MarkerType.Quest, 60201));
            markers.Add(CreateDirectionalMarker(mapId, 3, centerX - 20, centerY, "West Shop", MarkerType.Shop, 60301));
            markers.Add(CreateDirectionalMarker(mapId, 4, centerX, centerY + 20, "North Landmark", MarkerType.Landmark, 60501));
            markers.Add(CreateDirectionalMarker(mapId, 5, centerX, centerY - 20, "South Entrance", MarkerType.Entrance, 60901));

            // Add diagonal markers
            markers.Add(CreateDirectionalMarker(mapId, 6, centerX + 15, centerY + 15, "Northeast", MarkerType.Generic, 60701));
            markers.Add(CreateDirectionalMarker(mapId, 7, centerX - 15, centerY + 15, "Northwest", MarkerType.Generic, 60702));
            markers.Add(CreateDirectionalMarker(mapId, 8, centerX + 15, centerY - 15, "Southeast", MarkerType.Generic, 60703));
            markers.Add(CreateDirectionalMarker(mapId, 9, centerX - 15, centerY - 15, "Southwest", MarkerType.Generic, 60704));

            Debug.WriteLine($"Created {markers.Count} sample markers around ({centerX}, {centerY})");
            return markers;
        }

        private MapMarker CreateDirectionalMarker(uint mapId, uint id, double x, double y, string name, MarkerType type, uint iconId)
        {
            return new MapMarker
            {
                Id = id,
                MapId = mapId,
                X = x,
                Y = y,
                Z = 0.0,
                PlaceName = name,
                IconId = iconId,
                Type = type,
                IsVisible = true,
                IconPath = GetIconPath(iconId)
            };
        }

        private MarkerType DetermineMarkerType(uint iconId)
        {
            if (iconId == 60453)
                return MarkerType.Aetheryte;

            if (iconId == 61431)
                return MarkerType.Quest;

            if (iconId == 60412)
                return MarkerType.Shop;

            if (iconId == 60442)
                return MarkerType.Landmark;

            if ((iconId >= 60901 && iconId <= 60950) ||
                (iconId >= 61001 && iconId <= 61050))
                return MarkerType.Entrance;

            if (iconId >= 62000)
                return MarkerType.Custom;

            if (iconId >= 60300 && iconId <= 60999)
                return MarkerType.Symbol;

            return MarkerType.Generic;
        }

        public WpfPoint ConvertGameToMapCoordinates(double gameX, double gameY, Map map)
        {
            if (map == null)
                return new WpfPoint(0, 0);

            double worldX = (gameX - 1.0) * 50.0;
            double worldY = (gameY - 1.0) * 50.0;

            double scale = map.SizeFactor / 100.0;
            double pixelX = (worldX - map.OffsetX) * scale;
            double pixelY = (worldY - map.OffsetY) * scale;

            Debug.WriteLine($"Converting: Game ({gameX}, {gameY}) -> World ({worldX}, {worldY}) -> Pixel ({pixelX}, {pixelY})");
            return new WpfPoint(pixelX, pixelY);
        }

        public WpfPoint ConvertGameToCanvasCoordinates(
    double gameX,
    double gameY,
    Map map,
    double imageScale,
    WpfPoint imagePosition,
    WpfSize imageSize)
{
    // For territory 128, coordinates go from (0,0) at top-left to (42,42) at bottom-right
    
    // Convert from FFXIV game coordinates to relative position (0-1 range)
    double relativeX = gameX / 42.0;
    double relativeY = gameY / 42.0;
    
    // Convert relative position to image pixels
    double pixelX = relativeX * imageSize.Width;
    double pixelY = relativeY * imageSize.Height;
    
    Debug.WriteLine($"Converting: Game ({gameX:F1}, {gameY:F1}) -> " +
                   $"Relative ({relativeX:F2}, {relativeY:F2}) -> " +
                   $"Pixel ({pixelX:F1}, {pixelY:F1})");
    
    // Apply current zoom and pan to get final screen coordinates
    double screenX = pixelX * imageScale + imagePosition.X;
    double screenY = pixelY * imageScale + imagePosition.Y;
    
    return new WpfPoint(screenX, screenY);
}

public MapCoordinate ConvertMapToGameCoordinates(
    WpfPoint canvasPoint,
    WpfPoint imagePosition,
    double imageScale,
    WpfSize imageSize,
    Map? map)
{
    if (map == null)
        return new MapCoordinate();

    // Step 1: Undo zoom and pan to get pixel coordinates in the image
    double px = (canvasPoint.X - imagePosition.X) / imageScale;
    double py = (canvasPoint.Y - imagePosition.Y) / imageScale;

    // Step 2: Convert pixel coordinates to normalized coordinates (0-1)
    double normalizedX = px / imageSize.Width;
    double normalizedY = py / imageSize.Height;
    
    // Step 3: Scale to FFXIV's 41x41 coordinate system
    double scaledX = normalizedX * 41.0;
    double scaledY = normalizedY * 41.0;
    
    // Step 4: Apply map's scale factor and offsets
    double scale = map.SizeFactor / 100.0;
    double gameX = (scaledX * scale) + map.OffsetX;
    double gameY = (scaledY * scale) + map.OffsetY;

    Debug.WriteLine($"Canvas ({canvasPoint.X:F1}, {canvasPoint.Y:F1}) -> " +
                   $"Image Pixel ({px:F1}, {py:F1}) -> " +
                   $"Normalized ({normalizedX:F4}, {normalizedY:F4}) -> " +
                   $"Game ({gameX:F1}, {gameY:F1})");

    return new MapCoordinate
    {
        MapX = gameX,
        MapY = gameY,
        ClientX = gameX,
        ClientY = gameY,
        ClientZ = 0
    };
}

        public MapInfo? GetMapInfo(uint mapId)
        {
            if (_realm == null) return null;

            try
            {
                var mapSheet = _realm.GameData.GetSheet<Map>();
                var map = mapSheet.FirstOrDefault(m => m.Key == mapId);

                if (map == null) return null;

                var mapInfo = new MapInfo
                {
                    Id = (uint)map.Key,
                    SizeFactor = map.SizeFactor,
                    OffsetX = map.OffsetX,
                    OffsetY = map.OffsetY,
                    TerritoryType = (uint)map.TerritoryType.Key,
                    PlaceName = "",
                    Name = ""
                };

                if (map.PlaceName != null)
                {
                    mapInfo.PlaceNameId = (uint)map.PlaceName.Key;
                    mapInfo.PlaceName = map.PlaceName.Name?.ToString() ?? "";
                    mapInfo.Name = mapInfo.PlaceName;
                }

                mapInfo.Markers = LoadMapMarkers(mapId);

                return mapInfo;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting map info: {ex.Message}");
                return null;
            }
        }

        public void DiagnoseMapData(uint mapId)
        {
            if (_realm == null)
            {
                Debug.WriteLine("Realm is null, cannot diagnose map data");
                return;
            }

            try
            {
                Debug.WriteLine($"Diagnosing map data for map ID: {mapId}");

                var mapSheet = _realm.GameData.GetSheet<Map>();
                var map = mapSheet.FirstOrDefault(m => m.Key == mapId);

                if (map == null)
                {
                    Debug.WriteLine($"Map not found with ID: {mapId}");
                    return;
                }

                Debug.WriteLine($"Map: {map.PlaceName?.Name} (ID: {map.Key})");
                Debug.WriteLine($"  SizeFactor: {map.SizeFactor}");
                Debug.WriteLine($"  OffsetX: {map.OffsetX}");
                Debug.WriteLine($"  OffsetY: {map.OffsetY}");
                Debug.WriteLine($"  TerritoryType: {map.TerritoryType.Key} - {map.TerritoryType.PlaceName?.Name}");

                float[] testCoords = { 10f, 20f, 30f };
                foreach (var x in testCoords)
                {
                    foreach (var y in testCoords)
                    {
                        var pixelCoords = ConvertGameToMapCoordinates(x, y, map);
                        Debug.WriteLine($"  Game ({x}, {y}) -> Pixel ({pixelCoords.X:F2}, {pixelCoords.Y:F2})");
                    }
                }

                Debug.WriteLine("Checking available sheet columns:");

                try
                {
                    var sampleMap = mapSheet.First();
                    var mapType = sampleMap.GetType();
                    Debug.WriteLine($"Map properties: {string.Join(", ", mapType.GetProperties().Select(p => p.Name))}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error examining Map sheet: {ex.Message}");
                }

                try
                {
                    var markerSheet = _realm.GameData.GetSheet("MapMarker");
                    if (markerSheet != null)
                    {
                        var sampleRow = markerSheet.First();
                        if (sampleRow != null)
                        {
                            var rowType = sampleRow.GetType();
                            var propertyNames = rowType.GetProperties().Select(p => p.Name);
                            Debug.WriteLine($"MapMarker columns: {String.Join(", ", propertyNames)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error examining MapMarker sheet: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error diagnosing map data: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public void DiagnoseIconPaths(uint? mapId = null)
        {
            if (_realm == null)
            {
                Debug.WriteLine("Realm is null, cannot diagnose icon paths");
                return;
            }

            try
            {
                Debug.WriteLine("Diagnosing icon paths...");

                var iconIds = new HashSet<uint>();

                if (mapId.HasValue)
                {
                    if (_mapMarkerCache.TryGetValue(mapId.Value, out var markers))
                    {
                        foreach (var marker in markers)
                        {
                            iconIds.Add(marker.IconId);
                        }
                    }
                    else
                    {
                        var mapMarkers = LoadMapMarkers(mapId.Value);
                        foreach (var marker in mapMarkers)
                        {
                            iconIds.Add(marker.IconId);
                        }
                    }
                }
                else
                {
                    foreach (var markerList in _mapMarkerCache.Values)
                    {
                        foreach (var marker in markerList)
                        {
                            iconIds.Add(marker.IconId);
                        }
                    }

                    for (uint id = 60000; id <= 61000; id += 100)
                    {
                        iconIds.Add(id);
                        iconIds.Add(id + 1);
                        iconIds.Add(id + 2);
                    }
                }

                Debug.WriteLine($"Checking {iconIds.Count} unique icon IDs...");

                int found = 0;
                int notFound = 0;

                var dataType = _realm.GameData.GetType();
                var getFileMethod = dataType.GetMethod("GetFile", new[] { typeof(string) });
                var fileExistsMethod = dataType.GetMethod("FileExists", new[] { typeof(string) });

                var packCollectionProperty = dataType.GetProperty("PackCollection") ??
                                             dataType.GetProperty("Packs") ??
                                             dataType.GetProperty("Packages");

                Debug.WriteLine($"Available methods: GetFile={getFileMethod != null}, FileExists={fileExistsMethod != null}, " +
                               $"PackCollection={packCollectionProperty != null}");

                foreach (var iconId in iconIds)
                {
                    string iconPath = GetIconPath(iconId);

                    bool exists = false;
                    try
                    {
                        if (fileExistsMethod != null)
                        {
                            exists = (bool)fileExistsMethod.Invoke(_realm.GameData, new object[] { iconPath });
                        }
                        else if (getFileMethod != null)
                        {
                            var file = getFileMethod.Invoke(_realm.GameData, new object[] { iconPath });
                            exists = file != null;
                        }
                        else if (packCollectionProperty != null)
                        {
                            var packCollection = packCollectionProperty.GetValue(_realm.GameData);
                            if (packCollection != null)
                            {
                                var packType = packCollection.GetType();
                                var packGetFileMethod = packType.GetMethod("GetFile", new[] { typeof(string) });
                                var packFileExistsMethod = packType.GetMethod("FileExists", new[] { typeof(string) });

                                if (packFileExistsMethod != null)
                                {
                                    exists = (bool)packFileExistsMethod.Invoke(packCollection, new object[] { iconPath });
                                }
                                else if (packGetFileMethod != null)
                                {
                                    var file = packGetFileMethod.Invoke(packCollection, new object[] { iconPath });
                                    exists = file != null;
                                }
                            }
                        }
                        else
                        {
                            exists = _mapMarkerCache.Values.Any(markers => markers.Any(marker => marker.IconId == iconId));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error checking existence of {iconPath}: {ex.Message}");
                        exists = false;
                    }

                    if (exists)
                    {
                        found++;
                        Debug.WriteLine($"Icon {iconId} found at path: {iconPath}");
                    }
                    else
                    {
                        notFound++;
                        Debug.WriteLine($"Icon {iconId} NOT FOUND at expected path: {iconPath}");
                    }

                    if ((found + notFound) % 20 == 0)
                    {
                        Debug.WriteLine($"Progress: {found} found, {notFound} not found");
                    }
                }

                Debug.WriteLine($"Icon path diagnosis complete: {found} icons found, {notFound} icons not found");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error diagnosing icon paths: {ex.Message}");
                Debug.WriteLine($"{ex.StackTrace}");
            }
        }

        private string GetIconPath(uint iconId)
        {
            if (iconId == 0)
            {
                return "ui/icon/060000/060442.tex";
            }

            string folder = $"{iconId / 1000 * 1000:D6}";
            string file = $"{iconId:D6}";

            return $"ui/icon/{folder}/{file}.tex";
        }

        private List<MapMarker> LoadMapMarkersFromCsv(uint mapId)
        {
            var markers = new List<MapMarker>();
            string csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sheets", "MapMarker.csv");
            if (!File.Exists(csvPath))
            {
                Debug.WriteLine($"MapMarker.csv not found at {csvPath}");
                return markers;
            }

            var lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2)
                return markers;

            // Assume map image size (adjust if needed)
            const double mapImageSize = 2048.0;

            for (int i = 2; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var cols = line.Split(',');
                if (cols.Length < 5)
                    continue;

                var rowId = cols[0];
                if (!rowId.StartsWith(mapId.ToString() + ".", StringComparison.Ordinal))
                    continue;

                if (!double.TryParse(cols[1], out double x)) x = 0;
                if (!double.TryParse(cols[2], out double y)) y = 0;
                if (!uint.TryParse(cols[3], out uint iconId)) iconId = 0;
                if (!uint.TryParse(cols[4], out uint placeNameId)) placeNameId = 0;

                // Normalize X/Y from pixel to game coordinates
                double gameX = (x / mapImageSize) * 100.0;
                double gameY = (y / mapImageSize) * 100.0;

                string placeName = $"Marker {rowId}";

                markers.Add(new MapMarker
                {
                    Id = uint.TryParse(rowId.Replace(".", ""), out var id) ? id : (uint)i,
                    MapId = mapId,
                    X = gameX,
                    Y = gameY,
                    Z = 0,
                    PlaceNameId = placeNameId,
                    PlaceName = placeName,
                    IconId = iconId,
                    Type = DetermineMarkerType(iconId),
                    IsVisible = true,
                    IconPath = GetIconPath(iconId)
                });
            }

            Debug.WriteLine($"Loaded {markers.Count} markers from MapMarker.csv for map {mapId}");
            return markers;
        }

        private void LoadMapSymbols()
        {
            try
            {
                if (_realm == null || _symbolsLoaded)
                    return;

                Debug.WriteLine("Loading map symbols from game data...");
                _placeNameToIconMap.Clear();
                _placeNameStringToIconMap.Clear();
                _iconToPlaceNameMap.Clear();

                // Try loading from the MapSymbol sheet
                var symbolSheet = _realm.GameData.GetSheet("MapSymbol");
                if (symbolSheet == null)
                {
                    Debug.WriteLine("MapSymbol sheet not found!");
                    return;
                }

                // Load all symbols from the sheet
                int count = 0;
                foreach (var row in symbolSheet)
                {
                    try
                    {
                        // Get the source row
                        var sourceRow = row.GetType().GetProperty("SourceRow")?.GetValue(row) as SaintCoinach.Ex.Relational.IRelationalRow;
                        if (sourceRow == null) continue;

                        // Get icon ID (column 0)
                        uint iconId = 0;
                        if (sourceRow[0] != null && !(sourceRow[0] is SaintCoinach.Imaging.ImageFile))
                        {
                            iconId = Convert.ToUInt32(sourceRow[0]);
                        }

                        // Skip if no icon ID
                        if (iconId == 0) continue;

                        // Get PlaceName (column 1)
                        uint placeNameId = 0;
                        string placeNameString = string.Empty;

                        if (sourceRow[1] is SaintCoinach.Xiv.PlaceName placeNameObj)
                        {
                            // If we have the direct PlaceName object
                            placeNameId = (uint)placeNameObj.Key;
                            placeNameString = placeNameObj.Name?.ToString() ?? string.Empty;
                        }
                        else if (sourceRow[1] != null && !(sourceRow[1] is SaintCoinach.Imaging.ImageFile))
                        {
                            // If we have just the ID
                            try
                            {
                                placeNameId = Convert.ToUInt32(sourceRow[1]);
                                
                                // Get the actual name from PlaceName sheet
                                var placeNameSheet = _realm.GameData.GetSheet<PlaceName>();
                                var placeNameRow = placeNameSheet.FirstOrDefault(p => p.Key == placeNameId);
                                if (placeNameRow?.Name != null)
                                {
                                    placeNameString = placeNameRow.Name.ToString();
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error processing PlaceName ID: {ex.Message}");
                            }
                        }

                        // Map PlaceName ID to icon ID if we have both
                        if (placeNameId > 0 && iconId > 0)
                        {
                            _placeNameToIconMap[placeNameId] = iconId;
                            
                            // Also add to the reverse lookup
                            if (!_iconToPlaceNameMap.ContainsKey(iconId))
                            {
                                _iconToPlaceNameMap[iconId] = new List<uint>();
                            }
                            _iconToPlaceNameMap[iconId].Add(placeNameId);
                            
                            count++;
                        }

                        // Map PlaceName string to icon ID if we have both
                        if (!string.IsNullOrEmpty(placeNameString) && iconId > 0)
                        {
                            _placeNameStringToIconMap[placeNameString] = iconId;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing MapSymbol row: {ex.Message}");
                    }
                }

                Debug.WriteLine($"Successfully loaded {count} map symbols from game data");
                Debug.WriteLine($"  - Place name ID mappings: {_placeNameToIconMap.Count}");
                Debug.WriteLine($"  - Place name string mappings: {_placeNameStringToIconMap.Count}");
                Debug.WriteLine($"  - Icon to place name mappings: {_iconToPlaceNameMap.Count}");
                
                _symbolsLoaded = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading MapSymbols: {ex.Message}");
            }
        }

        // Also optimize the GetIconForPlaceName method to prevent potential infinite loops
        private uint GetIconForPlaceName(string placeName)
        {
            // Early exit for null/empty
            if (string.IsNullOrEmpty(placeName))
                return 60001; // Default generic icon

            // Make sure symbols are loaded (but with timeout to prevent hanging)
            if (!_symbolsLoaded)
            {
                try
                {
                    LoadMapSymbols();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading map symbols: {ex.Message}");
                    return 60001; // Return default if symbol loading fails
                }
            }

            // 1. Special cases based on exact mappings from the game data
            if (IsAetheryte(placeName))
            {
                return 60441; // Aetheryte icon from data
            }

            if (IsLandmark(placeName))
            {
                return 60442; // Landmark icon from data
            }

            // 2. Direct exact match of the full place name
            if (_placeNameStringToIconMap.TryGetValue(placeName, out var iconId))
            {
                return iconId;
            }

            // 3. Partial match by checking if any known place name is contained in this name
            // Limit the search to prevent performance issues
            int searchCount = 0;
            const int MAX_SEARCH_ITERATIONS = 50;
            
            foreach (var knownName in _placeNameStringToIconMap.Keys)
            {
                if (++searchCount > MAX_SEARCH_ITERATIONS)
                    break; // Prevent excessive searching
                
                if (placeName.Contains(knownName, StringComparison.OrdinalIgnoreCase))
                {
                    return _placeNameStringToIconMap[knownName];
                }
            }

            // 4. Type-based lookup based on name patterns
            if (IsCityOrSettlement(placeName))
                return 60430; // City/Settlement icon

            if (IsShopOrMarket(placeName))
                return 60314; // Shop/Market icon

            // 5. Final fallback
            return 60001; // Default generic icon
        }

        private bool IsAetheryte(string placeName)
        {
            if (string.IsNullOrEmpty(placeName))
                return false;

            // Check specific aetheryte references
            string lowerName = placeName.ToLower();
    
            // Direct aetheryte references
            if (lowerName.Contains("aetheryte") || 
                lowerName.Contains("aethernet") || 
                lowerName.Contains("airship landing"))
                return true;
    
            // Common teleport hub patterns based on MapMarker.csv data
            if ((lowerName.Contains("plaza") || lowerName.Contains("square")) && 
                (lowerName.Contains("city") || lowerName.Contains("town")))
                return true;
    
            return false;
        }

        private bool IsLandmark(string placeName)
        {
            if (string.IsNullOrEmpty(placeName))
                return false;

            // Common landmark identifiers based on MapMarker.csv data
            string[] landmarkKeywords = { 
                "yard", "tower", "keep", "falls", "gorge", "cliff", "peak", 
                "cave", "ruins", "temple", "bridge", "camp", "enclave"
            };

            string lowerName = placeName.ToLower();
    
            // Check if any of the landmark keywords are in the place name
            foreach (var keyword in landmarkKeywords)
            {
                if (lowerName.Contains(keyword))
                    return true;
            }
    
            return false;
        }

        private bool IsCityOrSettlement(string placeName)
        {
            if (string.IsNullOrEmpty(placeName))
                return false;
                
            string lowerName = placeName.ToLower();
    
            // Common city/settlement terms
            return lowerName.Contains("limsa lominsa") || 
                   lowerName.Contains("gridania") ||
                   lowerName.Contains("ul'dah") ||
                   lowerName.Contains("ishgard") ||
                   lowerName.Contains("kugane") ||
                   lowerName.Contains("crystarium") ||
                   lowerName.Contains("village") ||
                   lowerName.Contains("town") ||
                   lowerName.Contains("settlement");
        }

        private bool IsShopOrMarket(string placeName)
        {
            if (string.IsNullOrEmpty(placeName))
                return false;
                
            string lowerName = placeName.ToLower();
    
            // Common shop/market terms
            return lowerName.Contains("shop") ||
                   lowerName.Contains("market") ||
                   lowerName.Contains("stalls") ||
                   lowerName.Contains("vendor") ||
                   lowerName.Contains("guild") ||
                   lowerName.Contains("store");
        }

        // Add this method to MapService.cs to help diagnose canvas alignment issues
        public void DiagnoseCanvasAlignment(WpfSize mapCanvasSize, WpfSize overlayCanvasSize, 
                                           WpfPoint mapPosition, WpfPoint overlayPosition,
                                           double mapScale, double overlayScale)
        {
            Debug.WriteLine("Canvas Alignment Diagnostic:");
            Debug.WriteLine($"Map Canvas: Size={mapCanvasSize.Width}x{mapCanvasSize.Height}, " +
                            $"Position=({mapPosition.X},{mapPosition.Y}), Scale={mapScale}");
            Debug.WriteLine($"Overlay Canvas: Size={overlayCanvasSize.Width}x{overlayCanvasSize.Height}, " +
                            $"Position=({overlayPosition.X},{overlayPosition.Y}), Scale={overlayScale}");
            
            bool sizeMismatch = Math.Abs(mapCanvasSize.Width - overlayCanvasSize.Width) > 0.01 || 
                                Math.Abs(mapCanvasSize.Height - overlayCanvasSize.Height) > 0.01;
            bool positionMismatch = Math.Abs(mapPosition.X - overlayPosition.X) > 0.01 || 
                               Math.Abs(mapPosition.Y - overlayPosition.Y) > 0.01;
            bool scaleMismatch = Math.Abs(mapScale - overlayScale) > 0.01;
            
            if (sizeMismatch || positionMismatch || scaleMismatch)
            {
                Debug.WriteLine("MISALIGNMENT DETECTED:");
                if (sizeMismatch)
                    Debug.WriteLine("  - Canvas sizes do not match");
                if (positionMismatch)
                    Debug.WriteLine("  - Canvas positions do not match");
                if (scaleMismatch)
                    Debug.WriteLine("  - Canvas scales do not match");
            }
            else
            {
                Debug.WriteLine("Canvas alignment appears correct");
            }
        }
    }
}