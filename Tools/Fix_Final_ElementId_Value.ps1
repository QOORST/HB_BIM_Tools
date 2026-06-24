# PowerShell script to fix final remaining .Value occurrences
Write-Host "Fixing final remaining .Value occurrences..." -ForegroundColor Cyan

$projectRoot = Split-Path -Parent $PSScriptRoot

# Files with remaining errors based on compilation output
$filesToFix = @(
    "Commands\AR\Formwork\CmdExportCsv.cs",
    "Commands\AR\Formwork\CmdPickFace.cs",
    "Commands\AR\Formwork\CmdStructuralAnalysis.cs",
    "Commands\AR\Formwork\GeometryExtractor.cs",
    "Commands\MEP\PipeToISO\Services\ISOGenerator.cs"
)

$totalReplacements = 0

foreach ($file in $filesToFix) {
    $fullPath = Join-Path $projectRoot $file
    
    if (Test-Path $fullPath) {
        $content = Get-Content $fullPath -Raw -Encoding UTF8
        $originalContent = $content
        
        # Replace .Value with .GetIdValue() for ElementId
        # More comprehensive patterns
        $content = $content -replace '\.Id\.Value(?=\s*[;,\)\]])', '.Id.GetIdValue()'
        $content = $content -replace '\.Id\.Value(?=\s*==)', '.Id.GetIdValue()'
        $content = $content -replace '\.Id\.Value(?=\s*!=)', '.Id.GetIdValue()'
        $content = $content -replace '\.Id\.Value(?=\s*\))', '.Id.GetIdValue()'
        $content = $content -replace '\.Id\.Value(?=\s*:)', '.Id.GetIdValue()'
        $content = $content -replace '\.Id\.Value(?=\s*\})', '.Id.GetIdValue()'
        
        if ($content -ne $originalContent) {
            $replacements = ([regex]::Matches($originalContent, '\.Id\.Value')).Count - ([regex]::Matches($content, '\.Id\.Value')).Count
            Set-Content -Path $fullPath -Value $content -Encoding UTF8 -NoNewline
            Write-Host "✅ $file - $replacements replacements" -ForegroundColor Green
            $totalReplacements += $replacements
        }
    }
}

Write-Host "`n✅ Total replacements: $totalReplacements" -ForegroundColor Cyan

