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

    // NEW: Quest data models - Updated with correct SaintCoinach properties
    public class QuestInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string JournalGenre { get; set; } = string.Empty;
        public uint ClassJobCategoryId { get; set; }
        public uint ClassJobLevelRequired { get; set; }
        public string ClassJobCategoryName { get; set; } = string.Empty;
        public bool IsMainScenarioQuest { get; set; }
        public bool IsFeatureQuest { get; set; }
        public uint PreviousQuestId { get; set; }
        public uint ExpReward { get; set; }
        public uint GilReward { get; set; }

        public override string ToString()
        {
            string prefix = IsMainScenarioQuest ? "[MSQ] " : IsFeatureQuest ? "[FEATURE] " : "";
            return $"{Id} - {prefix}{Name}";
        }
    }

    // NEW: BNpc data models
    public class BNpcInfo
    {
        public uint BNpcNameId { get; set; }
        public string BNpcName { get; set; } = string.Empty;
        public uint BNpcBaseId { get; set; }
        public string Title { get; set; } = string.Empty;
        public uint TribeId { get; set; }
        public string TribeName { get; set; } = string.Empty;

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(Title))
                return $"{BNpcName} - {Title} (Base: {BNpcBaseId})";
            else
                return $"{BNpcName} (Base: {BNpcBaseId})";
        }
    }

    /// <summary>
    /// Helper class for marker-related functionality
    /// </summary>
    public static class MapMarkerHelper
    {
        /// <summary>
        /// Maps icon IDs to their PlaceName IDs from MapSymbol.csv
        /// </summary>
        private static Dictionary<uint, uint> IconToPlaceNameIdMap = new Dictionary<uint, uint>();

        /// <summary>
        /// Stores the PlaceName strings by ID for display purposes
        /// </summary>
        private static Dictionary<uint, string> PlaceNameIdToNameMap = new Dictionary<uint, string>();

        private static bool _isInitialized = false;

        /// <summary>
        /// Initializes the icon mappings from MapSymbol data
        /// </summary>
        public static void InitializeFromMapSymbolData(IEnumerable<MapSymbolRow> mapSymbols, Dictionary<uint, string> placeNames)
        {
            IconToPlaceNameIdMap.Clear();
            PlaceNameIdToNameMap = placeNames ?? new Dictionary<uint, string>();

            foreach (var symbol in mapSymbols)
            {
                if (symbol.IconId == 0) continue;

                // Store the icon -> PlaceNameId mapping
                IconToPlaceNameIdMap[symbol.IconId] = symbol.PlaceNameId;
            }

            _isInitialized = true;
        }

        /// <summary>
        /// Gets the PlaceName for a given icon ID
        /// </summary>
        public static string GetPlaceNameForIcon(uint iconId)
        {
            if (!_isInitialized)
                return string.Empty;

            if (IconToPlaceNameIdMap.TryGetValue(iconId, out uint placeNameId) &&
                PlaceNameIdToNameMap.TryGetValue(placeNameId, out string placeName))
            {
                return placeName;
            }

            return string.Empty;
        }

        /// <summary>
        /// Updates a MapMarker with its proper PlaceName from the icon mapping
        /// </summary>
        public static void UpdateMarkerPlaceName(MapMarker marker)
        {
            if (!_isInitialized || marker.IconId == 0)
                return;

            // If the marker already has a PlaceNameId from MapMarker.csv, use that
            if (marker.PlaceNameId > 0 && PlaceNameIdToNameMap.TryGetValue(marker.PlaceNameId, out string existingName))
            {
                marker.PlaceName = existingName;
            }
            // Otherwise, try to get it from the icon mapping
            else if (IconToPlaceNameIdMap.TryGetValue(marker.IconId, out uint placeNameId) &&
                     PlaceNameIdToNameMap.TryGetValue(placeNameId, out string placeName))
            {
                marker.PlaceNameId = placeNameId;
                marker.PlaceName = placeName;
            }
        }

        /// <summary>
        /// Infers the marker type based on its icon ID
        /// For backwards compatibility - prefer using the actual PlaceName data
        /// </summary>
        public static MarkerType InferMarkerTypeFromIconId(uint iconId)
        {
            // This method now just returns Generic - let the UI decide how to categorize
            // based on the actual PlaceName or let users categorize manually
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

    /// <summary>
    /// Represents a row from MapSymbol.csv
    /// </summary>
    public class MapSymbolRow
    {
        public uint RowId { get; set; }
        public uint IconId { get; set; }
        public uint PlaceNameId { get; set; }
        public bool DisplayNavi { get; set; }
    }
}