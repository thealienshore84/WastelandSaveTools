using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WastelandSaveTools.App
{
    internal class Program
    {
        private const string ToolVersion = "W3Tools v0.13";

        private static void Main(string[] args)
        {
            Console.WriteLine($"{ToolVersion} - Wasteland 3 Save Inspector");
            Console.WriteLine();

            var saveRoot = GetSaveRoot();
            if (!Directory.Exists(saveRoot))
            {
                Console.WriteLine("Could not find Wasteland 3 save directory:");
                Console.WriteLine($"  {saveRoot}");
                return;
            }

            var xmlFiles = FindXmlSaves(saveRoot);
            if (xmlFiles.Count == 0)
            {
                Console.WriteLine("No .xml files were found under:");
                Console.WriteLine($"  {saveRoot}");
                Console.WriteLine();
                Console.WriteLine("Hint: export at least one save using the in-game");
                Console.WriteLine("      XML export option, then run this tool again.");
                return;
            }

            Console.WriteLine("Available XML saves (most recent first):");
            for (var i = 0; i < xmlFiles.Count; i++)
            {
                Console.WriteLine($"  [{i + 1}] {xmlFiles[i].FullName}");
            }

            Console.WriteLine();
            Console.Write("Enter the number of the save to export (default 1 = most recent): ");

            var input = Console.ReadLine();
            if (!int.TryParse(input, out var selection) ||
                selection < 1 ||
                selection > xmlFiles.Count)
            {
                selection = 1;
            }

            var selected = xmlFiles[selection - 1];
            Console.WriteLine();
            Console.WriteLine("Selected save:");
            Console.WriteLine($"  {selected.FullName}");
            Console.WriteLine();

            var outDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "WL3Saves");

            Directory.CreateDirectory(outDir);

            Logger.Init(outDir);

            try
            {
                var bundle = ExportWithChainUpTo(selected, xmlFiles, outDir);

                Console.WriteLine("Export complete:");
                Console.WriteLine($"  RAW (compact):    {bundle.BundleBasePath}.raw.json");
                Console.WriteLine($"  RAW (pretty):     {bundle.BundleBasePath}.raw.pretty.json");
                Console.WriteLine($"  PARSED (compact): {bundle.BundleBasePath}.export.json");
                Console.WriteLine($"  PARSED (pretty):  {bundle.BundleBasePath}.export.pretty.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred during export. See log for details.");
                Logger.LogException("Export failure", ex);
            }
        }

        private static string GetSaveRoot()
        {
            var docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docPath, "My Games", "Wasteland3", "Save Games");
        }

        private static List<FileInfo> FindXmlSaves(string saveRoot)
        {
            var di = new DirectoryInfo(saveRoot);
            var files = di.GetFiles("*.xml", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            return files;
        }

        private static ExportBundle ExportWithChainUpTo(
            FileInfo selectedSave,
            List<FileInfo> allSaves,
            string outDir)
        {
            var chronological = allSaves
                .OrderBy(fi => fi.LastWriteTimeUtc)
                .ToList();

            var chain = new CampaignDiffChain
            {
                ToolVersion = ToolVersion,
                GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                Links = new List<CampaignDiffLink>()
            };

            SaveSnapshot? previous = null;
            SaveSnapshot? current = null;

            foreach (var fi in chronological)
            {
                // Stop once we go past the selected save in time
                if (fi.LastWriteTimeUtc > selectedSave.LastWriteTimeUtc)
                {
                    break;
                }

                Logger.Log($"Processing save for chain (in memory): '{fi.FullName}'");

                var snapshot = BuildSnapshot(fi);

                if (previous is not null)
                {
                    var diff = SaveDiff.Compare(
                        previous.Normalized,
                        snapshot.Normalized,
                        previous.SaveName,
                        snapshot.SaveName);

                    var link = new CampaignDiffLink
                    {
                        FromSaveName = previous.SaveName,
                        ToSaveName = snapshot.SaveName,
                        Diff = diff
                    };

                    chain.Links.Add(link);
                }

                if (string.Equals(
                        fi.FullName,
                        selectedSave.FullName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    current = snapshot;
                }

                previous = snapshot;
            }

            if (current is null)
            {
                throw new InvalidOperationException(
                    "Selected save did not appear in chronological chain.");
            }

            var summary = current.Normalized.Summary ?? new ParsedSummary();

            var bundle = new ExportBundle
            {
                ToolVersion = ToolVersion,
                GeneratedAtLocal = DateTime.Now.ToString("O"),

                SaveName = current.SaveName,
                TimestampLocal = current.TimestampLocal,

                // Inline header metadata from the save
                Version = summary.Version,
                Scene = summary.Scene,
                SaveTime = summary.SaveTime,
                Difficulty = summary.Difficulty,
                GameplaySeconds = summary.GameplaySeconds,
                Money = summary.Money,

                Current = current.Normalized,
                Chain = chain,
                Issues = current.Issues
            };

            var bundlePrefix = $"{current.TimestampLocal}.{current.SaveName}";
            var bundleBasePath = Path.Combine(outDir, bundlePrefix);

            WriteJson(bundleBasePath + ".raw.json", current.Normalized, false);
            WriteJson(bundleBasePath + ".raw.pretty.json", current.Normalized, true);

            WriteJson(bundleBasePath + ".export.json", bundle, false);
            WriteJson(bundleBasePath + ".export.pretty.json", bundle, true);

            Logger.Log($"Bundle written: {bundleBasePath}.export.json");

            bundle.BundleBasePath = bundleBasePath;
            return bundle;
        }

        private static SaveSnapshot BuildSnapshot(FileInfo saveFile)
        {
            var savePath = saveFile.FullName;

            var issues = new List<ExportIssue>();

            Logger.Log("Step: read XML from save...");
            var xml = SaveReader.ExtractXml(savePath);
            Logger.Log($"XML extracted. Length={xml.Length}");

            Logger.Log("Step: extract raw...");
            var raw = StateExtractor.Extract(xml, issues);

            Logger.Log("Step: parse...");
            var parsed = StateParser.Parse(raw, issues);

            Logger.Log("Step: normalize...");
            var normalized = Normalizer.Normalize(raw, parsed, ToolVersion);

            // - Phase 3.2 inline metadata -
            // Pull Version / Location / SaveTime from the physical save file header
            // and push into Normalized.Summary so it ends up both there and in the bundle.
            ApplyHeaderMetadata(saveFile, normalized);

            var timestamp = saveFile.LastWriteTime
                .ToString("yyyyMMdd_HHmmss_ffffff");

            var safeName = Path.GetFileNameWithoutExtension(savePath)
                .Replace(":", "_")
                .Replace("/", "_")
                .Replace("\\", "_");

            var snapshot = new SaveSnapshot
            {
                SaveName = safeName,
                TimestampLocal = timestamp,
                Normalized = normalized,
                Issues = issues
            };

            return snapshot;
        }

        private static void WriteJson(string path, object value, bool pretty)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = pretty
            };

            var json = JsonSerializer.Serialize(value, options);

            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Reads the plain-text header at the top of the Wasteland 3 save file:
        ///   Version:=0.91
        ///   Location:=ar_a2001_Downtown
        ///   SaveTime:=20251126T16:06:10-5
        /// and copies those into Normalized.Summary.
        /// </summary>
        private static void ApplyHeaderMetadata(FileInfo saveFile, NormalizedSaveState normalized)
        {
            try
            {
                string? version = null;
                string? location = null;
                string? saveTime = null;

                using (var reader = new StreamReader(saveFile.FullName))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("Version:=", StringComparison.OrdinalIgnoreCase))
                        {
                            version = line.Substring("Version:=".Length).Trim();
                        }
                        else if (line.StartsWith("Location:=", StringComparison.OrdinalIgnoreCase))
                        {
                            location = line.Substring("Location:=".Length).Trim();
                        }
                        else if (line.StartsWith("SaveTime:=", StringComparison.OrdinalIgnoreCase))
                        {
                            saveTime = line.Substring("SaveTime:=".Length).Trim();
                        }

                        // Once we have all three, we can stop.
                        if (version != null && location != null && saveTime != null)
                            break;

                        // Header lines are of form "Key:=Value".
                        // Once we hit something that isn't like that after seeing any header,
                        // we assume we've entered the compressed blob.
                        if (!line.Contains(":=") && (version != null || location != null || saveTime != null))
                            break;
                    }
                }

                var summary = normalized.Summary ?? new ParsedSummary();

                if (!string.IsNullOrWhiteSpace(version))
                    summary.Version = version;

                if (!string.IsNullOrWhiteSpace(location))
                    summary.Scene = location;

                if (!string.IsNullOrWhiteSpace(saveTime))
                    summary.SaveTime = saveTime;

                normalized.Summary = summary;
            }
            catch (Exception ex)
            {
                Logger.LogException("ApplyHeaderMetadata failed", ex);
            }
        }

        private class SaveSnapshot
        {
            public string SaveName { get; set; } = "";
            public string TimestampLocal { get; set; } = "";
            public NormalizedSaveState Normalized { get; set; } = new NormalizedSaveState();

            /// <summary>
            /// Non fatal and fatal issues detected while reading and parsing this save.
            /// </summary>
            public List<ExportIssue> Issues { get; set; } = new();
        }

        private class ExportBundle
        {
            public string ToolVersion { get; set; } = "";
            public string GeneratedAtLocal { get; set; } = "";

            public string SaveName { get; set; } = "";
            public string TimestampLocal { get; set; } = "";

            // Surfaced metadata from Current.Summary / ParsedSummary
            public string Version { get; set; } = "";
            public string Scene { get; set; } = "";
            public string SaveTime { get; set; } = "";
            public string Difficulty { get; set; } = "";
            public int GameplaySeconds
            {
                get; set;
            }
            public int Money
            {
                get; set;
            }

            public NormalizedSaveState Current { get; set; } = new NormalizedSaveState();
            public CampaignDiffChain Chain { get; set; } = new CampaignDiffChain();

            /// <summary>
            /// Issues detected while building the "Current" snapshot.
            /// For now this only includes issues from the selected save.
            /// </summary>
            public List<ExportIssue> Issues { get; set; } = new();

            // Not serialized - just for printing.
            public string BundleBasePath { get; set; } = "";
        }
    }
}
