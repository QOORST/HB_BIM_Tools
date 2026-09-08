using System;
using System.Diagnostics;
using System.IO;

using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;

namespace YD_RevitTools.LicenseManager.Services
{
    public class UpdateService
    {
        private static UpdateService _instance;
        private static readonly object _lock = new object();

        private const string VERSION_INFO_URL = "https://raw.githubusercontent.com/QOORST/HB_BIM_Tools/main/version.json";
        private const string GITHUB_RELEASES_URL = "https://api.github.com/repos/QOORST/HB_BIM_Tools/releases/latest";
        private const string TRUSTED_SIGNER_THUMBPRINT = "5EBE6DDEEBEBE5194CBDC9E71CDE8E6BB91AB166";
        private const string TRUSTED_SIGNER_SUBJECT = "CN=LAN";

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
                    client.DefaultRequestHeaders.Add("User-Agent", "HB_BIM_Tools");

                    string jsonResponse = await TryGetStringAsync(client, VERSION_INFO_URL);
                    bool usingLocalVersionInfo = false;
                    if (string.IsNullOrWhiteSpace(jsonResponse))
                    {
                        jsonResponse = TryReadLocalVersionJson();
                        usingLocalVersionInfo = !string.IsNullOrWhiteSpace(jsonResponse);
                    }

                    if (string.IsNullOrWhiteSpace(jsonResponse))
                    {
                        return new UpdateCheckResult
                        {
                            Success = false,
                            Message = "目前無法取得線上更新資訊，且本機未找到 version.json。"
                        };
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

                    // Remote fallback: only ask GitHub Releases when the online version file is available.
                    if (!usingLocalVersionInfo)
                    {
                        Version ghLatest = await TryGetGithubLatestVersionAsync(client);
                        if (ghLatest != null && ghLatest > latestVersion)
                        {
                            latestVersion = ghLatest;
                            versionInfo.Version = ghLatest.ToString();
                        }
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
                        Message = hasUpdate ? $"發現新版本 {versionInfo.Version}" : (usingLocalVersionInfo ? "目前無可用的線上更新資訊，已使用本機版本資訊確認目前版本。" : "目前已是最新版本。")
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

        private async Task<string> TryGetStringAsync(HttpClient client, string url)
        {
            try
            {
                return await client.GetStringAsync(url);
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"TryGetStringAsync failed: {url} - {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"TryGetStringAsync timeout: {url} - {ex.Message}");
                return null;
            }
        }

        private string TryReadLocalVersionJson()
        {
            string[] candidatePaths =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.json"),
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "version.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HB_BIM_Tools", "version.json"),
                Path.Combine(Path.GetTempPath(), "HB_BIM_Tools", "version.json")
            };

            foreach (string path in candidatePaths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        return File.ReadAllText(path);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TryReadLocalVersionJson failed: {path} - {ex.Message}");
                }
            }

            return null;
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
                string updateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HB_BIM_Tools", "Updates", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(updateDirectory);
                string tempPath = Path.Combine(updateDirectory, "HB_BIM_Tools_Update.exe");

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

                if (!IsTrustedInstaller(tempPath, out string signatureError))
                {
                    Debug.WriteLine($"Downloaded installer signature verification failed: {signatureError}");
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        // Ignore cleanup failure; the installer will not be launched.
                    }
                    return false;
                }

                UpdateInstallerLauncher.Launch(tempPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DownloadAndInstallUpdateAsync failed: {ex.Message}");
                return false;
            }
        }


        private bool IsTrustedInstaller(string installerPath, out string errorMessage)
        {
            errorMessage = null;

            try
            {
                if (!File.Exists(installerPath))
                {
                    errorMessage = "找不到下載的安裝檔。";
                    return false;
                }

                X509Certificate signer = X509Certificate.CreateFromSignedFile(installerPath);
                X509Certificate2 signerCertificate = new X509Certificate2(signer);
                string thumbprint = (signerCertificate.Thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
                string subject = signerCertificate.Subject ?? string.Empty;

                if (!string.Equals(thumbprint, TRUSTED_SIGNER_THUMBPRINT, StringComparison.OrdinalIgnoreCase) ||
                    !subject.Contains(TRUSTED_SIGNER_SUBJECT))
                {
                    errorMessage = "更新安裝檔簽署者不是受信任的 LAN 憑證。";
                    return false;
                }

                if (!signerCertificate.Verify())
                {
                    errorMessage = "更新安裝檔簽章無法建立信任鏈。";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
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
