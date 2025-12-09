using System;
using System.Reflection;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Helpers.Data
{
    /// <summary>
    /// Revit 2022–2026 版本相容性工具類別：統一處理 ForgeTypeId/SpecTypeId、封裝 InsertBinding 與 ElementId 轉換。
    /// 提供跨版本的 API 相容性支援，自動檢測並適配不同 Revit 版本的 API 差異。
    /// 支援條件編譯：REVIT2022, REVIT2023, REVIT2024, REVIT2025, REVIT2026
    /// </summary>
    internal static class ParamTypeCompat
    {
        #region 常數定義
        
        /// <summary>資料類型常數</summary>
        private static class DataTypes
        {
            public const string Number = "Number";
            public const string Integer = "Integer";
            public const string YesNo = "YesNo";
            public const string Text = "Text";
            public const string Date = "Date";
        }

        /// <summary>反射相關常數</summary>
        private static class ReflectionConstants
        {
            public const string GroupTypeIdTypeName = "Autodesk.Revit.DB.GroupTypeId, RevitAPI";
            public const string DataPropertyName = "Data";
            public const string ValuePropertyName = "Value";
            public const string InsertMethodName = "Insert";
        }

        #endregion

        #region 靜態快取

        private static readonly object _lockObject = new object();
        private static bool _cacheInitialized = false;
        
        // 快取反射結果以提升效能
        private static Type _groupTypeIdType;
        private static PropertyInfo _dataProperty;
        private static MethodInfo _insertMethodWithForgeTypeId;
        private static PropertyInfo _elementIdValueProperty;
        private static ConstructorInfo _elementIdLongConstructor;
        private static bool _supportsLongElementId;
        private static ForgeTypeId _cachedDataForgeTypeId;

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化反射快取，提升後續調用效能
        /// </summary>
        private static void InitializeCache()
        {
            if (_cacheInitialized) return;

            lock (_lockObject)
            {
                if (_cacheInitialized) return;

                try
                {
                    // 檢測 GroupTypeId 支援 (Revit 2024+)
                    _groupTypeIdType = Type.GetType(ReflectionConstants.GroupTypeIdTypeName);

                    if (_groupTypeIdType != null)
                    {
                        _dataProperty = _groupTypeIdType.GetProperty(ReflectionConstants.DataPropertyName,
                            BindingFlags.Public | BindingFlags.Static);

                        if (_dataProperty != null)
                        {
                            _cachedDataForgeTypeId = _dataProperty.GetValue(null) as ForgeTypeId;

                            // 嘗試查找 Insert 方法，先嘗試 ElementBinding，再嘗試 Binding
                            _insertMethodWithForgeTypeId = typeof(BindingMap).GetMethod(ReflectionConstants.InsertMethodName,
                                new[] { typeof(Definition), typeof(ElementBinding), typeof(ForgeTypeId) });

                            if (_insertMethodWithForgeTypeId == null)
                            {
                                _insertMethodWithForgeTypeId = typeof(BindingMap).GetMethod(ReflectionConstants.InsertMethodName,
                                    new[] { typeof(Definition), typeof(Binding), typeof(ForgeTypeId) });
                            }
                        }
                    }

                    // 檢測 ElementId 支援 (Revit 2024+ 使用 long，舊版使用 int)
                    _elementIdValueProperty = typeof(ElementId).GetProperty(ReflectionConstants.ValuePropertyName,
                        BindingFlags.Public | BindingFlags.Instance);
                    _elementIdLongConstructor = typeof(ElementId).GetConstructor(new[] { typeof(long) });
                    _supportsLongElementId = _elementIdLongConstructor != null;
                }
                catch (Exception ex)
                {
                    // 記錄初始化錯誤但不拋出異常，確保程式可以繼續運行
                    System.Diagnostics.Debug.WriteLine($"ParamTypeCompat 初始化警告: {ex.Message}");
                }
                finally
                {
                    _cacheInitialized = true;
                }
            }
        }

        #endregion

        #region 公開方法

        /// <summary>
        /// 建立 ExternalDefinitionCreationOptions，自動處理不同資料類型的 SpecTypeId 對應
        /// </summary>
        /// <param name="name">參數名稱</param>
        /// <param name="dataType">資料類型 (Number, Integer, YesNo, Text, Date)</param>
        /// <param name="desc">參數描述</param>
        /// <returns>建立的 ExternalDefinitionCreationOptions</returns>
        /// <exception cref="ArgumentException">當參數名稱為空時拋出</exception>
        public static ExternalDefinitionCreationOptions MakeCreationOptions(string name, string dataType, string desc = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("參數名稱不能為空", nameof(name));

            try
            {
                var opt = new ExternalDefinitionCreationOptions(name, ResolveSpecTypeId(dataType))
                {
                    Description = desc ?? string.Empty
                };
                return opt;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"建立 ExternalDefinitionCreationOptions 失敗: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 跨版本相容的 BindingMap.Insert 方法
        /// Revit 2024+ 使用 GroupTypeId.Data，舊版使用 BuiltInParameterGroup.PG_DATA
        /// </summary>
        /// <param name="map">BindingMap 實例</param>
        /// <param name="def">參數定義</param>
        /// <param name="binding">元素綁定</param>
        /// <returns>綁定是否成功</returns>
        public static bool InsertBinding(BindingMap map, Definition def, ElementBinding binding)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (binding == null) throw new ArgumentNullException(nameof(binding));

            InitializeCache();

            try
            {
                // 嘗試使用新版 API (Revit 2024+)
                if (_insertMethodWithForgeTypeId != null && _cachedDataForgeTypeId != null)
                {
                    var result = _insertMethodWithForgeTypeId.Invoke(map,
                        new object[] { def, binding, _cachedDataForgeTypeId });
                    return result is bool success && success;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"新版 InsertBinding 失敗，回退到舊版: {ex.Message}");
            }

            try
            {
                // 回退到舊版 API - 完全使用反射來避免類型載入問題
                // 在 Revit 2025 中，BuiltInParameterGroup 已經完全移除
                var builtInParamGroupType = Type.GetType("Autodesk.Revit.DB.BuiltInParameterGroup, RevitAPI");

                if (builtInParamGroupType != null)
                {
                    // Revit 2022/2023/2024 - 使用 BuiltInParameterGroup.PG_DATA
                    var insertMethod = typeof(BindingMap).GetMethod("Insert",
                        new[] { typeof(Definition), typeof(Binding), builtInParamGroupType });

                    if (insertMethod != null)
                    {
                        // 取得 PG_DATA 列舉值
                        var pgDataValue = Enum.Parse(builtInParamGroupType, "PG_DATA");
                        var result = insertMethod.Invoke(map, new object[] { def, binding, pgDataValue });
                        return result is bool success && success;
                    }
                }

                // 如果找不到舊版 API，表示是 Revit 2025+，但新版 API 也失敗了
                throw new InvalidOperationException(
                    "無法找到適用的 BindingMap.Insert 方法。\n" +
                    "新版 API (ForgeTypeId) 失敗，舊版 API (BuiltInParameterGroup) 不存在。\n" +
                    "請確認 Revit 版本和 API 相容性。");
            }
            catch (InvalidOperationException)
            {
                throw; // 重新拋出我們自己的異常
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"參數綁定失敗: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 將 ElementId 轉換為字串，自動處理不同版本的差異
        /// </summary>
        /// <param name="id">ElementId 實例</param>
        /// <returns>ElementId 的字串表示</returns>
        /// <exception cref="ArgumentNullException">當 id 為 null 時拋出</exception>
        public static string ElementIdToString(ElementId id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));

            InitializeCache();

            try
            {
                // Revit 2024+ 使用 Value 屬性
                if (_elementIdValueProperty != null)
                {
                    var value = _elementIdValueProperty.GetValue(id);
                    return Convert.ToString(value) ?? "0";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"使用 Value 屬性失敗，回退到 IntegerValue: {ex.Message}");
            }

            try
            {
                // 回退到舊版 IntegerValue 屬性
                return id.IntegerValue.ToString();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"ElementId 轉換字串失敗: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 從字串解析 ElementId，自動處理不同版本的建構函式差異
        /// </summary>
        /// <param name="s">要解析的字串</param>
        /// <returns>解析成功返回 ElementId，失敗返回 null</returns>
        public static ElementId ParseElementId(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            InitializeCache();

            try
            {
                // 優先使用 int（向下相容 Revit 2022/2023），在 Revit 2024+ 仍可接受 int 來源的 ElementId
                if (int.TryParse(s, out int iid))
                {
                    try
                    {
                        return new ElementId(iid);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"使用int建構ElementId失敗: {ex.Message}");
                        // 繼續嘗試其他方法
                    }
                }

                // 再嘗試 long（處理大型 ID）
                if (long.TryParse(s, out long lid))
                {
                    try
                    {
                        // 嘗試使用 long 建構函式 (Revit 2024+)
                        return new ElementId(lid);
                    }
                    catch (MissingMethodException)
                    {
                        // 某些環境（例如 Revit 2022）不支援 long 建構函式，回退到 int 範圍
                        if (lid >= int.MinValue && lid <= int.MaxValue)
                        {
                            try
                            {
                                return new ElementId((int)lid);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"回退到int範圍失敗: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"使用long建構ElementId失敗: {ex.Message}");
                        // 如果是其他錯誤，嘗試回退到int
                        if (lid >= int.MinValue && lid <= int.MaxValue)
                        {
                            try
                            {
                                return new ElementId((int)lid);
                            }
                            catch { }
                        }
                    }
                }
                
                // 最後嘗試使用 ElementId.InvalidElementId
                return ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ElementId 解析失敗: {ex.Message}");
                return ElementId.InvalidElementId;
            }

            return null;
        }

        /// <summary>
        /// 檢查 ElementId 是否為有效值（不是 InvalidElementId 且值大於 0）
        /// </summary>
        /// <param name="id">要檢查的 ElementId</param>
        /// <returns>true 表示有效，false 表示無效</returns>
        public static bool IsValidElementId(ElementId id)
        {
            if (id == null) return false;
            if (id == ElementId.InvalidElementId) return false;

            InitializeCache();

            try
            {
                // 使用新版 API (Revit 2024+)
                if (_elementIdValueProperty != null)
                {
                    var value = _elementIdValueProperty.GetValue(id);
                    if (value is long longVal) return longVal > 0;
                    if (value is int intVal) return intVal > 0;
                }
                
                // 回退到舊版 API
                return id.IntegerValue > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 檢查當前 Revit 版本是否支援新版 API
        /// </summary>
        /// <returns>true 表示支援 Revit 2024+ API</returns>
        public static bool SupportsNewApi()
        {
            InitializeCache();
            return _insertMethodWithForgeTypeId != null && _cachedDataForgeTypeId != null;
        }

        /// <summary>
        /// 檢查當前 Revit 版本是否支援 long 型別的 ElementId
        /// </summary>
        /// <returns>true 表示支援 long 型別 ElementId</returns>
        public static bool SupportsLongElementId()
        {
            InitializeCache();
            return _supportsLongElementId;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 根據資料類型字串解析對應的 SpecTypeId
        /// </summary>
        /// <param name="dataType">資料類型字串</param>
        /// <returns>對應的 ForgeTypeId</returns>
        private static ForgeTypeId ResolveSpecTypeId(string dataType)
        {
            switch ((dataType ?? DataTypes.Text).Trim())
            {
                case DataTypes.Number:
                    return SpecTypeId.Number;
                case DataTypes.Integer:
                    return SpecTypeId.Int.Integer;
                case DataTypes.YesNo:
                    return SpecTypeId.Boolean.YesNo;
                case DataTypes.Date:
                    return SpecTypeId.String.Text; // 日期類型暫時使用文字類型
                default:
                    return SpecTypeId.String.Text;
            }
        }

        #endregion
    }
}
