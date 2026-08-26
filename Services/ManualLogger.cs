using System.Text;

namespace Deltanium.Blocker.Services
{
    /// <summary>
    /// Manual logger that writes directly to file for real-time logging
    /// </summary>
    public static class ManualLogger
    {
        private static readonly object _lock = new object();
        private static string? _logDirectory;
        private static string? _logPrefix;

        /// <summary>
        /// Initialize the manual logger
        /// </summary>
        public static void Initialize(string logDirectory, string logPrefix = "blocker")
        {
            _logDirectory = logDirectory;
            _logPrefix = logPrefix;

            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }

        private static string GetLogFilePath()
        {
            if (_logDirectory == null || _logPrefix == null)
            {
                throw new InvalidOperationException("ManualLogger not initialized. Call Initialize() first.");
            }

            var dateStr = DateTime.Now.ToString("yyyyMMdd");
            return Path.Combine(_logDirectory, $"{_logPrefix}-{dateStr}.log");
        }

        public static void Log(string level, string message, Exception? exception = null)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var logFile = GetLogFilePath();

                var logEntry = new StringBuilder();
                logEntry.Append($"{timestamp} [{level}] {message}");

                if (exception != null)
                {
                    logEntry.AppendLine();
                    logEntry.Append($"Exception: {exception.GetType().Name}: {exception.Message}");
                    if (exception.StackTrace != null)
                    {
                        logEntry.AppendLine();
                        logEntry.Append($"StackTrace: {exception.StackTrace}");
                    }
                }
                logEntry.AppendLine();

                lock (_lock)
                {
                    File.AppendAllText(logFile, logEntry.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // best-effort logging only
            }
        }

        public static void Info(string message) => Log("INF", message);
        public static void Warning(string message) => Log("WRN", message);
        public static void Error(string message, Exception? exception = null) => Log("ERR", message, exception);
        public static void Debug(string message) => Log("DBG", message);
    }
}
