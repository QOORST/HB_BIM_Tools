param([Parameter(Mandatory=$true)][string]$Repo)
$ErrorActionPreference='Stop'
$work=Join-Path ([IO.Path]::GetTempPath()) ('HB-SleeveTests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$work/tests" -Force | Out-Null
foreach($file in @('SleeveAnnotationGuard.cs','SleeveOperationRolledBackException.cs')) {
    Copy-Item -LiteralPath (Join-Path $Repo "Commands/MEP/$file") -Destination $work
}
Copy-Item -LiteralPath "$PSScriptRoot/Program.cs.txt" -Destination "$work/tests/Program.cs"
Copy-Item -LiteralPath "$PSScriptRoot/Tests.csproj.txt" -Destination "$work/tests/Tests.csproj"
$service=Get-Content (Join-Path $Repo 'Commands/MEP/PipeSleeveService.cs') -Raw
$a=$service.IndexOf('        public static PipeSleeveResult ExecuteCreate(')
$b=$service.IndexOf('        private static PipeSleeveResult CreateSleeves(', $a)
if($a -lt 0 -or $b -lt 0){throw 'ExecuteCreate boundaries missing'}
$code='using Autodesk.Revit.DB; namespace YD_RevitTools.LicenseManager.Commands.MEP { internal static partial class PipeSleeveService {'+$service.Substring($a,$b-$a)+'}}'
[IO.File]::WriteAllText("$work/tests/ExecuteCreate.generated.cs",$code)
dotnet run --project "$work/tests/Tests.csproj"
if($LASTEXITCODE -ne 0){throw "Regression failed: $work"}
Write-Output "Test artifacts: $work"
