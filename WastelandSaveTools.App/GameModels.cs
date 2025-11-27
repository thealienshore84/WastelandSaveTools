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

    public class RawAttributes
    {
        public string Coordination { get; set; } = "";
        public string Awareness { get; set; } = "";
        public string Strength { get; set; } = "";
        public string Speed { get; set; } = "";
        public string Intelligence { get; set; } = "";
        public string Charisma { get; set; } = "";
        public string Luck { get; set; } = "";
    }

    public class RawSkillRecord
    {
        public string Id { get; set; } = "";
        public string Level { get; set; } = "";
    }

    public class RawCharacterRecord
    {
        public int Id
        {
            get; set;
        }
        public string? Name
        {
            get; set;
        }
        public bool IsCompanion
        {
            get; set;
        }
        public int Level
        {
            get; set;
        }
        public int XP
        {
            get; set;
        }

        public RawAttributes Attributes { get; set; } = new();
        public List<RawSkillRecord> Skills { get; set; } = new();
        public List<string> Perks { get; set; } = new();
        public List<string> Quirks { get; set; } = new();
        public string? Background
        {
            get; set;
        }
        public List<string> Abilities { get; set; } = new();
    }

    public class RawFollowerRecord
    {
        public int FollowerId
        {
            get; set;
        }
        public string? Name
        {
            get; set;
        }
        public string? OwnerName
        {
            get; set;
        }
        public bool IsAnimal
        {
            get; set;
        }
    }

    public class RawItemRecord
    {
        public string Id { get; set; } = "";
        public string Template { get; set; } = "";
        public string Name { get; set; } = "";
        public string Quantity { get; set; } = "";
        public string? OwnerName
        {
            get; set;
        }
        public string? Context
        {
            get; set;
        }
    }

    public class RawContainerRecord
    {
        public string Id { get; set; } = "";
        public string? Name
        {
            get; set;
        }
        public string? Context
        {
            get; set;
        }
    }

    public class RawSaveState
    {
        public RawSummary Summary { get; set; } = new();
        public RawXmlSections Xml { get; set; } = new();

        public List<RawCharacterRecord> Characters { get; set; } = new();
        public List<RawFollowerRecord> Followers { get; set; } = new();
        public List<RawItemRecord> Items { get; set; } = new();
        public List<RawContainerRecord> Containers { get; set; } = new();

        /// <summary>
        /// Full XML text of the save. Used as a fallback when specific sections
        /// are missing from RawXmlSections.
        /// </summary>
        public string RawXml { get; set; } = "";
    }

    // -----------------------------
    // PARSED MODELS (lightly typed)
    // -----------------------------

    public class ParsedAttributes
    {
        public int Coordination
        {
            get; set;
        }
        public int Awareness
        {
            get; set;
        }
        public int Strength
        {
            get; set;
        }
        public int Speed
        {
            get; set;
        }
        public int Intelligence
        {
            get; set;
        }
        public int Charisma
        {
            get; set;
        }
        public int Luck
        {
            get; set;
        }
    }

    /// <summary>
    /// Lightweight representation of a single skill on a character.
    /// </summary>
    public class ParsedSkill
    {
        /// <summary>
        /// Skill ID from the save (e.g. "Skill_WeirdScience").
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Skill level as an integer.
        /// </summary>
        public int Level
        {
            get; set;
        }
    }

    public class ParsedCharacter
    {
        public string Name { get; set; } = "";
        public bool IsCompanion
        {
            get; set;
        }
        public bool IsCustomName
        {
            get; set;
        }

        public int Level
        {
            get; set;
        }
        public int XP
        {
            get; set;
        }

        public ParsedAttributes Attributes { get; set; } = new();
        public List<ParsedSkill> Skills { get; set; } = new();
        public List<string> Perks { get; set; } = new();
        public List<string> Quirks { get; set; } = new();
        public string Background { get; set; } = "";
        public List<string> Abilities { get; set; } = new();
    }

    public class ParsedFollower
    {
        public int FollowerId
        {
            get; set;
        }
        public string Name { get; set; } = "";
        public string? OwnerName
        {
            get; set;
        }
        public bool IsAnimal
        {
            get; set;
        }

        /// <summary>
        /// Name of the PC this follower is attached to, e.g. "Astra".
        /// Populated from followerdata.pcName and animalCompanions.ownerName.
        /// </summary>
        public string AssignedTo { get; set; } = "";

        /// <summary>
        /// High-level type such as "Animal" or "Unknown".
        /// Populated when parsing animal companions.
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Whether this follower is currently active in the party.
        /// </summary>
        public bool IsActive
        {
            get; set;
        }
    }

    public class ParsedItemRecord
    {
        public string Id { get; set; } = "";
        public string Template { get; set; } = "";
        public string Name { get; set; } = "";
        public int Quantity
        {
            get; set;
        }

        /// <summary>
        /// Legacy owner name from older parsing logic (not used by the normalizer).
        /// </summary>
        public string? OwnerName
        {
            get; set;
        }

        public string? Context
        {
            get; set;
        }

        /// <summary>
        /// Normalized owner name used by the normalizer (PC name or container name).
        /// Set by StateParser.BuildItemRecord.
        /// </summary>
        public string Owner { get; set; } = "";

        /// <summary>
        /// Flat key/value map of all child elements of the item element,
        /// including an "Owner" key when available.
        /// </summary>
        public Dictionary<string, string>? Data
        {
            get; set;
        }
    }

    public class ParsedContainerRecord
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Context
        {
            get; set;
        }

        /// <summary>
        /// Container type from the XML (or inferred from context).
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Flat key/value map of container attributes and children.
        /// </summary>
        public Dictionary<string, string>? Data
        {
            get; set;
        }
    }

    public class ParsedSummary
    {
        public string Version { get; set; } = "";
        public string Scene { get; set; } = "";
        public string SaveTime { get; set; } = "";
        public int GameplaySeconds
        {
            get; set;
        }
        public string Difficulty { get; set; } = "";
        public int Money
        {
            get; set;
        }
    }

    /// <summary>
    /// Single global flag entry from the &lt;globals&gt; section.
    /// </summary>
    public class GlobalFlag
    {
        /// <summary>
        /// Key from the save, e.g. "HQ.MajorTom.Recruited".
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// Raw string value from the save.
        /// </summary>
        public string Value { get; set; } = "";
    }

    public class ParsedSaveState
    {
        public ParsedSummary Summary { get; set; } = new ParsedSummary();
        public List<ParsedCharacter> Characters { get; set; } = new();
        public List<ParsedFollower> Followers { get; set; } = new();

        // Items and containers pulled out of the save.
        public List<ParsedItemRecord> Items { get; set; } = new();
        public List<ParsedContainerRecord> Containers { get; set; } = new();

        /// <summary>
        /// Flattened list of globals from the &lt;globals&gt; section.
        /// </summary>
        public List<GlobalFlag> Globals { get; set; } = new();

        // Computed in StateParser.Parse: distinct names in the party.
        public List<string> Party { get; set; } = new();
    }

    // -----------------------------
    // Export issues (Phase 3 #1)
    // -----------------------------

    public enum ExportIssueSeverity
    {
        Warning,
        Error
    }

    public class ExportIssue
    {
        public ExportIssueSeverity Severity
        {
            get; set;
        }

        /// <summary>
        /// Component or stage where this issue occurred,
        /// for example "StateExtractor", "StateParser.ParseCharacters".
        /// </summary>
        public string Component { get; set; } = "";

        /// <summary>
        /// Optional contextual tag, for example "pcs", "followers",
        /// "globals", "inventory", or "save".
        /// </summary>
        public string Context { get; set; } = "";

        /// <summary>
        /// Human readable description of the problem.
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// Line number in the XML, if available.
        /// </summary>
        public int? LineNumber
        {
            get; set;
        }

        /// <summary>
        /// Line position in the XML, if available.
        /// </summary>
        public int? LinePosition
        {
            get; set;
        }
    }
}
