using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WastelandSaveTools.App
{
    internal class Program
    {
        private const string ToolVersion = "W3Tools v0.12";

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
                Console.WriteLine("No .xml save files found under:");
                Console.WriteLine($"  {saveRoot}");
                return;
            }

            PrintSaveList(xmlFiles);

            var outDir = GetOutputDirectory();
            Directory.CreateDirectory(outDir);
            Logger.Init(outDir);

            Console.WriteLine();
            Console.WriteLine("Enter the number of the save to export (default 1 = most recent):");
            Console.WriteLine();
            Console.Write("> ");

            var input = Console.ReadLine();
            var index = 0;

            if (!string.IsNullOrWhiteSpace(input))
            {
                if (!int.TryParse(input, out var parsed))
                {
                    Console.WriteLine("Invalid input. Exiting.");
                    return;
                }

                index = Math.Clamp(parsed - 1, 0, xmlFiles.Count - 1);
            }

            var selected = xmlFiles[index];

            Console.WriteLine();
            Console.WriteLine("Selected save:");
            Console.WriteLine($"  {selected.FullName}");
            Console.WriteLine();

            Logger.Log(
                $"Starting export with chain. Tool={ToolVersion}, Selected='{selected.FullName}'");

            try
            {
                var bundle = ExportWithChainUpTo(selected, xmlFiles, outDir);

                Console.WriteLine("Export complete.");
                Console.WriteLine("Main bundle (current + chain):");
                Console.WriteLine($"  {bundle.BundleBasePath}.export.json");
                Console.WriteLine($"  {bundle.BundleBasePath}.export.pretty.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR while exporting save:");
                Console.WriteLine(ex);

                Logger.LogException("ERROR exporting save with chain", ex);
            }

            if (Logger.LogFilePath is not null)
            {
                Console.WriteLine();
                Console.WriteLine("A log file was written to:");
                Console.WriteLine($"  {Logger.LogFilePath}");
            }
        }

        private static string GetSaveRoot()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var saveRoot = Path.Combine(
                documents,
                "My Games",
                "Wasteland3",
                "Save Games");

            return saveRoot;
        }

        private static string GetOutputDirectory()
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var outDir = Path.Combine(
                profile,
                "Downloads",
                "WL3Saves");

            return outDir;
        }

        private static List<FileInfo> FindXmlSaves(string root)
        {
            var xmlFiles = Directory
                .EnumerateFiles(root, "*.xml", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ToList();

            return xmlFiles;
        }

        private static void PrintSaveList(List<FileInfo> xmlFiles)
        {
            Console.WriteLine($"Found {xmlFiles.Count} XML save(s).");
            Console.WriteLine();

            for (var i = 0; i < xmlFiles.Count; i++)
            {
                var fi = xmlFiles[i];

                Console.WriteLine(
                    $"[{i + 1}] {fi.FullName}  (Modified: {fi.LastWriteTime})");
            }
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

            var bundle = new ExportBundle
            {
                ToolVersion = ToolVersion,
                GeneratedAtLocal = DateTime.Now.ToString("O"),
                SaveName = current.SaveName,
                TimestampLocal = current.TimestampLocal,
                Current = current.Normalized,
                Chain = chain
            };

            var bundlePrefix = $"{current.TimestampLocal}.{current.SaveName}";
            var bundleBasePath = Path.Combine(outDir, bundlePrefix);

            WriteJson(bundleBasePath + ".export.json", bundle, false);
            WriteJson(bundleBasePath + ".export.pretty.json", bundle, true);

            Logger.Log($"Bundle written: {bundleBasePath}.export.json");

            bundle.BundleBasePath = bundleBasePath;
            return bundle;
        }

        private static SaveSnapshot BuildSnapshot(FileInfo saveFile)
        {
            var savePath = saveFile.FullName;

            Logger.Log("Step: read XML from save...");
            var xml = SaveReader.ExtractXml(savePath);
            Logger.Log($"XML extracted. Length={xml.Length}");

            Logger.Log("Step: extract raw...");
            var raw = StateExtractor.Extract(xml);

            Logger.Log("Step: parse...");
            var parsed = StateParser.Parse(raw);

            Logger.Log("Step: normalize...");
            var normalized = Normalizer.Normalize(raw, parsed, ToolVersion);

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
                Normalized = normalized
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

        private class SaveSnapshot
        {
            public string SaveName { get; set; } = "";
            public string TimestampLocal { get; set; } = "";
            public NormalizedSaveState Normalized { get; set; } = new NormalizedSaveState();
        }

        private class ExportBundle
        {
            public string ToolVersion { get; set; } = "";
            public string GeneratedAtLocal { get; set; } = "";
            public string SaveName { get; set; } = "";
            public string TimestampLocal { get; set; } = "";
            public NormalizedSaveState Current { get; set; } = new NormalizedSaveState();
            public CampaignDiffChain Chain { get; set; } = new CampaignDiffChain();

            // Not serialized - just for printing.
            public string BundleBasePath { get; set; } = "";
        }
    }
}
