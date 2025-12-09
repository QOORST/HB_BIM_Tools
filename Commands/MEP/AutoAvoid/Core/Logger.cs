using System;
using System.IO;
using System.Text;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath;
        private static LogLevel _minLevel = LogLevel.Info;

        static Logger()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string logDir = Path.Combine(appData, "AR_AutoAvoidRouting", "Logs");
                Directory.CreateDirectory(logDir);
                _logFilePath = Path.Combine(logDir, $"AR_AutoAvoid_{DateTime.Now:yyyyMMdd}.log");
            }
            catch
            {
                _logFilePath = null;
            }
        }

        public static void SetMinLevel(LogLevel level)
        {
            _minLevel = level;
        }

        public static void Debug(string message)
        {
            Log(LogLevel.Debug, message);
        }

        public static void Info(string message)
        {
            Log(LogLevel.Info, message);
        }

        public static void Warning(string message)
        {
            Log(LogLevel.Warning, message);
        }

        public static void Error(string message, Exception ex = null)
        {
            if (ex != null)
                message = $"{message}\n例外詳情: {ex.Message}\n堆疊追蹤:\n{ex.StackTrace}";
            Log(LogLevel.Error, message);
        }

        private static void Log(LogLevel level, string message)
        {
            if (level < _minLevel) return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string logEntry = $"[{timestamp}] [{level}] {message}";

            lock (_lock)
            {
                try
                {
                    // 輸出到控制台
                    System.Diagnostics.Debug.WriteLine(logEntry);

                    // 輸出到檔案
                    if (!string.IsNullOrEmpty(_logFilePath))
                    {
                        File.AppendAllText(_logFilePath, logEntry + Environment.NewLine, Encoding.UTF8);
                    }
                }
                catch
                {
                    // 忽略日誌記錄錯誤，避免影響主程式
                }
            }
        }

        public static string GetLogFilePath() => _logFilePath;
    }
}
