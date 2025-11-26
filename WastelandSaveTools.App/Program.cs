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

            // 1. Locate save-game root
            string saveRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "My Games", "Wasteland3", "Save Games"
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
            for (int i = 0; i < xmlFiles.Count; i++)
            {
                var fi = xmlFiles[i];
                Console.WriteLine(
                    $"[{i + 1}] {fi.FullName}  (Last modified: {fi.LastWriteTime})"
                );
            }

            Console.WriteLine();
            Console.Write("Enter the number of the save to export (default 1 = most recent): ");
            string? input = Console.ReadLine();

            int index = 0;
            if (!string.IsNullOrWhiteSpace(input) && int.TryParse(input, out int parsed))
            {
                index = Math.Clamp(parsed - 1, 0, xmlFiles.Count - 1);
            }

            var chosenFile = xmlFiles[index].FullName;

            Console.WriteLine();
            Console.WriteLine("Selected save:");
            Console.WriteLine($"  {chosenFile}");
            Console.WriteLine();

            try
            {
                // 3. Extract XML from the WL3 file
                string xml = SaveReader.ExtractXml(chosenFile);

                // 4. Build raw + parsed state
                RawSaveState rawState = StateExtractor.Extract(xml);
                ParsedSaveState parsedState = StateParser.Parse(rawState);

                // 5. Build normalized state for AI / tooling
                NormalizedSaveState normalized = Normalizer.Normalize(rawState, parsedState, ToolVersion);

                // 6. Output directory
                string outDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads",
                    "WL3Saves"
                );
                Directory.CreateDirectory(outDir);

                string baseName = Path.GetFileNameWithoutExtension(chosenFile);
                string basePath = Path.Combine(outDir, baseName);

                var compactOptions = new JsonSerializerOptions
                {
                    WriteIndented = false
                };

                var prettyOptions = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                // 7. Write RAW state (what the extractor sees directly from XML)
                File.WriteAllText(
                    basePath + ".raw.json",
                    JsonSerializer.Serialize(rawState, compactOptions)
                );
                File.WriteAllText(
                    basePath + ".raw.pretty.json",
                    JsonSerializer.Serialize(rawState, prettyOptions)
                );

                // 8. Write PARSED state (characters, globals, etc.)
                File.WriteAllText(
                    basePath + ".parsed.json",
                    JsonSerializer.Serialize(parsedState, compactOptions)
                );
                File.WriteAllText(
                    basePath + ".parsed.pretty.json",
                    JsonSerializer.Serialize(parsedState, prettyOptions)
                );

                // 9. Write NORMALIZED state (clean, AI-friendly)
                File.WriteAllText(
                    basePath + ".normalized.json",
                    JsonSerializer.Serialize(normalized, compactOptions)
                );
                File.WriteAllText(
                    basePath + ".normalized.pretty.json",
                    JsonSerializer.Serialize(normalized, prettyOptions)
                );

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
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR while exporting save:");
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
