using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WastelandSaveTools.App
{
    internal class Program
    {
        private const string ToolVersion = "W3Tools v0.9";

        static void Main(string[] args)
        {
            Console.WriteLine($"{ToolVersion} - Wasteland 3 Save Inspector");
            Console.WriteLine();

            // 1. Locate save directory
            var saveRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "My Games",
                "Wasteland3",
                "Save Games"
            );

            if (!Directory.Exists(saveRoot))
            {
                Console.WriteLine("Could not find Wasteland 3 save directory:");
                Console.WriteLine($"  {saveRoot}");
                return;
            }

            // 2. Find XML save files
            var xmlFiles = Directory
                .EnumerateFiles(saveRoot, "*.xml", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .ToList();

            if (xmlFiles.Count == 0)
            {
                Console.WriteLine("No .xml save files found under:");
                Console.WriteLine($"  {saveRoot}");
                return;
            }

            Console.WriteLine($"Found {xmlFiles.Count} XML save file(s).\n");
            for (var i = 0; i < xmlFiles.Count; i++)
            {
                var fi = xmlFiles[i];
                Console.WriteLine(
                    $"[{i + 1}] {fi.FullName}  (Last modified: {fi.LastWriteTime})"
                );
            }

            Console.WriteLine();
            Console.WriteLine("Enter the number of the save to export (default 1 = most recent):");
            Console.WriteLine();

            Console.Write("> ");
            var input = Console.ReadLine();
            var index = 0;
            if (!string.IsNullOrWhiteSpace(input) && int.TryParse(input, out var parsed))
            {
                index = Math.Clamp(parsed - 1, 0, xmlFiles.Count - 1);
            }

            var chosenFile = xmlFiles[index].FullName;

            Console.WriteLine();
            Console.WriteLine("Selected save:");
            Console.WriteLine($"  {chosenFile}");
            Console.WriteLine();

            // 3. Choose output directory (also used for logs)
            var outDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "WL3Saves"
            );
            Logger.Init(outDir);
            Logger.Log($"Starting export. ToolVersion={ToolVersion}, SavePath='{chosenFile}'");

            try
            {
                // 4. Extract XML from the WL3 file
                Logger.Log("Step: Extracting XML from save...");
                var xml = SaveReader.ExtractXml(chosenFile);
                Logger.Log($"Step: XML extracted. Length={xml.Length} chars.");

                // 5. Build raw + parsed state
                Logger.Log("Step: Extracting raw state...");
                var rawState = StateExtractor.Extract(xml);
                Logger.Log("Step: Raw state extracted.");

                Logger.Log("Step: Parsing state...");
                var parsedState = StateParser.Parse(rawState);
                Logger.Log("Step: Parsed state built.");

                // 6. Build normalized state for AI / tooling
                Logger.Log("Step: Normalizing state...");
                var normalized = Normalizer.Normalize(rawState, parsedState, ToolVersion);
                Logger.Log("Step: Normalized state built.");

                // 7. Ensure output directory exists
                Directory.CreateDirectory(outDir);

                // 8. Choose base filename
                var baseName = Path.GetFileNameWithoutExtension(chosenFile);
                var basePath = Path.Combine(outDir, baseName);

                var compactOptions = new JsonSerializerOptions
                {
                    WriteIndented = false
                };
                var prettyOptions = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                // 9. Write RAW state
                File.WriteAllText(
                    basePath + ".raw.json",
                    JsonSerializer.Serialize(rawState, compactOptions)
                );
                File.WriteAllText(
                    basePath + ".raw.pretty.json",
                    JsonSerializer.Serialize(rawState, prettyOptions)
                );

                // 10. Write PARSED state
                File.WriteAllText(
                    basePath + ".parsed.json",
                    JsonSerializer.Serialize(parsedState, compactOptions)
                );
                File.WriteAllText(
                    basePath + ".parsed.pretty.json",
                    JsonSerializer.Serialize(parsedState, prettyOptions)
                );

                // 11. Write NORMALIZED state (clean, AI-friendly)
                File.WriteAllText(
                    basePath + ".normalized.json",
                    JsonSerializer.Serialize(normalized, compactOptions)
                );
                File.WriteAllText(
                    basePath + ".normalized.pretty.json",
                    JsonSerializer.Serialize(normalized, prettyOptions)
                );

                Logger.Log($"Export complete. BasePath='{basePath}'");

                Console.WriteLine("Export complete:");
                Console.WriteLine($"  RAW (compact):        {basePath}.raw.json");
                Console.WriteLine($"  RAW (pretty):         {basePath}.raw.pretty.json");
                Console.WriteLine($"  PARSED (compact):     {basePath}.parsed.json");
                Console.WriteLine($"  PARSED (pretty):      {basePath}.parsed.pretty.json");
                Console.WriteLine($"  NORMALIZED (compact): {basePath}.normalized.json");
                Console.WriteLine($"  NORMALIZED (pretty):  {basePath}.normalized.pretty.json");
                Console.WriteLine();
                Console.WriteLine("If you recompile later and forget, check the header for:");
                Console.WriteLine($"  Tool version = {ToolVersion}");

                if (Logger.LogFilePath is not null)
                {
                    Console.WriteLine();
                    Console.WriteLine("A log file was written to:");
                    Console.WriteLine($"  {Logger.LogFilePath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR while exporting save:");
                Console.WriteLine(ex.ToString());

                Logger.LogException("ERROR while exporting save", ex);

                if (Logger.LogFilePath is not null)
                {
                    Console.WriteLine();
                    Console.WriteLine("A log file was written to:");
                    Console.WriteLine($"  {Logger.LogFilePath}");
                }
            }
        }
    }
}
