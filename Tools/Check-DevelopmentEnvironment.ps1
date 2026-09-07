[CmdletBinding()]
param(
    [ValidateSet("Development", "Release")]
    [string]$Mode = "Development",

    [ValidateSet("2022", "2024", "2025", "2026")]
    [string[]]$RevitVersions = @("2022", "2024", "2025", "2026"),

    [switch]$RestorePackages,

    [switch]$CheckLicenseAdministration,

    [string]$LicensePrivateKeyPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:PassCount = 0
$script:WarningCount = 0
$script:FailureCount = 0

function Write-CheckResult {
    param(
        [ValidateSet("PASS", "WARN", "FAIL", "INFO")]
        [string]$Level,
        [string]$Message
    )

    $color = switch ($Level) {
        "PASS" { $script:PassCount++; "Green" }
        "WARN" { $script:WarningCount++; "Yellow" }
        "FAIL" { $script:FailureCount++; "Red" }
        default { "Gray" }
    }

    Write-Host "[$Level] $Message" -ForegroundColor $color
}

function Test-Tool {
    param(
        [string]$Name,
        [bool]$Required = $true
    )

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) {
        Write-CheckResult PASS "$Name found: $($command.Source)"
        return $true
    }

    if ($Required) {
        Write-CheckResult FAIL "$Name was not found in PATH."
    } else {
        Write-CheckResult WARN "$Name was not found in PATH."
    }
    return $false
}

function Get-CodeSigningCertificate {
    param([string]$Thumbprint)

    foreach ($store in @("Cert:\CurrentUser\My", "Cert:\LocalMachine\My")) {
        try {
            $certificate = Get-ChildItem -Path $store -CodeSigningCert -ErrorAction Stop |
                Where-Object {
                    $_.HasPrivateKey -and
                    ($_.Thumbprint -replace " ", "").Equals($Thumbprint, [StringComparison]::OrdinalIgnoreCase)
                } |
                Sort-Object NotAfter -Descending |
                Select-Object -First 1

            if ($certificate) {
                return $certificate
            }
        } catch {
            Write-CheckResult WARN "Unable to inspect certificate store $store."
        }
    }

    return $null
}

function Get-FamilyLibraryPayloadSource {
    param(
        [string]$ProjectRoot,
        [string]$Version
    )

    $revitApiRoot = Split-Path (Split-Path $ProjectRoot -Parent) -Parent
    $familyProjectRoot = Join-Path $revitApiRoot "Codex\work\family-library-management\addin"
    $candidates = @(
        (Join-Path $familyProjectRoot "bin\x64\Release$Version\net48\CompanyFamilyLibraryMvp.dll"),
        (Join-Path $ProjectRoot "bin\Release$Version\CompanyFamilyLibraryMvp.dll"),
        "C:\ProgramData\Autodesk\Revit\Addins\$Version\HB_BIM\CompanyFamilyLibraryMvp.dll"
    )

    return $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

$projectRoot = Split-Path $PSScriptRoot -Parent
$projectFile = Join-Path $projectRoot "YD_RevitTools.LicenseManager.csproj"
$solutionFile = Join-Path $projectRoot "YD_RevitTools.LicenseManager.sln"
$publicCertificatePath = Join-Path $projectRoot "Installer\LAN_CodeSigning.cer"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "HB_BIM Tools development environment check" -ForegroundColor Cyan
Write-Host "Mode: $Mode" -ForegroundColor Cyan
Write-Host "Project: $projectRoot" -ForegroundColor DarkGray
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Core development tools" -ForegroundColor White
$gitAvailable = Test-Tool -Name "git"
$dotnetAvailable = Test-Tool -Name "dotnet"

if (Test-Path -LiteralPath $projectFile) {
    Write-CheckResult PASS "Project file found."
} else {
    Write-CheckResult FAIL "Project file not found: $projectFile"
}

if (Test-Path -LiteralPath $solutionFile) {
    Write-CheckResult PASS "Solution file found."
} else {
    Write-CheckResult FAIL "Solution file not found: $solutionFile"
}

$net48ReferencePath = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll"
if (Test-Path -LiteralPath $net48ReferencePath) {
    Write-CheckResult PASS ".NET Framework 4.8 reference assemblies found."
} else {
    Write-CheckResult FAIL ".NET Framework 4.8 Developer Pack is missing."
}

if ($dotnetAvailable) {
    try {
        $dotnetVersion = (& dotnet --version 2>$null | Select-Object -First 1)
        Write-CheckResult INFO ".NET SDK version: $dotnetVersion"
    } catch {
        Write-CheckResult FAIL "Unable to run dotnet --version."
    }
}

Write-Host ""
Write-Host "Git workspace" -ForegroundColor White
if ($gitAvailable -and (Test-Path -LiteralPath (Join-Path $projectRoot ".git"))) {
    try {
        $branch = (& git -C $projectRoot branch --show-current 2>$null | Select-Object -First 1)
        $remote = (& git -C $projectRoot remote get-url origin 2>$null | Select-Object -First 1)
        $changes = @(& git -C $projectRoot status --porcelain 2>$null)

        Write-CheckResult PASS "Git repository found. Current branch: $branch"
        if ($remote -match '^https://github\.com/QOORST/HB_BIM_Tools(?:\.git)?$') {
            Write-CheckResult PASS "GitHub origin points to QOORST/HB_BIM_Tools."
        } else {
            Write-CheckResult WARN "GitHub origin differs from the expected repository."
        }

        if ($changes.Count -eq 0) {
            Write-CheckResult PASS "Working tree is clean."
        } else {
            Write-CheckResult WARN "Working tree contains $($changes.Count) changed or untracked item(s)."
        }

        $trackedFiles = @(& git -C $projectRoot ls-files 2>$null)
        $sensitiveTrackedFiles = @($trackedFiles | Where-Object {
            $_ -match '(?i)(^|/)(\.env(?:\.|$)|.*private.*\.xml$|issued_licenses\.csv$|license_key_.*\.txt$|.*\.(pfx|p12|pem|key)$)'
        })

        if ($sensitiveTrackedFiles.Count -eq 0) {
            Write-CheckResult PASS "No private-key or issued-license files are tracked by Git."
        } else {
            Write-CheckResult FAIL "Sensitive file names are tracked by Git: $($sensitiveTrackedFiles -join ', ')"
        }
    } catch {
        Write-CheckResult FAIL "Unable to inspect Git workspace: $($_.Exception.Message)"
    }
} else {
    Write-CheckResult FAIL "This folder is not a Git working copy."
}

Write-Host ""
Write-Host "Revit build targets" -ForegroundColor White
$availableRevitCount = 0
foreach ($version in $RevitVersions) {
    $revitDirectory = "C:\Program Files\Autodesk\Revit $version"
    $apiPath = Join-Path $revitDirectory "RevitAPI.dll"
    $apiUiPath = Join-Path $revitDirectory "RevitAPIUI.dll"

    if ((Test-Path -LiteralPath $apiPath) -and (Test-Path -LiteralPath $apiUiPath)) {
        $availableRevitCount++
        Write-CheckResult PASS "Revit $version API references found."
    } elseif ($Mode -eq "Release") {
        Write-CheckResult FAIL "Revit $version API references are required for a full release build."
    } else {
        Write-CheckResult WARN "Revit $version is unavailable; skip its build configuration on this computer."
    }
}

if ($Mode -eq "Development" -and $availableRevitCount -eq 0) {
    Write-CheckResult FAIL "Install at least one supported Revit version to compile the add-in."
}

Write-Host ""
Write-Host "Optional company library module" -ForegroundColor White
$familyPayloadCount = 0
foreach ($version in $RevitVersions) {
    $payloadSource = Get-FamilyLibraryPayloadSource -ProjectRoot $projectRoot -Version $version
    if ($payloadSource) {
        $familyPayloadCount++
        Write-CheckResult PASS "Company library payload available for Revit $version."
    } elseif ($Mode -eq "Release") {
        Write-CheckResult FAIL "Company library payload is missing for Revit $version. Build the release on the company computer."
    } else {
        Write-CheckResult INFO "Company library payload is not present for Revit $version; other tools remain buildable."
    }
}

Write-Host ""
Write-Host "Release tooling" -ForegroundColor White
$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $isccPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($iscc) {
    Write-CheckResult PASS "Inno Setup compiler found."
} elseif ($Mode -eq "Release") {
    Write-CheckResult FAIL "Inno Setup 6 is required for release builds."
} else {
    Write-CheckResult INFO "Inno Setup is not required for normal development."
}

$publicCertificate = $null
if (Test-Path -LiteralPath $publicCertificatePath) {
    try {
        $publicCertificate = Get-PfxCertificate -FilePath $publicCertificatePath
        Write-CheckResult PASS "LAN public signing certificate found in the repository."
    } catch {
        Write-CheckResult FAIL "Unable to read LAN public signing certificate."
    }
} else {
    Write-CheckResult FAIL "LAN public signing certificate is missing from Installer."
}

if ($publicCertificate) {
    $signingCertificate = Get-CodeSigningCertificate -Thumbprint $publicCertificate.Thumbprint
    if ($signingCertificate) {
        Write-CheckResult PASS "Matching LAN code-signing certificate with private key is installed."
    } elseif ($Mode -eq "Release") {
        Write-CheckResult FAIL "The LAN code-signing private certificate is required for a release build."
    } else {
        Write-CheckResult INFO "Signing private certificate is not installed; normal development is unaffected."
    }
}

Write-Host ""
Write-Host "License administration" -ForegroundColor White
if ($CheckLicenseAdministration) {
    if ([string]::IsNullOrWhiteSpace($LicensePrivateKeyPath)) {
        Write-CheckResult FAIL "Specify -LicensePrivateKeyPath when checking license administration."
    } elseif (-not (Test-Path -LiteralPath $LicensePrivateKeyPath)) {
        Write-CheckResult FAIL "The supplied license private-key path does not exist."
    } else {
        $resolvedKeyPath = (Resolve-Path -LiteralPath $LicensePrivateKeyPath).Path
        if ($resolvedKeyPath.StartsWith($projectRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Write-CheckResult FAIL "The license private key must be stored outside the Git project."
        } else {
            Write-CheckResult PASS "License private key exists outside the Git project. Contents were not read."
        }
    }
} else {
    Write-CheckResult INFO "License private key was not checked. Use -CheckLicenseAdministration only on authorized computers."
}

if ($RestorePackages -and $dotnetAvailable -and (Test-Path -LiteralPath $projectFile)) {
    Write-Host ""
    Write-Host "NuGet restore" -ForegroundColor White
    & dotnet restore $projectFile
    if ($LASTEXITCODE -eq 0) {
        Write-CheckResult PASS "NuGet packages restored successfully."
    } else {
        Write-CheckResult FAIL "NuGet package restore failed."
    }
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Summary: $script:PassCount passed, $script:WarningCount warning(s), $script:FailureCount failure(s)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if ($script:FailureCount -gt 0) {
    exit 1
}

exit 0
