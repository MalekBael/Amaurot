using System.Collections.Generic;

namespace Amaurot.Services.Entities
{
    public abstract class EntityInfoBase
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public uint MapId { get; set; }
        public double MapX { get; set; }
        public double MapY { get; set; }
        public double MapZ { get; set; }

        public abstract string DisplayName { get; }

        public override string ToString() => DisplayName;
    }

    public class TerritoryInfo : EntityInfoBase
    {
        public string TerritoryNameId { get; set; } = string.Empty;
        public uint PlaceNameId { get; set; }
        public string PlaceName { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public uint PlaceNameIdTerr { get; set; }
        public uint RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;

        public override string DisplayName => !string.IsNullOrEmpty(PlaceName) && !PlaceName.StartsWith("[Territory ID:")
            ? $"{Id} - {PlaceName}"
            : $"{Id} - Territory {Id}";
    }

    public class QuestInfo : EntityInfoBase
    {
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
        public uint IconId { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsRepeatable { get; set; }
        public uint TerritoryId { get; set; }
        public List<QuestNpcInfo> StartNpcs { get; set; } = new();
        public List<QuestNpcInfo> ObjectiveNpcs { get; set; } = new();
        public List<QuestNpcInfo> EndNpcs { get; set; } = new();

        public override string DisplayName => Name;
    }

    public class NpcInfo : EntityInfoBase
    {
        public uint NpcId => Id;
        public string NpcName { get; set; } = string.Empty;
        public uint TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;
        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public float WorldZ { get; set; }
        public int QuestCount { get; set; }
        public List<NpcQuestInfo> Quests { get; set; } = new();

        public override string DisplayName => $"{NpcName} ({QuestCount} quest{(QuestCount != 1 ? "s" : "")})";
    }

    public class BNpcInfo : EntityInfoBase
    {
        public string BNpcName { get; set; } = string.Empty;
        public uint BNpcBaseId { get; set; }
        public uint BNpcNameId { get; set; }
        public string TribeName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public uint TribeId { get; set; }
        public uint TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;

        public override string DisplayName => BNpcName;
    }

    public class FateInfo : EntityInfoBase
    {
        public uint FateId { get; set; }
        public string Description { get; set; } = string.Empty;
        public uint Level { get; set; }
        public uint ClassJobLevel { get; set; }
        public uint TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;
        public uint IconId { get; set; }

        // Add the X, Y, Z properties that DataLoaderService expects
        public double X { get; set; }

        public double Y { get; set; }
        public double Z { get; set; }

        public override string DisplayName => Name;
    }

    public class EventInfo : EntityInfoBase
    {
        public uint EventId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public uint TerritoryId { get; set; }
        public string TerritoryName { get; set; } = string.Empty;

        public override string DisplayName => Name;
    }

    // Supporting classes
    public class NpcQuestInfo
    {
        public uint QuestId { get; set; }
        public string QuestName { get; set; } = string.Empty;
        public uint MapId { get; set; }
        public uint TerritoryId { get; set; }
        public double MapX { get; set; }
        public double MapY { get; set; }
        public double MapZ { get; set; }
        public string JournalGenre { get; set; } = string.Empty;
        public uint LevelRequired { get; set; }
        public bool IsMainScenario { get; set; }
        public bool IsFeatureQuest { get; set; }
        public uint ExpReward { get; set; }
        public uint GilReward { get; set; }
        public uint PlaceNameId { get; set; }
        public string PlaceName { get; set; } = string.Empty;

        public override string ToString() => $"{QuestName} (ID: {QuestId})";
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

        // Add the missing properties that DataLoaderService expects
        public float WorldX { get; set; }

        public float WorldY { get; set; }
        public float WorldZ { get; set; }
        public string Role { get; set; } = string.Empty;

        public override string ToString() => $"{NpcName} (ID: {NpcId})";
    }
}