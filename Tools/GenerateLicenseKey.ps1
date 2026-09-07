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
    [string]$MachineCode = "",

    [Parameter(Mandatory=$true)]
    [string]$PrivateKeyPath,

    [Parameter(Mandatory=$false)]
    [string]$PrivateKeyPassword = "",

    [Parameter(Mandatory=$false)]
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\LicenseRecords\Generated"),

    [Parameter(Mandatory=$false)]
    [string]$RecordPath = (Join-Path $PSScriptRoot "..\LicenseRecords\issued_licenses.csv")
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "HB_BIM Tools 授權金鑰生成器" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

function Get-RsaPrivateKey {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,

        [Parameter(Mandatory=$false)]
        [string]$Password = ""
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "找不到私鑰檔案：$Path"
    }

    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension -eq ".pfx" -or $extension -eq ".p12") {
        $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable
        $cert = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList @($Path, $Password, $flags)
        $rsa = $cert.PrivateKey
        if ($null -eq $rsa) {
            throw "指定的憑證沒有 RSA 私鑰。"
        }

        return $rsa
    }

    $xml = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $rsaProvider = New-Object -TypeName System.Security.Cryptography.RSACryptoServiceProvider -ArgumentList 2048
    $rsaProvider.FromXmlString($xml)
    return $rsaProvider
}

function New-RsaSha256Signature {
    param(
        [Parameter(Mandatory=$true)]
        [byte[]]$Payload,

        [Parameter(Mandatory=$true)]
        [System.Security.Cryptography.RSA]$PrivateKey
    )

    if ($PrivateKey -is [System.Security.Cryptography.RSACryptoServiceProvider]) {
        return $PrivateKey.SignData($Payload, [System.Security.Cryptography.CryptoConfig]::MapNameToOID("SHA256"))
    }

    return $PrivateKey.SignData($Payload, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
}

function ConvertTo-CsvCell {
    param([string]$Value)

    if ($null -eq $Value) {
        $Value = ""
    }

    return '"' + $Value.Replace('"', '""') + '"'
}

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

# Base64 編碼並使用 RSA-SHA256 簽章
$bytes = [System.Text.Encoding]::UTF8.GetBytes($jsonData)
$rsa = Get-RsaPrivateKey -Path $PrivateKeyPath -Password $PrivateKeyPassword
$signature = New-RsaSha256Signature -Payload $bytes -PrivateKey $rsa
$licenseKey = [Convert]::ToBase64String($bytes) + "." + [Convert]::ToBase64String($signature)

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
if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$safeUserName = ($UserName -replace '[\\/:*?"<>|]', '_')
$outputFileName = "license_key_$($LicenseType)_$($safeUserName)_$(Get-Date -Format 'yyyyMMdd_HHmmss').txt"
$outputFile = Join-Path $OutputDirectory $outputFileName
$licenseKey | Out-File -FilePath $outputFile -Encoding UTF8
Write-Host "✓ 授權金鑰已儲存到：$outputFile" -ForegroundColor Green
Write-Host ""

# 追加發碼紀錄，不寫入完整授權碼，避免紀錄檔外流即可直接啟用。
$recordDirectory = Split-Path -Parent $RecordPath
if (-not (Test-Path -LiteralPath $recordDirectory)) {
    New-Item -ItemType Directory -Path $recordDirectory -Force | Out-Null
}

if (-not (Test-Path -LiteralPath $RecordPath)) {
    "IssuedAt,LicenseType,UserName,Company,MachineCode,StartDate,ExpiryDate,Days,LicenseKeyFile" | Out-File -FilePath $RecordPath -Encoding UTF8
}

$record = @(
    ConvertTo-CsvCell -Value (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    ConvertTo-CsvCell -Value $LicenseType
    ConvertTo-CsvCell -Value $UserName
    ConvertTo-CsvCell -Value $Company
    ConvertTo-CsvCell -Value $MachineCode
    ConvertTo-CsvCell -Value $startDate.ToString("yyyy-MM-dd")
    ConvertTo-CsvCell -Value $expiryDate.ToString("yyyy-MM-dd")
    ConvertTo-CsvCell -Value $Days.ToString()
    ConvertTo-CsvCell -Value $outputFileName
) -join ","
$record | Out-File -FilePath $RecordPath -Encoding UTF8 -Append
Write-Host "✓ 發碼紀錄已追加到：$RecordPath" -ForegroundColor Green
Write-Host ""

Write-Host "使用說明：" -ForegroundColor Yellow
Write-Host "1. 在 Revit 中點擊「HB_BIM Tools」→「About」→「授權管理」" -ForegroundColor White
Write-Host "2. 切換到「啟用授權」頁籤" -ForegroundColor White
Write-Host "3. 貼上授權金鑰並點擊「啟用授權」" -ForegroundColor White
Write-Host ""

# 顯示 JSON 內容（用於除錯）
Write-Host "JSON 內容（除錯用）：" -ForegroundColor Gray
Write-Host $jsonData -ForegroundColor DarkGray
Write-Host ""

