# 發版門檻與封裝來源

本次只修改來源及執行 Python 原始碼契約檢查；環境沒有 dotnet、PowerShell、
Windows／Revit。未執行建置腳本、安裝程式、簽章、系統變更或公開發佈。

## 新封裝流程

`Installer/Build_Installer.ps1` 是完整套件入口。公司族庫可用
`-FamilyLibraryProject <CompanyFamilyLibraryMvp.csproj 的完整路徑>` 指定；
預設保留原先相鄰工作區位置。缺少專案直接停止，不再拿已安裝或舊 DLL 代替。

1. 核對 version.json、Inno Setup MyAppVersion、AssemblyInformationalVersion。
2. 確認四年版 RevitAPI／RevitAPIUI、主專案、公司族庫及 dotnet 可用。
3. 為本次操作建立唯一空白暫存目錄；每年版分別 Rebuild 主專案與公司族庫，
   明確指定 Configuration、RevitVersion、x64 及輸出目錄。任何失敗停止。
4. 檢查主 DLL FileVersion；八個 DLL 均產出後寫入含年版配置及 SHA256 的 receipt。
5. 準備腳本只接受上述 BuildRoot／公司族庫路徑，逐一核對 receipt 與 DLL hash。
   不支援舊的無參數準備方式，也不提供 2025→2026 fallback。
6. 必要依賴、x64 原生 SQLite、新版主機用 OpenXML／Packaging、文件、資源、
   族庫資料庫／預覽器缺失皆停止。共用 DLL 必須在四年版輸出中 SHA256 相同；
   若不同，停止並要求調整 Inno Setup 的年版專屬依賴，不能默默挑一份。
7. 完成預檢後才重建 staging；其後維持既有授權簽章／Inno Setup 流程。
   最終只接受本次編譯時間以後、版本符合且唯一的 Setup 檔案。

Receipt 用於流程內防止誤用或變更產物，不是來源簽章／防偽機制。直接手動執行
ISCC 會繞過此流程，不能作為合格發版流程。共享 staging 不支援同時執行多個
建置；發布機應序列執行。NuGet 快取的 netstandard OpenXML／Packaging 路徑
目前沿用既有版本，若 restore 後不存在需先確認套件解析，不可略過。

## 發版前必填證據

- [ ] 在有四年版 SDK 的 Windows 機器解析 PowerShell，執行八個 fresh builds。
- [ ] 刪除任一年版 DLL、必要依賴、資料庫、receipt 或修改其 hash，流程必須停止。
- [ ] 製造 metadata／DLL 版本不一致，流程必須停止。
- [ ] 主專案或公司族庫編譯失敗時，不執行簽章／ISCC，也不發佈既有 staging。
- [ ] 四年版主機啟動與 [功能矩陣](capability-matrix.md) 中適用項目有獨立紀錄。
- [ ] 乾淨機／升級／解除安裝及憑證行為完成驗收。
- [ ] 核對公開 release、下載 URL、版本說明、SHA256 與實際套件；測試包不可宣稱正式版。
- [ ] 所有公開發布與系統操作另經授權。

## 可在此環境執行的來源回歸檢查

```sh
python -m unittest discover -s Tests/ReleaseSafeguards -v
```

這些測試檢查腳本中的防護契約及文件，不執行 PowerShell；測試通過不是腳本
語法、MSBuild、封裝可用性或 Revit 安全性的動態驗證。
