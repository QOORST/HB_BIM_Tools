using System;
using System.IO;
using System.Text.RegularExpressions;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    internal static class OutputPreflight
    {
        internal static void Check(string folder, string number)
        {
            if (string.IsNullOrWhiteSpace(number) || number.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                number.EndsWith(".") || number.EndsWith(" ") ||
                Regex.IsMatch(number, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
                throw new InvalidOperationException("ISO 編號不可空白、使用保留名稱或包含檔名不允許的字元。");
            if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("請選擇輸出資料夾。");
            try
            {
                Directory.CreateDirectory(folder);
                string probe = Path.Combine(folder, ".hb-iso-write-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.DeleteOnClose))
                {
                    stream.WriteByte(0);
                    stream.Flush(true);
                }
            }
            catch (Exception ex)
            {
                throw new IOException("輸出資料夾無法完成寫入測試，請改選可寫入的位置：" + folder, ex);
            }
        }
    }
}
