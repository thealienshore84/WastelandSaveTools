using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WastelandSaveTools.App
{
    internal static class StateParser
    {
        private static readonly Dictionary<int, string> CompanionIdToName = new()
        {
            { 6, "Kwon" }
        };

        public static ParsedSaveState Parse(RawSaveState raw)
            => Parse(raw, null);

        public static ParsedSaveState Parse(RawSaveState raw, List<ExportIssue>? issues)
        {
            var parsed = new ParsedSaveState
            {
                Summary = BuildSummary(raw)
            };

            ParseCharacters(raw, parsed, issues);
            ParseFollowers(raw, parsed, issues);
            ParseInventoryAndContainers(raw, parsed, issues);
            ParseGlobals(raw, parsed, issues);

            parsed.Party = parsed.Characters
                .Select(c => c.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return parsed;
        }

        private static ParsedSummary BuildSummary(RawSaveState raw)
        {
            int gameplaySeconds = int.TryParse(raw.Summary.GameplayTime, out var t) ? t : 0;
            int money = int.TryParse(raw.Summary.Money, out var m) ? m : 0;

            return new ParsedSummary
            {
                Version = raw.Summary.Version ?? string.Empty,
                Scene = raw.Summary.Scene ?? string.Empty,
                SaveTime = raw.Summary.SaveTime ?? string.Empty,
                GameplaySeconds = gameplaySeconds,
                Difficulty = raw.Summary.Difficulty ?? string.Empty,
                Money = money
            };
        }

        // ------------------------------------------------------------
        // CHARACTERS
        // ------------------------------------------------------------

        private static void ParseCharacters(
            RawSaveState raw,
            ParsedSaveState target,
            List<ExportIssue>? issues)
        {
            if (string.IsNullOrWhiteSpace(raw.Xml.Pcs))
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Warning,
                    Component = "StateParser.ParseCharacters",
                    Context = "pcs",
                    Message = "Missing <pcs> section."
                });
                return;
            }

            XDocument pcsDoc;
            try
            {
                pcsDoc = XDocument.Parse(raw.Xml.Pcs, LoadOptions.SetLineInfo);
            }
            catch (XmlException ex)
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Error,
                    Component = "StateParser.ParseCharacters",
                    Context = "pcs",
                    Message = $"Malformed <pcs> block: {ex.Message}",
                    LineNumber = ex.LineNumber,
                    LinePosition = ex.LinePosition
                });
                return;
            }

            XElement root = pcsDoc.Root!;
            XElement pcsEl = root.Name.LocalName == "pcs"
                ? root
                : root.Element("pcs") ?? root;

            foreach (var pc in pcsEl.Elements("pc"))
            {
                try
                {
                    ParseSingleCharacter(pc, target);
                }
                catch (Exception ex)
                {
                    var info = (IXmlLineInfo)pc;
                    issues?.Add(new ExportIssue
                    {
                        Severity = ExportIssueSeverity.Error,
                        Component = "StateParser.ParseCharacters",
                        Context = "pc",
                        Message = $"Error parsing <pc>: {ex.Message}",
                        LineNumber = info?.LineNumber,
                        LinePosition = info?.LinePosition
                    });
                }
            }
        }

        private static void ParseSingleCharacter(XElement pc, ParsedSaveState target)
        {
            string? name = pc.Element("displayName")?.Value;
            int companionId = ReadInt(pc, "companionId");
            bool isCompanion = companionId > 0;

            if (string.IsNullOrWhiteSpace(name))
                name = TryInferPcName(pc);

            if (string.IsNullOrWhiteSpace(name))
                name = isCompanion ? $"Companion_{companionId}" : "Unknown";

            var ch = new ParsedCharacter
            {
                Name = name,
                IsCompanion = isCompanion,
                IsCustomName = !isCompanion,
                Level = ReadInt(pc, "level"),
                XP = ReadInt(pc, "xp"),
                UnspentAttributePoints = ReadIntAny(pc, "unspentAttributePoints", "attributePoints"),
                UnspentSkillPoints = ReadIntAny(pc, "unspentSkillPoints", "skillPoints"),
                UnspentPerkPoints = ReadIntAny(pc, "unspentPerkPoints", "perkPoints"),
                Attributes = new ParsedAttributes
                {
                    Coordination = ReadInt(pc, "coordination"),
                    Awareness = ReadInt(pc, "awareness"),
                    Strength = ReadInt(pc, "strength"),
                    Speed = ReadInt(pc, "speed"),
                    Intelligence = ReadInt(pc, "intelligence"),
                    Charisma = ReadInt(pc, "charisma"),
                    Luck = ReadInt(pc, "luck")
                }
            };

            // Skills
            var skillsEl = pc.Element("skills");
            if (skillsEl != null)
            {
                foreach (var s in skillsEl.Elements("skill"))
                {
                    var id = s.Element("skillId")?.Value;
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    ch.Skills.Add(new ParsedSkill
                    {
                        Id = id!,
                        Level = ReadInt(s, "level")
                    });
                }
            }

            // Perks
            var perksEl = pc.Element("perks");
            if (perksEl != null)
            {
                foreach (var p in perksEl.Elements("perk"))
                {
                    var val = p.Value;
                    if (!string.IsNullOrWhiteSpace(val))
                        ch.Perks.Add(val);
                }
            }

            // Quirks
            var qEl = pc.Element("quirks");
            if (qEl != null)
            {
                foreach (var q in qEl.Elements("quirk"))
                {
                    var val = q.Value;
                    if (!string.IsNullOrWhiteSpace(val))
                        ch.Quirks.Add(val);
                }
            }

            // Background
            var bEl = pc.Element("background");
            if (bEl != null)
            {
                var val = bEl.Value;
                if (!string.IsNullOrWhiteSpace(val))
                    ch.Background = val;
            }

            // Abilities
            var abEl = pc.Element("abilities");
            if (abEl != null)
            {
                foreach (var a in abEl.Elements("ability"))
                {
                    var val = a.Value;
                    if (!string.IsNullOrWhiteSpace(val))
                        ch.Abilities.Add(val);
                }
            }

            target.Characters.Add(ch);
        }

        // ------------------------------------------------------------
        // FOLLOWERS
        // ------------------------------------------------------------

        private static void ParseFollowers(
            RawSaveState raw,
            ParsedSaveState target,
            List<ExportIssue>? issues)
        {
            if (string.IsNullOrWhiteSpace(raw.Xml.Followers))
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Warning,
                    Component = "StateParser.ParseFollowers",
                    Context = "followers",
                    Message = "Missing <followers> section."
                });
                return;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Parse(raw.Xml.Followers, LoadOptions.SetLineInfo);
            }
            catch (XmlException ex)
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Error,
                    Component = "StateParser.ParseFollowers",
                    Context = "followers",
                    Message = $"Malformed <followers> block: {ex.Message}",
                    LineNumber = ex.LineNumber,
                    LinePosition = ex.LinePosition
                });
                return;
            }

            XElement root = doc.Root!;
            var fEl = root.Name.LocalName == "followers"
                ? root
                : root.Element("followers") ?? root;

            foreach (var fd in fEl.Elements("followerdata"))
            {
                try
                {
                    int id = ReadInt(fd, "follower");
                    var assigned = ResolveOwnerName(fd.Element("pcName")?.Value, target.Characters);
                    target.Followers.Add(new ParsedFollower
                    {
                        FollowerId = id,
                        AssignedTo = assigned,
                        IsActive = true
                    });
                }
                catch (Exception ex)
                {
                    var info = (IXmlLineInfo)fd;
                    issues?.Add(new ExportIssue
                    {
                        Severity = ExportIssueSeverity.Error,
                        Component = "StateParser.ParseFollowers",
                        Context = "followerdata",
                        Message = $"Error parsing follower: {ex.Message}",
                        LineNumber = info?.LineNumber,
                        LinePosition = info?.LinePosition
                    });
                }
            }

            // Animal companions
            foreach (var ac in root.Descendants("animalCompanions"))
            {
                var info = (IXmlLineInfo)ac;
                try
                {
                    string type = ac.Element("type")?.Value ?? string.Empty;
                    string owner = ResolveOwnerName(ac.Element("ownerName")?.Value, target.Characters);
                    int id = ReadInt(ac, "id");

                    var f = target.Followers.FirstOrDefault(x => x.FollowerId == id);
                    if (f == null)
                    {
                        f = new ParsedFollower
                        {
                            FollowerId = id,
                            AssignedTo = owner,
                            IsActive = true
                        };
                        target.Followers.Add(f);
                    }

                    if (string.IsNullOrWhiteSpace(f.AssignedTo) && !string.IsNullOrWhiteSpace(owner))
                    {
                        f.AssignedTo = owner;
                    }

                    f.Type = "Animal";
                    f.Name = $"AnimalCompanion_{type}";
                }
                catch (Exception ex)
                {
                    issues?.Add(new ExportIssue
                    {
                        Severity = ExportIssueSeverity.Error,
                        Component = "StateParser.ParseFollowers",
                        Context = "animalCompanions",
                        Message = $"Error parsing animal companion: {ex.Message}",
                        LineNumber = info?.LineNumber,
                        LinePosition = info?.LinePosition
                    });
                }
            }
        }

        // ------------------------------------------------------------
        // INVENTORY + CONTAINERS
        // ------------------------------------------------------------

        private static void ParseInventoryAndContainers(
            RawSaveState raw,
            ParsedSaveState target,
            List<ExportIssue>? issues)
        {
            ParseCharacterInventory(raw, target, issues);
            ScanContainersAndItems(raw.Xml.Inventory, "inventory", target, issues);
            ScanContainersAndItems(raw.Xml.Vehicle, "vehicle", target, issues);
        }

        private static void ParseCharacterInventory(
            RawSaveState raw,
            ParsedSaveState target,
            List<ExportIssue>? issues)
        {
            if (string.IsNullOrWhiteSpace(raw.Xml.Inventory))
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Warning,
                    Component = "StateParser.ParseCharacterInventory",
                    Context = "inventory",
                    Message = "Missing <inventory> section."
                });
                return;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Parse(raw.Xml.Inventory, LoadOptions.SetLineInfo);
            }
            catch (XmlException ex)
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Error,
                    Component = "StateParser.ParseCharacterInventory",
                    Context = "inventory",
                    Message = $"Malformed <inventory> block: {ex.Message}",
                    LineNumber = ex.LineNumber,
                    LinePosition = ex.LinePosition
                });
                return;
            }

            XElement root = doc.Root!;
            foreach (var itemsEl in root.Descendants("items"))
            {
                if (itemsEl.Ancestors("container").Any())
                    continue;

                var owner = itemsEl.Attribute("owner")?.Value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(owner))
                    continue;

                foreach (var itemEl in itemsEl.Elements("item"))
                {
                    try
                    {
                        var item = BuildItemRecord(itemEl, owner);
                        item.Context = "characterInventory";
                        target.Items.Add(item);
                    }
                    catch (Exception ex)
                    {
                        var info = (IXmlLineInfo)itemEl;
                        issues?.Add(new ExportIssue
                        {
                            Severity = ExportIssueSeverity.Error,
                            Component = "StateParser.ParseCharacterInventory",
                            Context = "item",
                            Message = $"Error parsing item: {ex.Message}",
                            LineNumber = info?.LineNumber,
                            LinePosition = info?.LinePosition
                        });
                    }
                }
            }
        }

        private static void ScanContainersAndItems(
            string? xml,
            string contextHint,
            ParsedSaveState target,
            List<ExportIssue>? issues)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return;

            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml, LoadOptions.SetLineInfo);
            }
            catch (XmlException ex)
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Error,
                    Component = "StateParser.ScanContainersAndItems",
                    Context = contextHint,
                    Message = $"Malformed <{contextHint}> block: {ex.Message}",
                    LineNumber = ex.LineNumber,
                    LinePosition = ex.LinePosition
                });
                return;
            }

            XElement root = doc.Root!;

            // Containers
            foreach (var cEl in root.Descendants("container"))
            {
                var info = (IXmlLineInfo)cEl;
                try
                {
                    var c = new ParsedContainerRecord
                    {
                        Id = cEl.Element("id")?.Value ??
                             cEl.Attribute("id")?.Value ?? string.Empty,
                        Name = cEl.Element("name")?.Value ??
                               cEl.Element("displayName")?.Value ?? string.Empty,
                        Type = cEl.Element("type")?.Value ??
                               cEl.Attribute("type")?.Value ?? contextHint,
                        Data = Flatten(cEl)
                    };

                    target.Containers.Add(c);

                    foreach (var itemEl in cEl.Descendants("item"))
                    {
                        var item = BuildItemRecord(itemEl, c.Name);
                        item.Context = $"container:{c.Name}";
                        target.Items.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    issues?.Add(new ExportIssue
                    {
                        Severity = ExportIssueSeverity.Error,
                        Component = "StateParser.ScanContainersAndItems",
                        Context = "container",
                        Message = $"Error parsing container: {ex.Message}",
                        LineNumber = info?.LineNumber,
                        LinePosition = info?.LinePosition
                    });
                }
            }

            // Loose <items> blocks
            foreach (var itemsEl in root.Descendants("items"))
            {
                var info = (IXmlLineInfo)itemsEl;
                if (itemsEl.Ancestors("container").Any())
                    continue;

                string contextAttr = itemsEl.Attribute("context")?.Value ?? string.Empty;
                string context = string.IsNullOrWhiteSpace(contextAttr)
                    ? contextHint
                    : contextAttr;

                foreach (var itemEl in itemsEl.Elements("item"))
                {
                    try
                    {
                        var item = BuildItemRecord(itemEl, string.Empty);
                        item.Context = context;
                        target.Items.Add(item);
                    }
                    catch (Exception ex)
                    {
                        var ii = (IXmlLineInfo)itemEl;
                        issues?.Add(new ExportIssue
                        {
                            Severity = ExportIssueSeverity.Error,
                            Component = "StateParser.ScanContainersAndItems",
                            Context = "item",
                            Message = $"Error parsing item: {ex.Message}",
                            LineNumber = ii?.LineNumber,
                            LinePosition = ii?.LinePosition
                        });
                    }
                }
            }
        }

        // ------------------------------------------------------------
        // GLOBAL FLAGS
        // ------------------------------------------------------------

        private static void ParseGlobals(
            RawSaveState raw,
            ParsedSaveState target,
            List<ExportIssue>? issues)
        {
            if (string.IsNullOrWhiteSpace(raw.Xml.Globals))
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Warning,
                    Component = "StateParser.ParseGlobals",
                    Context = "globals",
                    Message = "Missing <globals> section."
                });
                return;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Parse(raw.Xml.Globals, LoadOptions.SetLineInfo);
            }
            catch (XmlException ex)
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Error,
                    Component = "StateParser.ParseGlobals",
                    Context = "globals",
                    Message = $"Malformed <globals> block: {ex.Message}",
                    LineNumber = ex.LineNumber,
                    LinePosition = ex.LinePosition
                });
                return;
            }

            XElement root = doc.Root!;
            foreach (var g in root.Elements("global"))
            {
                try
                {
                    var key = g.Element("k")?.Value;
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    var value = g.Element("v")?.Value ?? string.Empty;

                    target.Globals.Add(new GlobalFlag
                    {
                        Key = key,
                        Value = value
                    });
                }
                catch (Exception ex)
                {
                    var info = (IXmlLineInfo)g;
                    issues?.Add(new ExportIssue
                    {
                        Severity = ExportIssueSeverity.Error,
                        Component = "StateParser.ParseGlobals",
                        Context = "global",
                        Message = $"Error parsing <global>: {ex.Message}",
                        LineNumber = info?.LineNumber,
                        LinePosition = info?.LinePosition
                    });
                }
            }
        }

        // ------------------------------------------------------------
        // HELPERS
        // ------------------------------------------------------------

        private static int ReadInt(XElement parent, string name)
        {
            var el = parent.Element(name);
            if (el == null)
                return 0;

            return int.TryParse(el.Value, out var i) ? i : 0;
        }

        private static int ReadIntAny(XElement parent, params string[] names)
        {
            foreach (var name in names)
            {
                var el = parent.Element(name);
                if (el == null)
                {
                    continue;
                }

                if (int.TryParse(el.Value, out var i))
                {
                    return i;
                }
            }

            return 0;
        }

        private static string ResolveOwnerName(string? rawName, List<ParsedCharacter> party)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return string.Empty;

            var normalizedRaw = NormalizeName(rawName);

            foreach (var ch in party)
            {
                if (string.Equals(ch.Name, rawName, StringComparison.OrdinalIgnoreCase))
                    return ch.Name;

                if (string.Equals(NormalizeName(ch.Name), normalizedRaw, StringComparison.OrdinalIgnoreCase))
                    return ch.Name;
            }

            return rawName;
        }

        private static string NormalizeName(string name)
        {
            return Regex.Replace(name ?? string.Empty, "[^A-Za-z0-9]", string.Empty);
        }

        private static string? TryInferPcName(XElement pc)
        {
            var name = pc.Element("name")?.Value;
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            var tags = pc.Element("tags");
            if (tags != null)
            {
                foreach (var tag in tags.Elements("tag"))
                {
                    var v = tag.Value;
                    var m = Regex.Match(v, "CNPC_([A-Za-z0-9]+)");
                    if (m.Success)
                        return m.Groups[1].Value;
                }
            }

            return pc.Element("speakerName")?.Value;
        }

        private static ParsedItemRecord BuildItemRecord(XElement el, string owner)
        {
            var rec = new ParsedItemRecord
            {
                Id = el.Element("uid")?.Value ??
                     el.Element("id")?.Value ??
                     el.Attribute("id")?.Value ?? string.Empty,
                Template = el.Element("templateName")?.Value ??
                           el.Element("template")?.Value ?? string.Empty,
                Name = el.Element("displayName")?.Value ??
                       el.Element("name")?.Value ?? string.Empty,
                Owner = owner
            };

            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in el.Elements())
                data[child.Name.LocalName] = child.Value ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(owner))
                data["Owner"] = owner;

            rec.Data = data;
            return rec;
        }

        private static Dictionary<string, string> Flatten(XElement element)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var el in element.DescendantsAndSelf())
            {
                var key = el.Name.LocalName;
                var value = el.Value ?? string.Empty;
                if (!dict.ContainsKey(key))
                    dict[key] = value;
            }

            return dict;
        }
    }
}
