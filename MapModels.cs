using System.Collections.Generic;

namespace map_editor
{
    // Map marker data structure
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

    // Coordinate conversion data structure
    public class MapCoordinate
    {
        public double MapX { get; set; }
        public double MapY { get; set; }
        public double ClientX { get; set; }
        public double ClientY { get; set; }
        public double ClientZ { get; set; }
    }

    // Map information extended from the CSV data
    public class MapInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint TerritoryType { get; set; }
        public double SizeFactor { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public uint PlaceNameId { get; set; }
        public string PlaceName { get; set; } = string.Empty;
        public List<MapMarker> Markers { get; set; } = new();
    }

    // Types of map markers
    public enum MarkerType
    {
        Generic,
        Aetheryte,
        Quest,
        Shop,
        Landmark,
        Entrance,
        Custom,
        Symbol // Add this new value for map symbols
    }

    // Click event data
    public class MapClickEventArgs
    {
        public MapCoordinate Coordinate { get; set; } = new();
        public System.Windows.Point CanvasPosition { get; set; }
        public System.Windows.Input.MouseButtonEventArgs OriginalEventArgs { get; set; } = null!;
    }
}