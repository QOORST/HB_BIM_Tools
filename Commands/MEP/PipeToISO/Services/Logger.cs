using System;
using System.IO;
using System.Text;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    /// <summary>
    /// 簡單的日誌記錄工具
    /// </summary>
    public static class Logger
    {
        private static string _logFilePath;
        private static object _lockObject = new object();

        static Logger()
        {
            // 在桌面建立日誌資料夾
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string logFolder = Path.Combine(desktopPath, "PipeToISO_Logs");
            
            if (!Directory.Exists(logFolder))
            {
                Directory.CreateDirectory(logFolder);
            }

            _logFilePath = Path.Combine(logFolder, $"PipeToISO_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        }

        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        public static void Warning(string message)
        {
            WriteLog("WARNING", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(message);
            
            if (ex != null)
            {
                sb.AppendLine($"例外類型: {ex.GetType().FullName}");
                sb.AppendLine($"例外訊息: {ex.Message}");
                sb.AppendLine($"堆疊追蹤:\n{ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    sb.AppendLine($"內部例外: {ex.InnerException.Message}");
                    sb.AppendLine($"內部堆疊:\n{ex.InnerException.StackTrace}");
                }
            }
            
            WriteLog("ERROR", sb.ToString());
        }

        private static void WriteLog(string level, string message)
        {
            lock (_lockObject)
            {
                try
                {
                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // 日誌寫入失敗不應影響主流程
                }
            }
        }

        public static string GetLogFilePath()
        {
            return _logFilePath;
        }
    }
}
