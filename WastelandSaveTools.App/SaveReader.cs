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
        /// Reads a Wasteland 3 save which is actually:
        /// [XML content][binary junk footer]
        ///
        /// This method extracts the XML cleanly and ignores the footer.
        /// </summary>
        public static string ReadSave(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Save not found.", path);

            // Read raw bytes (the file is NOT pure XML)
            byte[] data = File.ReadAllBytes(path);

            // Try decoding as UTF8, replacing invalid bytes so we can search
            string text = Encoding.UTF8.GetString(data);

            int start = text.IndexOf("<save>", StringComparison.OrdinalIgnoreCase);
            int end = text.IndexOf("</save>", StringComparison.OrdinalIgnoreCase);

            if (start < 0 || end < 0)
                throw new Exception("Could not locate <save> XML block.");

            end += "</save>".Length;

            //test

            string xml = text.Substring(start, end - start);

            // Validate XML
            XDocument.Parse(xml);

            return xml;
        }
    }
}
