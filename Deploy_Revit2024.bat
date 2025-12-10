@echo off
echo ========================================
echo YD BIM Tools - 部署到 Revit 2024
echo ========================================
echo.

set SOURCE=bin\Release2024
set TARGET=C:\ProgramData\Autodesk\Revit\Addins\2024\YD_BIM_Tools

echo 來源資料夾: %SOURCE%
echo 目標資料夾: %TARGET%
echo.

REM 檢查來源資料夾是否存在
if not exist "%SOURCE%" (
    echo 錯誤: 找不到來源資料夾 %SOURCE%
    pause
    exit /b 1
)

REM 建立目標資料夾（如果不存在）
if not exist "%TARGET%" (
    echo 建立目標資料夾...
    mkdir "%TARGET%"
)

echo 正在複製檔案...
echo.

REM 複製主程式 DLL
echo [1/17] 複製主程式...
copy /Y "%SOURCE%\YD_RevitTools.LicenseManager.dll" "%TARGET%\"

REM 複製 EPPlus 相關 DLL
echo [2/17] 複製 EPPlus.dll...
copy /Y "%SOURCE%\EPPlus.dll" "%TARGET%\"

echo [3/17] 複製 EPPlus.Interfaces.dll...
copy /Y "%SOURCE%\EPPlus.Interfaces.dll" "%TARGET%\"

echo [4/17] 複製 EPPlus.System.Drawing.dll...
copy /Y "%SOURCE%\EPPlus.System.Drawing.dll" "%TARGET%\"

echo [5/17] 複製 Microsoft.IO.RecyclableMemoryStream.dll...
copy /Y "%SOURCE%\Microsoft.IO.RecyclableMemoryStream.dll" "%TARGET%\"

echo [6/17] 複製 System.ComponentModel.Annotations.dll...
copy /Y "%SOURCE%\System.ComponentModel.Annotations.dll" "%TARGET%\"

REM 複製其他依賴項
echo [7/17] 複製 Newtonsoft.Json.dll...
copy /Y "%SOURCE%\Newtonsoft.Json.dll" "%TARGET%\"

echo [8/17] 複製 System.Buffers.dll...
copy /Y "%SOURCE%\System.Buffers.dll" "%TARGET%\"

echo [9/17] 複製 System.Drawing.Common.dll...
copy /Y "%SOURCE%\System.Drawing.Common.dll" "%TARGET%\"

echo [10/17] 複製 System.Memory.dll...
copy /Y "%SOURCE%\System.Memory.dll" "%TARGET%\"

echo [11/17] 複製 System.Numerics.Vectors.dll...
copy /Y "%SOURCE%\System.Numerics.Vectors.dll" "%TARGET%\"

echo [12/17] 複製 System.Runtime.CompilerServices.Unsafe.dll...
copy /Y "%SOURCE%\System.Runtime.CompilerServices.Unsafe.dll" "%TARGET%\"

echo [13/17] 複製 System.Text.Encodings.Web.dll...
copy /Y "%SOURCE%\System.Text.Encodings.Web.dll" "%TARGET%\"

echo [14/17] 複製 System.Text.Json.dll...
copy /Y "%SOURCE%\System.Text.Json.dll" "%TARGET%\"

echo [15/17] 複製 System.Threading.Tasks.Extensions.dll...
copy /Y "%SOURCE%\System.Threading.Tasks.Extensions.dll" "%TARGET%\"

echo [16/17] 複製 System.ValueTuple.dll...
copy /Y "%SOURCE%\System.ValueTuple.dll" "%TARGET%\"

echo [17/17] 複製 Microsoft.Bcl.AsyncInterfaces.dll...
copy /Y "%SOURCE%\Microsoft.Bcl.AsyncInterfaces.dll" "%TARGET%\"

echo.
echo ========================================
echo 部署完成！
echo ========================================
echo.
echo 請重新啟動 Revit 2024 以載入更新的插件。
echo.
pause

