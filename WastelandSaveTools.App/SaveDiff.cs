using System;
using System.Collections.Generic;
using System.Linq;

namespace WastelandSaveTools.App
{
    public class SaveDiffResult
    {
        public string FromSaveName { get; set; } = string.Empty;
        public string ToSaveName { get; set; } = string.Empty;

        // Party membership changes
        public List<string> PartyJoined { get; set; } = new();
        public List<string> PartyLeft { get; set; } = new();

        // Character level / XP / attributes
        public List<CharacterLevelChange> CharacterChanges { get; set; } = new();

        // Inventory-level diffs (by owner/container + item)
        public List<InventoryChange> InventoryChanges { get; set; } = new();

        // Container-level diffs
        public List<ContainerChange> ContainerChanges { get; set; } = new();

        // Global flag changes (story / world state)
        public List<GlobalChange> GlobalChanges { get; set; } = new();
    }

    public class CharacterLevelChange
    {
        public string Name { get; set; } = string.Empty;

        public int FromLevel
        {
            get; set;
        }
        public int ToLevel
        {
            get; set;
        }
        public int LevelDelta => ToLevel - FromLevel;

        public int FromXP
        {
            get; set;
        }
        public int ToXP
        {
            get; set;
        }
        public int XPDelta => ToXP - FromXP;

        public int FromUnspentAttributePoints
        {
            get; set;
        }

        public int ToUnspentAttributePoints
        {
            get; set;
        }
        public int UnspentAttributeDelta => ToUnspentAttributePoints - FromUnspentAttributePoints;

        public int FromUnspentSkillPoints
        {
            get; set;
        }

        public int ToUnspentSkillPoints
        {
            get; set;
        }
        public int UnspentSkillDelta => ToUnspentSkillPoints - FromUnspentSkillPoints;

        public int FromUnspentPerkPoints
        {
            get; set;
        }

        public int ToUnspentPerkPoints
        {
            get; set;
        }
        public int UnspentPerkDelta => ToUnspentPerkPoints - FromUnspentPerkPoints;

        // Optional: basic “size” indicator
        public int Weight
        {
            get
            {
                return Math.Abs(LevelDelta) * 10
                    + Math.Abs(XPDelta) / 100
                    + Math.Abs(UnspentAttributeDelta)
                    + Math.Abs(UnspentSkillDelta)
                    + Math.Abs(UnspentPerkDelta);
            }
        }
    }

    public enum InventoryChangeType
    {
        Added,
        Removed,
        QuantityChanged
    }

    public class InventoryChange
    {
        public InventoryChangeType ChangeType
        {
            get; set;
        }

        /// <summary>Owner name (PC or container name).</summary>
        public string Owner { get; set; } = string.Empty;

        /// <summary>High-level context: e.g. "pc:Astra:equipment", "container:Shared Stash".</summary>
        public string Context { get; set; } = string.Empty;

        public string Template { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public int FromQuantity
        {
            get; set;
        }
        public int ToQuantity
        {
            get; set;
        }
        public int QuantityDelta => ToQuantity - FromQuantity;

        // Basic size metric for later weighting.
        public int Weight => Math.Abs(QuantityDelta);
    }

    public enum ContainerChangeType
    {
        Added,
        Removed,
        TypeChanged
    }

    public class ContainerChange
    {
        public ContainerChangeType ChangeType
        {
            get; set;
        }

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FromType { get; set; } = string.Empty;
        public string ToType { get; set; } = string.Empty;

        public int Weight => 1;
    }

    public enum GlobalChangeType
    {
        Added,
        Removed,
        Changed
    }

    public class GlobalChange
    {
        public GlobalChangeType ChangeType
        {
            get; set;
        }

        /// <summary>
        /// Full key from FlatGlobals, e.g. "a1001.Prisoner.State".
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Category derived from the prefix before the first dot, e.g. "a1001".
        /// Useful for grouping.
        /// </summary>
        public string Category { get; set; } = string.Empty;

        public string? FromValue
        {
            get; set;
        }
        public string? ToValue
        {
            get; set;
        }

        public int Weight => 1;
    }

    public static class SaveDiff
    {
        public static SaveDiffResult Compare(
            NormalizedSaveState from,
            NormalizedSaveState to,
            string fromName,
            string toName)
        {
            var result = new SaveDiffResult
            {
                FromSaveName = fromName,
                ToSaveName = toName
            };

            CompareParty(from, to, result);
            CompareCharacters(from, to, result);
            CompareInventory(from, to, result);
            CompareContainers(from, to, result);
            CompareGlobals(from, to, result);

            return result;
        }

        // ------------------------------------------------------------
        // PARTY
        // ------------------------------------------------------------

        private static void CompareParty(
            NormalizedSaveState from,
            NormalizedSaveState to,
            SaveDiffResult result)
        {
            var fromParty = new HashSet<string>(from.Party ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var toParty = new HashSet<string>(to.Party ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            var joined = new List<string>();
            var left = new List<string>();

            foreach (var name in toParty)
            {
                if (!fromParty.Contains(name))
                {
                    joined.Add(name);
                }
            }

            foreach (var name in fromParty)
            {
                if (!toParty.Contains(name))
                {
                    left.Add(name);
                }
            }

            joined.Sort(StringComparer.OrdinalIgnoreCase);
            left.Sort(StringComparer.OrdinalIgnoreCase);

            result.PartyJoined = joined;
            result.PartyLeft = left;
        }

        // ------------------------------------------------------------
        // CHARACTERS
        // ------------------------------------------------------------

        private static void CompareCharacters(
            NormalizedSaveState from,
            NormalizedSaveState to,
            SaveDiffResult result)
        {
            var fromChars = new Dictionary<string, NormalizedCharacter>(StringComparer.OrdinalIgnoreCase);
            var toChars = new Dictionary<string, NormalizedCharacter>(StringComparer.OrdinalIgnoreCase);

            foreach (var character in from.Characters ?? new List<NormalizedCharacter>())
            {
                if (string.IsNullOrWhiteSpace(character.Name))
                    continue;

                if (!fromChars.ContainsKey(character.Name))
                    fromChars[character.Name] = character;
            }

            foreach (var character in to.Characters ?? new List<NormalizedCharacter>())
            {
                if (string.IsNullOrWhiteSpace(character.Name))
                    continue;

                if (!toChars.ContainsKey(character.Name))
                    toChars[character.Name] = character;
            }

            var changes = new List<CharacterLevelChange>();

            foreach (var pair in fromChars)
            {
                var name = pair.Key;
                var fromChar = pair.Value;

                if (!toChars.TryGetValue(name, out var toChar))
                {
                    // Character disappeared - ignore for now, treat as party-level info.
                    continue;
                }

                var spentUnchanged = fromChar.UnspentAttributePoints == toChar.UnspentAttributePoints
                    && fromChar.UnspentSkillPoints == toChar.UnspentSkillPoints
                    && fromChar.UnspentPerkPoints == toChar.UnspentPerkPoints;

                if (fromChar.Level == toChar.Level
                    && fromChar.XP == toChar.XP
                    && spentUnchanged)
                    continue;

                var change = new CharacterLevelChange
                {
                    Name = name,
                    FromLevel = fromChar.Level,
                    ToLevel = toChar.Level,
                    FromXP = fromChar.XP,
                    ToXP = toChar.XP,
                    FromUnspentAttributePoints = fromChar.UnspentAttributePoints,
                    ToUnspentAttributePoints = toChar.UnspentAttributePoints,
                    FromUnspentSkillPoints = fromChar.UnspentSkillPoints,
                    ToUnspentSkillPoints = toChar.UnspentSkillPoints,
                    FromUnspentPerkPoints = fromChar.UnspentPerkPoints,
                    ToUnspentPerkPoints = toChar.UnspentPerkPoints
                };

                changes.Add(change);
            }

            changes = changes
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.CharacterChanges = changes;
        }

        // ------------------------------------------------------------
        // INVENTORY (STRUCTURAL)
        // ------------------------------------------------------------

        private static void CompareInventory(
            NormalizedSaveState from,
            NormalizedSaveState to,
            SaveDiffResult result)
        {
            var fromItems = from.Items ?? new List<NormalizedItem>();
            var toItems = to.Items ?? new List<NormalizedItem>();

            // Group by a stable logical key:
            // owner + context + template + name
            string MakeKey(NormalizedItem i) =>
                $"{i.Owner}||{i.Context}||{i.Template}||{i.Name}";

            var fromMap = fromItems
                .GroupBy(MakeKey)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(i => i.Quantity),
                    StringComparer.OrdinalIgnoreCase);

            var toMap = toItems
                .GroupBy(MakeKey)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(i => i.Quantity),
                    StringComparer.OrdinalIgnoreCase);

            var allKeys = new HashSet<string>(fromMap.Keys, StringComparer.OrdinalIgnoreCase);
            allKeys.UnionWith(toMap.Keys);

            var changes = new List<InventoryChange>();

            foreach (var key in allKeys)
            {
                fromMap.TryGetValue(key, out var fromQty);
                toMap.TryGetValue(key, out var toQty);

                if (fromQty == 0 && toQty == 0)
                    continue;

                if (fromQty == toQty)
                    continue;

                // Decode the key back into pieces for readability.
                var parts = key.Split(new[] { "||" }, StringSplitOptions.None);
                var owner = parts.Length > 0 ? parts[0] : string.Empty;
                var context = parts.Length > 1 ? parts[1] : string.Empty;
                var template = parts.Length > 2 ? parts[2] : string.Empty;
                var name = parts.Length > 3 ? parts[3] : string.Empty;

                InventoryChangeType type;
                if (fromQty == 0 && toQty > 0)
                    type = InventoryChangeType.Added;
                else if (fromQty > 0 && toQty == 0)
                    type = InventoryChangeType.Removed;
                else
                    type = InventoryChangeType.QuantityChanged;

                var change = new InventoryChange
                {
                    ChangeType = type,
                    Owner = owner,
                    Context = context,
                    Template = template,
                    Name = name,
                    FromQuantity = fromQty,
                    ToQuantity = toQty
                };

                changes.Add(change);
            }

            // Stable ordering: by owner, then name, then template.
            result.InventoryChanges = changes
                .OrderBy(c => c.Owner, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Context, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Template, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ------------------------------------------------------------
        // CONTAINERS
        // ------------------------------------------------------------

        private static void CompareContainers(
            NormalizedSaveState from,
            NormalizedSaveState to,
            SaveDiffResult result)
        {
            var fromContainers = from.Containers ?? new List<NormalizedContainer>();
            var toContainers = to.Containers ?? new List<NormalizedContainer>();

            // Use Id if present, otherwise fall back to Name.
            string MakeKey(NormalizedContainer c) =>
                string.IsNullOrWhiteSpace(c.Id)
                    ? c.Name
                    : c.Id;

            var fromMap = fromContainers
                .GroupBy(MakeKey)
                .ToDictionary(
                    g => g.Key,
                    g => g.First(),
                    StringComparer.OrdinalIgnoreCase);

            var toMap = toContainers
                .GroupBy(MakeKey)
                .ToDictionary(
                    g => g.Key,
                    g => g.First(),
                    StringComparer.OrdinalIgnoreCase);

            var allKeys = new HashSet<string>(fromMap.Keys, StringComparer.OrdinalIgnoreCase);
            allKeys.UnionWith(toMap.Keys);

            var changes = new List<ContainerChange>();

            foreach (var key in allKeys)
            {
                var hasFrom = fromMap.TryGetValue(key, out var fromC);
                var hasTo = toMap.TryGetValue(key, out var toC);

                if (!hasFrom && !hasTo)
                    continue;

                if (!hasFrom && hasTo)
                {
                    changes.Add(new ContainerChange
                    {
                        ChangeType = ContainerChangeType.Added,
                        Id = key,
                        Name = toC!.Name,
                        FromType = string.Empty,
                        ToType = toC.Type
                    });
                    continue;
                }

                if (hasFrom && !hasTo)
                {
                    changes.Add(new ContainerChange
                    {
                        ChangeType = ContainerChangeType.Removed,
                        Id = key,
                        Name = fromC!.Name,
                        FromType = fromC.Type,
                        ToType = string.Empty
                    });
                    continue;
                }

                // Both exist - compare type only for now.
                if (!string.Equals(fromC!.Type, toC!.Type, StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add(new ContainerChange
                    {
                        ChangeType = ContainerChangeType.TypeChanged,
                        Id = key,
                        Name = toC.Name,
                        FromType = fromC.Type,
                        ToType = toC.Type
                    });
                }
            }

            result.ContainerChanges = changes
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ------------------------------------------------------------
        // GLOBALS (STORY / WORLD STATE)
        // ------------------------------------------------------------

        private static void CompareGlobals(
            NormalizedSaveState from,
            NormalizedSaveState to,
            SaveDiffResult result)
        {
            var fromGlobals = from.FlatGlobals ?? new Dictionary<string, string>();
            var toGlobals = to.FlatGlobals ?? new Dictionary<string, string>();

            var allKeys = new HashSet<string>(fromGlobals.Keys, StringComparer.OrdinalIgnoreCase);
            allKeys.UnionWith(toGlobals.Keys);

            var changes = new List<GlobalChange>();

            foreach (var key in allKeys)
            {
                var hasFrom = fromGlobals.TryGetValue(key, out var fromVal);
                var hasTo = toGlobals.TryGetValue(key, out var toVal);

                if (!hasFrom && !hasTo)
                    continue;

                GlobalChangeType type;
                if (!hasFrom && hasTo)
                {
                    type = GlobalChangeType.Added;
                }
                else if (hasFrom && !hasTo)
                {
                    type = GlobalChangeType.Removed;
                }
                else
                {
                    if (string.Equals(fromVal, toVal, StringComparison.Ordinal))
                        continue;

                    type = GlobalChangeType.Changed;
                }

                var category = string.Empty;
                var dotIndex = key.IndexOf('.');
                if (dotIndex > 0)
                    category = key.Substring(0, dotIndex);

                changes.Add(new GlobalChange
                {
                    ChangeType = type,
                    Key = key,
                    Category = category,
                    FromValue = hasFrom ? fromVal : null,
                    ToValue = hasTo ? toVal : null
                });
            }

            result.GlobalChanges = changes
                .OrderBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public class CampaignDiffLink
    {
        public string FromSaveName { get; set; } = string.Empty;
        public string ToSaveName { get; set; } = string.Empty;
        public SaveDiffResult Diff { get; set; } = new SaveDiffResult();
    }

    public class CampaignDiffChain
    {
        public string ToolVersion { get; set; } = string.Empty;
        public string GeneratedAtUtc { get; set; } = string.Empty;
        public List<CampaignDiffLink> Links { get; set; } = new();
    }
}