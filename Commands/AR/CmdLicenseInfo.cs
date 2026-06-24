using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
#if !REVIT2025 && !REVIT2026
using System.Windows.Interop;
using YD_RevitTools.LicenseManager.UI;
#else
using WinForms = System.Windows.Forms;
#endif

namespace YD_RevitTools.LicenseManager.Commands.AR
{
    [Transaction(TransactionMode.ReadOnly)]
    public class CmdLicenseInfo : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
#if !REVIT2025 && !REVIT2026
                var window = new LicenseWindow();
                try
                {
                    var mainWindowHandle = commandData.Application.MainWindowHandle;
                    if (mainWindowHandle != IntPtr.Zero)
                    {
                        var helper = new WindowInteropHelper(window);
                        helper.Owner = mainWindowHandle;
                    }
                }
                catch
                {
                    // Ignore owner binding failures; dialog can still be shown.
                }

                window.ShowDialog();
                return Result.Succeeded;
#else
                using (var dlg = new LicenseManagerDialog())
                {
                    dlg.ShowDialog();
                }
                return Result.Succeeded;
#endif
            }
            catch (Exception ex)
            {
                message = $"開啟授權管理視窗失敗：{ex.Message}";
                return Result.Failed;
            }
        }
    }

#if REVIT2025 || REVIT2026
    internal sealed class LicenseManagerDialog : WinForms.Form
    {
        private readonly WinForms.TextBox _txtInfo;
        private readonly WinForms.TextBox _txtKey;

        public LicenseManagerDialog()
        {
            Text = "YD_BIM Tools - 授權管理";
            Width = 760;
            Height = 560;
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new System.Drawing.Font("Microsoft JhengHei UI", 10f);

            Controls.Add(new WinForms.Label
            {
                Left = 18,
                Top = 14,
                Width = 700,
                Text = "授權狀態與基本資訊",
                Font = new System.Drawing.Font("Microsoft JhengHei UI", 11f, System.Drawing.FontStyle.Bold)
            });

            _txtInfo = new WinForms.TextBox
            {
                Left = 18,
                Top = 44,
                Width = 710,
                Height = 200,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = WinForms.ScrollBars.Vertical
            };
            Controls.Add(_txtInfo);

            Controls.Add(new WinForms.Label
            {
                Left = 18,
                Top = 258,
                Width = 700,
                Text = "授權碼輸入（貼上後按「啟用 / 更新授權」）",
                Font = new System.Drawing.Font("Microsoft JhengHei UI", 10f, System.Drawing.FontStyle.Bold)
            });

            _txtKey = new WinForms.TextBox
            {
                Left = 18,
                Top = 286,
                Width = 710,
                Height = 120,
                Multiline = true,
                ScrollBars = WinForms.ScrollBars.Vertical
            };
            Controls.Add(_txtKey);

            var btnRefresh = new WinForms.Button { Left = 18, Top = 424, Width = 120, Height = 36, Text = "重新整理狀態" };
            btnRefresh.Click += (s, e) => RefreshInfo();
            Controls.Add(btnRefresh);

            var btnActivate = new WinForms.Button { Left = 148, Top = 424, Width = 170, Height = 36, Text = "啟用 / 更新授權" };
            btnActivate.Click += (s, e) => ActivateLicense();
            Controls.Add(btnActivate);

            var btnCopyMachine = new WinForms.Button { Left = 328, Top = 424, Width = 120, Height = 36, Text = "複製機器碼" };
            btnCopyMachine.Click += (s, e) =>
            {
                try
                {
                    WinForms.Clipboard.SetText(LicenseManager.Instance.GetMachineCode() ?? string.Empty);
                    WinForms.MessageBox.Show("機器碼已複製到剪貼簿。", "授權管理", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    WinForms.MessageBox.Show($"複製機器碼失敗：{ex.Message}", "授權管理", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                }
            };
            Controls.Add(btnCopyMachine);

            var btnClose = new WinForms.Button { Left = 608, Top = 470, Width = 120, Height = 36, Text = "關閉" };
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);

            RefreshInfo();
        }

        private void RefreshInfo()
        {
            var lm = LicenseManager.Instance;
            lm.ReloadLicense();
            var validation = lm.ValidateLicense();
            var machineCode = lm.GetMachineCode();

            _txtInfo.Text =
                $"授權狀態：{(validation.IsValid ? "有效" : "未啟用或已過期")}\r\n" +
                $"原因：{validation.Message}\r\n\r\n" +
                (validation.LicenseInfo != null
                    ? $"使用者：{validation.LicenseInfo.UserName}\r\n公司：{validation.LicenseInfo.Company}\r\n方案：{validation.LicenseInfo.LicenseType}\r\n啟用日：{validation.LicenseInfo.StartDate:yyyy-MM-dd}\r\n到期日：{validation.LicenseInfo.ExpiryDate:yyyy-MM-dd}\r\n\r\n"
                    : string.Empty) +
                $"機器碼：{machineCode}";
        }

        private void ActivateLicense()
        {
            var key = (_txtKey.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                WinForms.MessageBox.Show("請先貼上授權碼。", "授權管理", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            var result = LicenseManager.Instance.ActivateLicense(key);
            if (result.IsValid)
            {
                WinForms.MessageBox.Show("授權已成功啟用/更新。", "授權管理", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                _txtKey.Clear();
                RefreshInfo();
            }
            else
            {
                WinForms.MessageBox.Show($"授權啟用失敗：\r\n{result.Message}", "授權管理", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            }
        }
    }
#endif
}
