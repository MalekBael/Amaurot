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
using Amaurot.Services; // ✅ ADD: This should already be there, but make sure it includes FateLgbService

namespace Amaurot
{
    public class MapService
    {
        private readonly ARealmReversed? _realm;
        private readonly Action<string> _logDebug;
        private bool _verboseDebugMode = false;
        private readonly Dictionary<uint, List<MapMarker>> _mapMarkerCache = new Dictionary<uint, List<MapMarker>>();
        private readonly Dictionary<uint, List<FateInfo>> _mapFateCache = new Dictionary<uint, List<FateInfo>>();
        private readonly Dictionary<uint, string> _placeNameCache = new Dictionary<uint, string>();

        private class MapSymbolCategory
        {
            public string Name { get; set; } = string.Empty;
            public uint DefaultIconId { get; set; }
            public HashSet<string> Keywords { get; set; } = new HashSet<string>();
        }

        private Dictionary<string, uint> _mapSymbols = new();
        private Dictionary<uint, uint> _placeNameToSymbol = new();
        private Dictionary<uint, uint> _placeNameToIconMap = new();
        private Dictionary<string, uint> _placeNameStringToIconMap = new();
        private Dictionary<uint, List<uint>> _iconToPlaceNameMap = new();
        private bool _symbolsLoaded = false;

        public MapService(ARealmReversed? realm, Action<string>? logDebug = null)
        {
            _realm = realm;
            _logDebug = logDebug ?? (msg => { });
            LoadMapSymbols();
            LoadPlaceNameCache();
        }

        private void LoadPlaceNameCache()
        {
            if (_realm == null) return;

            try
            {
                _logDebug("Loading PlaceName cache...");
                var placeNameSheet = _realm.GameData.GetSheet("PlaceName");

                foreach (var placeNameRow in placeNameSheet)
                {
                    try
                    {
                        var id = (uint)placeNameRow.Key;
                        var name = placeNameRow.AsString("Name") ?? "";

                        if (!string.IsNullOrEmpty(name))
                        {
                            _placeNameCache[id] = name;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_verboseDebugMode)
                        {
                            _logDebug($"Error loading PlaceName {placeNameRow.Key}: {ex.Message}");
                        }
                    }
                }

                _logDebug($"Loaded {_placeNameCache.Count} place names into cache");
            }
            catch (Exception ex)
            {
                _logDebug($"Error loading PlaceName cache: {ex.Message}");
            }
        }

        public List<MapMarker> LoadMapMarkers(uint mapId)
        {
            _logDebug($"🗺️ === MAP SERVICE DEBUG START === MapId: {mapId}");

            // Check cache first
            if (_mapMarkerCache.ContainsKey(mapId))
            {
                _logDebug($"💾 Returning {_mapMarkerCache[mapId].Count} cached markers for map {mapId}");
                return _mapMarkerCache[mapId];
            }

            var markers = new List<MapMarker>();

            if (_realm == null)
            {
                _logDebug("❌ Realm is null in LoadMapMarkers");
                return markers;
            }

            try
            {
                var mapSheet = _realm.GameData.GetSheet<Map>();
                var map = mapSheet[(int)mapId];

                if (map == null)
                {
                    _logDebug($"❌ Map {mapId} not found");
                    return markers;
                }

                _logDebug($"✅ Found map {mapId}: {map.PlaceName?.Name}");

                // Load regular map markers
                _logDebug($"📍 Loading regular map markers from sheet...");
                LoadMapMarkersFromSheet(markers, map);
                _logDebug($"📍 Loaded {markers.Count} regular markers");

                // ✅ ENHANCED: Load FATE markers with detailed debugging
                _logDebug($"🎯 Loading FATE markers from LGB data...");
                if (_realm != null)
                {
                    var fateLgbService = new FateLgbService(_realm, _logDebug);
                    var fateMarkers = fateLgbService.LoadFateMarkersFromLgb(mapId);

                    _logDebug($"🎯 FateLgbService returned {fateMarkers.Count} FATE markers");

                    if (fateMarkers.Count > 0)
                    {
                        _logDebug($"📋 FATE markers details:");
                        for (int i = 0; i < Math.Min(fateMarkers.Count, 5); i++) // Show first 5
                        {
                            var marker = fateMarkers[i];
                            _logDebug($"   #{i + 1}: '{marker.PlaceName}' at ({marker.X:F1}, {marker.Y:F1}) - Type: {marker.Type}, Visible: {marker.IsVisible}, Icon: {marker.IconId}");
                        }
                        if (fateMarkers.Count > 5)
                        {
                            _logDebug($"   ... and {fateMarkers.Count - 5} more FATE markers");
                        }
                    }

                    markers.AddRange(fateMarkers);
                    _logDebug($"✅ Added {fateMarkers.Count} FATE markers to total marker list");
                }

                _logDebug($"📊 Total markers loaded for map {mapId}: {markers.Count} (including {markers.Count(m => m.Type == MarkerType.Fate)} FATE markers)");

                // Cache the results
                _mapMarkerCache[mapId] = markers;

                _logDebug($"🗺️ === MAP SERVICE COMPLETE === Cached {markers.Count} markers for map {mapId}");
            }
            catch (Exception ex)
            {
                _logDebug($"❌ CRITICAL ERROR in LoadMapMarkers: {ex.Message}");
                _logDebug($"Stack trace: {ex.StackTrace}");
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
                                                catch { }

                                                try
                                                {
                                                    var yValue = indexerMethod.Invoke(subRow, new object[] { "Y" });
                                                    if (yValue != null && !(yValue is SaintCoinach.Imaging.ImageFile))
                                                        y = Convert.ToSingle(yValue);
                                                }
                                                catch { }

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
                                                                var placeNameSheet = _realm?.GameData.GetSheet<PlaceName>();
                                                                var placeNameRow = placeNameSheet?.FirstOrDefault(p => p.Key == placeNameSubtextId);
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
                                                catch { }

                                                try
                                                {
                                                    var placeNameIdValue = indexerMethod.Invoke(subRow, new object[] { "PlaceNameId" });
                                                    if (placeNameIdValue != null && !(placeNameIdValue is SaintCoinach.Imaging.ImageFile))
                                                        placeNameId = Convert.ToUInt32(placeNameIdValue);
                                                }
                                                catch { }

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
            // Add Fate icon detection
            if (iconId == 60093 || iconId == 60501 || iconId == 60502 || iconId == 60503 || iconId == 60504 || iconId == 60505)
                return MarkerType.Fate;

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
                        var sizeFactorValue = indexer.GetValue(map, new object[] { "SizeFactor" });
                        if (sizeFactorValue != null)
                        {
                            sizeFactor = (float)Convert.ChangeType(sizeFactorValue, typeof(float));
                        }

                        var offsetXValue = indexer.GetValue(map, new object[] { "OffsetX" });
                        if (offsetXValue != null)
                        {
                            offsetX = (float)Convert.ChangeType(offsetXValue, typeof(float));
                        }

                        var offsetYValue = indexer.GetValue(map, new object[] { "OffsetY" });
                        if (offsetYValue != null)
                        {
                            offsetY = (float)Convert.ChangeType(offsetYValue, typeof(float));
                        }
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
                                var placeNameValue = sourceRow[1];
                                if (placeNameValue != null)
                                {
                                    placeNameId = Convert.ToUInt32(placeNameValue);

                                    var placeNameSheet = _realm.GameData.GetSheet<PlaceName>();
                                    var placeNameRow = placeNameSheet.FirstOrDefault(p => p.Key == placeNameId);
                                    if (placeNameRow?.Name != null)
                                    {
                                        placeNameString = placeNameRow.Name.ToString();
                                    }
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

        public List<FateInfo> LoadMapFates(uint mapId)
        {
            if (_mapFateCache.ContainsKey(mapId))
            {
                return _mapFateCache[mapId];
            }

            var mapFates = new List<FateInfo>();

            if (_realm == null) return mapFates;

            try
            {
                var mapSheet = _realm.GameData.GetSheet<Map>();
                Map? map = null;

                try
                {
                    map = mapSheet[(int)mapId];
                }
                catch (KeyNotFoundException)
                {
                    _logDebug($"Map {mapId} not found in sheet");
                    return mapFates;
                }

                if (map == null)
                {
                    _logDebug($"Map {mapId} is null");
                    return mapFates;
                }

                var territoryId = (uint)map.TerritoryType.Key;

                // Load sheets
                var fateSheet = _realm.GameData.GetSheet("Fate");

                if (_verboseDebugMode)
                {
                    _logDebug($"Loading FATEs for map {mapId}, territory {territoryId}");
                    _logDebug($"Fate sheet contains {fateSheet.Count()} entries");
                }

                int processedFates = 0;
                int validFates = 0;
                int fatesWithoutLocation = 0;

                foreach (var fateRow in fateSheet)
                {
                    processedFates++;

                    try
                    {
                        if (fateRow == null)
                        {
                            if (_verboseDebugMode)
                            {
                                _logDebug($"Null FATE row encountered");
                            }
                            continue;
                        }

                        int fateKey = 0;
                        try
                        {
                            fateKey = fateRow.Key;
                        }
                        catch (Exception ex)
                        {
                            if (_verboseDebugMode)
                            {
                                _logDebug($"Error getting FATE key: {ex.Message}");
                            }
                            continue;
                        }

                        string fateName = "";
                        try
                        {
                            var nameValue = fateRow[24];
                            fateName = nameValue?.ToString() ?? "";
                        }
                        catch (Exception ex)
                        {
                            if (_verboseDebugMode)
                            {
                                _logDebug($"Error accessing FATE name at index 24 for key {fateKey}: {ex.Message}");
                            }
                            // Fallback to string access
                            try
                            {
                                if (fateRow is SaintCoinach.Xiv.XivRow xivRow)
                                {
                                    fateName = xivRow.AsString("Name") ?? "";
                                }
                            }
                            catch
                            {
                                fateName = "";
                            }
                        }

                        if (string.IsNullOrWhiteSpace(fateName)) continue;

                        var locationValue = 0;
                        try
                        {
                            if (fateRow is SaintCoinach.Xiv.XivRow xivRow)
                            {
                                locationValue = xivRow.AsInt32("Location");
                            }
                            else
                            {
                                var locValue = fateRow[9];
                                if (locValue != null)
                                {
                                    locationValue = Convert.ToInt32(locValue);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (_verboseDebugMode)
                            {
                                _logDebug($"Error accessing Location for FATE {fateKey}: {ex.Message}");
                            }
                        }

                        if (locationValue == 0)
                        {
                            fatesWithoutLocation++;
                        }

                        uint classJobLevel = 0;
                        uint iconId = 60093; // Default FATE icon

                        try
                        {
                            if (fateRow is SaintCoinach.Xiv.XivRow xivRow)
                            {
                                classJobLevel = (uint)Math.Max(0, xivRow.AsInt32("ClassJobLevel"));
                            }
                            else
                            {
                                var levelValue = fateRow[2];
                                if (levelValue != null)
                                {
                                    classJobLevel = (uint)Math.Max(0, Convert.ToInt32(levelValue));
                                }
                            }
                        }
                        catch { }

                        try
                        {
                            if (fateRow is SaintCoinach.Xiv.XivRow xivRow)
                            {
                                iconId = (uint)Math.Max(60093, xivRow.AsInt32("Icon"));
                            }
                            else
                            {
                                var iconValue = fateRow[3];
                                if (iconValue != null)
                                {
                                    iconId = (uint)Math.Max(60093, Convert.ToInt32(iconValue));
                                }
                            }
                        }
                        catch { }

                        string description = "";
                        try
                        {
                            if (fateRow is SaintCoinach.Xiv.XivRow xivRow)
                            {
                                description = xivRow.AsString("Description") ?? "";
                            }
                            else
                            {
                                var descValue = fateRow[25];
                                description = descValue?.ToString() ?? "";
                            }
                        }
                        catch { }

                        var random = new Random(fateKey);
                        double fateMapX = 21 + (random.NextDouble() - 0.5) * 20;
                        double fateMapY = 21 + (random.NextDouble() - 0.5) * 20;
                        double worldZ = 0;

                        var fateInfo = new FateInfo
                        {
                            Id = (uint)fateKey,
                            FateId = (uint)fateKey,
                            Name = fateName,
                            Description = description,
                            Level = classJobLevel,
                            ClassJobLevel = classJobLevel,
                            TerritoryId = territoryId,
                            TerritoryName = map.PlaceName?.ToString() ?? "",
                            IconId = iconId,
                            X = fateMapX,
                            Y = fateMapY,
                            Z = worldZ
                        };

                        mapFates.Add(fateInfo);
                        validFates++;
                    }
                    catch (Exception ex)
                    {
                        if (_verboseDebugMode)
                        {
                            _logDebug($"Error processing FATE at index {processedFates}: {ex.Message}");
                        }
                    }
                }

                _mapFateCache[mapId] = mapFates;

                _logDebug($"FATE loading summary for map {mapId}:");
                _logDebug($"  - Processed: {processedFates} FATEs");
                _logDebug($"  - Valid FATEs added: {validFates}");
                _logDebug($"  - FATEs without location: {fatesWithoutLocation}");
                _logDebug($"  - Total FATEs for this map: {mapFates.Count}");

                if (mapFates.Count == 0)
                {
                    _logDebug($"No FATEs found for map {mapId} (territory {territoryId}). Loading all FATEs as fallback...");

                    // As a fallback, load ALL FATEs regardless of territory
                    // This ensures the FATE list window has something to show
                    foreach (var fateRow in fateSheet)
                    {
                        try
                        {
                            if (fateRow == null) continue;

                            var fateKey = fateRow.Key;
                            var fateName = "";

                            try
                            {
                                var nameValue = fateRow[24];
                                fateName = nameValue?.ToString() ?? "";
                            }
                            catch
                            {
                                try
                                {
                                    if (fateRow is SaintCoinach.Xiv.XivRow xivRow)
                                    {
                                        fateName = xivRow.AsString("Name") ?? "";
                                    }
                                }
                                catch
                                {
                                    fateName = $"FATE {fateKey}";
                                }
                            }

                            if (string.IsNullOrWhiteSpace(fateName)) continue;

                            // Get basic properties
                            uint classJobLevel = 0;
                            try
                            {
                                if (fateRow is SaintCoinach.Xiv.XivRow xivRow)
                                {
                                    classJobLevel = (uint)Math.Max(0, xivRow.AsInt32("ClassJobLevel"));
                                }
                                else
                                {
                                    // Fixed: Add null check for unboxing operation (Line 1665)
                                    var levelValue = fateRow[2];
                                    if (levelValue != null)
                                    {
                                        classJobLevel = (uint)Math.Max(0, Convert.ToInt32(levelValue));
                                    }
                                }
                            }
                            catch { }

                            uint iconId = 60093;
                            try
                            {
                                if (fateRow is SaintCoinach.Xiv.XivRow xivRow)
                                {
                                    iconId = (uint)Math.Max(60093, xivRow.AsInt32("Icon"));
                                }
                                else
                                {
                                    // Fixed: Add null check for unboxing operation (Line 1666)
                                    var iconValue = fateRow[3];
                                    if (iconValue != null)
                                    {
                                        iconId = (uint)Math.Max(60093, Convert.ToInt32(iconValue));
                                    }
                                }
                            }
                            catch { }

                            string description = "";
                            try
                            {
                                if (fateRow is SaintCoinach.Xiv.XivRow xivRow)
                                {
                                    description = xivRow.AsString("Description") ?? "";
                                }
                                else
                                {
                                    // Fixed: Add null check for unboxing operation (Line 1667)
                                    var descValue = fateRow[25];
                                    if (descValue != null)
                                    {
                                        description = descValue.ToString() ?? "";
                                    }
                                }
                            }
                            catch { }

                            var fateInfo = new FateInfo
                            {
                                Id = (uint)fateKey,
                                FateId = (uint)fateKey,
                                Name = fateName,
                                Description = description,
                                Level = classJobLevel,
                                ClassJobLevel = classJobLevel,
                                TerritoryId = territoryId,
                                TerritoryName = map.PlaceName?.ToString() ?? "",
                                IconId = iconId,
                                X = 21,
                                Y = 21,
                                Z = 0
                            };

                            mapFates.Add(fateInfo);
                        }
                        catch { }
                    }

                    _logDebug($"Loaded {mapFates.Count} FATEs as fallback");
                }
            }
            catch (Exception ex)
            {
                _logDebug($"Error in LoadMapFates: {ex.Message}");
                if (_verboseDebugMode)
                {
                    _logDebug($"Stack trace: {ex.StackTrace}");
                }
            }

            return mapFates;
        }

        private double ConvertWorldToMapCoordinate(double worldCoord, Map map)
        {
            try
            {
                // Get map properties
                float sizeFactor = 100.0f;
                float offsetX = 0;
                float offsetY = 0;

                try
                {
                    var type = map.GetType();
                    var indexer = type.GetProperty("Item", new[] { typeof(string) });

                    if (indexer != null)
                    {
                        var sizeFactorValue = indexer.GetValue(map, new object[] { "SizeFactor" });
                        if (sizeFactorValue != null)
                        {
                            sizeFactor = (float)Convert.ChangeType(sizeFactorValue, typeof(float));
                        }

                        var offsetXValue = indexer.GetValue(map, new object[] { "OffsetX" });
                        if (offsetXValue != null)
                        {
                            offsetX = (float)Convert.ChangeType(offsetXValue, typeof(float));
                        }

                        var offsetYValue = indexer.GetValue(map, new object[] { "OffsetY" });
                        if (offsetYValue != null)
                        {
                            offsetY = (float)Convert.ChangeType(offsetYValue, typeof(float));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error accessing map properties: {ex.Message}");
                }

                // FFXIV world coordinate conversion
                // World coordinates are typically in the range of roughly -1000 to +1000
                // Map coordinates in your system are 0-42

                // Apply the standard FFXIV coordinate conversion
                double c = sizeFactor / 100.0;

                // Convert world coordinate to map coordinate
                // This formula converts FFXIV world coordinates to map coordinates
                double mapCoord = ((worldCoord + offsetX) * c + 1024.0) / 2048.0 * 41.0 + 1.0;

                // Clamp to valid range
                mapCoord = Math.Max(1, Math.Min(42, mapCoord));

                return mapCoord;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error converting world coordinate {worldCoord}: {ex.Message}");
                return 21.0; // Return center coordinate as fallback
            }
        }

        public List<MapMarker> ConvertFatesToMarkers(List<FateInfo> fates, uint mapId)
        {
            var markers = new List<MapMarker>();

            foreach (var fate in fates)
            {
                // Use the FATE's actual icon from the game data
                uint iconId = fate.IconId;

                // If no icon is specified, determine a default based on FATE name/description patterns
                if (iconId == 0)
                {
                    iconId = DetermineFateTypeIcon(fate.Name, fate.Description);
                }

                var marker = new MapMarker
                {
                    Id = fate.Id,
                    MapId = mapId,
                    X = fate.X,
                    Y = fate.Y,
                    Z = fate.Z,
                    PlaceName = $"{fate.Name} (Lv.{fate.Level})",
                    PlaceNameId = fate.FateId,
                    IconId = iconId,
                    Type = MarkerType.Fate,
                    IsVisible = true,
                    IconPath = GetIconPath(iconId)
                };

                markers.Add(marker);

                // DEBUG: Log first few FATE markers to see if they're created correctly
                if (markers.Count <= 5)
                {
                    Debug.WriteLine($"Created FATE marker: '{marker.PlaceName}' at ({marker.X:F1}, {marker.Y:F1}) with icon {marker.IconId}, Type={marker.Type}, Visible={marker.IsVisible}");
                }
            }

            Debug.WriteLine($"Converted {fates.Count} fates to {markers.Count} markers");
            return markers;
        }

        private uint DetermineFateTypeIcon(string fateName, string fateDescription)
        {
            // Analyze the FATE name and description to determine the appropriate icon
            string combined = $"{fateName} {fateDescription}".ToLowerInvariant();

            // Boss/Named Monster FATEs
            if (combined.Contains("slay") || combined.Contains("defeat") ||
                combined.Contains("eliminate") || combined.Contains("hunt") ||
                combined.Contains("kill") || combined.Contains("destroy") ||
                fateName.Contains("vs") || fateName.Contains("Vs"))
            {
                return 60501; // Boss/Combat icon
            }

            // Escort/Protection FATEs
            if (combined.Contains("escort") || combined.Contains("protect") ||
                combined.Contains("defend") || combined.Contains("guard") ||
                combined.Contains("accompany") || combined.Contains("guide"))
            {
                return 60502; // Escort/Protection icon
            }

            // Collection/Gathering FATEs
            if (combined.Contains("collect") || combined.Contains("gather") ||
                combined.Contains("deliver") || combined.Contains("retrieve") ||
                combined.Contains("harvest") || combined.Contains("supply"))
            {
                return 60503; // Collection icon
            }

            // Survival/Defense FATEs
            if (combined.Contains("survive") || combined.Contains("hold") ||
                combined.Contains("defend") || combined.Contains("withstand") ||
                combined.Contains("last") || combined.Contains("endure"))
            {
                return 60504; // Defense icon
            }

            // Special Event FATEs
            if (combined.Contains("special") || combined.Contains("event") ||
                combined.Contains("seasonal") || combined.Contains("festival"))
            {
                return 60505; // Special event icon
            }

            // Default FATE icon for anything else
            return 60093; // Generic FATE icon
        }

        public void SetVerboseDebugMode(bool enabled)
        {
            _verboseDebugMode = enabled;
            Debug.WriteLine($"MapService verbose debug mode set to: {enabled}");
        }

        // Create a helper method for conditional debug output
        private void DebugWriteLine(string message)
        {
            if (_verboseDebugMode)
            {
                Debug.WriteLine(message);
            }
        }

        private uint ConvertToTerritoryId(object territoryValue)
        {
            if (territoryValue == null)
                return 0;

            try
            {
                // If it's already a TerritoryType object
                if (territoryValue is SaintCoinach.Xiv.TerritoryType territoryType)
                {
                    return (uint)territoryType.Key;
                }

                // If it's a direct integer value
                if (territoryValue is int intValue)
                {
                    return (uint)Math.Max(0, intValue);
                }

                if (territoryValue is uint uintValue)
                {
                    return uintValue;
                }

                // Try to convert directly
                try
                {
                    return Convert.ToUInt32(territoryValue);
                }
                catch
                {
                    // If direct conversion fails, try to get Key property via reflection
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
            catch (Exception ex)
            {
                if (_verboseDebugMode)
                {
                    _logDebug($"Error converting territory value: {ex.Message}");
                }
            }

            return 0;
        }
    }
}