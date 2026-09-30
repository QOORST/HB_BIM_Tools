param([string]$PythonPath,[string]$CasePath)
$ErrorActionPreference='Stop'
if (!$PythonPath) { $PythonPath=Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' }
if (!(Test-Path -LiteralPath $PythonPath)) { throw 'Specify -PythonPath for Python with Pillow, pdfplumber and pypdfium2.' }
$entry=Join-Path $PSScriptRoot 'workflow.py'
$arguments=@(('"'+$entry+'"'),'gui')
if($CasePath){$arguments+=@('--case',('"'+[IO.Path]::GetFullPath($CasePath)+'"'))}
Start-Process -FilePath $PythonPath -ArgumentList $arguments -WindowStyle Hidden
