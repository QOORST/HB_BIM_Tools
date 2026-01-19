// LicenseManager.cs (更新部分)
using System;
using System.Collections.Generic;
using System.IO;
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
    }

    public class LicenseManager
    {
        private static LicenseManager _instance;
        private static readonly object _lock = new object();
        private LicenseInfo _currentLicense;

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
                "MEP.PipeSleeve"              // 管線套管
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
                "MEP.PipeSleeve"              // 管線套管
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

        private string LicenseFilePath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string licenseFolder = Path.Combine(appData, "YD", "RevitTools");

                if (!Directory.Exists(licenseFolder))
                    Directory.CreateDirectory(licenseFolder);

                return Path.Combine(licenseFolder, "license.dat");
            }
        }

        private void LoadLicense()
        {
            try
            {
                if (File.Exists(LicenseFilePath))
                {
                    string encryptedData = File.ReadAllText(LicenseFilePath);
                    string decryptedData = Decrypt(encryptedData);
                    _currentLicense = JsonConvert.DeserializeObject<LicenseInfo>(decryptedData);
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
                if (File.Exists(LicenseFilePath))
                {
                    File.Delete(LicenseFilePath);
                    _currentLicense = null;
                    return true;
                }
                return false;
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

        /// <summary>
        /// 生成機器碼（基於硬體資訊）
        /// </summary>
        public string GetMachineCode()
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

                    // 取前16個字節並轉換為16進制字串
                    string machineCode = BitConverter.ToString(hash, 0, 16).Replace("-", "");

                    // 格式化為 XXXX-XXXX-XXXX-XXXX
                    return $"{machineCode.Substring(0, 4)}-{machineCode.Substring(4, 4)}-{machineCode.Substring(8, 4)}-{machineCode.Substring(12, 4)}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"生成機器碼失敗: {ex.Message}");
                return "無法生成機器碼";
            }
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

                // 解析授權金鑰（Base64 解碼）
                string jsonData;
                try
                {
                    byte[] data = Convert.FromBase64String(licenseKey);
                    jsonData = Encoding.UTF8.GetString(data);
                }
                catch (FormatException)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Message = "授權金鑰格式錯誤（無效的 Base64 編碼）",
                        Severity = ValidationSeverity.Error
                    };
                }

                // 解析 JSON
                LicenseInfo license;
                try
                {
                    license = JsonConvert.DeserializeObject<LicenseInfo>(jsonData);
                }
                catch (JsonException ex)
                {
                    return new LicenseValidationResult
                    {
                        IsValid = false,
                        Message = $"授權金鑰格式錯誤（無效的 JSON）：{ex.Message}",
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