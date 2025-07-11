using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace map_editor
{
    public class MapMarker
    {
        public uint Id { get; set; }
        public uint MapId { get; set; }
        public uint PlaceNameId { get; set; }
        public string PlaceName { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public uint IconId { get; set; }
        public string IconPath { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
        public MarkerType Type { get; set; } = MarkerType.Generic;

        /// <summary>
        /// Determines the marker type based on its icon ID if not already set
        /// </summary>
        public void DetermineType()
        {
            if (Type != MarkerType.Generic && Type != MarkerType.Custom)
            {
                // Type already set, don't override
                return;
            }

            Type = MapMarkerHelper.InferMarkerTypeFromIconId(IconId);
        }
    }

    public class MapCoordinate
    {
        public double MapX { get; set; }
        public double MapY { get; set; }
        public double ClientX { get; set; }
        public double ClientY { get; set; }
        public double ClientZ { get; set; }
    }

    public enum MarkerType
    {
        Generic,
        Aetheryte,
        Quest,
        Shop,
        Landmark,
        Entrance,
        Symbol,
        Custom
    }

    public class MapInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PlaceName { get; set; } = string.Empty;
        public uint PlaceNameId { get; set; }
        public uint TerritoryType { get; set; }
        public float SizeFactor { get; set; }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public List<MapMarker> Markers { get; set; } = new List<MapMarker>();
    }

    /// <summary>
    /// Helper class for marker-related functionality
    /// </summary>
    public static class MapMarkerHelper
    {
        /// <summary>
        /// Maps icon IDs to marker types based on MapSymbol.csv
        /// </summary>
        private static readonly Dictionary<uint, MarkerType> IconToTypeMap = new Dictionary<uint, MarkerType>
        {
            // Aetheryte icons
            { 60441, MarkerType.Aetheryte },
            { 60447, MarkerType.Aetheryte },
            { 60446, MarkerType.Aetheryte },
            
            // Quest icons
            { 60314, MarkerType.Quest },
            
            // Shop icons
            { 60412, MarkerType.Shop },
            { 60425, MarkerType.Shop },
            { 60434, MarkerType.Shop },
            { 60551, MarkerType.Shop },
            { 60570, MarkerType.Shop },
            
            // Landmark icons
            { 60430, MarkerType.Landmark },
            { 60451, MarkerType.Landmark },
            { 60448, MarkerType.Landmark },
            { 60311, MarkerType.Landmark },
            
            // Entrance icons
            { 60456, MarkerType.Entrance },
            
            // Symbol icons
            { 60436, MarkerType.Symbol },
            { 60453, MarkerType.Symbol },
            { 60467, MarkerType.Symbol }
        };

        /// <summary>
        /// Infers the marker type based on its icon ID
        /// </summary>
        public static MarkerType InferMarkerTypeFromIconId(uint iconId)
        {
            // First check our explicit mapping
            if (IconToTypeMap.TryGetValue(iconId, out MarkerType type))
            {
                return type;
            }

            // If not found, try inferring from icon ID range
            if (iconId >= 60440 && iconId <= 60449)
                return MarkerType.Aetheryte;
            else if (iconId >= 60310 && iconId <= 60319)
                return MarkerType.Quest;
            else if (iconId >= 60410 && iconId <= 60439)
                return MarkerType.Shop;
            else if (iconId >= 60450 && iconId <= 60459)
                return MarkerType.Landmark;
            else if (iconId >= 60430 && iconId <= 60439)
                return MarkerType.Landmark;
            else
                return MarkerType.Generic;
        }

        /// <summary>
        /// Gets the fill color for a marker type
        /// </summary>
        public static System.Windows.Media.Color GetMarkerFillColor(MarkerType type)
        {
            return type switch
            {
                MarkerType.Aetheryte => Colors.Blue,
                MarkerType.Quest => Colors.Gold,
                MarkerType.Shop => Colors.Green,
                MarkerType.Landmark => Colors.Purple,
                MarkerType.Entrance => Colors.Brown,
                MarkerType.Symbol => Colors.Teal,
                MarkerType.Custom => Colors.Magenta,
                _ => Colors.Red, // Generic
            };
        }

        /// <summary>
        /// Gets the stroke color for a marker type
        /// </summary>
        public static System.Windows.Media.Color GetMarkerStrokeColor(MarkerType type)
        {
            return type switch
            {
                MarkerType.Aetheryte => Colors.LightBlue,
                MarkerType.Quest => Colors.Orange,
                MarkerType.Shop => Colors.LightGreen,
                MarkerType.Landmark => Colors.Violet,
                MarkerType.Entrance => Colors.SandyBrown,
                MarkerType.Symbol => Colors.Cyan,
                MarkerType.Custom => Colors.Pink,
                _ => Colors.Yellow, // Generic
            };
        }

        /// <summary>
        /// Returns the shape type for a marker type (for fallback rendering)
        /// </summary>
        public static string GetMarkerShapeType(MarkerType type)
        {
            return type switch
            {
                MarkerType.Aetheryte => "Ellipse",
                MarkerType.Quest => "Diamond",
                MarkerType.Shop => "Rectangle",
                MarkerType.Landmark => "Triangle",
                MarkerType.Entrance => "Ellipse",
                MarkerType.Symbol => "Ellipse",
                MarkerType.Custom => "Star",
                _ => "Ellipse", // Generic
            };
        }

        /// <summary>
        /// Gets the formatted icon path for the game's file system
        /// </summary>
        public static string GetIconPath(uint iconId)
        {
            if (iconId == 0)
                return string.Empty;

            // Format according to FFXIV's icon naming convention (060321 for ID 60321)
            string iconFolder = $"{iconId / 1000 * 1000:D6}";
            return $"ui/icon/{iconFolder}/{iconId:D6}.tex";
        }
    }
}