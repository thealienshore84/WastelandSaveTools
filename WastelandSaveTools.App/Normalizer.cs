using System;
using System.Collections.Generic;
using System.Linq;

namespace WastelandSaveTools.App
{
    public class NormalizedSaveState
    {
        public string GeneratedBy { get; set; } = "";
        public ParsedSummary Summary { get; set; } = new ParsedSummary();
        public List<string> Party { get; set; } = new();

        /// <summary>
        /// Characters in the party plus any other parsed PCs / companions.
        /// </summary>
        public List<NormalizedCharacter> Characters { get; set; } = new();

        /// <summary>
        /// Followers / tag-alongs such as animal companions. These come from
        /// the <followers> and <animalCompanions> blocks.
        /// </summary>
        public List<NormalizedFollower> Followers { get; set; } = new();

        /// <summary>
        /// High-level named followers inferred from global flags
        /// (e.g. "Major Tomcat", "Two-Headed Goat").
        /// </summary>
        public List<NamedFollower> NamedFollowers { get; set; } = new();

        /// <summary>
        /// Normalized inventory items.
        /// </summary>
        public List<NormalizedItem> Items { get; set; } = new();

        /// <summary>
        /// Normalized containers (stash, Kodiak, world containers, etc.).
        /// </summary>
        public List<NormalizedContainer> Containers { get; set; } = new();

        /// <summary>
        /// Nested globals built from dotted keys (e.g. "a1001.Prisoner.State").
        /// Good for hierarchical inspection.
        /// </summary>
        public Dictionary<string, object> Globals { get; set; } = new();

        /// <summary>
        /// Flat key -> value view of all globals, exactly as they appear in the save.
        /// Good for quick lookups and diffing between saves.
        /// </summary>
        public Dictionary<string, string> FlatGlobals { get; set; } = new();
    }

    public class NormalizedCharacter
    {
        public string Name { get; set; } = "";
        public bool IsCompanion { get; set; }
        public bool IsCustomName { get; set; }
        public int Level { get; set; }
        public int XP { get; set; }
        public ParsedAttributes Attributes { get; set; } = new ParsedAttributes();

        /// <summary>
        /// Skill name -> level, using friendlier names where known
        /// (e.g. "sniperRifles", "mechanics", "sneakyShit").
        /// </summary>
        public Dictionary<string, int> Skills { get; set; } = new();

        /// <summary>
        /// Raw ability template names from the save.
        /// </summary>
        public List<string> Abilities { get; set; } = new();
    }

    public class NormalizedFollower
    {
        /// <summary>Numeric follower ID from the save (e.g. 37).</summary>
        public int FollowerId { get; set; }

        /// <summary>
        /// Low-level name inferred from the animalCompanions block, e.g.
        /// "AnimalCompanion_1". Kept primarily for debugging.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Name of the PC this follower is attached to, e.g. "Astra".
        /// </summary>
        public string AssignedTo { get; set; } = "";

        /// <summary>
        /// High-level type such as "Animal" or "Unknown".
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Whether this follower is currently active in the party.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Candidate high-level names derived from NamedFollowers of the same
        /// category. For example, ["Major Tomcat","Two-Headed Goat"].
        /// This makes the ambiguity explicit instead of pretending we know.
        /// </summary>
        public List<string> NameCandidates { get; set; } = new();
    }

    public class NamedFollower
    {
        /// <summary>
        /// Stable identifier for the follower, e.g. "MajorTom" or "TwoHeadedGoat".
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// Human-friendly display name, e.g. "Major Tomcat".
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Category such as "AnimalCompanion".
        /// </summary>
        public string Category { get; set; } = "";

        /// <summary>
        /// Whether this follower is currently travelling with the party
        /// according to story/global state.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// The raw global flags that caused this follower to be inferred.
        /// </summary>
        public List<string> SourceFlags { get; set; } = new();
    }

    /// <summary>
    /// Normalized, schema-agnostic view of a single inventory item.
    /// </summary>
    public class NormalizedItem
    {
        public string Id { get; set; } = "";
        public string Template { get; set; } = "";
        public string Name { get; set; } = "";
        public int Quantity { get; set; }

        /// <summary>
        /// Owner name if known - typically a PC name or container name.
        /// </summary>
        public string Owner { get; set; } = "";

        /// <summary>
        /// High-level context string (e.g. "pc:Astra:equipment", "container:Shared Stash").
        /// </summary>
        public string Context { get; set; } = "";

        /// <summary>
        /// Bag of extra attributes flattened from the underlying XML.
        /// </summary>
        public Dictionary<string, string> Data { get; set; } = new();
    }

    /// <summary>
    /// Normalized view of a container record (shared stash, Kodiak storage, world chest, etc.).
    /// </summary>
    public class NormalizedContainer
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";

        /// <summary>
        /// Bag of extra attributes flattened from the underlying XML.
        /// </summary>
        public Dictionary<string, string> Data { get; set; } = new();
    }

    internal static class Normalizer
    {
        private static readonly Dictionary<string, string> SkillIdToName =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Combat
                ["10"] = "automaticWeapons",
                ["20"] = "meleeCombat",
                ["30"] = "bigGuns",
                ["40"] = "brawling",
                ["60"] = "smallArms",
                ["80"] = "sniperRifles",
                ["570"] = "combatShooting",

                // General / exploration / social
                ["210"] = "animalWhisperer",
                ["220"] = "barter",
                ["230"] = "nerdStuff",
                ["240"] = "explosives",
                ["250"] = "firstAid",
                ["260"] = "leadership",
                ["270"] = "lockpicking",
                ["280"] = "mechanics",
                ["290"] = "survival",
                ["310"] = "toasterRepair",
                ["320"] = "weaponModding",
                ["330"] = "weirdScience",
                ["400"] = "hardAss",
                ["410"] = "kissAss",
                ["550"] = "sneakyShit",
                ["560"] = "armorModding"
            };

        /// <summary>
        /// Map specific global follower status flags to high-level descriptors.
        /// The save files never actually store the literal text "Major Tomcat",
        /// so we inject canonical display names here based on well-known keys.
        /// </summary>
        private static readonly Dictionary<string, (string Id, string Name, string Category)> FollowerGlobalsMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["G_FollowerStatus_MajorTom"] = ("MajorTom", "Major Tomcat", "AnimalCompanion"),
                ["G_FollowerStatus_TwoHeadedGoat"] = ("TwoHeadedGoat", "Two-Headed Goat", "AnimalCompanion")
            };

        public static NormalizedSaveState Normalize(
            RawSaveState raw,
            ParsedSaveState parsed,
            string toolVersion)
        {
            var normalized = new NormalizedSaveState
            {
                GeneratedBy = toolVersion,
                Summary = parsed.Summary,
                Party = parsed.Party
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            // Characters
            foreach (var ch in parsed.Characters)
            {
                var nChar = new NormalizedCharacter
                {
                    Name = ch.Name,
                    IsCompanion = ch.IsCompanion,
                    IsCustomName = ch.IsCustomName,
                    Level = ch.Level,
                    XP = ch.XP,
                    Attributes = ch.Attributes ?? new ParsedAttributes()
                };

                foreach (var sk in ch.Skills)
                {
                    if (string.IsNullOrWhiteSpace(sk.Id))
                        continue;

                    string key = ResolveSkillName(sk.Id);

                    if (!nChar.Skills.TryAdd(key, sk.Level))
                    {
                        // If somehow duplicate, keep the highest level.
                        nChar.Skills[key] = Math.Max(nChar.Skills[key], sk.Level);
                    }
                }

                nChar.Abilities.AddRange(ch.Abilities);
                normalized.Characters.Add(nChar);
            }

            // Build flat globals first so we can use them for named followers.
            var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var flag in parsed.Globals)
            {
                if (string.IsNullOrWhiteSpace(flag.Key))
                    continue;

                flat[flag.Key] = flag.Value ?? "";
            }

            normalized.FlatGlobals = flat;
            normalized.Globals = BuildNestedGlobals(parsed.Globals);

            // High-level named followers from globals (Major Tomcat, Two-Headed Goat, etc.)
            normalized.NamedFollowers = BuildNamedFollowersFromGlobals(flat);

            // Followers - low-level follower records from ParsedSaveState.
            normalized.Followers = parsed.Followers
                .Select(f => new NormalizedFollower
                {
                    FollowerId = f.FollowerId,
                    Name = f.Name,
                    AssignedTo = f.AssignedTo,
                    Type = string.IsNullOrWhiteSpace(f.Type) ? "Unknown" : f.Type,
                    IsActive = f.IsActive
                })
                .ToList();

            // Attach name candidates to each follower, to make ambiguity explicit.
            AttachNameCandidates(normalized);

            // Inventory - flatten generic ParsedItemRecord entries.
            normalized.Items = parsed.Items
                .Select(i => new NormalizedItem
                {
                    Id = i.Id,
                    Template = i.Template,
                    Name = i.Name,
                    Quantity = i.Quantity,
                    Owner = i.Owner ?? string.Empty,
                    Context = i.Context ?? string.Empty,
                    Data = new Dictionary<string, string>(
                        i.Data ?? new Dictionary<string, string>(),
                        StringComparer.OrdinalIgnoreCase)
                })
                .ToList();

            // Containers - flattened generic ParsedContainerRecord entries.
            normalized.Containers = parsed.Containers
                .Select(c => new NormalizedContainer
                {
                    Id = c.Id,
                    Name = c.Name,
                    Type = string.IsNullOrWhiteSpace(c.Type) ? "Unknown" : c.Type,
                    Data = new Dictionary<string, string>(
                        c.Data ?? new Dictionary<string, string>(),
                        StringComparer.OrdinalIgnoreCase)
                })
                .ToList();

            // Extend Party with any active named followers (animal companions, etc.).
            var activeFollowerNames = normalized.NamedFollowers
                .Where(nf => nf.IsActive)
                .Select(nf => nf.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n));

            normalized.Party = normalized.Party
                .Concat(activeFollowerNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return normalized;
        }

        private static string ResolveSkillName(string id)
        {
            if (SkillIdToName.TryGetValue(id, out var name))
                return name;

            return "skill_" + id;
        }

        private static List<NamedFollower> BuildNamedFollowersFromGlobals(
            IReadOnlyDictionary<string, string> flatGlobals)
        {
            var result = new List<NamedFollower>();

            foreach (var kvp in FollowerGlobalsMap)
            {
                if (!flatGlobals.TryGetValue(kvp.Key, out var value))
                    continue;

                // In the saves you've sent so far, "1" means "active follower".
                if (!string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
                    continue;

                var desc = kvp.Value;

                result.Add(new NamedFollower
                {
                    Id = desc.Id,
                    Name = desc.Name,
                    Category = desc.Category,
                    IsActive = true,
                    SourceFlags = new List<string> { kvp.Key }
                });
            }

            return result;
        }

        /// <summary>
        /// For each low-level follower, attach the list of *possible* high-level names
        /// based on the active NamedFollowers of the same category.
        /// This is intentionally explicit about ambiguity.
        /// </summary>
        private static void AttachNameCandidates(NormalizedSaveState state)
        {
            // Active named animal companions.
            var activeAnimals = state.NamedFollowers
                .Where(nf => nf.IsActive && string.Equals(nf.Category, "AnimalCompanion", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (activeAnimals.Count == 0)
                return;

            foreach (var follower in state.Followers)
            {
                if (!string.Equals(follower.Type, "Animal", StringComparison.OrdinalIgnoreCase))
                    continue;

                follower.NameCandidates = activeAnimals
                    .Select(a => a.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static Dictionary<string, object> BuildNestedGlobals(IEnumerable<GlobalFlag> globals)
        {
            var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var flag in globals)
            {
                if (string.IsNullOrWhiteSpace(flag.Key))
                    continue;

                string[] parts = flag.Key.Split('.');
                Dictionary<string, object> current = root;

                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i];
                    bool isLast = (i == parts.Length - 1);

                    if (isLast)
                    {
                        current[part] = flag.Value ?? "";
                    }
                    else
                    {
                        if (!current.TryGetValue(part, out var next))
                        {
                            var newDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            current[part] = newDict;
                            current = newDict;
                        }
                        else if (next is Dictionary<string, object> dict)
                        {
                            current = dict;
                        }
                        else
                        {
                            var newDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["_value"] = next
                            };
                            current[part] = newDict;
                            current = newDict;
                        }
                    }
                }
            }

            return root;
        }
    }
}
