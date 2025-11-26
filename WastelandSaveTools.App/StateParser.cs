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

        private static ParsedSummary BuildSummary(RawSaveState raw)
        {
            int gameplaySeconds = int.TryParse(raw.Summary.GameplayTime, out var t) ? t : 0;
            int money = int.TryParse(raw.Summary.Money, out var m) ? m : 0;

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
            string pcsXml = raw.Xml.Pcs ?? "";

            if (string.IsNullOrWhiteSpace(pcsXml) && !string.IsNullOrWhiteSpace(raw.RawXml))
            {
                try
                {
                    var full = XDocument.Parse(raw.RawXml);
                    var pcs = full.Descendants("pcs").FirstOrDefault();
                    if (pcs != null)
                        pcsXml = pcs.ToString(SaveOptions.DisableFormatting);
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(pcsXml))
                return;

            XDocument pcsDoc = XDocument.Parse(pcsXml!);

            XElement root = pcsDoc.Root!;
            XElement pcsElement =
                root.Name.LocalName.Equals("pcs", StringComparison.OrdinalIgnoreCase)
                    ? root
                    : root.Element("pcs") ?? root;

            foreach (var pc in pcsElement.Elements("pc"))
            {
                string? name = pc.Element("displayName")?.Value;
                int companionId = ReadInt(pc, "companionId");
                bool isCompanion = companionId > 0;

                if (string.IsNullOrWhiteSpace(name))
                    name = TryInferPcName(pc);

                if (string.IsNullOrWhiteSpace(name) &&
                    isCompanion &&
                    CompanionIdToName.TryGetValue(companionId, out var mapped))
                {
                    name = mapped;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    if (isCompanion)
                        name = $"Companion #{companionId}";
                    else
                        continue;
                }

                var ch = new ParsedCharacter
                {
                    Name = name!,
                    IsCompanion = isCompanion,
                    IsCustomName = ReadBool(pc, "isCustomDisplayName"),
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
                        string? id = skill.Element("skillId")?.Value;
                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        ch.Skills.Add(new ParsedSkill
                        {
                            Id = id!,
                            Level = ReadInt(skill, "level")
                        });
                    }
                }

                var abilitiesEl = pc.Element("abilities");
                if (abilitiesEl != null)
                {
                    foreach (var ab in abilitiesEl.Elements("ability"))
                    {
                        string val = ab.Value;
                        if (!string.IsNullOrWhiteSpace(val))
                            ch.Abilities.Add(val);
                    }
                }

                target.Characters.Add(ch);
            }
        }

        // nullable return is explicit now
        private static string? TryInferPcName(XElement pc)
        {
            foreach (var t in pc.Descendants("templateName"))
            {
                string? v = t.Value;
                if (string.IsNullOrWhiteSpace(v))
                    continue;

                var m = Regex.Match(v, "CNPC_([A-Za-z0-9]+)");
                if (m.Success)
                    return m.Groups[1].Value;
            }

            string? speaker = pc.Element("speakerName")?.Value;
            return string.IsNullOrWhiteSpace(speaker) ? null : speaker;
        }

        // --------------------------
        //  FOLLOWERS
        // --------------------------
        private static void ParseFollowers(RawSaveState raw, ParsedSaveState target)
        {
            var list = new List<ParsedFollower>();

            if (!string.IsNullOrWhiteSpace(raw.Xml.Followers))
            {
                try
                {
                    var doc = XDocument.Parse(raw.Xml.Followers);
                    var root = doc.Root;
                    if (root != null)
                    {
                        XElement fEl =
                            root.Name.LocalName.Equals("followers", StringComparison.OrdinalIgnoreCase)
                                ? root
                                : root.Element("followers") ?? root;

                        foreach (var fd in fEl.Elements("followerdata"))
                        {
                            int id = ReadInt(fd, "follower");
                            string assigned = fd.Element("pcName")?.Value ?? "";

                            list.Add(new ParsedFollower
                            {
                                FollowerId = id,
                                AssignedTo = assigned,
                                IsActive = true
                            });
                        }
                    }
                }
                catch { }
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(raw.RawXml) &&
                    raw.RawXml.Contains("<animalCompanions", StringComparison.OrdinalIgnoreCase))
                {
                    var doc = XDocument.Parse(raw.RawXml);
                    var root = doc.Descendants("animalCompanions").FirstOrDefault();
                    if (root != null)
                    {
                        foreach (var ad in root.Elements("animaldata"))
                        {
                            int id = ReadInt(ad, "follower");
                            string assigned = ad.Element("pcName")?.Value ?? "";
                            int typeId = ReadInt(ad, "id");

                            var f = list.FirstOrDefault(x => x.FollowerId == id);
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
                                f.AssignedTo = assigned;
                        }
                    }
                }
            }
            catch { }

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
            string pcsXml = raw.Xml.Pcs ?? "";

            if (string.IsNullOrWhiteSpace(pcsXml) && !string.IsNullOrWhiteSpace(raw.RawXml))
            {
                try
                {
                    var doc = XDocument.Parse(raw.RawXml);
                    var pcs = doc.Descendants("pcs").FirstOrDefault();
                    if (pcs != null)
                        pcsXml = pcs.ToString(SaveOptions.DisableFormatting);
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(pcsXml))
                return;

            var pcsDoc = XDocument.Parse(pcsXml!);
            XElement root = pcsDoc.Root!;
            XElement pcsElement =
                root.Name.LocalName.Equals("pcs", StringComparison.OrdinalIgnoreCase)
                    ? root
                    : root.Element("pcs") ?? root;

            foreach (var pc in pcsElement.Elements("pc"))
            {
                string owner = pc.Element("displayName")?.Value ?? "";
                int companionId = ReadInt(pc, "companionId");

                if (string.IsNullOrWhiteSpace(owner))
                    owner = TryInferPcName(pc) ?? "";

                if (string.IsNullOrWhiteSpace(owner) &&
                    CompanionIdToName.TryGetValue(companionId, out var mapped))
                    owner = mapped;

                if (string.IsNullOrWhiteSpace(owner) && companionId > 0)
                    owner = $"Companion #{companionId}";

                foreach (var itemEl in pc.Descendants("item"))
                {
                    var rec = BuildItemRecord(itemEl, owner);
                    target.Items.Add(rec);
                }
            }
        }

        private static void ScanContainersAndItems(string? xml, string contextHint, ParsedSaveState target)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return;

            XDocument doc;
            try { doc = XDocument.Parse(xml!); }
            catch { return; }

            XElement root = doc.Root!;
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
                if (itemsEl.Ancestors("container").Any())
                    continue;

                foreach (var itemEl in itemsEl.Elements("item"))
                {
                    var item = BuildItemRecord(itemEl, "");
                    item.Context = contextHint;
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

            rec.Quantity =
                int.TryParse(el.Element("quantity")?.Value, out var q) ? q : 1;

            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in el.Elements())
                data[child.Name.LocalName] = child.Value ?? "";

            if (!string.IsNullOrWhiteSpace(owner))
                data["Owner"] = owner;

            rec.Data = data;
            return rec;
        }

        // --------------------------
        //  GLOBAL FLAGS
        // --------------------------
        private static void ParseGlobals(RawSaveState raw, ParsedSaveState target)
        {
            if (string.IsNullOrWhiteSpace(raw.Xml.Globals))
                return;

            try
            {
                var doc = XDocument.Parse(raw.Xml.Globals);
                var root = doc.Root;
                if (root == null)
                    return;

                foreach (var g in root.Elements("global"))
                {
                    string? key = g.Element("k")?.Value;
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    string value = g.Element("v")?.Value ?? "";

                    target.Globals.Add(new GlobalFlag
                    {
                        Key = key!,
                        Value = value
                    });
                }
            }
            catch { }
        }

        // --------------------------
        //  HELPERS
        // --------------------------
        private static int ReadInt(XElement parent, string name)
        {
            var el = parent.Element(name);
            return (el != null && int.TryParse(el.Value, out var v)) ? v : 0;
        }

        private static bool ReadBool(XElement parent, string name)
        {
            var el = parent.Element(name);
            if (el == null)
                return false;

            if (bool.TryParse(el.Value, out var b))
                return b;

            return int.TryParse(el.Value, out var i) && i != 0;
        }

        private static Dictionary<string, string> Flatten(XElement el)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in el.DescendantsAndSelf())
            {
                if (!node.HasElements)
                    dict[node.Name.LocalName] = node.Value ?? "";
            }

            foreach (var attr in el.Attributes())
                dict["@" + attr.Name.LocalName] = attr.Value ?? "";

            return dict;
        }
    }
}
