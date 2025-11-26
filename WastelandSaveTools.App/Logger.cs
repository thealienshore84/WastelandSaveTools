using System;
using System.IO;

namespace WastelandSaveTools.App
{
    internal static class Logger
    {
        private static readonly object _sync = new();
        private static string? _logFilePath;

        public static string? LogFilePath => _logFilePath;

        /// <summary>
        /// Initialize logging to a timestamped file in the given directory.
        /// Safe to call multiple times - later calls overwrite the path.
        /// </summary>
        public static void Init(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(outputDirectory);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logFilePath = Path.Combine(outputDirectory, $"W3Tools_{timestamp}.log");
                Log("=== Log started ===");
            }
            catch
            {
                // If logging setup fails, just disable logging silently.
                _logFilePath = null;
            }
        }

        public static void Log(string message)
        {
            if (string.IsNullOrEmpty(_logFilePath))
            {
                return;
            }

            try
            {
                var line = $"{DateTime.Now:O} {message}{Environment.NewLine}";
                lock (_sync)
                {
                    File.AppendAllText(_logFilePath, line);
                }
            }
            catch
            {
                // Logging must never throw.
            }
        }

        public static void LogException(string context, Exception ex)
        {
            Log($"{context}: {ex}");
        }
    }
}
