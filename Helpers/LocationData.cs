namespace Amaurot.Services.Entities
{
    public class LocationData
    {
        public uint Id { get; set; }
        public uint TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;
        public uint MapId { get; set; }
        public double MapX { get; set; }
        public double MapY { get; set; }
        public double MapZ { get; set; }
        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public float WorldZ { get; set; }
    }
}