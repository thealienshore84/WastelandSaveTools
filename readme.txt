WastelandSaveTools
==================

WastelandSaveTools is a .NET console utility for inspecting and exporting Wasteland 3 save files. It walks the local save directory, asks which exported XML save to process, and produces JSON snapshots that are easier to explore and diff.

What the tool does
------------------
- Locates exported `.xml` saves under `Documents/My Games/Wasteland3/Save Games` and lists them with the most recent first. If no XML exports exist, the tool explains how to export one in-game.
- Reads the chosen save file, including support for XLZF-compressed payloads inside the Wasteland 3 format, and extracts the `<save>...</save>` XML block. The reader validates the XML and captures issues when the input is malformed.
- Parses the save into a normalized model that tracks party members, followers, inventory, globals, and other campaign metadata. The normalized state also includes lightweight issue reporting so you can see unexpected or missing data in the save.
- Builds a diff chain across saves leading up to the selected one. It compares normalized snapshots to highlight party members who joined or left and per-character XP/level changes between saves.
- Writes four JSON outputs to `~/Downloads/WL3Saves/<timestamp>.<save-name>`:
  - `*.raw.json` / `*.raw.pretty.json`: normalized state from a single save.
  - `*.export.json` / `*.export.pretty.json`: bundle containing the normalized state, high-level metadata, detected issues, and the diff chain between saves.
- Logs every step to the same output directory for troubleshooting.

Project layout
--------------
- `Program.cs` orchestrates the console flow: finding saves, prompting for a selection, exporting snapshots, and writing JSON bundles.
- `SaveReader.cs` loads a save file, handles XLZF decompression, and extracts the XML payload.
- `StateExtractor.cs` parses the XML into a raw model while recording parsing issues.
- `StateParser.cs` converts the raw model into strongly typed structures (characters, inventory, globals, etc.).
- `Normalizer.cs` reshapes parsed data into a consistent, diff-friendly `NormalizedSaveState`.
- `SaveDiff.cs` compares normalized saves to create the campaign diff chain.
- `Logger.cs` provides simple log file output in the export directory.

Building and running
--------------------
1. Ensure the .NET 7 SDK (or later) is installed.
2. From the repository root, run:
   ```
   dotnet run --project WastelandSaveTools.App
   ```
3. Follow the prompt to pick an exported XML save. Results and logs will be written to `~/Downloads/WL3Saves`.

Notes
-----
- The tool currently targets exported XML saves. Native binary saves must be exported from within the game before running this utility.
- Output filenames include the save timestamp and cleaned save name so multiple exports can coexist in the output directory.
