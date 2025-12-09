// Services/UpdateService.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace YD_RevitTools.LicenseManager.Services
{
    /// <summary>
    /// 自動更新服務
    /// </summary>
    public class UpdateService
    {
        private static UpdateService _instance;
        private static readonly object _lock = new object();

        // 更新伺服器 URL - 使用 GitHub Raw URL
        private const string VERSION_INFO_URL = "https://raw.githubusercontent.com/QOORST/YD_BIM_Tools/refs/heads/main/version.json";

        // GitHub Releases URL（備用）
        private const string GITHUB_RELEASES_URL = "https://api.github.com/repos/QOORST/YD_BIM_Tools/releases/latest";

        public static UpdateService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new UpdateService();
                        }
                    }
                }
                return _instance;
            }
        }

        private UpdateService() { }

        /// <summary>
        /// 獲取當前版本
        /// </summary>
        public Version GetCurrentVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            return assembly.GetName().Version;
        }

        /// <summary>
        /// 檢查更新（異步）
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.Add("User-Agent", "YD_BIM_Tools");

                    // 嘗試從主伺服器獲取版本資訊
                    string jsonResponse = await client.GetStringAsync(VERSION_INFO_URL);

                    // 檢查回應是否為空
                    if (string.IsNullOrWhiteSpace(jsonResponse))
                    {
                        return new UpdateCheckResult
                        {
                            Success = false,
                            Message = "伺服器回應為空\n\n請檢查網路連線或稍後再試。"
                        };
                    }

                    var versionInfo = JsonSerializer.Deserialize<VersionInfo>(jsonResponse);

                    if (versionInfo == null)
                    {
                        return new UpdateCheckResult
                        {
                            Success = false,
                            Message = "無法解析版本資訊\n\n伺服器回應格式錯誤。"
                        };
                    }

                    // 檢查版本號是否為空
                    if (string.IsNullOrWhiteSpace(versionInfo.Version))
                    {
                        return new UpdateCheckResult
                        {
                            Success = false,
                            Message = "版本資訊不完整\n\n版本號為空。"
                        };
                    }

                    Version currentVersion = GetCurrentVersion();
                    Version latestVersion;

                    // 嘗試解析版本號
                    try
                    {
                        latestVersion = new Version(versionInfo.Version);
                    }
                    catch (Exception ex)
                    {
                        return new UpdateCheckResult
                        {
                            Success = false,
                            Message = $"版本號格式錯誤\n\n無法解析版本號：{versionInfo.Version}\n錯誤：{ex.Message}"
                        };
                    }

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
            }
            catch (HttpRequestException ex)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    Message = $"網路連線失敗\n\n{ex.Message}\n\n" +
                             $"請檢查：\n" +
                             $"• 網路連線是否正常\n" +
                             $"• 是否可以訪問 GitHub\n" +
                             $"• 防火牆設定"
                };
            }
            catch (TaskCanceledException ex)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    Message = $"連線超時\n\n{ex.Message}\n\n請檢查網路連線後重試。"
                };
            }
            catch (System.Text.Json.JsonException ex)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    Message = $"版本資訊格式錯誤\n\n{ex.Message}\n\n" +
                             $"這可能是伺服器端的問題，請稍後再試或聯繫技術支援。"
                };
            }
            catch (Exception ex)
            {
                // 提供詳細的錯誤資訊用於診斷
                string errorDetails = $"錯誤類型：{ex.GetType().Name}\n" +
                                    $"錯誤訊息：{ex.Message}\n";

                if (ex.InnerException != null)
                {
                    errorDetails += $"內部錯誤：{ex.InnerException.Message}\n";
                }

                return new UpdateCheckResult
                {
                    Success = false,
                    Message = $"檢查更新失敗\n\n{errorDetails}\n" +
                             $"請聯繫技術支援並提供此錯誤訊息。"
                };
            }
        }

        /// <summary>
        /// 下載並安裝更新
        /// </summary>
        public async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl, IProgress<int> progress = null)
        {
            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), "YD_BIM_Tools_Update.exe");

                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10);

                    using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReportProgress = totalBytes != -1 && progress != null;

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            long totalRead = 0;
                            int bytesRead;

                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalRead += bytesRead;

                                if (canReportProgress)
                                {
                                    var progressPercentage = (int)((totalRead * 100) / totalBytes);
                                    progress.Report(progressPercentage);
                                }
                            }
                        }
                    }
                }

                // 啟動安裝程式
                LaunchInstaller(tempPath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"下載更新失敗：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 啟動安裝程式
        /// </summary>
        private void LaunchInstaller(string installerPath)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    Verb = "runas" // 以管理員權限執行
                };

                Process.Start(startInfo);

                // 提示使用者關閉 Revit
                // 注意：實際關閉 Revit 需要在 UI 層處理
            }
            catch (Exception ex)
            {
                throw new Exception($"啟動安裝程式失敗：{ex.Message}", ex);
            }
        }

        /// <summary>
        /// 檢查更新（同步版本，用於 Revit UI）
        /// </summary>
        public UpdateCheckResult CheckForUpdates()
        {
            try
            {
                var task = CheckForUpdatesAsync();
                task.Wait();
                return task.Result;
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    Message = $"檢查更新失敗：{ex.Message}"
                };
            }
        }
    }

    /// <summary>
    /// 更新檢查結果
    /// </summary>
    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
        public DateTime ReleaseDate { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// 版本資訊（從伺服器獲取）
    /// </summary>
    public class VersionInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
        public DateTime ReleaseDate { get; set; }
        public bool IsCritical { get; set; } // 是否為重要更新
        public string MinimumVersion { get; set; } // 最低相容版本
    }
}


