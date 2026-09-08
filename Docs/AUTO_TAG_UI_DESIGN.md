# 自動標註功能介面設計規範

## 1. 設計目標

- **專業版體驗**：現代化深色主題、流暢互動、清晰資訊層級
- **多解析度適應**：支援 1080p、1440p、4K 螢幕及高 DPI 環境
- **專業工作流**：Modeless 模式讓使用者可在視窗開啟時操作 Revit
- **設定持久化**：每專案記憶設定，快速重複執行

---

## 2. 視覺設計規範

### 2.1 色彩方案

| 用途 | 深色主題 | 說明 |
|---|---|---|
| 背景主色 | `#272727` | 主要面板背景 |
| 背景次要 | `#393939` | 群組框、頁籤背景 |
| 強調色 | `#2d84f7` | 主要按鈕、選取狀態 |
| 文字主色 | `#ffffff` | 主要文字 |
| 文字次要 | `#cdcdcd` | 說明文字、提示 |
| 邊框色 | `#4c4c4c` | 控件邊框 |
| 錯誤色 | `#b02d2d` | 錯誤訊息 |
| 成功色 | `#4caf50` | 成功狀態 |

### 2.2 字型規範

| 用途 | 字型 | 大小 | 粗細 |
|---|---|---|---|
| 視窗標題 | Microsoft JhengHei UI / Segoe UI | 14pt | Bold |
| 主要標籤 | Microsoft JhengHei UI / Segoe UI | 10pt | Bold |
| 主要文字 | Microsoft JhengHei UI / Segoe UI | 9pt | Regular |
| 說明文字 | Microsoft JhengHei UI / Segoe UI | 9pt | Regular |
| 按鈕文字 | Microsoft JhengHei UI / Segoe UI | 10pt | Bold |

### 2.3 間距規範

- 外邊距：18px
- 內邊距：12-14px
- 控件間距：8px
- 區塊間距：16px

---

## 3. 視窗規格

### 3.1 基本尺寸

| 模式 | 寬度 | 高度 | 最小寬度 | 最小高度 |
|---|---|---|---|---|
| 完整模式 | 1120px | 760px | 820px | 600px |
| 鎖定模式 | 980px | 560px | 720px | 500px |

### 3.2 解析度適應策略

```csharp
// 根據螢幕工作區域動態調整視窗大小
private void FitToCurrentScreen()
{
    Rectangle workingArea = Screen.FromHandle(Handle).WorkingArea;
    int margin = Math.Max(12, (int)Math.Round(12D * DeviceDpi / 96D));

    // 計算最大可用寬度和高度
    int maxWidth = Math.Max(640, workingArea.Width - margin * 2);
    int maxHeight = Math.Max(480, workingArea.Height - margin * 2);

    // 限制視窗大小不超過可用區域
    MaximumSize = new Size(maxWidth, maxHeight);
    Size = new Size(Math.Min(Width, maxWidth), Math.Min(Height, maxHeight));

    // 置中顯示
    Location = new Point(
        workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2),
        workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2));
}
```

### 3.3 DPI 感知

```csharp
// 啟用高 DPI 支援
AutoScaleMode = AutoScaleMode.Dpi;

// 動態調整間距
int scaledPadding = (int)Math.Round(12D * DeviceDpi / 96D);
```

---

## 4. 版面佈局

### 4.1 整體結構

```
┌─────────────────────────────────────────────────────────────┐
│  Header (標題 + 說明)                                        │
├─────────────────────────────────────────────────────────────┤
│  TabControl (主要設定頁籤)                                   │
│  ├─ 標註設定                                                  │
│  │   ├─ Type Panel (標註型式選擇)                           │
│  │   ├─ Mode Content (模式內容 - 依模式切換)                │
│  │   └─ Mode Hint (操作提示)                                │
│  └─ 距離設定                                                  │
│      ├─ Level Offset (一般標註距離)                         │
│      └─ Grid Offset (軸線距離)                              │
├─────────────────────────────────────────────────────────────┤
│  Button Panel (執行/關閉/重新整理)                           │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌────────────┐   │
│  │ 執行標註 │  │  關閉    │  │重新整理  │  │ 狀態訊息   │   │
│  └──────────┘  └──────────┘  └──────────┘  └────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 4.2 頁籤設計

#### 標註設定頁籤
- **標註型式**：下拉選單選擇標註族型
- **模式切換**：子頁籤切換不同標註模式
  - 柱 (Column)
  - 軸線 (Grid)
  - 梁 (Beam)

#### 距離設定頁籤
- **圖示說明**：左側繪製距離示意圖
- **數值輸入**：右側提供 NumericUpDown 輸入

### 4.3 模式內容 (柱模式)

```
┌─────────────────────────────────────────────────────┐
│  選擇標註放置側                                      │
│  ┌─────────────────────────────────────────────┐   │
│  │              前                             │   │
│  │                                             │   │
│  │   左        [X]        右                   │   │
│  │                                             │   │
│  │              後                             │   │
│  └─────────────────────────────────────────────┘   │
│                                                     │
│  模式：[自動 ▼]                                     │
│                                                     │
│  提示：可選擇一個水平側與/或一個垂直側...           │
└─────────────────────────────────────────────────────┘
```

### 4.4 模式內容 (軸線模式)

```
┌─────────────────────────────────────────────────────────────┐
│  柱線生成方向                                                 │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  ↖ 左上    ↑ 上      ↗ 右上                         │    │
│  │  ← 左      預設      → 右                           │    │
│  │  ↙ 左下    ↓ 下      ↘ 右下                         │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                               │
│  水平軸線          │  垂直軸線                                │
│  ┌──────────────┐  │  ┌──────────────┐                       │
│  │ ☑ X-1        │  │  │ ☑ Y-1        │                       │
│  │ ☑ X-2        │  │  │ ☑ Y-2        │                       │
│  │ ☑ X-3        │  │  │ ☑ Y-3        │                       │
│  │ ☐ X-4        │  │  │ ☐ Y-4        │                       │
│  └──────────────┘  │  └──────────────┘                       │
│  ☑ 全選水平        │  ☑ 全選垂直                              │
└─────────────────────────────────────────────────────────────┘
```

---

## 5. 控件規範

### 5.1 按鈕樣式

```csharp
private Button CreatePrimaryButton(string text)
{
    return new Button
    {
        Text = text,
        Width = 118,
        Height = 34,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(45, 132, 247),
        ForeColor = Color.White,
        Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold)
    };
}

private Button CreateSecondaryButton(string text)
{
    return new Button
    {
        Text = text,
        Width = 108,
        Height = 34,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(64, 64, 64),
        ForeColor = Color.FromArgb(205, 205, 205),
        Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Regular)
    };
}
```

### 5.2 輸入控件

| 控件 | 背景色 | 文字色 | 高度 |
|---|---|---|---|
| ComboBox | `#4a4a4a` | `#ffffff` | 30px |
| NumericUpDown | `#4a4a4a` | `#ffffff` | 30px |
| CheckedListBox | `#3a3a3a` | `#ffffff` | 自適應 |

### 5.3 頁籤樣式

```csharp
private class ThemedTabControl : TabControl
{
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(39, 39, 39));
        for (int i = 0; i < TabCount; i++)
            OnDrawItem(new DrawItemEventArgs(
                e.Graphics, Font, GetTabRect(i), i,
                i == SelectedIndex ? DrawItemState.Selected : DrawItemState.None));
    }
}
```

---

## 6. 響應式佈局

### 6.1 寬度閾值

```csharp
// 當寬度小於閾值時，切換為堆疊佈局
int stackingThreshold = (int)Math.Round(900D * DeviceDpi / 96D);

if (ClientSize.Width < stackingThreshold)
{
    // 水平軸線和垂直軸線上下堆疊
    tableLayout.ColumnStyles[0].Width = 100;
    tableLayout.ColumnStyles[1].Width = 0;
}
else
{
    // 左右並排
    tableLayout.ColumnStyles[0].Width = 50;
    tableLayout.ColumnStyles[1].Width = 50;
}
```

### 6.2 圖示自動縮放

```csharp
// 根據可用空間調整圖示大小
Rectangle area = panel.ClientRectangle;
area.Inflate(-28, -20);

if (area.Width < 240 || area.Height < 140)
{
    // 空間不足時隱藏圖示或顯示簡化版本
    return;
}
```

---

## 7. 狀態管理

### 7.1 執行狀態

| 狀態 | 按鈕狀態 | 狀態訊息 |
|---|---|---|
| 初始 | 全部啟用 | "可操作 Revit；調整設定後按執行標註" |
| 執行中 | 禁用 | "正在等候 Revit 執行..." |
| 完成 | 全部啟用 | "完成。可繼續調整並再次執行。" |
| 錯誤 | 全部啟用 | "錯誤訊息（紅色）" |

### 7.2 設定持久化

```csharp
public class AutoTagSavedSettings
{
    public string Template { get; set; }
    public string Scope { get; set; }
    public string Placement { get; set; }
    public double OffsetMillimeters { get; set; }
    public bool AddLeader { get; set; }
    public bool SkipExistingTags { get; set; }
    public bool AvoidTagOverlap { get; set; }
    public List<SavedRule> Rules { get; set; }
}

// 儲存位置：%AppData%\HB_BIM_Tools\AutoDimensionSettings.json
```

---

## 8. 專業版擴充功能

### 8.1 進階選項（專業版專屬）

- **批量處理**：支援多視圖批量標註
- **自訂規則**：使用者可定義標註規則
- **模板管理**：儲存和載入標註模板
- **報表匯出**：標註統計報表

### 8.2 整合功能

- **與自動尺寸整合**：共用設定和邏輯
- **與房間裝修整合**：標註裝修範圍
- **與釋疑工具整合**：標註作為釋疑素材

---

## 9. 測試檢查清單

### 9.1 解析度測試

- [ ] 1920x1080 (100% DPI)
- [ ] 2560x1440 (125% DPI)
- [ ] 3840x2160 (150% DPI)
- [ ] 多螢幕環境

### 9.2 功能測試

- [ ] 設定持久化
- [ ] 模式切換
- [ ] 軸線選擇
- [ ] 方向選擇
- [ ] 數值輸入驗證
- [ ] 錯誤處理

### 9.3 相容性測試

- [ ] Revit 2022
- [ ] Revit 2024
- [ ] Revit 2025
- [ ] Revit 2026

---

## 10. 實作範例

### 10.1 視窗初始化

```csharp
public AutoTagOptionsForm(
    Document doc,
    AutoTagMode mode,
    Action<AutoTagOptions, IReadOnlyList<AutoTagRuleSelection>> applyAction,
    Action refreshAction)
{
    _doc = doc;
    _mode = mode;
    _applyAction = applyAction;
    _refreshAction = refreshAction;

    // 基本設定
    Text = mode == AutoTagMode.Vertical ? "自動標籤 - 垂直元素" : "自動標籤 - 水平元素";
    Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    BackColor = Color.FromArgb(39, 39, 39);
    AutoScaleMode = AutoScaleMode.Dpi;
    ClientSize = new Size(1120, 760);
    MinimumSize = new Size(820, 600);
    FormBorderStyle = FormBorderStyle.Sizable;
    MaximizeBox = true;
    MinimizeBox = false;
    StartPosition = FormStartPosition.CenterParent;

    // 螢幕適應
    Shown += (_, _) => FitToCurrentScreen();
    DpiChanged += (_, _) => BeginInvoke((Action)FitToCurrentScreen);

    BuildLayout();
    ApplyModernTheme();
}
```

### 10.2 深色主題應用

```csharp
private void ApplyModernTheme()
{
    // 套用深色主題到所有控件
    ApplyDarkTheme(this);

    // 自訂頁籤繪製
    _tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
    _tabControl.DrawItem += TabControl_DrawItem;
}

private static void ApplyDarkTheme(Control control)
{
    if (control is Panel || control is TableLayoutPanel)
        control.BackColor = Color.FromArgb(39, 39, 39);

    if (control is Label || control is GroupBox)
    {
        control.ForeColor = Color.White;
        control.BackColor = Color.FromArgb(39, 39, 39);
    }

    if (control is ComboBox || control is NumericUpDown)
    {
        control.BackColor = Color.FromArgb(74, 74, 74);
        control.ForeColor = Color.White;
    }

    foreach (Control child in control.Controls)
        ApplyDarkTheme(child);
}
```

---

*最後更新：2026-09-08*
*版本：1.1*
