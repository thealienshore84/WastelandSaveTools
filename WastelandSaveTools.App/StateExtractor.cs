using System;
using System.Linq;
using System.Xml.Linq;

namespace WastelandSaveTools.App
{
    internal static class StateExtractor
    {
        public static RawSaveState Extract(string xml)
        {
            var result = new RawSaveState();

            if (string.IsNullOrWhiteSpace(xml))
                return result;

            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null)
                return result;

            // Summary
            result.Summary.Version = root.Attribute("version")?.Value ?? "";
            result.Summary.Scene = root.Attribute("scene")?.Value ?? "";
            result.Summary.SaveTime = root.Attribute("saveTime")?.Value ?? "";
            result.Summary.GameplayTime = root.Attribute("gameplayTime")?.Value ?? "";
            result.Summary.Difficulty = root.Attribute("difficulty")?.Value ?? "";
            result.Summary.Money = root.Attribute("money")?.Value ?? "";

            // Individual blocks
            result.Xml.Levels = ExtractSection(doc, "levels") ?? "";
            result.Xml.Globals = ExtractSection(doc, "globals") ?? "";
            result.Xml.Quests = ExtractSection(doc, "quests") ?? "";
            result.Xml.Reputation = ExtractSection(doc, "reputation") ?? "";
            result.Xml.Pcs = ExtractSection(doc, "pcs") ?? "";
            result.Xml.Followers = ExtractSection(doc, "followers") ?? "";
            result.Xml.Inventory = ExtractSection(doc, "inventory") ?? "";
            result.Xml.Vehicle = ExtractSection(doc, "vehicle") ?? "";

            // Full XML
            result.RawXml = xml;

            return result;
        }

        private static string ExtractSection(XDocument doc, string name)
        {
            // Always returns a non-null string for compiler happiness.
            var el = doc.Descendants(name).FirstOrDefault();
            return el != null
                ? el.ToString(SaveOptions.DisableFormatting)
                : "";
        }
    }
}
