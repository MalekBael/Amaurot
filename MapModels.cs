using System;

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
}