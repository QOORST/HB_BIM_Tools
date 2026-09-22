param([Parameter(Mandatory=$true)][string]$Repo)
$ErrorActionPreference='Stop'
$work=Join-Path ([IO.Path]::GetTempPath()) ('HB-LinkedDimensionTests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$work/tests" -Force | Out-Null
Copy-Item -LiteralPath "$Repo/Commands/MEP/MepLinkedDimensionTargets.cs" -Destination $work
Copy-Item -LiteralPath "$PSScriptRoot/Program.cs.txt" -Destination "$work/tests/Program.cs"
Copy-Item -LiteralPath "$PSScriptRoot/Tests.csproj.txt" -Destination "$work/tests/Tests.csproj"
dotnet run --project "$work/tests/Tests.csproj"
if($LASTEXITCODE -ne 0){throw "Regression failed: $work"}
Write-Output "Test artifacts: $work"
