using SaintCoinach;
using SaintCoinach.Xiv;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Amaurot.Services
{
    public class QuestMarkerService
    {
        private readonly ARealmReversed? _realm;
        private readonly QuestLocationService _questLocationService;

        public QuestMarkerService(ARealmReversed? realm)
        {
            _realm = realm;
            _questLocationService = new QuestLocationService(realm);
        }

        /// <summary>
        /// Extract ALL quest markers using Libra Eorzea database
        /// </summary>
        public async Task<List<MapMarker>> ExtractAllQuestMarkersAsync()
        {
            var questMarkers = new List<MapMarker>();

            if (_realm?.GameData == null)
            {
                DebugModeManager.LogError("Realm is null, cannot extract quest markers");
                return questMarkers;
            }

            try
            {
                DebugModeManager.LogDebug("QUEST MARKER EXTRACTION: Using Libra Eorzea database approach...");

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

                DebugModeManager.LogMarkerCreation("Quest", questMarkers.Count);
            }
            catch (Exception ex)
            {
                DebugModeManager.LogError($"Error in quest marker extraction: {ex.Message}");
            }

            return questMarkers;
        }
    }
}