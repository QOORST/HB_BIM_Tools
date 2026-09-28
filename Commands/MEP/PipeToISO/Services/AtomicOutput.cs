using System;
using System.IO;
using System.Text;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    internal static class AtomicOutput
    {
        // 同目錄寫完後才換檔；失敗不可留下看似完整的半份算量表。
        internal static void Write(string path, Action<TextWriter> write)
        {
            string full = Path.GetFullPath(path);
            string temp = Path.Combine(Path.GetDirectoryName(full), ".hb-iso-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
                {
                    write(writer);
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(full)) File.Replace(temp, full, null);
                else File.Move(temp, full);
            }
            catch (IOException ex) { throw new IOException("檔案寫入未完成，原檔保留：" + full + "。請確認檔案未被其他程式占用。", ex); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
