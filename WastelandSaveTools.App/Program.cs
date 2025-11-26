using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WastelandSaveTools.App
{
    internal class Program
    {
        private const string ToolVersion = "W3Tools v0.9";

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
            Console.WriteLine("or type 'all' to export every save and generate diffs:");
            Console.WriteLine();
            Console.Write("> ");

            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                ExportSingleInteractive(xmlFiles, outDir);
            }
            else if (string.Equals(input.Trim(), "all", StringComparison.OrdinalIgnoreCase))
            {
                ExportAllSaves(xmlFiles, outDir);
            }
            else
            {
                if (!int.TryParse(input, out var parsedIndex))
                {
                    Console.WriteLine("Invalid input. Exiting.");
                    return;
                }

                var index = Math.Clamp(parsedIndex - 1, 0, xmlFiles.Count - 1);
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
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var outDir = Path.Combine(
                userProfile,
                "Downloads",
                "WL3Saves");

            return outDir;
        }

        private static List<FileInfo> FindXmlSaves(string saveRoot)
        {
            var xmlFiles = Directory
                .EnumerateFiles(saveRoot, "*.xml", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ToList();

            return xmlFiles;
        }

        private static void PrintSaveList(List<FileInfo> xmlFiles)
        {
            Console.WriteLine($"Found {xmlFiles.Count} XML save file(s).");
            Console.WriteLine();

            for (var i = 0; i < xmlFiles.Count; i++)
            {
                var fi = xmlFiles[i];

                Console.WriteLine(
                    $"[{i + 1}] {fi.FullName}  (Last modified: {fi.LastWriteTime})");
            }
        }

        private static void ExportSingleInteractive(
            List<FileInfo> xmlFiles,
            string outDir)
        {
            ExportSingleByIndex(xmlFiles, 0, outDir);
        }

        private static void ExportSingleByIndex(
            List<FileInfo> xmlFiles,
            int index,
            string outDir)
        {
            var chosenFileInfo = xmlFiles[index];
            var chosenFile = chosenFileInfo.FullName;

            Console.WriteLine();
            Console.WriteLine("Selected save:");
            Console.WriteLine($"  {chosenFile}");
            Console.WriteLine();

            Logger.Log($"Starting export (single). ToolVersion={ToolVersion}, SavePath='{chosenFile}'");

            try
            {
                var currentResult = ProcessSingleSave(chosenFileInfo, outDir);

                Console.WriteLine("Export complete:");
                PrintExportSummary(currentResult);

                TryAutoDiffWithPrevious(xmlFiles, index, outDir, currentResult);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR while exporting save:");
                Console.WriteLine(ex);

                Logger.LogException("ERROR while exporting save (single)", ex);
            }
        }

        private static void ExportAllSaves(
            List<FileInfo> xmlFiles,
            string outDir)
        {
            Console.WriteLine();
            Console.WriteLine("Exporting ALL saves in chronological order and generating diffs...");
            Console.WriteLine();

            var chronological = xmlFiles
                .OrderBy(fi => fi.LastWriteTimeUtc)
                .ToList();

            ExportResult? previous = null;

            for (var i = 0; i < chronological.Count; i++)
            {
                var fileInfo = chronological[i];

                Console.WriteLine(
                    $"[{i + 1}/{chronological.Count}] {fileInfo.FullName}");

                Logger.Log(
                    $"Starting export (batch). ToolVersion={ToolVersion}, SavePath='{fileInfo.FullName}'");

                ExportResult current;

                try
                {
                    current = ProcessSingleSave(fileInfo, outDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  ERROR exporting this save. Skipping.");
                    Console.WriteLine($"  {ex.Message}");

                    Logger.LogException("ERROR while exporting save (batch)", ex);
                    previous = null;
                    continue;
                }

                Console.WriteLine("  Export complete.");
                PrintExportSummaryInline(current);

                if (previous is not null)
                {
                    TryWriteDiff(previous, current, outDir, isBatch: true);
                }

                previous = current;
                Console.WriteLine();
            }

            Console.WriteLine("Batch export complete.");
        }

        private static ExportResult ProcessSingleSave(
            FileInfo saveFile,
            string outDir)
        {
            var savePath = saveFile.FullName;

            Logger.Log("Step: Extracting XML from save...");
            var xml = SaveReader.ExtractXml(savePath);
            Logger.Log($"Step: XML extracted. Length={xml.Length} chars.");

            Logger.Log("Step: Extracting raw state...");
            var rawState = StateExtractor.Extract(xml);
            Logger.Log("Step: Raw state extracted.");

            Logger.Log("Step: Parsing state...");
            var parsedState = StateParser.Parse(rawState);
            Logger.Log("Step: Parsed state built.");

            Logger.Log("Step: Normalizing state...");
            var normalized = Normalizer.Normalize(rawState, parsedState, ToolVersion);
            Logger.Log("Step: Normalized state built.");

            Directory.CreateDirectory(outDir);

            var baseName = Path.GetFileNameWithoutExtension(savePath);
            var basePath = Path.Combine(outDir, baseName);

            var compactOptions = new JsonSerializerOptions
            {
                WriteIndented = false
            };

            var prettyOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            File.WriteAllText(
                basePath + ".raw.json",
                JsonSerializer.Serialize(rawState, compactOptions));

            File.WriteAllText(
                basePath + ".raw.pretty.json",
                JsonSerializer.Serialize(rawState, prettyOptions));

            File.WriteAllText(
                basePath + ".parsed.json",
                JsonSerializer.Serialize(parsedState, compactOptions));

            File.WriteAllText(
                basePath + ".parsed.pretty.json",
                JsonSerializer.Serialize(parsedState, prettyOptions));

            File.WriteAllText(
                basePath + ".normalized.json",
                JsonSerializer.Serialize(normalized, compactOptions));

            File.WriteAllText(
                basePath + ".normalized.pretty.json",
                JsonSerializer.Serialize(normalized, prettyOptions));

            Logger.Log($"Export complete. BasePath='{basePath}'");

            var result = new ExportResult
            {
                SaveName = baseName,
                BasePath = basePath,
                Normalized = normalized
            };

            return result;
        }

        private static void TryAutoDiffWithPrevious(
            List<FileInfo> xmlFiles,
            int index,
            string outDir,
            ExportResult currentResult)
        {
            if (index + 1 >= xmlFiles.Count)
            {
                return;
            }

            var prevFileInfo = xmlFiles[index + 1];
            var prevFile = prevFileInfo.FullName;

            Logger.Log($"Step: Auto-compare with previous save '{prevFile}'");

            try
            {
                var prevResult = ProcessSingleSave(prevFileInfo, outDir);

                TryWriteDiff(prevResult, currentResult, outDir, isBatch: false);
            }
            catch (Exception ex)
            {
                Logger.LogException("Auto-compare failed", ex);

                Console.WriteLine();
                Console.WriteLine("Warning: comparison with previous save failed:");
                Console.WriteLine(ex.Message);
            }
        }

        private static void TryWriteDiff(
            ExportResult from,
            ExportResult to,
            string outDir,
            bool isBatch)
        {
            var compactOptions = new JsonSerializerOptions
            {
                WriteIndented = false
            };

            var prettyOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var diff = SaveDiff.Compare(
                from.Normalized,
                to.Normalized,
                from.SaveName,
                to.SaveName);

            var diffBase = Path.Combine(
                outDir,
                $"{from.SaveName}--{to.SaveName}.diff");

            File.WriteAllText(
                diffBase + ".json",
                JsonSerializer.Serialize(diff, compactOptions));

            File.WriteAllText(
                diffBase + ".pretty.json",
                JsonSerializer.Serialize(diff, prettyOptions));

            Logger.Log($"Diff written: {diffBase}.json");

            if (!isBatch)
            {
                Console.WriteLine();
                Console.WriteLine("Auto-comparison with previous save:");
                Console.WriteLine($"  DIFF (compact):       {diffBase}.json");
                Console.WriteLine($"  DIFF (pretty):        {diffBase}.pretty.json");
            }
            else
            {
                Console.WriteLine("  Diff written:");
                Console.WriteLine($"    {diffBase}.json");
                Console.WriteLine($"    {diffBase}.pretty.json");
            }
        }

        private static void PrintExportSummary(ExportResult result)
        {
            Console.WriteLine($"  RAW (compact):        {result.BasePath}.raw.json");
            Console.WriteLine($"  RAW (pretty):         {result.BasePath}.raw.pretty.json");
            Console.WriteLine($"  PARSED (compact):     {result.BasePath}.parsed.json");
            Console.WriteLine($"  PARSED (pretty):      {result.BasePath}.parsed.pretty.json");
            Console.WriteLine($"  NORMALIZED (compact): {result.BasePath}.normalized.json");
            Console.WriteLine($"  NORMALIZED (pretty):  {result.BasePath}.normalized.pretty.json");
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
            public NormalizedSaveState Normalized { get; set; } = new NormalizedSaveState();
        }
    }
}
