using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WastelandSaveTools.App
{
    internal class Program
    {
        private const string ToolVersion = "W3Tools v0.10";

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
            Logger.Init(outDir);

            Console.WriteLine();
            Console.WriteLine("Enter the number of the save to export (default 1 = most recent),");
            Console.WriteLine("or type 'all' to export every save and generate a single diff chain:");
            Console.WriteLine();
            Console.Write("> ");

            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                ExportSingleByIndex(xmlFiles, 0, outDir);
            }
            else if (string.Equals(input.Trim(), "all", StringComparison.OrdinalIgnoreCase))
            {
                ExportAllSavesWithChain(xmlFiles, outDir);
            }
            else
            {
                if (!int.TryParse(input, out var parsed))
                {
                    Console.WriteLine("Invalid input. Exiting.");
                    return;
                }

                var index = Math.Clamp(parsed - 1, 0, xmlFiles.Count - 1);
                ExportSingleByIndex(xmlFiles, index, outDir);
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

        private static void ExportSingleByIndex(
            List<FileInfo> xmlFiles,
            int index,
            string outDir)
        {
            var fi = xmlFiles[index];

            Console.WriteLine();
            Console.WriteLine("Selected save:");
            Console.WriteLine($"  {fi.FullName}");
            Console.WriteLine();

            Logger.Log($"Start export (single). Tool={ToolVersion}, Save='{fi.FullName}'");

            try
            {
                var result = ProcessSingleSave(fi, outDir);

                Console.WriteLine("Export complete:");
                PrintExportSummary(result);

                TryAutoDiff(xmlFiles, index, outDir, result);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR while exporting save:");
                Console.WriteLine(ex);

                Logger.LogException("ERROR exporting save (single)", ex);
            }
        }

        private static void ExportAllSavesWithChain(
            List<FileInfo> xmlFiles,
            string outDir)
        {
            Console.WriteLine();
            Console.WriteLine("Exporting all saves and building diff chain...");
            Console.WriteLine();

            var chronological = xmlFiles
                .OrderBy(fi => fi.LastWriteTimeUtc)
                .ToList();

            var chain = new CampaignDiffChain
            {
                ToolVersion = ToolVersion,
                GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
                Links = new List<CampaignDiffLink>()
            };

            var optionsCompact = new JsonSerializerOptions { WriteIndented = false };
            var optionsPretty = new JsonSerializerOptions { WriteIndented = true };

            ExportResult? previous = null;

            for (var i = 0; i < chronological.Count; i++)
            {
                var fi = chronological[i];

                Console.WriteLine(
                    $"[{i + 1}/{chronological.Count}] {fi.FullName}");

                Logger.Log($"Batch export. Tool={ToolVersion}, Save='{fi.FullName}'");

                ExportResult current;

                try
                {
                    current = ProcessSingleSave(fi, outDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  ERROR exporting. Skipping.");
                    Console.WriteLine($"  {ex.Message}");

                    Logger.LogException("Batch export error", ex);
                    previous = null;
                    continue;
                }

                Console.WriteLine("  Export complete.");
                PrintExportSummaryInline(current);

                if (previous is not null)
                {
                    var diff = SaveDiff.Compare(
                        previous.Normalized,
                        current.Normalized,
                        previous.SaveName,
                        current.SaveName);

                    var link = new CampaignDiffLink
                    {
                        FromSaveName = previous.SaveName,
                        ToSaveName = current.SaveName,
                        Diff = diff
                    };

                    chain.Links.Add(link);

                    Console.WriteLine("  Diff added to chain.");
                }

                previous = current;
                Console.WriteLine();
            }

            var chainBase = Path.Combine(outDir, "campaign.diffchain");

            File.WriteAllText(
                chainBase + ".json",
                JsonSerializer.Serialize(chain, optionsCompact));

            File.WriteAllText(
                chainBase + ".pretty.json",
                JsonSerializer.Serialize(chain, optionsPretty));

            Logger.Log($"Campaign diff chain written: {chainBase}.json");

            Console.WriteLine("Batch export complete.");
            Console.WriteLine($"  {chainBase}.json");
            Console.WriteLine($"  {chainBase}.pretty.json");
        }

        private static ExportResult ProcessSingleSave(
            FileInfo saveFile,
            string outDir)
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

            Directory.CreateDirectory(outDir);

            var timestamp = saveFile.LastWriteTime
                .ToString("yyyyMMdd_HHmmss_ffffff");

            var safeName = Path.GetFileNameWithoutExtension(savePath)
                .Replace(":", "_")
                .Replace("/", "_")
                .Replace("\\", "_");

            var filePrefix = $"{timestamp}.{safeName}";
            var basePath = Path.Combine(outDir, filePrefix);

            WriteJson(basePath + ".raw.json", raw, false);
            WriteJson(basePath + ".raw.pretty.json", raw, true);
            WriteJson(basePath + ".parsed.json", parsed, false);
            WriteJson(basePath + ".parsed.pretty.json", parsed, true);
            WriteJson(basePath + ".normalized.json", normalized, false);
            WriteJson(basePath + ".normalized.pretty.json", normalized, true);

            Logger.Log($"Export written: {basePath}.*");

            var result = new ExportResult
            {
                SaveName = safeName,
                BasePath = basePath,
                Normalized = normalized,
                TimestampLocal = timestamp
            };

            return result;
        }

        private static void WriteJson(string path, object value, bool pretty)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = pretty
            };

            File.WriteAllText(path, JsonSerializer.Serialize(value, options));
        }

        private static void TryAutoDiff(
            List<FileInfo> xmlFiles,
            int index,
            string outDir,
            ExportResult current)
        {
            if (index + 1 >= xmlFiles.Count)
            {
                return;
            }

            var fiPrev = xmlFiles[index + 1];

            Logger.Log($"Auto-diff vs previous save '{fiPrev.FullName}'");

            try
            {
                var prev = ProcessSingleSave(fiPrev, outDir);

                WriteSingleDiff(prev, current, outDir);
            }
            catch (Exception ex)
            {
                Logger.LogException("Auto-diff failed", ex);

                Console.WriteLine();
                Console.WriteLine("Warning: auto-diff failed:");
                Console.WriteLine(ex.Message);
            }
        }

        private static void WriteSingleDiff(
            ExportResult from,
            ExportResult to,
            string outDir)
        {
            var compact = new JsonSerializerOptions { WriteIndented = false };
            var pretty = new JsonSerializerOptions { WriteIndented = true };

            var diff = SaveDiff.Compare(
                from.Normalized,
                to.Normalized,
                from.SaveName,
                to.SaveName);

            var filePrefix = $"{to.TimestampLocal}.{from.SaveName}--{to.SaveName}.diff";

            var basePath = Path.Combine(outDir, filePrefix);

            File.WriteAllText(
                basePath + ".json",
                JsonSerializer.Serialize(diff, compact));

            File.WriteAllText(
                basePath + ".pretty.json",
                JsonSerializer.Serialize(diff, pretty));

            Logger.Log($"Single diff written: {basePath}.json");

            Console.WriteLine();
            Console.WriteLine("Auto-diff:");
            Console.WriteLine($"  {basePath}.json");
            Console.WriteLine($"  {basePath}.pretty.json");
        }

        private static void PrintExportSummary(ExportResult result)
        {
            Console.WriteLine($"  RAW:        {result.BasePath}.raw.json");
            Console.WriteLine($"  PARSED:     {result.BasePath}.parsed.json");
            Console.WriteLine($"  NORMALIZED: {result.BasePath}.normalized.json");
        }

        private static void PrintExportSummaryInline(ExportResult result)
        {
            Console.WriteLine("  Output:");
            Console.WriteLine($"    {result.BasePath}.raw.json");
            Console.WriteLine($"    {result.BasePath}.parsed.json");
            Console.WriteLine($"    {result.BasePath}.normalized.json");
        }

        private class ExportResult
        {
            public string SaveName { get; set; } = "";
            public string BasePath { get; set; } = "";
            public string TimestampLocal { get; set; } = "";
            public NormalizedSaveState Normalized { get; set; } = new NormalizedSaveState();
        }
    }
}
