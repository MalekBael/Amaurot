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

        public void DetermineType()
        {
            if (Type != MarkerType.Generic && Type != MarkerType.Custom)
            {
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
        Npc,
        BattleNpc,
        InstancedContent,
        GatheringPoint,
        FishingSpot,
        Shop,
        Landmark,
        Entrance,
        Symbol,
        Custom,
        Fate
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

    public class QuestInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        
        public string QuestIdString { get; set; } = string.Empty;
        
        public string JournalGenre { get; set; } = string.Empty;
        public uint ClassJobCategoryId { get; set; }
        public uint ClassJobLevelRequired { get; set; }
        public string ClassJobCategoryName { get; set; } = string.Empty;
        public bool IsMainScenarioQuest { get; set; }
        public bool IsFeatureQuest { get; set; }
        public uint PreviousQuestId { get; set; }
        public uint ExpReward { get; set; }
        public uint GilReward { get; set; }
        
        public string PlaceName { get; set; } = string.Empty;
        public uint PlaceNameId { get; set; }
        public uint MapId { get; set; }
        public uint IconId { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsRepeatable { get; set; }
        

        public double X { get; set; } = 0;
        public double Y { get; set; } = 0;
        public double Z { get; set; } = 0;
        public uint TerritoryId { get; set; } = 0;

        public List<QuestNpcInfo> StartNpcs { get; set; } = new List<QuestNpcInfo>();
        public List<QuestNpcInfo> ObjectiveNpcs { get; set; } = new List<QuestNpcInfo>();
        public List<QuestNpcInfo> EndNpcs { get; set; } = new List<QuestNpcInfo>();

        public override string ToString()
        {
            string prefix = IsMainScenarioQuest ? "[MSQ] " : IsFeatureQuest ? "[FEATURE] " : "";
            return $"{Id} - {prefix}{Name}";
        }
    }

    public class QuestNpcInfo
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
        public string Role { get; set; } = string.Empty; 

        public override string ToString()
        {
            return $"{NpcName} ({Role}) - {TerritoryName} ({MapX:F1}, {MapY:F1})";
        }
    }

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

    public class EventInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint EventId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public uint TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{EventId} - {Name}";
        }
    }

    public class FateInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint FateId { get; set; }
        public string Description { get; set; } = string.Empty;
        public uint Level { get; set; }
        public uint ClassJobLevel { get; set; }
        public uint TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;
        public uint IconId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public override string ToString()
        {
            return $"{FateId} - {Name} (Lv.{Level})";
        }
    }

    public static class MapMarkerHelper
    {
        private static Dictionary<uint, uint> IconToPlaceNameIdMap = new Dictionary<uint, uint>();

        private static Dictionary<uint, string> PlaceNameIdToNameMap = new Dictionary<uint, string>();

        private static bool _isInitialized = false;

        public static void InitializeFromMapSymbolData(IEnumerable<MapSymbolRow> mapSymbols, Dictionary<uint, string>? placeNames)
        {
            IconToPlaceNameIdMap.Clear();
            PlaceNameIdToNameMap = placeNames ?? new Dictionary<uint, string>();

            foreach (var symbol in mapSymbols)
            {
                if (symbol.IconId == 0) continue;

                IconToPlaceNameIdMap[symbol.IconId] = symbol.PlaceNameId;
            }

            _isInitialized = true;
        }

        public static string GetPlaceNameForIcon(uint iconId)
        {
            if (!_isInitialized)
                return string.Empty;

            if (IconToPlaceNameIdMap.TryGetValue(iconId, out uint placeNameId) &&
                PlaceNameIdToNameMap.TryGetValue(placeNameId, out string? placeName))
            {
                return placeName ?? string.Empty;
            }

            return string.Empty;
        }

        public static void UpdateMarkerPlaceName(MapMarker marker)
        {
            if (!_isInitialized || marker.IconId == 0)
                return;

            if (marker.PlaceNameId > 0 && PlaceNameIdToNameMap.TryGetValue(marker.PlaceNameId, out string? existingName))
            {
                marker.PlaceName = existingName ?? string.Empty;
            }

            else if (IconToPlaceNameIdMap.TryGetValue(marker.IconId, out uint placeNameId) &&
                     PlaceNameIdToNameMap.TryGetValue(placeNameId, out string? placeName))
            {
                marker.PlaceNameId = placeNameId;
                marker.PlaceName = placeName ?? string.Empty;
            }
        }


        public static MarkerType InferMarkerTypeFromIconId(uint iconId)
        {
            if (iconId == 60722 || iconId == 60502 || iconId == 60503 || iconId == 60504 || iconId == 60505)
                return MarkerType.Fate;

            return MarkerType.Generic;
        }

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
                MarkerType.Fate => Colors.Orange, 
                _ => Colors.Red,
            };
        }

        public static System.Windows.Media.Color GetMarkerStrokeColor(MarkerType type)
        {
            return type switch
            {
                MarkerType.Aetheryte => Colors.LightBlue,
                MarkerType.Quest => Colors.DarkGoldenrod, 
                MarkerType.Shop => Colors.LightGreen,
                MarkerType.Landmark => Colors.Violet,
                MarkerType.Entrance => Colors.SandyBrown,
                MarkerType.Symbol => Colors.Cyan,
                MarkerType.Custom => Colors.Pink,
                MarkerType.Fate => Colors.DarkOrange,
                _ => Colors.Yellow,
            };
        }

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
                MarkerType.Fate => "Diamond", 
                _ => "Ellipse",
            };
        }

        public static string GetIconPath(uint iconId)
        {
            if (iconId == 0)
                return string.Empty;

            string iconFolder = $"{iconId / 1000 * 1000:D6}";
            return $"ui/icon/{iconFolder}/{iconId:D6}.tex";
        }
    }

    public class MapSymbolRow
    {
        public uint RowId { get; set; }
        public uint IconId { get; set; }
        public uint PlaceNameId { get; set; }
        public bool DisplayNavi { get; set; }
    }
}