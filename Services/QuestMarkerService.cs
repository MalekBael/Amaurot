using SaintCoinach;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace map_editor.Services
{
    public class QuestMarkerService
    {
        private readonly ARealmReversed? _realm;
        private readonly Action<string> _logDebug;
        private readonly QuestLocationService _questLocationService;

        public QuestMarkerService(ARealmReversed? realm, Action<string> logDebug)
        {
            _realm = realm;
            _logDebug = logDebug ?? (msg => { });
            _questLocationService = new QuestLocationService(realm, logDebug);
        }

        /// <summary>
        /// Extract ALL quest markers using Libra Eorzea database
        /// </summary>
        public async Task<List<MapMarker>> ExtractAllQuestMarkersAsync()
        {
            var questMarkers = new List<MapMarker>();

            if (_realm?.GameData == null)
            {
                _logDebug("Realm is null, cannot extract quest markers");
                return questMarkers;
            }

            try
            {
                _logDebug("🎯 QUEST MARKER EXTRACTION: Using Libra Eorzea database approach...");


                var questLocationData = await _questLocationService.ExtractQuestLocationsAsync();


                foreach (var kvp in questLocationData)
                {
                    var locationData = kvp.Value;
                    
                    var questMarker = new MapMarker
                    {
                        Id = 2000000 + locationData.QuestId,
                        MapId = locationData.MapId,
                        PlaceNameId = 0,
                        PlaceName = $"Quest {locationData.QuestId} ({locationData.ObjectName})",
                        X = locationData.MapX,
                        Y = locationData.MapY,
                        Z = locationData.MapZ,
                        IconId = 61411,
                        IconPath = "ui/icon/061000/061411.tex",
                        Type = MarkerType.Quest,
                        IsVisible = true
                    };

                    questMarkers.Add(questMarker);
                }

                _logDebug($"🎯 QUEST MARKER EXTRACTION: Complete! Created {questMarkers.Count} quest markers using Libra Eorzea");
            }
            catch (Exception ex)
            {
                _logDebug($"❌ Error in quest marker extraction: {ex.Message}");
            }

            return questMarkers;
        }
    }
}