using System.Collections.Generic;

namespace WastelandSaveTools.App
{
    // -----------------------------
    // RAW MODELS (direct XML parse)
    // -----------------------------

    public class RawSummary
    {
        public string Version { get; set; } = "";
        public string Scene { get; set; } = "";
        public string SaveTime { get; set; } = "";
        public string GameplayTime { get; set; } = "";
        public string Difficulty { get; set; } = "";
        public string Money { get; set; } = "";
    }

    public class RawXmlSections
    {
        public string Levels { get; set; } = "";
        public string Globals { get; set; } = "";
        public string Quests { get; set; } = "";
        public string Reputation { get; set; } = "";
        public string Pcs { get; set; } = "";
        public string Followers { get; set; } = "";
        public string Inventory { get; set; } = "";
        public string Vehicle { get; set; } = "";
    }

    public class RawSaveState
    {
        public RawSummary Summary { get; set; } = new RawSummary();

        // Extracted raw block content (already trimmed by StateExtractor)
        public RawXmlSections Xml { get; set; } = new RawXmlSections();

        // The full <save>...</save> XML
        public string RawXml { get; set; } = "";
    }

    // ------------------------------------
    // PARSED MODELS (structured objects)
    // ------------------------------------

    public class ParsedAttributes
    {
        public int Coordination { get; set; }
        public int Awareness { get; set; }
        public int Strength { get; set; }
        public int Speed { get; set; }
        public int Intelligence { get; set; }
        public int Charisma { get; set; }
        public int Luck { get; set; }
    }

    public class ParsedSkill
    {
        public string Id { get; set; } = "";
        public int Level { get; set; }
    }

    public class ParsedCharacter
    {
        public string Name { get; set; } = "";
        public bool IsCompanion { get; set; }
        public bool IsCustomName { get; set; }
        public int Level { get; set; }
        public int XP { get; set; }
        public ParsedAttributes Attributes { get; set; } = new();
        public List<ParsedSkill> Skills { get; set; } = new();
        public List<string> Abilities { get; set; } = new();
    }

    public class GlobalFlag
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class ParsedFollower
    {
        public int FollowerId { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
        public string Type { get; set; } = "";
        public string AssignedTo { get; set; } = "";
    }

    public class ParsedItemRecord
    {
        public string Id { get; set; } = "";
        public string Template { get; set; } = "";
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public string Owner { get; set; } = "";
        public string Context { get; set; } = "";
        public Dictionary<string, string> Data { get; set; } = new();
    }

    public class ParsedContainerRecord
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public Dictionary<string, string> Data { get; set; } = new();
    }

    public class ParsedVehicleState
    {
        public string Name { get; set; } = "";
        public int Armor { get; set; }
        public int HP { get; set; }
        public int MaxHP { get; set; }
        public int Initiative { get; set; }
        public Dictionary<string, string> Data { get; set; } = new();
    }

    public class ParsedSaveState
    {
        public ParsedSummary Summary { get; set; } = new ParsedSummary();
        public List<ParsedCharacter> Characters { get; set; } = new();
        public List<string> Party { get; set; } = new();
        public List<GlobalFlag> Globals { get; set; } = new();
        public List<ParsedFollower> Followers { get; set; } = new();

        // Phase 1 additions:
        public List<ParsedItemRecord> Items { get; set; } = new();
        public List<ParsedContainerRecord> Containers { get; set; } = new();
    }

    public class ParsedSummary
    {
        public string Version { get; set; } = "";
        public string Scene { get; set; } = "";
        public string SaveTime { get; set; } = "";
        public int GameplaySeconds { get; set; }
        public string Difficulty { get; set; } = "";
        public int Money { get; set; }
    }
}
