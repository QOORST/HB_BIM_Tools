using System;
using System.IO;
using System.Text;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish
{
    /// <summary>
    /// 簡單的日誌記錄器
    /// </summary>
    public static class Logger
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
            "YD_RoomFinish_Debug.log");

        /// <summary>
        /// 記錄訊息到日誌檔案
        /// </summary>
        public static void Log(string message)
        {
            try
            {
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(LogFilePath, logEntry + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 忽略日誌錯誤
            }
        }

        /// <summary>
        /// 清除日誌檔案
        /// </summary>
        public static void ClearLog()
        {
            try
            {
                if (File.Exists(LogFilePath))
                    File.Delete(LogFilePath);
            }
            catch
            {
                // 忽略清除錯誤
            }
        }
    }
}

