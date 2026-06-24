using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace YD_RevitTools.LicenseManager.Services
{
    public class UpdateService
    {
        private static UpdateService _instance;
        private static readonly object _lock = new object();

        private const string VERSION_INFO_URL = "https://raw.githubusercontent.com/QOORST/YD_BIM_Tools/main/version.json";
        private const string GITHUB_RELEASES_URL = "https://api.github.com/repos/QOORST/YD_BIM_Tools/releases/latest";

        public static UpdateService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null) _instance = new UpdateService();
                    }
                }
                return _instance;
            }
        }

        private UpdateService() { }

        public Version GetCurrentVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            return assembly.GetName().Version;
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.Add("User-Agent", "YD_BIM_Tools");

                    string jsonResponse = await client.GetStringAsync(VERSION_INFO_URL);
                    if (string.IsNullOrWhiteSpace(jsonResponse))
                    {
                        return new UpdateCheckResult { Success = false, Message = "更新資訊為空白。" };
                    }

                    VersionInfo versionInfo = JsonSerializer.Deserialize<VersionInfo>(
                        jsonResponse,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    if (versionInfo == null || string.IsNullOrWhiteSpace(versionInfo.Version))
                    {
                        return new UpdateCheckResult { Success = false, Message = "更新資訊格式不正確。" };
                    }

                    Version currentVersion = NormalizeVersion(GetCurrentVersion().ToString());
                    Version latestVersion = NormalizeVersion(versionInfo.Version);

                    // 雙來源保護：若 version.json 較舊，改取 GitHub release tag
                    Version ghLatest = await TryGetGithubLatestVersionAsync(client);
                    if (ghLatest != null && ghLatest > latestVersion)
                    {
                        latestVersion = ghLatest;
                        versionInfo.Version = ghLatest.ToString();
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
                        Message = hasUpdate ? $"發現新版本 {versionInfo.Version}" : "目前已是最新版本。"
                    };
                }
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

        private async Task<Version> TryGetGithubLatestVersionAsync(HttpClient client)
        {
            try
            {
                string releaseJson = await client.GetStringAsync(GITHUB_RELEASES_URL);
                using (JsonDocument doc = JsonDocument.Parse(releaseJson))
                {
                    if (doc.RootElement.TryGetProperty("tag_name", out JsonElement tagElement))
                    {
                        string tag = tagElement.GetString();
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            return NormalizeVersion(tag);
                        }
                    }
                }
            }
            catch
            {
                // ignore fallback failure
            }
            return null;
        }

        private static Version NormalizeVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new Version(0, 0, 0, 0);
            }

            string v = raw.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                v = v.Substring(1);
            }

            if (Version.TryParse(v, out Version parsed))
            {
                return parsed;
            }

            string[] parts = v.Split('.');
            int[] nums = new[] { 0, 0, 0, 0 };
            for (int i = 0; i < parts.Length && i < 4; i++)
            {
                int.TryParse(parts[i], out nums[i]);
            }
            return new Version(nums[0], nums[1], nums[2], nums[3]);
        }

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

                        long totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        bool canReportProgress = totalBytes > 0 && progress != null;

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
                                    int percent = (int)((totalRead * 100) / totalBytes);
                                    progress.Report(percent);
                                }
                            }
                        }
                    }
                }

                LaunchInstaller(tempPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DownloadAndInstallUpdateAsync failed: {ex.Message}");
                return false;
            }
        }

        private void LaunchInstaller(string installerPath)
        {
            try
            {
                // 以背景腳本方式等待 Revit 關閉後再安裝，避免檔案鎖定
                string waitScript = Path.Combine(Path.GetTempPath(), "YD_BIM_WaitAndInstall.cmd");
                string scriptContent =
                    "@echo off\r\n" +
                    "setlocal\r\n" +
                    ":WAITREVIT\r\n" +
                    "tasklist /FI \"IMAGENAME eq Revit.exe\" | find /I \"Revit.exe\" >nul\r\n" +
                    "if %ERRORLEVEL%==0 (\r\n" +
                    "  timeout /t 2 /nobreak >nul\r\n" +
                    "  goto WAITREVIT\r\n" +
                    ")\r\n" +
                    "start \"\" \"" + installerPath + "\"\r\n" +
                    "del \"%~f0\" >nul 2>nul\r\n";

                File.WriteAllText(waitScript, scriptContent);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = waitScript,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                throw new Exception($"啟動更新安裝程序失敗：{ex.Message}", ex);
            }
        }

        public UpdateCheckResult CheckForUpdates()
        {
            try
            {
                return CheckForUpdatesAsync().GetAwaiter().GetResult();
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

    public class VersionInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
        public DateTime ReleaseDate { get; set; }
        public bool IsCritical { get; set; }
        public string MinimumVersion { get; set; }
    }
}
