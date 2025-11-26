using System;
using System.Collections.Generic;
using System.Linq;

namespace WastelandSaveTools.App
{
    public class SaveDiffResult
    {
        public string FromSaveName { get; set; } = "";
        public string ToSaveName { get; set; } = "";

        public List<string> PartyJoined { get; set; } = new();
        public List<string> PartyLeft { get; set; } = new();

        public List<CharacterLevelChange> CharacterChanges { get; set; } = new();
    }

    public class CharacterLevelChange
    {
        public string Name { get; set; } = "";

        public int FromLevel { get; set; }
        public int ToLevel { get; set; }

        public int FromXP { get; set; }
        public int ToXP { get; set; }
    }

    internal static class SaveDiff
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

            return result;
        }

        private static void CompareParty(
            NormalizedSaveState from,
            NormalizedSaveState to,
            SaveDiffResult result)
        {
            var fromParty = new HashSet<string>(from.Party, StringComparer.OrdinalIgnoreCase);
            var toParty = new HashSet<string>(to.Party, StringComparer.OrdinalIgnoreCase);

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

        private static void CompareCharacters(
            NormalizedSaveState from,
            NormalizedSaveState to,
            SaveDiffResult result)
        {
            var fromChars = new Dictionary<string, NormalizedCharacter>(StringComparer.OrdinalIgnoreCase);
            var toChars = new Dictionary<string, NormalizedCharacter>(StringComparer.OrdinalIgnoreCase);

            foreach (var character in from.Characters)
            {
                if (string.IsNullOrWhiteSpace(character.Name))
                {
                    continue;
                }

                if (!fromChars.ContainsKey(character.Name))
                {
                    fromChars[character.Name] = character;
                }
            }

            foreach (var character in to.Characters)
            {
                if (string.IsNullOrWhiteSpace(character.Name))
                {
                    continue;
                }

                if (!toChars.ContainsKey(character.Name))
                {
                    toChars[character.Name] = character;
                }
            }

            var changes = new List<CharacterLevelChange>();

            foreach (var pair in fromChars)
            {
                var name = pair.Key;
                var fromChar = pair.Value;

                if (!toChars.TryGetValue(name, out var toChar))
                {
                    continue;
                }

                if (fromChar.Level == toChar.Level && fromChar.XP == toChar.XP)
                {
                    continue;
                }

                var change = new CharacterLevelChange
                {
                    Name = name,
                    FromLevel = fromChar.Level,
                    ToLevel = toChar.Level,
                    FromXP = fromChar.XP,
                    ToXP = toChar.XP
                };

                changes.Add(change);
            }

            changes = changes
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.CharacterChanges = changes;
        }
    }
}
