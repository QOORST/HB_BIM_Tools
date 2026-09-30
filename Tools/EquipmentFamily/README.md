# HB 設備圖資工作台 v0.2

第一階段可執行成果：本機多格式圖資匯入 → 來源快照及解析 → 人工覆核 → 已支援範本路由 → Revit 2024 建族。
這不是任意設備一鍵重建工具。現有已驗證範本為 EVERGUSH TOS-EF-05／21，以及東元 PJ0043-HS／PJ0063-HS 空調箱概念外殼。空調箱已通過 Revit 2024 實際建族、儲存重開、兩型號尺寸、獨立尺寸變更與四處送回風接續驗證，結果未記錄 Revit 警告。

## 空調箱範本

重開工作台才會載入更新。選擇「空調箱」，匯入原始 `11 AHU組合式空調箱系列.pdf`，建立工作後按「3 套用此設備分類範本」。尺寸表會切換為型號、總長 C、寬 D、總高 F、出風 G、出風 H。

本次已解析的工作位於 `artifacts/EquipmentFamily/ahu-catalog-001/case.json`，可用「開啟既有工作」選取。該工作中的覆核者標記為 AI 圖面核對／開發驗證，不代表使用者、設計人員或原廠簽認。

| 型號 | 總長 C | 寬 D | 箱高 E | 總高 F | 出風 G×H |
|---|---:|---:|---:|---:|---|
| PJ0043-HS | 1400 | 950 | 650 | 725 | 289×334 |
| PJ0063-HS | 1700 | 1250 | 750 | 825 | 344×398 |

單位 mm，來源為型錄第 10 頁，已目視核對。底座高 75 mm。完整設定及來源指紋在 `Docs/FamilyAutomation/ahu-teco-hs.input.json`。

建模範圍與限制：

- 外殼與簡化整塊底座；長寬高、底座高與送風口尺寸設為族群類型參數。任意調整後不代表原廠型號規格。
- 表列 A+B 不等於 C，外殼採 C，A/B 僅保留來源欄位；不推定內部分段。
- 送風接頭位於頂面中心，位置是概念假設。回風接頭位於正 X 端面中心，暫採 D×E，並非已核定淨開口。
- 不含混合箱、內部風車盤管、維修空間、冰水／排水／電氣接頭。
- 風量、靜壓、冷房能力、馬達功率、電壓相數保留待選定文字；沒有將型錄性能表任一工況自動當成設計值。
- 本版只接受上述兩型號及此原始 PDF 指紋，其他版次與型號需另行核對。
- 僅 Revit 2024 引擎；2025／2026 尚未驗證。

Revit 若顯示未簽章增益集安全提示，由使用者自行確認本機編譯的 `HB Equipment Case` 並決定是否載入。工具不自動操作安全提示，也不修改信任設定。若建族中斷，查看工作包 `output/failure.txt`；`result.json` 及接續檢查均成功才算通過。

### 已完成成果（2026-10-01）

位置：`artifacts/EquipmentFamily/ahu-catalog-001/builds/96b03353eec6/output/`。

- `TECO_PJ0043_0063_HS_CONCEPT_2024.rfa`：含兩個類型，可在 Revit「族群類型」調整幾何參數。
- `Duct-Connection-QA-2024.rvt`：已放置兩類型，每型均接上送風、回風風管。
- `geometry-checks.json`：兩型號及 1900×1400×900 mm 箱體尺寸變更檢查通過。
- `connection-checks.json`：四處接續成功，包含系統、流向與尺寸。
- `result.json`：族群儲存重開成功、無建族警告；2025／2026 未測。

首次兩次執行的失敗紀錄仍保留，分別為族群尺寸 API 與草圖邊線排列問題；已修正為族群專用線性尺寸及依幾何位置配對約束。請使用上述成功工作包。新版工作台標題仍可能顯示「開發版」，不影響此成果的已驗證狀態。

## 開始使用

雙擊同目錄 `Start-Workbench.cmd`，或執行 `Start-Workbench.ps1`。預設使用本機 Codex 已配置的 Python；其他電腦可用 `-PythonPath` 指定環境。

1. 選擇設備分類與交付版本，再選取同一設備的圖面及型錄。
2. 建立工作資料。原檔不修改；程式複製來源快照並記錄 SHA256。
3. 查看圖資與解析報告，對照頁面影像、文字、尺寸來源及版本差異。
4. 按「套用此設備分類範本」。泵浦為 TOS-EF-05／21，空調箱為 PJ0043-HS／PJ0063-HS。這一步載入已核對資料，並非 OCR 自動完成尺寸配對。
5. 填寫覆核者與來源說明，確認概念幾何範圍，再產生建族工作包。
6. 儲存並關閉既有 Revit 工作後執行 Revit 2024。新程序讀取工作包，完成後在 `builds/<編號>/output` 產生 RFA、接管 QA RVT 與結果。

不要把介面的「已擷取」解讀為設備尺寸已經確認。圖紙上的說明文字、OCR 與 CAD 文字一律是資料，不作為系統指令執行。

## 格式能力

| 來源 | 目前實作 | 限制 |
|---|---|---|
| DWG 2D／3D | AutoCAD 2024 內建 DXFOUT 轉換來源快照，再讀取幾何種類、文字、標註、單位 | 不載入自訂 CAD 外掛、不修改安全設定；需要有效本機 AutoCAD 環境 |
| DXF 2D／3D | ezdxf 解析 | 不需 AutoCAD；ACIS、圖塊、外參與特定自訂物件不保證完整解析 |
| 文字／向量 PDF | 文字擷取、頁面預覽、線條統計 | 未自動解讀尺寸延伸線或三視圖配對 |
| 掃描 PDF、PNG、JPG、BMP、TIFF、WebP | Windows OCR，保存頁面與文字位置 | OCR 需覆核；沒有尺寸依據時不從像素推算實際尺寸 |
| STEP／SAT／IGES | 保存來源並標記待轉換 | 尚未實作轉換器，不允許直接建族 |

CAD 圖面包圍盒只是可計算物件的範圍，不是設備寬深高。3D 幾何只辨識類型，尚未自動變成原生參數化幾何。最多 100 MB／檔、32 頁／檔、100000 個模型空間物件；超限明確回報，不把部分解析當完整資料。

## 設備與版本

- 可登錄：泵浦、風機、發電機、空調箱、配電盤、冷卻水塔、冰水主機、燈具、消防、弱電。
- 已驗證建族：TOS-EF-05／21。新增空調箱範本的驗證狀態見上節；兩者電氣接頭均尚未建立。
- 執行引擎：Revit 2024。選 2025／2026 可建立圖資工作，但建族前會阻擋；本版不宣稱這兩版已驗證。
- 泵浦尺寸資料限定前次已核對配置，不能把不同尺寸或電氣值塞入輸入而被默默忽略。擴充型號必須更新範本及驗證。
- 導桿預設 1500 mm、外徑 42 mm 是試作假設。來源尺寸參數不全部控制幾何。

## 資料與覆核

每個工作資料夾包含 `case.json`、`sources/`、`evidence/`、`report.html`、`validation.json`。每次產生工作包，都凍結一份尺寸 JSON、來源快照、覆核記錄及檔案指紋。改動來源或尺寸後需重新覆核。

工作包使用同一個 Revit 建族器，透過 `RunRequest` 驗證檔案指紋與支援範圍，不回退讀取 DLL 旁的預設資料。暫時的 `HB-EquipmentCase-*.addin` 在執行結束後自行移除；若 Revit 未完成啟動或程式崩潰，可依工作包 `launch.json` 找到並移除本次專用註冊檔。不要刪除其他外掛。

## 環境與開發

- Python：Pillow、pdfplumber、pypdfium2；DXF 依賴安裝於本資料夾 `.deps`，不修改其他專案的 Python。
- 安裝：`python -m pip install --target .deps -r requirements.txt`（在本資料夾執行）。
- Windows OCR：系統需已安裝適用語言，本機實測為 zh-Hant-TW。
- 建族器：`dotnet build Tests/PumpFamilyBuilder/PumpFamilyBuilder.csproj -c Release`（在儲存庫根目錄執行）。
- 原主工具的專案與功能區沒有被改動；工作台獨立運作，待更多範本驗證後再整合 HB_BIM 功能區。

CLI 與 GUI 共用 `workflow.py`。可使用 `ingest`、`set-recipe`、`review`、`validate`、`prepare`、`gui` 子命令；各命令 `--help` 可查看參數。

## 驗證證據

- `Tests/EquipmentFamily/test_workflow.py`：21 項測試通過，涵蓋覆核、範本、版本、單位、來源竄改、工作包快照、PDF 文字／掃描分流、報告跳脫，以及空調箱來源匹配、範本隔離與工作包路由。
- `Tests/EquipmentFamily/native_smoke.py`：合成 2D／3D DWG、DXF 與真實型錄頁面 OCR，產生泵浦工作包。
- `artifacts/EquipmentFamily/qa-native-final/native-results.json`：CAD／OCR 執行結果。
- `artifacts/EquipmentFamily/qa-solid-02/`：另外確認 DWG 內的真實 ACIS 3DSOLID 可被辨識，並非僅測試 3DFACE。
- `artifacts/EquipmentFamily/qa-native-final/pump-workflow/builds/60fd206dad63/output/result.json`：本次實際 Revit 建族結果；兩類型接管成功，無 Revit 警告。

原始兩份 PDF 的舊桌面路徑本次已不存在，因此端到端示範沿用之前保留的 EF 第 4 頁影像與人工核對尺寸，不宣稱重新讀取了原 PDF。
