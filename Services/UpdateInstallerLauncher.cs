using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace YD_RevitTools.LicenseManager.Services
{
    internal static class UpdateInstallerLauncher
    {
        internal static void Launch(string installerPath)
        {
            installerPath = Path.GetFullPath(installerPath);
            string expectedHash;
            using (var stream = File.OpenRead(installerPath))
            using (var sha = SHA256.Create())
                expectedHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");

            string eventName = @"Local\HB_BIM_Update_" + Guid.NewGuid().ToString("N");
            string logPath = Path.Combine(Path.GetDirectoryName(installerPath), "update-launch.log");
            // Inline encoded commands avoid a mutable temporary script file. The helper
            // lives outside Revit and acknowledges readiness before we report success.
            string script = @"
$ErrorActionPreference = 'Stop'
$installer = '" + Quote(installerPath) + @"'
$log = '" + Quote(logPath) + @"'
try {
    Add-Content -LiteralPath $log -Value ('Waiting for Revit: ' + [DateTime]::Now)
    $ready = [Threading.EventWaitHandle]::OpenExisting('" + eventName + @"')
    $ready.Set() | Out-Null
    $ready.Dispose()
    while (@(Get-Process -Name Revit -ErrorAction SilentlyContinue).Count -gt 0) {
        Start-Sleep -Seconds 2
    }
    # Keep the verified file locked against modification until launch completes.
    $stream = [IO.File]::Open($installer, 'Open', 'Read', 'Read')
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose() }
        if ($hash -ne '" + expectedHash + @"') { throw 'Installer changed after verification.' }
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = $installer
        $info.UseShellExecute = $true
        $process = [Diagnostics.Process]::Start($info)
        if ($null -eq $process) { throw 'Installer did not start.' }
        Add-Content -LiteralPath $log -Value ('Installer started: ' + [DateTime]::Now)
        $process.Dispose()
    } finally { $stream.Dispose() }
} catch {
    $failure = $_.Exception.Message
    Add-Content -LiteralPath $log -Value ('ERROR: ' + $failure) -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show(('更新安裝程式無法啟動：' + $failure + [Environment]::NewLine + '紀錄：' + $log), 'HB_BIM Tools 更新失敗') | Out-Null
    exit 1
}
";
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName))
            {
                var info = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                        @"WindowsPowerShell\v1.0\powershell.exe"),
                    Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " +
                        Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var helper = Process.Start(info))
                {
                    if (helper == null)
                        throw new InvalidOperationException("無法啟動更新等待程序。");
                    if (!ready.WaitOne(TimeSpan.FromSeconds(15)))
                    {
                        if (!helper.HasExited) helper.Kill();
                        throw new TimeoutException("更新等待程序未就緒。紀錄：" + logPath);
                    }
                }
            }
        }

        private static string Quote(string value) => value.Replace("'", "''");
    }
}
