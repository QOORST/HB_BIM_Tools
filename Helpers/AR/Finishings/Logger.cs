using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace YD_RevitTools.LicenseManager.Helpers.AR.Finishings
{
    public static class Logger
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "AR_Finishings_Debug.log");

        private static bool _loggingEnabled = true;

        /// <summary>
        /// 記錄訊息到日誌檔案
        /// </summary>
        public static void Log(string message)
        {
            if (!_loggingEnabled) return;

            try
            {
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(LogFilePath, logEntry + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // 如果日誌寫入失敗，至少輸出到 Debug
                Debug.WriteLine($"Logger 寫入失敗: {ex.Message}");
                Debug.WriteLine($"原始訊息: {message}");

                // 禁用後續日誌以避免重複錯誤
                _loggingEnabled = false;
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
                {
                    File.Delete(LogFilePath);
                    _loggingEnabled = true; // 重新啟用日誌
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Logger 清除失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 取得日誌檔案路徑
        /// </summary>
        public static string GetLogFilePath() => LogFilePath;
    }
}