using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace WastelandSaveTools.App
{
    internal static class SaveReader
    {
        public static string ExtractXml(string path)
        {
            return ReadSave(path);
        }

        /// <summary>
        /// Reads a Wasteland 3 save file.
        /// - For normal Wasteland 3 saves (XLZF header), it:
        ///   * Parses the text header (XLZF, DataSize, etc.)
        ///   * Decompresses the LZF payload after the header
        ///   * Extracts the &lt;save&gt; XML block
        /// - If there is no XLZF header, it falls back to treating the file
        ///   as plain text and searching for &lt;save&gt;…&lt;/save&gt;.
        /// </summary>
        public static string ReadSave(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Save not found.", path);

            byte[] data = File.ReadAllBytes(path);

            // Peek at the first chunk of the file as ASCII to detect the XLZF header.
            string headerProbe = Encoding.ASCII.GetString(
                data, 0, Math.Min(data.Length, 4096)
            );

            if (!headerProbe.StartsWith("XLZF", StringComparison.Ordinal))
            {
                // Fallback: treat as plain text and search for <save>...</save>
                return ExtractPlainXml(data);
            }

            // We have an XLZF header - parse DataSize and locate the end of the header.
            int dataSize = ParseDataSize(headerProbe);
            int headerLength = FindHeaderLength(headerProbe);

            if (headerLength <= 0 || headerLength >= data.Length)
                throw new InvalidOperationException("Could not locate end of XLZF header.");

            if (dataSize <= 0)
                throw new InvalidOperationException($"Invalid DataSize value in header: {dataSize}");

            int compressedLength = data.Length - headerLength;
            if (compressedLength <= 0)
                throw new InvalidOperationException("No compressed payload found after header.");

            // Copy the compressed payload into its own buffer.
            var compressed = new byte[compressedLength];
            Buffer.BlockCopy(data, headerLength, compressed, 0, compressedLength);

            // Decompress using the existing Lzf implementation.
            var decompressed = new byte[dataSize];
            int written = Lzf.Decompress(compressed, compressed.Length, decompressed, decompressed.Length);
            if (written <= 0)
                throw new InvalidOperationException("LZF decompression failed for save payload.");

            string xmlText = Encoding.UTF8.GetString(decompressed, 0, written);

            // Trim BOM / stray nulls / whitespace at the ends.
            xmlText = xmlText.Trim('\0', '\t', '\r', '\n', ' ');

            // Extract the <save>...</save> block from the decompressed text.
            string xml = ExtractSaveBlock(xmlText);

            // Validate XML so we fail early if it is corrupt.
            XDocument.Parse(xml);

            return xml;
        }

        /// <summary>
        /// Fallback for files that are already plain text: search for &lt;save&gt;…&lt;/save&gt;.
        /// </summary>
        private static string ExtractPlainXml(byte[] data)
        {
            string text = Encoding.UTF8.GetString(data);

            return ExtractSaveBlock(text);
        }

        /// <summary>
        /// Extracts the first &lt;save&gt;…&lt;/save&gt; block from the given text.
        /// </summary>
        private static string ExtractSaveBlock(string text)
        {
            int start = text.IndexOf("<save", StringComparison.OrdinalIgnoreCase);
            int end = text.LastIndexOf("</save>", StringComparison.OrdinalIgnoreCase);

            if (start < 0 || end < 0)
                throw new Exception("Could not locate <save> XML block in decompressed data.");

            end += "</save>".Length;

            return text.Substring(start, end - start);
        }

        /// <summary>
        /// Parses the DataSize value from the XLZF header text.
        /// </summary>
        private static int ParseDataSize(string headerText)
        {
            const string marker = "DataSize:=";

            int idx = headerText.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
                throw new InvalidOperationException("DataSize field not found in XLZF header.");

            int valueStart = idx + marker.Length;
            int valueEnd = headerText.IndexOf('\n', valueStart);
            if (valueEnd < 0)
                valueEnd = headerText.Length;

            string value = headerText.Substring(valueStart, valueEnd - valueStart).Trim();

            if (!int.TryParse(value, out int result))
                throw new InvalidOperationException($"Could not parse DataSize value: '{value}'.");

            return result;
        }

        /// <summary>
        /// Finds the byte offset where the XLZF header ends and the compressed
        /// payload begins. Wasteland 3 headers end on the line with "DLCReq:=".
        /// </summary>
        private static int FindHeaderLength(string headerProbe)
        {
            // The header is small, so we just look for the final header line.
            const string lastHeaderKey = "DLCReq:=";

            int dlcIndex = headerProbe.IndexOf(lastHeaderKey, StringComparison.Ordinal);
            if (dlcIndex < 0)
                throw new InvalidOperationException("DLCReq field not found in XLZF header.");

            int newlineIndex = headerProbe.IndexOf('\n', dlcIndex);
            if (newlineIndex < 0)
                throw new InvalidOperationException("Could not find end of DLCReq header line.");

            // Position after the newline is the start of the compressed payload.
            return newlineIndex + 1;
        }
    }
}
