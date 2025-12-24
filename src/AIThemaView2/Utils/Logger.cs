using System;
using System.IO;

namespace AIThemaView2.Utils
{
    public interface ILogger
    {
        void Log(string message);
        void LogError(string message, Exception? ex = null);
    }

    public class Logger : ILogger
    {
        private readonly string _logFilePath;
        private readonly object _lockObject = new object();

        public Logger(string logFilePath = "logs/app.log")
        {
            _logFilePath = logFilePath;

            // Ensure logs directory exists
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public void Log(string message)
        {
            WriteToFile($"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public void LogError(string message, Exception? ex = null)
        {
            var errorMessage = $"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            if (ex != null)
            {
                errorMessage += $"\nException: {ex.Message}\nStackTrace: {ex.StackTrace}";
            }
            WriteToFile(errorMessage);
        }

        private void WriteToFile(string message)
        {
            lock (_lockObject)
            {
                try
                {
                    File.AppendAllText(_logFilePath, message + Environment.NewLine);
                    // Also write to console for debugging
                    Console.WriteLine(message);
                }
                catch
                {
                    // Silently fail if can't write to log file
                }
            }
        }
    }
}
