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
using System.Windows.Media;
using System.Windows.Controls;

namespace map_editor
{
    public class MapService
    {
        private readonly ARealmReversed? _realm;
        private readonly Dictionary<uint, List<MapMarker>> _mapMarkerCache = new();
        private readonly Dictionary<uint, Dictionary<uint, string>> _placeNameCache = new();
        private bool _csvDataLoaded = false;

        private class MapSymbolCategory
        {
            public string Name { get; set; } = string.Empty;
            public uint DefaultIconId { get; set; }
            public HashSet<string> Keywords { get; set; } = new HashSet<string>();
        }

        private Dictionary<string, uint> _mapSymbols = new();
        private Dictionary<uint, uint> _placeNameToSymbol = new();
        private Dictionary<string, uint> _keywordToSymbol = new();
        private List<MapSymbolCategory> _symbolCategories = new();
        private Dictionary<uint, uint> _placeNameToIconMap = new(); 
        private Dictionary<string, uint> _placeNameStringToIconMap = new(); 
        private Dictionary<uint, List<uint>> _iconToPlaceNameMap = new();
        private bool _symbolsLoaded = false;

        public MapService(ARealmReversed? realm)
        {
            _realm = realm;
        }

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

        private void LoadMapMarkersFromSheet(List<MapMarker> markers, Map map)
        {
            try
            {
                Debug.WriteLine("Loading map markers from sheet...");
                int count = 0;
                const int MAX_MARKERS = 100; 

                var mapMarkerSheet = _realm?.GameData?.GetSheet("MapMarker");
                if (mapMarkerSheet == null)
                {
                    Debug.WriteLine("MapMarker sheet not found in SaintCoinach");
                    return;
                }

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

                var variantRow = mapMarkerSheet.FirstOrDefault(r => Convert.ToUInt32(r.Key) == markerRange);
                if (variantRow == null)
                {
                    Debug.WriteLine($"No row found for markerRange {markerRange}");
                    return;
                }

                Debug.WriteLine($"Found row for markerRange {markerRange}, type: {variantRow.GetType().FullName}");

                try
                {
                    var sourceRowProp = variantRow.GetType().GetProperty("SourceRow");
                    if (sourceRowProp != null)
                    {
                        var sourceRow = sourceRowProp.GetValue(variantRow);
                        if (sourceRow != null)
                        {
                            Debug.WriteLine($"Found SourceRow of type: {sourceRow.GetType().FullName}");
                            
                            var subRowsProp = sourceRow.GetType().GetProperty("SubRows");
                            if (subRowsProp != null)
                            {
                                var subRowsCollection = subRowsProp.GetValue(sourceRow) as System.Collections.IEnumerable;
                                if (subRowsCollection != null)
                                {
                                    Debug.WriteLine("Successfully retrieved SubRows collection");
                                    
                                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                                    const int MAX_PROCESSING_TIME_MS = 2000;
                                    
                                    foreach (var subRow in subRowsCollection)
                                    {
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
                                                catch {}

                                                try
                                                {
                                                    var yValue = indexerMethod.Invoke(subRow, new object[] { "Y" });
                                                    if (yValue != null && !(yValue is SaintCoinach.Imaging.ImageFile))
                                                        y = Convert.ToSingle(yValue);
                                                }
                                                catch {}

                                                try
                                                {
                                                    var intIndexerMethod = subRowType.GetMethod("get_Item", new[] { typeof(int) });
                                                    
                                                    if (intIndexerMethod != null)
                                                    {
                                                        var iconValue = intIndexerMethod.Invoke(subRow, new object[] { 2 });
                                                        if (iconValue != null)
                                                        {
                                                            if (iconValue is SaintCoinach.Imaging.ImageFile imageFile)
                                                            {
                                                                var imagePath = imageFile.Path;
                                                                if (!string.IsNullOrEmpty(imagePath))
                                                                {
                                                                    var match = System.Text.RegularExpressions.Regex.Match(imagePath, @"/(\d{6})\.tex$");
                                                                    if (match.Success && uint.TryParse(match.Groups[1].Value, out uint extractedIconId))
                                                                    {
                                                                        iconId = extractedIconId;
                                                                        Debug.WriteLine($"Extracted icon ID {iconId} from image path: {imagePath}");
                                                                    }
                                                                    else
                                                                    {
                                                                        Debug.WriteLine($"Could not extract icon ID from path: {imagePath}");
                                                                    }
                                                                }
                                                            }
                                                            else if (iconValue is ushort iconUShort)
                                                            {
                                                                iconId = iconUShort;
                                                            }
                                                            else if (iconValue is short iconShort && iconShort >= 0)
                                                            {
                                                                iconId = (uint)iconShort;
                                                            }
                                                            else
                                                            {
                                                                iconId = Convert.ToUInt32(iconValue);
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        var iconValue = indexerMethod.Invoke(subRow, new object[] { "Icon" });
                                                        if (iconValue != null)
                                                        {
                                                            if (iconValue is SaintCoinach.Imaging.ImageFile imageFile)
                                                            {
                                                                var imagePath = imageFile.Path;
                                                                if (!string.IsNullOrEmpty(imagePath))
                                                                {
                                                                    var match = System.Text.RegularExpressions.Regex.Match(imagePath, @"/(\d{6})\.tex$");
                                                                    if (match.Success && uint.TryParse(match.Groups[1].Value, out uint extractedIconId))
                                                                    {
                                                                        iconId = extractedIconId;
                                                                    }
                                                                }
                                                            }
                                                            else
                                                            {
                                                                iconId = Convert.ToUInt32(iconValue);
                                                            }
                                                        }
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    Debug.WriteLine($"Error reading icon from column 2: {ex.Message}");
                                                    iconId = 0;
                                                }

                                                try
                                                {
                                                    var placeNameValue = indexerMethod.Invoke(subRow, new object[] { "PlaceNameSubtext" });
                                                    if (placeNameValue != null && !(placeNameValue is SaintCoinach.Imaging.ImageFile))
                                                    {
                                                        if (placeNameValue is SaintCoinach.Xiv.PlaceName placeNameObj)
                                                        {
                                                            placeName = placeNameObj.Name?.ToString() ?? "Marker";
                                                            placeNameId = (uint)placeNameObj.Key;
                                                        }
                                                        else
                                                        {
                                                            uint placeNameSubtextId = Convert.ToUInt32(placeNameValue);
                                                            if (placeNameSubtextId > 0)
                                                            {
                                                                placeNameId = placeNameSubtextId;
                                                                var placeNameSheet = _realm.GameData.GetSheet<PlaceName>();
                                                                var placeNameRow = placeNameSheet.FirstOrDefault(p => p.Key == placeNameSubtextId);
                                                                if (placeNameRow?.Name != null)
                                                                {
                                                                    placeName = placeNameRow.Name.ToString();
                                                                }
                                                                else
                                                                {
                                                                    placeName = placeNameValue.ToString() ?? "Marker";
                                                                }
                                                            }
                                                            else
                                                            {
                                                                placeName = placeNameValue.ToString() ?? "Marker";
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        placeNameValue = indexerMethod.Invoke(subRow, new object[] { "PlaceName" });
                                                        if (placeNameValue != null && !(placeNameValue is SaintCoinach.Imaging.ImageFile))
                                                            placeName = placeNameValue.ToString() ?? "Marker";
                                                    }
                                                }
                                                catch {}

                                                try
                                                {
                                                    var placeNameIdValue = indexerMethod.Invoke(subRow, new object[] { "PlaceNameId" });
                                                    if (placeNameIdValue != null && !(placeNameIdValue is SaintCoinach.Imaging.ImageFile))
                                                        placeNameId = Convert.ToUInt32(placeNameIdValue);
                                                }
                                                catch {}

                                                uint subRowKey = 0;
                                                var keyProp = subRowType.GetProperty("Key");
                                                if (keyProp != null)
                                                {
                                                    var keyValue = keyProp.GetValue(subRow);
                                                    if (keyValue != null)
                                                        subRowKey = Convert.ToUInt32(keyValue);
                                                }
                                                
                                                if (x > 0 && y > 0)
                                                {
                                                    Debug.WriteLine($"SubRow {subRowKey}: IconId from data = {iconId}, PlaceName = '{placeName}', PlaceNameId = {placeNameId}");
                                                    
                                                    if (iconId == 0 && placeNameId > 0)
                                                    {
                                                        Debug.WriteLine($"SubRow {subRowKey}: Text-only marker detected for '{placeName}'");
                                                        iconId = 0;
                                                    }
                                                    else if (iconId == 0)
                                                    {
                                                        Debug.WriteLine($"SubRow {subRowKey}: No icon in data, calling GetIconForPlaceName('{placeName}')");
                                                        iconId = GetIconForPlaceName(placeName);
                                                        Debug.WriteLine($"SubRow {subRowKey}: GetIconForPlaceName returned {iconId}");
                                                    }
                                                    
                                                    if (count < 10)
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
                                                        Type = iconId == 0 ? MarkerType.Symbol : DetermineMarkerType(iconId),
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

            markers.Add(CreateDirectionalMarker(mapId, 2, centerX + 20, centerY, "East Quest", MarkerType.Quest, 60201));
            markers.Add(CreateDirectionalMarker(mapId, 3, centerX - 20, centerY, "West Shop", MarkerType.Shop, 60301));
            markers.Add(CreateDirectionalMarker(mapId, 4, centerX, centerY + 20, "North Landmark", MarkerType.Landmark, 60501));
            markers.Add(CreateDirectionalMarker(mapId, 5, centerX, centerY - 20, "South Entrance", MarkerType.Entrance, 60901));

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
    double relativeX = gameX / 42.0;
    double relativeY = gameY / 42.0;
    double pixelX = relativeX * imageSize.Width;
    double pixelY = relativeY * imageSize.Height;
    
    Debug.WriteLine($"Converting: Game ({gameX:F1}, {gameY:F1}) -> " +
                   $"Relative ({relativeX:F2}, {relativeY:F2}) -> " +
                   $"Pixel ({pixelX:F1}, {pixelY:F1})");
    

    double screenX = pixelX * imageScale + imagePosition.X;
    double screenY = pixelY * imageScale + imagePosition.Y;
    
    return new WpfPoint(screenX, screenY);
}

public MapCoordinate ConvertMapToGameCoordinates(
    WpfPoint clickPoint, WpfPoint imagePosition, 
    double scale, WpfSize imageSize, Map? map)
{
    var result = new MapCoordinate();
    
    try
    {
        if (map == null) return result;
        float sizeFactor = 100.0f;
        float offsetX = 0;
        float offsetY = 0;
        
        try
        {
            var type = map.GetType();
            var indexer = type.GetProperty("Item", new[] { typeof(string) });
            
            if (indexer != null)
            {
                sizeFactor = (float)Convert.ChangeType(
                    indexer.GetValue(map, new object[] { "SizeFactor" }), typeof(float));
                offsetX = (float)Convert.ChangeType(
                    indexer.GetValue(map, new object[] { "OffsetX" }), typeof(float));
                offsetY = (float)Convert.ChangeType(
                    indexer.GetValue(map, new object[] { "OffsetY" }), typeof(float));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error accessing map properties: {ex.Message}");
        }

        double relativeX = (clickPoint.X - imagePosition.X) / (imageSize.Width * scale);
        double relativeY = (clickPoint.Y - imagePosition.Y) / (imageSize.Height * scale);
        relativeX = Math.Max(0, Math.Min(1, relativeX));
        relativeY = Math.Max(0, Math.Min(1, relativeY));
        
        // Calculate raw map coordinates
        double rawX = relativeX * 2048.0 - offsetX;
        double rawY = relativeY * 2048.0 - offsetY;
        
        // Calculate game coordinates
        double c = sizeFactor / 100.0;
        double gameX = (41.0 / c) * relativeX + 1.0;
        double gameY = (41.0 / c) * relativeY + 1.0;
        
        // Populate result
        result.MapX = gameX;
        result.MapY = gameY;
        result.ClientX = clickPoint.X;
        result.ClientY = clickPoint.Y;
        
        return result;
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Error converting coordinates: {ex.Message}");
        return result;
    }
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

                var symbolSheet = _realm.GameData.GetSheet("MapSymbol");
                if (symbolSheet == null)
                {
                    Debug.WriteLine("MapSymbol sheet not found!");
                    return;
                }

                int count = 0;
                foreach (var row in symbolSheet)
                {
                    try
                    {
                        var sourceRow = row.GetType().GetProperty("SourceRow")?.GetValue(row) as SaintCoinach.Ex.Relational.IRelationalRow;
                        if (sourceRow == null) continue;

                        uint iconId = 0;
                        if (sourceRow[0] != null && !(sourceRow[0] is SaintCoinach.Imaging.ImageFile))
                        {
                            iconId = Convert.ToUInt32(sourceRow[0]);
                        }

                        if (iconId == 0) continue;

                        uint placeNameId = 0;
                        string placeNameString = string.Empty;

                        if (sourceRow[1] is SaintCoinach.Xiv.PlaceName placeNameObj)
                        {
                            placeNameId = (uint)placeNameObj.Key;
                            placeNameString = placeNameObj.Name?.ToString() ?? string.Empty;
                        }
                        else if (sourceRow[1] != null && !(sourceRow[1] is SaintCoinach.Imaging.ImageFile))
                        {
                            try
                            {
                                placeNameId = Convert.ToUInt32(sourceRow[1]);
                                
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

                        if (placeNameId > 0 && iconId > 0)
                        {
                            _placeNameToIconMap[placeNameId] = iconId;
                            
                            if (!_iconToPlaceNameMap.ContainsKey(iconId))
                            {
                                _iconToPlaceNameMap[iconId] = new List<uint>();
                            }
                            _iconToPlaceNameMap[iconId].Add(placeNameId);
                            
                            count++;
                        }

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

        private uint GetIconForPlaceName(string placeName)
        {
            if (string.IsNullOrEmpty(placeName))
            {
                Debug.WriteLine($"GetIconForPlaceName: Empty place name, returning default icon");
                return 60001;
            }

            Debug.WriteLine($"GetIconForPlaceName: Looking up icon for '{placeName}'");

            if (!_symbolsLoaded)
            {
                try
                {
                    Debug.WriteLine("GetIconForPlaceName: Symbols not loaded, attempting to load...");
                    LoadMapSymbols();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading map symbols: {ex.Message}");
                    return 60001; 
                }
            }

            Debug.WriteLine($"GetIconForPlaceName: PlaceNameStringToIconMap has {_placeNameStringToIconMap.Count} entries");

            if (IsAetheryte(placeName))
            {
                Debug.WriteLine($"GetIconForPlaceName: '{placeName}' identified as Aetheryte");
                return 60441; 
            }

            if (IsLandmark(placeName))
            {
                Debug.WriteLine($"GetIconForPlaceName: '{placeName}' identified as Landmark");
                return 60442; 
            }


            if (_placeNameStringToIconMap.TryGetValue(placeName, out var iconId))
            {
                Debug.WriteLine($"GetIconForPlaceName: Found exact match for '{placeName}' -> {iconId}");
                return iconId;
            }

            int searchCount = 0;
            const int MAX_SEARCH_ITERATIONS = 50;
            
            foreach (var knownName in _placeNameStringToIconMap.Keys)
            {
                if (++searchCount > MAX_SEARCH_ITERATIONS)
                    break;
                
                if (placeName.Contains(knownName, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"GetIconForPlaceName: Found partial match '{knownName}' in '{placeName}' -> {_placeNameStringToIconMap[knownName]}");
                    return _placeNameStringToIconMap[knownName];
                }
            }

            if (IsCityOrSettlement(placeName))
            {
                Debug.WriteLine($"GetIconForPlaceName: '{placeName}' identified as City/Settlement");
                return 60430;
            }

            if (IsShopOrMarket(placeName))
            {
                Debug.WriteLine($"GetIconForPlaceName: '{placeName}' identified as Shop/Market");
                return 60314;
            }


            Debug.WriteLine($"GetIconForPlaceName: No match found for '{placeName}', returning default icon");
            return 60001;
        }


        private bool IsAetheryte(string placeName)
        {
            if (string.IsNullOrEmpty(placeName))
                return false;

            string lowerName = placeName.ToLower();
    
            if (lowerName.Contains("aetheryte") || 
                lowerName.Contains("aethernet") || 
                lowerName.Contains("airship landing"))
                return true;
    
            if ((lowerName.Contains("plaza") || lowerName.Contains("square")) && 
                (lowerName.Contains("city") || lowerName.Contains("town")))
                return true;
    
            return false;
        }

        private bool IsLandmark(string placeName)
        {
            if (string.IsNullOrEmpty(placeName))
                return false;

            string[] landmarkKeywords = { 
                "yard", "tower", "keep", "falls", "gorge", "cliff", "peak", 
                "cave", "ruins", "temple", "bridge"
            };

            string lowerName = placeName.ToLower();
    
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


            Debug.WriteLine($"MapImageControl transform: Scale={mapScale:F2}, Position=({mapPosition.X:F1},{mapPosition.Y:F1})");
            Debug.WriteLine($"OverlayCanvas transform: Scale={overlayScale:F2}, Position=({overlayPosition.X:F1},{overlayPosition.Y:F1})");
        }

        public void LogCursorPosition(WpfPoint mousePosition, WpfPoint imagePosition, 
                            double scale, WpfSize imageSize, Map? map)
        {
            var mapCoords = ConvertMapToGameCoordinates(mousePosition, imagePosition, scale, imageSize, map);
            
            Debug.WriteLine($"Cursor - Screen: ({mousePosition.X:F1}, {mousePosition.Y:F1}), " +
                           $"Map: ({mapCoords.MapX:F2}, {mapCoords.MapY:F2})");
            
            bool isInMapBounds = 
                mousePosition.X >= imagePosition.X && 
                mousePosition.X <= imagePosition.X + (imageSize.Width * scale) &&
                mousePosition.Y >= imagePosition.Y && 
                mousePosition.Y <= imagePosition.Y + (imageSize.Height * scale);
            
            Debug.WriteLine($"Cursor is {(isInMapBounds ? "INSIDE" : "OUTSIDE")} map bounds");
        }

        public MapMarker? GetMarkerAtPosition(WpfPoint mousePosition, WpfPoint imagePosition, 
                                     double scale, WpfSize imageSize, List<MapMarker> markers)
        {
            double hitTestRadius = 15 / scale;
            
            foreach (var marker in markers.Where(m => m.IsVisible))
            {
                double screenX = (marker.X / 42.0) * imageSize.Width * scale + imagePosition.X;
                double screenY = (marker.Y / 42.0) * imageSize.Height * scale + imagePosition.Y;
                
                double distance = Math.Sqrt(
                    Math.Pow(mousePosition.X - screenX, 2) + 
                    Math.Pow(mousePosition.Y - screenY, 2));
                
                if (distance <= hitTestRadius)
                {
                    return marker;
                }
            }
            
            return null;
        }
    }
}