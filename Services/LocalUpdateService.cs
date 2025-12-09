// Services/LocalUpdateService.cs
using System;
using System.IO;
using System.Text.Json;

namespace YD_RevitTools.LicenseManager.Services
{
    /// <summary>
    /// 本地更新服務（用於測試或離線環境）
    /// </summary>
    public class LocalUpdateService
    {
        private const string LOCAL_VERSION_FILE = "version.json";
        private const string LOCAL_INSTALLER_PATH = "YD_BIM_Tools_Setup.exe";

        /// <summary>
        /// 從本地檔案檢查更新
        /// </summary>
        public static UpdateCheckResult CheckLocalUpdate(string versionFilePath = null)
        {
            try
            {
                // 如果沒有指定路徑，使用預設路徑
                if (string.IsNullOrEmpty(versionFilePath))
                {
                    // 嘗試從多個位置查找
                    string[] searchPaths = new[]
                    {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "YD_BIM_Tools", LOCAL_VERSION_FILE),
                        Path.Combine(Path.GetTempPath(), "YD_BIM_Tools", LOCAL_VERSION_FILE),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LOCAL_VERSION_FILE)
                    };

                    foreach (var path in searchPaths)
                    {
                        if (File.Exists(path))
                        {
                            versionFilePath = path;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(versionFilePath))
                    {
                        return new UpdateCheckResult
                        {
                            Success = false,
                            Message = "找不到本地版本檔案"
                        };
                    }
                }

                // 讀取版本檔案
                string jsonContent = File.ReadAllText(versionFilePath);
                var versionInfo = JsonSerializer.Deserialize<VersionInfo>(jsonContent);

                if (versionInfo == null)
                {
                    return new UpdateCheckResult
                    {
                        Success = false,
                        Message = "無法解析版本資訊"
                    };
                }

                // 獲取當前版本
                var updateService = UpdateService.Instance;
                Version currentVersion = updateService.GetCurrentVersion();
                Version latestVersion = new Version(versionInfo.Version);

                bool hasUpdate = latestVersion > currentVersion;

                return new UpdateCheckResult
                {
                    Success = true,
                    HasUpdate = hasUpdate,
                    CurrentVersion = currentVersion.ToString(),
                    LatestVersion = versionInfo.Version,
                    DownloadUrl = versionInfo.DownloadUrl,
                    ReleaseNotes = versionInfo.ReleaseNotes,
                    ReleaseDate = versionInfo.ReleaseDate,
                    Message = hasUpdate
                        ? $"發現新版本 {versionInfo.Version}"
                        : "您已使用最新版本"
                };
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    Message = $"檢查本地更新失敗：{ex.Message}"
                };
            }
        }

        /// <summary>
        /// 創建測試用的版本檔案
        /// </summary>
        public static void CreateTestVersionFile(string outputPath, string version, string downloadUrl, string releaseNotes)
        {
            var versionInfo = new VersionInfo
            {
                Version = version,
                DownloadUrl = downloadUrl,
                ReleaseNotes = releaseNotes,
                ReleaseDate = DateTime.Now,
                IsCritical = false,
                MinimumVersion = "2.0.0"
            };

            string json = JsonSerializer.Serialize(versionInfo, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(outputPath, json);
        }
    }
}

