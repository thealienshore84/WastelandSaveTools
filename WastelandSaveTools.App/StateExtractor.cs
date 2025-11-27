using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;

namespace WastelandSaveTools.App
{
    internal static class StateExtractor
    {
        public static RawSaveState Extract(string xml)
        {
            // Legacy behavior preserved. Caller should use the overload with issues.
            return Extract(xml, null);
        }

        public static RawSaveState Extract(string xml, List<ExportIssue>? issues)
        {
            var result = new RawSaveState();

            if (string.IsNullOrWhiteSpace(xml))
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Error,
                    Component = "StateExtractor",
                    Context = "save",
                    Message = "XML input string was empty."
                });
                return result;
            }

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
                    Component = "StateExtractor",
                    Context = "save",
                    Message = $"Malformed XML: {ex.Message}",
                    LineNumber = ex.LineNumber,
                    LinePosition = ex.LinePosition
                });

                // Cannot recover from malformed root
                return result;
            }

            var root = doc.Root;
            if (root == null)
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Error,
                    Component = "StateExtractor",
                    Context = "save",
                    Message = "XML root was null.",
                });
                return result;
            }

            // Summary extraction
            result.Summary.Version = root.Attribute("version")?.Value ?? "";
            result.Summary.Scene = root.Attribute("scene")?.Value ?? "";
            result.Summary.SaveTime = root.Attribute("saveTime")?.Value ?? "";
            result.Summary.GameplayTime = root.Attribute("gameplayTime")?.Value ?? "";
            result.Summary.Difficulty = root.Attribute("difficulty")?.Value ?? "";
            result.Summary.Money = root.Attribute("money")?.Value ?? "";

            // Individual sections
            ExtractSection(doc, "levels", result.Xml, issues);
            ExtractSection(doc, "globals", result.Xml, issues);
            ExtractSection(doc, "quests", result.Xml, issues);
            ExtractSection(doc, "reputation", result.Xml, issues);
            ExtractSection(doc, "pcs", result.Xml, issues);
            ExtractSection(doc, "followers", result.Xml, issues);
            ExtractSection(doc, "inventory", result.Xml, issues);
            ExtractSection(doc, "vehicle", result.Xml, issues);

            result.RawXml = xml;
            return result;
        }

        private static void ExtractSection(
            XDocument doc,
            string name,
            RawXmlSections target,
            List<ExportIssue>? issues)
        {
            var el = doc.Root?.Element(name);
            if (el == null)
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Warning,
                    Component = "StateExtractor",
                    Context = name,
                    Message = $"Section <{name}> is missing."
                });
                return;
            }

            try
            {
                var s = el.ToString(SaveOptions.DisableFormatting);
                switch (name)
                {
                    case "levels": target.Levels = s; break;
                    case "globals": target.Globals = s; break;
                    case "quests": target.Quests = s; break;
                    case "reputation": target.Reputation = s; break;
                    case "pcs": target.Pcs = s; break;
                    case "followers": target.Followers = s; break;
                    case "inventory": target.Inventory = s; break;
                    case "vehicle": target.Vehicle = s; break;
                }
            }
            catch (Exception ex)
            {
                issues?.Add(new ExportIssue
                {
                    Severity = ExportIssueSeverity.Error,
                    Component = "StateExtractor",
                    Context = name,
                    Message = $"Failed to serialize <{name}>: {ex.Message}"
                });
            }
        }
    }
}
