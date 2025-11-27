using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        {
            var parsed = new ParsedSaveState
            {
                Summary = BuildSummary(raw)
            };

            ParseCharacters(raw, parsed);
            ParseFollowers(raw, parsed);
            ParseInventoryAndContainers(raw, parsed);
            ParseGlobals(raw, parsed);

            parsed.Party = parsed.Characters
                .Select(c => c.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return parsed;
        }

        public static ParsedSaveState Parse(RawSaveState raw, List<ExportIssue>? issues)
        {
            // Phase 3 issue plumbing - issues will be populated later.
            return Parse(raw);
        }

        private static ParsedSummary BuildSummary(RawSaveState raw)
        {
            var gameplaySeconds = int.TryParse(raw.Summary.GameplayTime, out var t) ? t : 0;
            var money = int.TryParse(raw.Summary.Money, out var m) ? m : 0;

            return new ParsedSummary
            {
                Version = raw.Summary.Version ?? "",
                Scene = raw.Summary.Scene ?? "",
                SaveTime = raw.Summary.SaveTime ?? "",
                Difficulty = raw.Summary.Difficulty ?? "",
                GameplaySeconds = gameplaySeconds,
                Money = money
            };
        }

        // --------------------------
        //  CHARACTER PARSING
        // --------------------------

        private static void ParseCharacters(RawSaveState raw, ParsedSaveState target)
        {
            if (string.IsNullOrWhiteSpace(raw.Xml.Pcs))
            {
                return;
            }

            try
            {
                var pcsDoc = XDocument.Parse(raw.Xml.Pcs!);
                var root = pcsDoc.Root!;
                var pcsElement =
                    root.Name.LocalName.Equals("pcs", StringComparison.OrdinalIgnoreCase)
                        ? root
                        : root.Element("pcs") ?? root;

                foreach (var pc in pcsElement.Elements("pc"))
                {
                    var name = pc.Element("displayName")?.Value;
                    var companionId = ReadInt(pc, "companionId");
                    var isCompanion = companionId > 0;

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = TryInferPcName(pc);
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        if (isCompanion)
                        {
                            name = $"Companion #{companionId}";
                        }
                        else
                        {
                            continue;
                        }
                    }

                    var ch = new ParsedCharacter
                    {
                        Name = name,
                        IsCompanion = isCompanion,
                        IsCustomName = !isCompanion,
                        Level = ReadInt(pc, "level"),
                        XP = ReadInt(pc, "xp"),
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

                    var skillsEl = pc.Element("skills");
                    if (skillsEl != null)
                    {
                        foreach (var skill in skillsEl.Elements("skill"))
                        {
                            var id = skill.Element("skillId")?.Value;
                            if (string.IsNullOrWhiteSpace(id))
                            {
                                continue;
                            }

                            ch.Skills.Add(new ParsedSkill
                            {
                                Id = id!,
                                Level = ReadInt(skill, "level")
                            });
                        }
                    }

                    var perksEl = pc.Element("perks");
                    if (perksEl != null)
                    {
                        foreach (var p in perksEl.Elements("perk"))
                        {
                            var val = p.Value;
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                ch.Perks.Add(val);
                            }
                        }
                    }

                    var quirksEl = pc.Element("quirks");
                    if (quirksEl != null)
                    {
                        foreach (var q in quirksEl.Elements("quirk"))
                        {
                            var val = q.Value;
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                ch.Quirks.Add(val);
                            }
                        }
                    }

                    var backgroundEl = pc.Element("background");
                    if (backgroundEl != null)
                    {
                        var val = backgroundEl.Value;
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            ch.Background = val;
                        }
                    }

                    var abilitiesEl = pc.Element("abilities");
                    if (abilitiesEl != null)
                    {
                        foreach (var ab in abilitiesEl.Elements("ability"))
                        {
                            var val = ab.Value;
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                ch.Abilities.Add(val);
                            }
                        }
                    }

                    target.Characters.Add(ch);
                }
            }
            catch
            {
                // For now we fail silently - issues will be wired in later.
            }
        }

        private static string? TryInferPcName(XElement pc)
        {
            // Sometimes the "displayName" is empty but "name" or "speakerName"
            // contains a useful identifier.

            var name = pc.Element("name")?.Value;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            // Try to infer from "tag" entries, e.g. CNPC_Kwon
            var tagsEl = pc.Element("tags");
            if (tagsEl != null)
            {
                foreach (var t in tagsEl.Elements("tag"))
                {
                    var v = t.Value;
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        continue;
                    }

                    var m = Regex.Match(v, "CNPC_([A-Za-z0-9]+)");
                    if (m.Success)
                    {
                        return m.Groups[1].Value;
                    }
                }
            }

            var speaker = pc.Element("speakerName")?.Value;
            if (!string.IsNullOrWhiteSpace(speaker))
            {
                return speaker;
            }

            return null;
        }

        // --------------------------
        //  FOLLOWER PARSING
        // --------------------------

        private static void ParseFollowers(RawSaveState raw, ParsedSaveState target)
        {
            var list = new List<ParsedFollower>();

            // Followers from the <followers> block.
            if (!string.IsNullOrWhiteSpace(raw.Xml.Followers))
            {
                try
                {
                    var doc = XDocument.Parse(raw.Xml.Followers!);
                    var root = doc.Root;
                    if (root != null)
                    {
                        var fEl =
                            root.Name.LocalName.Equals("followers", StringComparison.OrdinalIgnoreCase)
                                ? root
                                : root.Element("followers") ?? root;

                        foreach (var fd in fEl.Elements("followerdata"))
                        {
                            var id = ReadInt(fd, "follower");
                            var assigned = fd.Element("pcName")?.Value ?? "";

                            list.Add(new ParsedFollower
                            {
                                FollowerId = id,
                                AssignedTo = assigned,
                                IsActive = true
                            });
                        }
                    }
                }
                catch
                {
                }
            }

            // Animal companions from the <globals> or other blocks (if needed).
            // For now we treat animal companions as a special-case extension of
            // followers when they appear in the followers XML.
            if (!string.IsNullOrWhiteSpace(raw.Xml.Followers))
            {
                try
                {
                    var doc = XDocument.Parse(raw.Xml.Followers!);
                    var root = doc.Root;
                    if (root != null)
                    {
                        foreach (var acEl in root.Descendants("animalCompanions"))
                        {
                            var typeId = acEl.Element("type")?.Value ?? "";
                            var assigned = acEl.Element("ownerName")?.Value ?? "";

                            if (string.IsNullOrWhiteSpace(typeId))
                            {
                                continue;
                            }

                            var id = ReadInt(acEl, "id");
                            ParsedFollower? f = list.FirstOrDefault(x => x.FollowerId == id);
                            if (f == null)
                            {
                                f = new ParsedFollower
                                {
                                    FollowerId = id,
                                    IsActive = true
                                };
                                list.Add(f);
                            }

                            f.Type = "Animal";
                            f.Name = $"AnimalCompanion_{typeId}";
                            if (!string.IsNullOrWhiteSpace(assigned))
                            {
                                f.AssignedTo = assigned;
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            target.Followers.AddRange(list);
        }

        // --------------------------
        //  INVENTORY + CONTAINERS
        // --------------------------

        private static void ParseInventoryAndContainers(RawSaveState raw, ParsedSaveState target)
        {
            ParseCharacterInventory(raw, target);
            ScanContainersAndItems(raw.Xml.Inventory, "inventory", target);
            ScanContainersAndItems(raw.Xml.Vehicle, "vehicle", target);
        }

        private static void ParseCharacterInventory(RawSaveState raw, ParsedSaveState target)
        {
            if (string.IsNullOrWhiteSpace(raw.Xml.Inventory))
            {
                return;
            }

            try
            {
                var doc = XDocument.Parse(raw.Xml.Inventory!);
                var root = doc.Root;
                if (root == null)
                {
                    return;
                }

                // We treat <inventory><items owner="PCName"> as character inventories
                // for now and ignore more exotic patterns.
                foreach (var itemsEl in root.Descendants("items"))
                {
                    var owner = itemsEl.Attribute("owner")?.Value ?? "";
                    var context = itemsEl.Attribute("context")?.Value ?? "characterInventory";

                    if (string.IsNullOrWhiteSpace(owner))
                    {
                        continue;
                    }

                    // If this is inside a container, that container will be handled
                    // by ScanContainersAndItems instead.
                    if (itemsEl.Ancestors("container").Any())
                    {
                        continue;
                    }

                    foreach (var itemEl in itemsEl.Elements("item"))
                    {
                        var item = BuildItemRecord(itemEl, owner);
                        item.Context = context;
                        target.Items.Add(item);
                    }
                }
            }
            catch
            {
            }
        }

        private static void ScanContainersAndItems(string? xml, string contextHint, ParsedSaveState target)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return;
            }

            XDocument doc;
            try { doc = XDocument.Parse(xml!); }
            catch { return; }

            var root = doc.Root!;
            foreach (var cEl in root.Descendants("container"))
            {
                var c = new ParsedContainerRecord
                {
                    Id = cEl.Element("id")?.Value ??
                         cEl.Attribute("id")?.Value ??
                         "",
                    Name = cEl.Element("name")?.Value ??
                           cEl.Element("displayName")?.Value ??
                           "",
                    Type = cEl.Element("type")?.Value ??
                           cEl.Attribute("type")?.Value ??
                           contextHint,
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

            foreach (var itemsEl in root.Descendants("items"))
            {
                var contextAttr = itemsEl.Attribute("context")?.Value ?? "";
                var context = string.IsNullOrWhiteSpace(contextAttr)
                    ? contextHint
                    : contextAttr;

                // Skip ones already processed as part of a container
                if (itemsEl.Ancestors("container").Any())
                {
                    continue;
                }

                foreach (var itemEl in itemsEl.Elements("item"))
                {
                    var item = BuildItemRecord(itemEl, "");
                    item.Context = context;
                    target.Items.Add(item);
                }
            }
        }

        private static ParsedItemRecord BuildItemRecord(XElement el, string owner)
        {
            var rec = new ParsedItemRecord
            {
                Id = el.Element("uid")?.Value ??
                     el.Element("id")?.Value ??
                     el.Attribute("id")?.Value ??
                     string.Empty,
                Template = el.Element("templateName")?.Value ??
                           el.Element("template")?.Value ??
                           "",
                Name = el.Element("displayName")?.Value ??
                       el.Element("name")?.Value ??
                       "",
                Owner = owner
            };

            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in el.Elements())
            {
                data[child.Name.LocalName] = child.Value ?? "";
            }

            if (!string.IsNullOrWhiteSpace(owner))
            {
                data["Owner"] = owner;
            }

            rec.Data = data;
            return rec;
        }

        // --------------------------
        //  GLOBAL FLAGS
        // --------------------------

        private static void ParseGlobals(RawSaveState raw, ParsedSaveState target)
        {
            if (string.IsNullOrWhiteSpace(raw.Xml.Globals))
            {
                return;
            }

            try
            {
                var doc = XDocument.Parse(raw.Xml.Globals);
                var root = doc.Root;
                if (root == null)
                {
                    return;
                }

                foreach (var g in root.Elements("global"))
                {
                    var key = g.Element("k")?.Value;
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    var value = g.Element("v")?.Value ?? "";

                    target.Globals.Add(new GlobalFlag
                    {
                        Key = key!,
                        Value = value
                    });
                }
            }
            catch
            {
            }
        }

        // --------------------------
        //  HELPERS
        // --------------------------

        private static int ReadInt(XElement parent, string name)
        {
            var el = parent.Element(name);
            if (el == null)
            {
                return 0;
            }

            if (int.TryParse(el.Value, out var i))
            {
                return i;
            }

            return 0;
        }

        private static Dictionary<string, string> Flatten(XElement element)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in element.DescendantsAndSelf())
            {m
                var key = el.Name.LocalName;
                var value = el.Value ?? "";
                if (dict.ContainsKey(key))
                {
                    continue;
                }

                dict[key] = value;
            }

            return dict;
        }
    }
}
