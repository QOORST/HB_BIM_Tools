# PowerShell script to fix remaining .Value occurrences
Write-Host "Fixing remaining .Value occurrences..." -ForegroundColor Cyan

$projectRoot = Split-Path -Parent $PSScriptRoot

# Files with remaining errors
$filesToFix = @(
    "Commands\MEP\PipeToISO\Services\ScheduleGenerator.cs",
    "Commands\MEP\PipeToISO\Command.cs",
    "Commands\AR\Finishings\CmdFaceToFace.cs",
    "Commands\AR\Formwork\AreaCalculator.cs",
    "Commands\AR\Formwork\CmdPickFace.cs",
    "Helpers\AR\AutoJoin\AutoJoinEngine.cs",
    "Commands\AR\Formwork\CmdMain.cs",
    "Commands\MEP\PipeToISO\Services\ISOGenerator.cs",
    "Commands\AR\Formwork\ImprovedFormworkEngine.cs",
    "Commands\AR\Formwork\CmdExportCsv.cs",
    "Commands\AR\Formwork\CmdStructuralAnalysis.cs",
    "Commands\AR\Formwork\GeometryExtractor.cs",
    "Commands\AR\Formwork\FormworkEngine.cs",
    "Helpers\AR\Finishings\GeometryGenerator.cs"
)

$totalReplacements = 0

foreach ($file in $filesToFix) {
    $fullPath = Join-Path $projectRoot $file
    
    if (Test-Path $fullPath) {
        $content = Get-Content $fullPath -Raw -Encoding UTF8
        $originalContent = $content
        
        # Replace .Value with .GetIdValue() for ElementId
        # Pattern: .Value followed by whitespace and specific characters
        $content = $content -replace '\.Value(?=\s*[;\),\]])', '.GetIdValue()'
        $content = $content -replace '\.Value(?=\s*==)', '.GetIdValue()'
        $content = $content -replace '\.Value(?=\s*!=)', '.GetIdValue()'
        $content = $content -replace '\.Value(?=\s*\))', '.GetIdValue()'
        
        if ($content -ne $originalContent) {
            $replacements = ([regex]::Matches($originalContent, '\.Value')).Count - ([regex]::Matches($content, '\.Value')).Count
            Set-Content -Path $fullPath -Value $content -Encoding UTF8 -NoNewline
            Write-Host "✅ $file - $replacements replacements" -ForegroundColor Green
            $totalReplacements += $replacements
        }
    }
}

Write-Host "`n✅ Total replacements: $totalReplacements" -ForegroundColor Cyan

