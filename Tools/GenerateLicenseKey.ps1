# 授權金鑰生成器
# 用於生成測試授權金鑰

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("Trial", "Standard", "Professional")]
    [string]$LicenseType = "Professional",
    
    [Parameter(Mandatory=$false)]
    [string]$UserName = "Owen",
    
    [Parameter(Mandatory=$false)]
    [string]$Company = "YD",
    
    [Parameter(Mandatory=$false)]
    [int]$Days = 365,
    
    [Parameter(Mandatory=$false)]
    [string]$MachineCode = ""
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "YD_BIM Tools 授權金鑰生成器" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 計算日期
$startDate = Get-Date
$expiryDate = $startDate.AddDays($Days)

# 創建授權資訊物件
$license = @{
    IsEnabled = $true
    LicenseType = $LicenseType
    UserName = $UserName
    Company = $Company
    StartDate = $startDate.ToString("yyyy-MM-ddTHH:mm:ss")
    ExpiryDate = $expiryDate.ToString("yyyy-MM-ddTHH:mm:ss")
    LicenseKey = ""
    MachineCode = $MachineCode
}

# 轉換為 JSON
$jsonData = $license | ConvertTo-Json -Compress

# Base64 編碼
$bytes = [System.Text.Encoding]::UTF8.GetBytes($jsonData)
$licenseKey = [Convert]::ToBase64String($bytes)

# 顯示資訊
Write-Host "授權資訊：" -ForegroundColor Green
Write-Host "  授權類型：$LicenseType" -ForegroundColor White
Write-Host "  使用者：$UserName" -ForegroundColor White
Write-Host "  公司：$Company" -ForegroundColor White
Write-Host "  啟用日期：$($startDate.ToString('yyyy-MM-dd'))" -ForegroundColor White
Write-Host "  到期日期：$($expiryDate.ToString('yyyy-MM-dd'))" -ForegroundColor White
Write-Host "  有效天數：$Days 天" -ForegroundColor White
if ($MachineCode) {
    Write-Host "  綁定機器碼：$MachineCode" -ForegroundColor Yellow
} else {
    Write-Host "  綁定機器碼：無（將自動綁定到啟用的電腦）" -ForegroundColor Gray
}
Write-Host ""

Write-Host "授權金鑰：" -ForegroundColor Green
Write-Host $licenseKey -ForegroundColor Cyan
Write-Host ""

# 複製到剪貼簿
$licenseKey | Set-Clipboard
Write-Host "✓ 授權金鑰已複製到剪貼簿！" -ForegroundColor Green
Write-Host ""

# 儲存到檔案
$outputFile = "license_key_$($LicenseType)_$(Get-Date -Format 'yyyyMMdd_HHmmss').txt"
$licenseKey | Out-File -FilePath $outputFile -Encoding UTF8
Write-Host "✓ 授權金鑰已儲存到：$outputFile" -ForegroundColor Green
Write-Host ""

Write-Host "使用說明：" -ForegroundColor Yellow
Write-Host "1. 在 Revit 中點擊「YD_BIM Tools」→「About」→「授權管理」" -ForegroundColor White
Write-Host "2. 切換到「啟用授權」頁籤" -ForegroundColor White
Write-Host "3. 貼上授權金鑰並點擊「啟用授權」" -ForegroundColor White
Write-Host ""

# 顯示 JSON 內容（用於除錯）
Write-Host "JSON 內容（除錯用）：" -ForegroundColor Gray
Write-Host $jsonData -ForegroundColor DarkGray
Write-Host ""

