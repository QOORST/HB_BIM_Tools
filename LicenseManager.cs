// LicenseManager.cs (更新部分)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Newtonsoft.Json;

namespace YD_RevitTools.LicenseManager
{
    // 授權類型列舉
    public enum LicenseType
    {
        Trial,      // 試用版
        Standard,   // 標準版
        Professional // 專業版
    }

    // 授權資訊類別
    public class LicenseInfo
    {
        public bool IsEnabled { get; set; }
        public LicenseType LicenseType { get; set; }
        public string UserName { get; set; }
        public string Company { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string LicenseKey { get; set; }
        public string MachineCode { get; set; }

        // 取得授權類型的顯示名稱
        public string GetLicenseTypeName()
        {
            switch (LicenseType)
            {
                case LicenseType.Trial:
                    return "試用版";
                case LicenseType.Standard:
                    return "標準版";
                case LicenseType.Professional:
                    return "專業版";
                default:
                    return "未知";
            }
        }

        // 取得授權期限（天數）
        public int GetLicenseDuration()
        {
            switch (LicenseType)
            {
                case LicenseType.Trial:
                    return 30;
                case LicenseType.Standard:
                    return 365;
                case LicenseType.Professional:
                    return 365;
                default:
                    return 0;
            }
        }

        // 檢查授權類型是否允許特定功能
        public bool HasFeature(string featureName)
        {
            // 根據不同的授權類型返回功能權限
            switch (LicenseType)
            {
                case LicenseType.Trial:
                    // 試用版：基本功能
                    return featureName == "BasicFeatures";

                case LicenseType.Standard:
                    // 標準版：基本功能 + 標準功能
                    return featureName == "BasicFeatures" ||
                           featureName == "StandardFeatures";

                case LicenseType.Professional:
                    // 專業版：所有功能
                    return true;

                default:
                    return false;
            }
        }

        internal static bool VerifyLicenseSignature(byte[] payload, byte[] signature)
        {
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(ParseRsaPublicKeyXml(LicenseManager.LICENSE_PUBLIC_KEY_XML));
                return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }

        /// <summary>
        /// 將 RSAKeyValue XML 格式解析為 RSAParameters（公鑰：Modulus + Exponent）
        /// </summary>
        private static RSAParameters ParseRsaPublicKeyXml(string xml)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            string modulusB64 = doc.SelectSingleNode("//Modulus")?.InnerText ?? throw new CryptographicException("XML 中缺少 Modulus");
            string exponentB64 = doc.SelectSingleNode("//Exponent")?.InnerText ?? throw new CryptographicException("XML 中缺少 Exponent");
            return new RSAParameters
            {
                Modulus = Convert.FromBase64String(modulusB64),
                Exponent = Convert.FromBase64String(exponentB64)
            };
        }
    }

    public class LicenseManager
    {
        private static LicenseManager _instance;
        private static readonly object _lock = new object();
        private LicenseInfo _currentLicense;
        internal const string LICENSE_PUBLIC_KEY_XML = "<RSAKeyValue><Modulus>vOabCDg4iCCKhHUjKis6vYfyL89Q0znE50X+/LOINwnakq672O8e2lvBtcXyWClJMmhNJ9v6hG8C9uZbPvsWKJss1Ng0hBs7OHxns/2qauAXxzxvmnoS5VpS8W6avea1nViUi7HAf5qPbm/XfTGXVlk841IV0c0hSHHpK4RwwB/PvtCupOavFy6QPf2LKEPXrODJhur2vD348NkXfVGlBxIjhCOeKPlXNSFfHo79CEr4Hcy/3Y5j9veIZNPchZ7zI7DrJ+s7w/ek/ySwXQOM+i5pimp1505nsuX41mhh0nAA3GU5v8+WhDITyIGzgHxhoVqr8j6DCJaDtL/K1VyWqQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        // 功能權限映射表
        private static readonly Dictionary<LicenseType, HashSet<string>> FeatureMap = new Dictionary<LicenseType, HashSet<string>>
        {
            [LicenseType.Trial] = new HashSet<string>
            {
                // 試用版 - 基本功能
                "Tool1.BasicFeature",
                "Tool2.BasicFeature",
                // AR_Formwork - 模板工具基本功能
                "Formwork.Generate",          // 模板生成
                "FormworkGeneration",         // 模板生成 (別名)
                "Formwork.Delete",            // 刪除模板
                "DeleteFormwork",             // 刪除模板 (別名)
                // AR_Finishings - 裝修工具基本功能
                "Finishings.Generate",        // 裝修生成
                "Finishings.Delete",          // 刪除裝修
                "DeleteFinishings",           // 刪除裝修 (別名)
                // AR_AutoJoin - 接合工具基本功能
                "AutoJoin",                   // 自動接合
                "JoinToPicked",               // 接合到選取
                // COBie - COBie 工具基本功能
                "COBie.FieldManager",         // 欄位管理
                "COBie.ExportTemplate",       // 範本匯出
                // Family - 族參數工具基本功能
                "Family.ParameterSlider",     // 族參數滑桿
                "Family.ProjectSlider",       // 專案參數滑桿
                // MEP - 機電工具基本功能
                "MEP.PipeSleeve",             // 管線套管
                // Data - 資料工具基本功能
                "Schedule.Export",            // 明細表匯出
                "Data.ModelManager",          // 模型資料管理
                "Data.BimStandardAudit"       // BIM 標準檢查
            },
            [LicenseType.Standard] = new HashSet<string>
            {
                // 標準版 - 基本 + 標準功能
                "Tool1.BasicFeature",
                "Tool1.FloorCopy",
                "Tool1.FloorOffset",
                "Tool2.BasicFeature",
                "Tool2.FamilyCheck",
                "Tool3.BasicFeature",
                "Tool3.ParameterExport",
                // AR_Formwork - 模板工具標準功能
                "Formwork.Generate",          // 模板生成
                "FormworkGeneration",         // 模板生成 (別名)
                "Formwork.PickFace",          // 面選模板
                "FaceFormwork",               // 面選模板 (別名)
                "Formwork.Delete",            // 刪除模板
                "DeleteFormwork",             // 刪除模板 (別名)
                "Formwork.ExportCsv",         // 匯出 CSV
                "ExportCSV",                  // 匯出 CSV (別名)
                "Formwork.SmartFormwork",     // 智能模板
                "SmartFormwork",              // 智能模板 (別名)
                "Formwork.StructuralAnalysis", // 結構分析
                "StructuralAnalysis",         // 結構分析 (別名)
                // AR_Finishings - 裝修工具標準功能
                "Finishings.Generate",        // 裝修生成
                "Finishings.Delete",          // 刪除裝修
                "DeleteFinishings",           // 刪除裝修 (別名)
                // AR_AutoJoin - 接合工具標準功能
                "AutoJoin",                   // 自動接合
                "JoinToPicked",               // 接合到選取
                // COBie - COBie 工具標準功能
                "COBie.FieldManager",         // 欄位管理
                "COBie.Export",               // COBie 匯出
                "COBie.ExportTemplate",       // 範本匯出
                "COBie.Import",               // COBie 匯入
                // Family - 族參數工具標準功能
                "Family.ParameterSlider",     // 族參數滑桿
                "Family.ProjectSlider",       // 專案參數滑桿
                // MEP - 機電工具標準功能
                "MEP.PipeSleeve",             // 管線套管
                // Data - 資料工具標準功能
                "Schedule.Export",            // 明細表匯出
                "Data.ModelManager",          // 模型資料管理
                "Data.BimStandardAudit",      // BIM 標準檢查
                "Data.BimStandardReport",     // BIM 標準檢查報告匯出
                "Data.ClarificationDeckExport" // 釋疑簡報快速產出
            },
            [LicenseType.Professional] = new HashSet<string>
            {
                // 專業版 - 所有功能 (使用 * 表示)
                "*"
            }
        };

        public static LicenseManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new LicenseManager();
                    }
                }
                return _instance;
            }
        }

        private LicenseManager()
        {
            LoadLicense();
        }

        private string LicenseFilePath => GetPrimaryLicenseFilePath();

        private string GetPrimaryLicenseFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string licenseFolder = Path.Combine(appData, "YD", "RevitTools");

            if (!Directory.Exists(licenseFolder))
                Directory.CreateDirectory(licenseFolder);

            return Path.Combine(licenseFolder, "license.dat");
        }

        private IEnumerable<string> EnumerateLicenseFileCandidates()
        {
            string primary = GetPrimaryLicenseFilePath();
            yield return primary;

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
                yield return Path.Combine(programData, "YD", "RevitTools", "license.dat");

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
                yield return Path.Combine(localAppData, "YD", "RevitTools", "license.dat");
        }

        private void LoadLicense()
        {
            try
            {
                foreach (var path in EnumerateLicenseFileCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!File.Exists(path)) continue;
                    try
                    {
                        string encryptedData = File.ReadAllText(path);
                        string decryptedData = Decrypt(encryptedData);
                        var loaded = JsonConvert.DeserializeObject<LicenseInfo>(decryptedData);
                        if (loaded == null) continue;
                        if (!VerifyStoredLicense(loaded, out _)) continue;

                        _currentLicense = loaded;

                        string primary = GetPrimaryLicenseFilePath();
                        if (!path.Equals(primary, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                File.WriteAllText(primary, encryptedData);
                            }
                            catch { }
                        }
                        return;
                    }
                    catch
                    {
                        // 嘗試下一個候選路徑
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"授權載入失敗: {ex.Message}");
                _currentLicense = null;
            }
        }

        public LicenseValidationResult ValidateLicense()
        {
            if (_currentLicense == null)
            {
                return new LicenseValidationResult
                {
                    IsValid = false,
                    Message = "找不到授權文件",
                    Severity = ValidationSeverity.Error
                };
            }

            if (!_currentLicense.IsEnabled)
            {
                return new LicenseValidationResult
                {
                    IsValid = false,
                    Message = "授權未啟用",
                    Severity = ValidationSeverity.Error
                };
            }

            if (!VerifyStoredLicense(_currentLicense, out string storedLicenseError))
            {
                return new LicenseValidationResult
                {
                    IsValid = false,
                    Message = storedLicenseError,
                    Severity = ValidationSeverity.Error
                };
            }

            if (DateTime.Now > _currentLicense.ExpiryDate)
            {
                return new LicenseValidationResult
                {
                    IsValid = false,
                    Message = $"授權已過期 (到期日: {_currentLicense.ExpiryDate:yyyy-MM-dd})",
                    Severity = ValidationSeverity.Error
                };
            }

            if (DateTime.Now < _currentLicense.StartDate)
            {
                return new LicenseValidationResult
                {
                    IsValid = false,
                    Message = $"授權尚未生效 (啟用日期: {_currentLicense.StartDate:yyyy-MM-dd})",
                    Severity = ValidationSeverity.Error
                };
            }

            int daysUntilExpiry = (_currentLicense.ExpiryDate - DateTime.Now).Days;

            // 根據授權類型設定不同的警告閾值
            int warningThreshold = _currentLicense.LicenseType == LicenseType.Trial ? 7 : 30;

            if (daysUntilExpiry <= warningThreshold && daysUntilExpiry > 0)
            {
                return new LicenseValidationResult
                {
                    IsValid = true,
                    Message = $"授權有效 (剩餘 {daysUntilExpiry} 天)",
                    LicenseInfo = _currentLicense,
                    DaysUntilExpiry = daysUntilExpiry,
                    Severity = ValidationSeverity.Warning
                };
            }

            return new LicenseValidationResult
            {
                IsValid = true,
                Message = "授權有效",
                LicenseInfo = _currentLicense,
                DaysUntilExpiry = daysUntilExpiry,
                Severity = ValidationSeverity.Success
            };
        }

        /// <summary>
        /// 檢查是否有權限使用指定功能
        /// </summary>
        /// <param name="featureName">功能名稱，格式: "ToolName.FeatureName"</param>
        /// <returns>true 表示有權限，false 表示無權限</returns>
        public bool HasFeatureAccess(string featureName)
        {
            // 如果授權無效，沒有任何權限
            var result = ValidateLicense();
            if (!result.IsValid || result.LicenseInfo == null)
            {
                return false;
            }

            // 取得當前授權類型的功能集合
            if (FeatureMap.TryGetValue(_currentLicense.LicenseType, out var features))
            {
                // 專業版使用 "*" 表示所有功能
                if (features.Contains("*"))
                {
                    return true;
                }

                // 檢查具體功能權限
                return features.Contains(featureName);
            }

            return false;
        }

        /// <summary>
        /// 取得當前授權類型可用的所有功能列表
        /// </summary>
        public HashSet<string> GetAvailableFeatures()
        {
            var result = ValidateLicense();
            if (!result.IsValid || !FeatureMap.TryGetValue(_currentLicense.LicenseType, out var features))
            {
                return new HashSet<string>();
            }

            return new HashSet<string>(features);
        }

        public bool SaveLicense(LicenseInfo license)
        {
            try
            {
                if (license == null)
                    return false;

                if (license.IsEnabled && !VerifyStoredLicense(license, out string validationError))
                {
                    System.Diagnostics.Debug.WriteLine($"授權儲存前驗證失敗: {validationError}");
                    return false;
                }

                string jsonData = JsonConvert.SerializeObject(license, Newtonsoft.Json.Formatting.Indented);
                string encryptedData = Encrypt(jsonData);
                File.WriteAllText(LicenseFilePath, encryptedData);
                _currentLicense = license;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"授權儲存失敗: {ex.Message}");
                return false;
            }
        }

        public LicenseInfo GetCurrentLicense()
        {
            return _currentLicense;
        }

        public void ReloadLicense()
        {
            LoadLicense();
        }

        public bool RemoveLicense()
        {
            try
            {
                foreach (var path in EnumerateLicenseFileCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch { }
                }
                _currentLicense = null;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"授權刪除失敗: {ex.Message}");
                return false;
            }
        }

        private string Encrypt(string plainText)
        {
            byte[] data = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(encrypted);
        }

        private string Decrypt(string encryptedText)
        {
            byte[] data = Convert.FromBase64String(encryptedText);
            byte[] decrypted = ProtectedData.Unprotect(data, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(decrypted);
        }

        private bool TryReadSignedLicense(string licenseKey, out LicenseInfo license, out string errorMessage)
        {
            license = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                errorMessage = "授權金鑰不存在。";
                return false;
            }

            string cleanKey = licenseKey.Replace("\r", "").Replace("\n", "").Replace(" ", "").Trim();
            int dotIdx = cleanKey.LastIndexOf('.');
            if (dotIdx <= 0 || dotIdx >= cleanKey.Length - 1)
            {
                errorMessage = "授權金鑰格式錯誤，請使用新版簽章授權碼。";
                return false;
            }

            try
            {
                string jsonB64 = cleanKey.Substring(0, dotIdx);
                string sigB64 = cleanKey.Substring(dotIdx + 1);
                byte[] jsonBytes = Convert.FromBase64String(jsonB64);
                byte[] sigBytes = Convert.FromBase64String(sigB64);

                if (!LicenseInfo.VerifyLicenseSignature(jsonBytes, sigBytes))
                {
                    errorMessage = "授權金鑰簽章無效。";
                    return false;
                }

                license = JsonConvert.DeserializeObject<LicenseInfo>(Encoding.UTF8.GetString(jsonBytes));
                if (license == null)
                {
                    errorMessage = "授權金鑰解析失敗。";
                    return false;
                }

                return true;
            }
            catch (FormatException)
            {
                errorMessage = "授權金鑰格式錯誤（無效的 Base64 編碼）。";
                return false;
            }
            catch (JsonException ex)
            {
                errorMessage = $"授權金鑰格式錯誤（無效的 JSON）：{ex.Message}";
                return false;
            }
            catch (CryptographicException ex)
            {
                errorMessage = $"授權金鑰簽章驗證失敗：{ex.Message}";
                return false;
            }
        }

        private bool VerifyStoredLicense(LicenseInfo storedLicense, out string errorMessage)
        {
            errorMessage = null;

            if (storedLicense == null)
            {
                errorMessage = "找不到授權文件";
                return false;
            }

            if (!TryReadSignedLicense(storedLicense.LicenseKey, out LicenseInfo signedLicense, out errorMessage))
            {
                return false;
            }

            if (storedLicense.LicenseType != signedLicense.LicenseType ||
                !StringEquals(storedLicense.UserName, signedLicense.UserName) ||
                !StringEquals(storedLicense.Company, signedLicense.Company) ||
                storedLicense.StartDate != signedLicense.StartDate ||
                storedLicense.ExpiryDate != signedLicense.ExpiryDate)
            {
                errorMessage = "授權文件內容與簽章授權碼不一致，可能已被修改。";
                return false;
            }

            // 取得當前機器碼（含 backward compatibility）
            string[] currentCodes = GetMachineCodes();
            string currentMachineCodeV2 = currentCodes[0];
            string currentMachineCodeLegacy = currentCodes[1];

            // 比對授權文件中的機器碼（支援新版和舊版）
            if (!string.IsNullOrWhiteSpace(storedLicense.MachineCode))
            {
                bool matchesV2 = string.Equals(storedLicense.MachineCode, currentMachineCodeV2, StringComparison.OrdinalIgnoreCase);
                bool matchesLegacy = currentMachineCodeLegacy != null &&
                                    string.Equals(storedLicense.MachineCode, currentMachineCodeLegacy, StringComparison.OrdinalIgnoreCase);

                if (!matchesV2 && !matchesLegacy)
                {
                    errorMessage = "授權文件已綁定到其他電腦，請重新啟用授權。";
                    return false;
                }
            }

            // 比對簽章授權碼中的機器碼（支援新版和舊版）
            if (!string.IsNullOrWhiteSpace(signedLicense.MachineCode))
            {
                bool matchesV2 = string.Equals(signedLicense.MachineCode, currentMachineCodeV2, StringComparison.OrdinalIgnoreCase);
                bool matchesLegacy = currentMachineCodeLegacy != null &&
                                    string.Equals(signedLicense.MachineCode, currentMachineCodeLegacy, StringComparison.OrdinalIgnoreCase);

                if (!matchesV2 && !matchesLegacy)
                {
                    errorMessage = "授權金鑰已綁定到其他電腦，請聯繫技術支援重新綁定。";
                    return false;
                }
            }

            return true;
        }

        private bool StringEquals(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
        }

        /// <summary>
        /// 生成機器碼（基於硬體資訊）
        /// 指紋來源：CPU ProcessorId + BaseBoard SerialNumber + MachineName
        /// 不含 UserName，避免使用者變更導致失效
        /// 支援 backward compatibility：若授權綁定的是舊版機器碼，仍可通過驗證
        /// </summary>
        public string GetMachineCode()
        {
            // 先嘗試用新版算法
            string v2Code = GetMachineCodeV2();
            // 同時生成舊版算法（用於比對既有授權）
            string legacyCode = GetLegacyMachineCode();
            
            // 儲存兩者供驗證時使用
            _currentMachineCodeV2 = v2Code;
            _currentMachineCodeLegacy = legacyCode;
            
            return v2Code;
        }

        private static string _currentMachineCodeV2;
        private static string _currentMachineCodeLegacy;

        /// <summary>
        /// 取得當前機器碼（含 backward compatibility）
        /// 返回陣列：[0] = 新版，[1] = 舊版（若有）
        /// </summary>
        public string[] GetMachineCodes()
        {
            if (_currentMachineCodeV2 == null)
            {
                _currentMachineCodeV2 = GetMachineCodeV2();
                _currentMachineCodeLegacy = GetLegacyMachineCode();
            }
            return new[] { _currentMachineCodeV2, _currentMachineCodeLegacy };
        }

        /// <summary>
        /// 生成舊版機器碼（用於 backward compatibility）
        /// 指紋來源：MachineName + UserName + ProcessorCount
        /// </summary>
        private static string GetLegacyMachineCode()
        {
            try
            {
                string machineInfo = Environment.MachineName +
                                   Environment.UserName +
                                   Environment.ProcessorCount.ToString();

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(machineInfo);
                    byte[] hash = sha256.ComputeHash(bytes);

                    string machineCode = BitConverter.ToString(hash, 0, 16).Replace("-", "");
                    return $"{machineCode.Substring(0, 4)}-{machineCode.Substring(4, 4)}-{machineCode.Substring(8, 4)}-{machineCode.Substring(12, 4)}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetLegacyMachineCode failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 生成新版機器碼（基於硬體指紋）
        /// </summary>
        private static string GetMachineCodeV2()
        {
            try
            {
                string cpuId = GetCpuId();
                string baseBoardSerial = GetBaseBoardSerialNumber();
                string machineName = Environment.MachineName;

                // 以固定分隔組合，降低不同欄位拼接碰撞機率
                string machineInfo = string.Join("|",
                    cpuId ?? "UNKNOWN_CPU",
                    baseBoardSerial ?? "UNKNOWN_BOARD",
                    machineName ?? "UNKNOWN_HOST");

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(machineInfo);
                    byte[] hash = sha256.ComputeHash(bytes);

                    // 取前 16 個字節並轉換為 16 進制字串
                    string machineCode = BitConverter.ToString(hash, 0, 16).Replace("-", "");

                    // 格式化為 XXXX-XXXX-XXXX-XXXX
                    return $"{machineCode.Substring(0, 4)}-{machineCode.Substring(4, 4)}-{machineCode.Substring(8, 4)}-{machineCode.Substring(12, 4)}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetMachineCodeV2 failed: {ex.Message}");
                return "無法生成機器碼";
            }
        }


        /// <summary>
        /// 取得 CPU ProcessorId（WMIC: Win32_Processor.ProcessorId）
        /// </summary>
        private static string GetCpuId()
        {
            try
            {
                var search = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessorId FROM Win32_Processor");
                foreach (System.Management.ManagementObject obj in search.Get())
                {
                    string id = obj["ProcessorId"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(id))
                        return id;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetCpuId failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 取得主機板序號（WMIC: Win32_BaseBoard.SerialNumber）
        /// 部分機型（如筆記型電腦）此值可能為空，屬正常
        /// </summary>
        private static string GetBaseBoardSerialNumber()
        {
            try
            {
                var search = new System.Management.ManagementObjectSearcher(
                    "SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (System.Management.ManagementObject obj in search.Get())
                {
                    string sn = obj["SerialNumber"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(sn) && sn != "To Be Filled By O.E.M.")
                        return sn;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetBaseBoardSerialNumber failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 啟用授權（解析並驗證授權金鑰）
        /// </summary>
        /// <param name="licenseKey">授權金鑰（Base64 編碼的 JSON）</param>
        /// <returns>驗證結果</returns>
        public LicenseValidationResult ActivateLicense(string licenseKey)
        {
            try
            {
                // 移除空白字元和換行
                licenseKey = licenseKey.Replace("\r", "").Replace("\n", "").Replace(" ", "").Trim();

                if (string.IsNullOrWhiteSpace(licenseKey))
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Message = "授權金鑰不能為空",
                        Severity = ValidationSeverity.Error
                    };
                }

                if (!TryReadSignedLicense(licenseKey, out LicenseInfo license, out string licenseKeyError))
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Message = licenseKeyError,
                        Severity = ValidationSeverity.Error
                    };
                }

                // 驗證授權資訊
                if (license == null)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Message = "授權金鑰解析失敗",
                        Severity = ValidationSeverity.Error
                    };
                }

                // 檢查必要欄位
                if (string.IsNullOrWhiteSpace(license.UserName))
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Message = "授權金鑰缺少使用者名稱",
                        Severity = ValidationSeverity.Error
                    };
                }

                // 檢查機器碼綁定（如果授權有指定機器碼）
                if (!string.IsNullOrWhiteSpace(license.MachineCode))
                {
                    string currentMachineCode = GetMachineCode();
                    if (license.MachineCode != currentMachineCode)
                    {
                        return new LicenseValidationResult
                        {
                            IsValid = false,
                            Message = $"此授權金鑰已綁定到其他電腦\n\n" +
                                     $"授權機器碼：{license.MachineCode}\n" +
                                     $"目前機器碼：{currentMachineCode}\n\n" +
                                     $"請聯繫技術支援以重新綁定授權。",
                            Severity = ValidationSeverity.Error
                        };
                    }
                }

                // 檢查授權是否過期
                if (DateTime.Now > license.ExpiryDate)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Message = $"授權已過期（到期日：{license.ExpiryDate:yyyy-MM-dd}）",
                        Severity = ValidationSeverity.Error
                    };
                }

                // 設定授權為啟用狀態
                license.IsEnabled = true;
                license.LicenseKey = licenseKey;

                // 如果授權沒有綁定機器碼，自動綁定到當前電腦
                if (string.IsNullOrWhiteSpace(license.MachineCode))
                {
                    license.MachineCode = GetMachineCode();
                }

                // 儲存授權
                if (!SaveLicense(license))
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Message = "授權儲存失敗",
                        Severity = ValidationSeverity.Error
                    };
                }

                // 返回成功結果
                return new LicenseValidationResult
                {
                    IsValid = true,
                    Message = "授權啟用成功",
                    LicenseInfo = license,
                    DaysUntilExpiry = (license.ExpiryDate - DateTime.Now).Days,
                    Severity = ValidationSeverity.Success
                };
            }
            catch (Exception ex)
            {
                return new LicenseValidationResult
                {
                    IsValid = false,
                    Message = $"授權啟用時發生錯誤：{ex.Message}",
                    Severity = ValidationSeverity.Error
                };
            }
        }
    }

    // 驗證嚴重性
    public enum ValidationSeverity
    {
        Success,
        Warning,
        Error
    }

    // 授權驗證結果
    public class LicenseValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public LicenseInfo LicenseInfo { get; set; }
        public int DaysUntilExpiry { get; set; }
        public ValidationSeverity Severity { get; set; }
    }
}
