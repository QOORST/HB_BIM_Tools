# COBie 匯入功能修復報告 - v2.2.4

## 🐛 問題描述

**問題：** COBie 匯入功能將資料錯誤地填入內建參數（模型、製造商），而不是共用參數

**影響範圍：**
- 06.型號名稱 (Component.TypeName) - 資料被寫入內建參數「模型」
- 11.製造廠商 (Component.Manufacturer) - 資料被寫入內建參數「製造商」

**根本原因：**
1. 預設配置中，這兩個欄位設定為使用內建參數，沒有共用參數
2. `ApplyValue` 方法在共用參數寫入失敗時，會自動回退到內建參數

---

## ✅ 修復內容

### 1. 更新預設欄位配置

**修改檔案：** `Commands/Data/CmdCobieFieldManager.cs`

**修改前：**
```csharp
// 06.型號名稱 - 使用內建參數
new CobieFieldConfig{ 
    DisplayName="06.型號名稱", 
    CobieName="Component.TypeName", 
    Category="維護資訊", 
    IsBuiltIn=true, 
    BuiltInParam=BuiltInParameter.ALL_MODEL_MODEL, 
    ExportEnabled=true, 
    ImportEnabled=true, 
    DataType="Text" 
},

// 11.製造廠商 - 使用內建參數
new CobieFieldConfig{ 
    DisplayName="11.製造廠商", 
    CobieName="Component.Manufacturer", 
    Category="維護資訊", 
    IsBuiltIn=true, 
    BuiltInParam=BuiltInParameter.ALL_MODEL_MANUFACTURER, 
    ExportEnabled=true, 
    ImportEnabled=true, 
    DataType="Text" 
},
```

**修改後：**
```csharp
// 06.型號名稱 - 改用共用參數
new CobieFieldConfig{ 
    DisplayName="06.型號名稱", 
    CobieName="Component.TypeName", 
    Category="維護資訊", 
    SharedParameterName="COBie_TypeName",  // ✅ 新增共用參數
    ExportEnabled=true, 
    ImportEnabled=true, 
    DataType="Text" 
},

// 11.製造廠商 - 改用共用參數
new CobieFieldConfig{ 
    DisplayName="11.製造廠商", 
    CobieName="Component.Manufacturer", 
    Category="維護資訊", 
    SharedParameterName="COBie_Manufacturer",  // ✅ 新增共用參數
    ExportEnabled=true, 
    ImportEnabled=true, 
    DataType="Text" 
},
```

---

### 2. 修改 ApplyValue 邏輯

**修改檔案：** `Commands/Data/CmdCobieImportEnhanced.cs`

**關鍵修改：**
```csharp
private static bool ApplyValue(Element e, CmdCobieFieldManager.CobieFieldConfig cfg, string raw)
{
    // 優先寫入共用參數
    if (!string.IsNullOrWhiteSpace(cfg.SharedParameterName))
    {
        // ... 嘗試寫入共用參數 ...
        
        // ✅ 關鍵修改：如果有共用參數設定但寫入失敗，直接返回 false
        // 不要回退到內建參數
        return false;
    }

    // 只有在沒有設定共用參數時，才使用內建參數（用於向後相容）
    if (cfg.IsBuiltIn && cfg.BuiltInParam.HasValue)
    {
        // ... 使用內建參數 ...
    }
    
    return false;
}
```

**修改說明：**
- ✅ 當欄位設定了共用參數時，**只寫入共用參數**
- ✅ 如果共用參數不存在或寫入失敗，**不會回退到內建參數**
- ✅ 只有在沒有設定共用參數時，才使用內建參數（向後相容舊配置）

---

## 📋 使用說明

### 步驟 1：確保模型有 COBie 共用參數

在使用 COBie 匯入功能前，請確保模型中已經新增了 COBie 相關的共用參數：

1. 開啟 Revit 模型
2. 點擊「資料」面板 → 「COBie 欄位管理」
3. 選擇需要的欄位（如「06.型號名稱」、「11.製造廠商」）
4. 點擊「📥 載入共用參數」按鈕
5. 選擇要綁定的類別（如 MEP 設備）
6. 點擊「建立參數」

### 步驟 2：匯入 COBie 資料

1. 準備 CSV 檔案，包含以下欄位：
   - UniqueId / ElementId / Mark（用於識別元件）
   - 06.型號名稱 (Component.TypeName)
   - 11.製造廠商 (Component.Manufacturer)
   - 其他 COBie 欄位

2. 點擊「資料」面板 → 「COBie 匯入」
3. 選擇 CSV 檔案
4. 資料將被寫入共用參數，而不是內建參數

---

## 🎯 修復效果

**修復前：**
- ❌ 資料被寫入內建參數「模型」和「製造商」
- ❌ 無法控制資料寫入位置
- ❌ 與共用參數設定不一致

**修復後：**
- ✅ 資料正確寫入共用參數 `COBie_TypeName` 和 `COBie_Manufacturer`
- ✅ 完全遵循欄位配置設定
- ✅ 不會意外修改內建參數

---

## 📦 版本資訊

**版本號：** 2.2.4  
**修復日期：** 2025-12-08  
**影響功能：** COBie 匯入

**更新內容：**
- ✅ 修復 COBie 匯入功能欄位讀取錯誤
- ✅ 將「型號名稱」和「製造廠商」改為使用共用參數
- ✅ 優化 ApplyValue 邏輯，防止回退到內建參數
- ✅ 整合 PipeToISO 工具到 MEP 面板

---

## ⚠️ 注意事項

1. **舊配置升級**
   - 如果您之前使用過 COBie 功能，建議重新開啟「COBie 欄位管理」
   - 系統會自動升級配置，將內建參數改為共用參數

2. **共用參數必須存在**
   - 匯入前請確保模型中已建立 COBie 共用參數
   - 如果參數不存在，匯入會失敗並記錄在失敗清單中

3. **向後相容**
   - 如果欄位沒有設定共用參數，仍會使用內建參數（舊版行為）
   - 建議所有欄位都設定共用參數，以獲得最佳控制

---

**修復完成！** ✅

