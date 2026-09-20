using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace YD_RevitTools.LicenseManager.Helpers.Data
{
    internal static class CsvRecordReader
    {
        // Preserve quoted newlines and ignore comments only at the start of an unquoted record.
        public static List<List<string>> Read(TextReader reader)
        {
            var records = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false, closedQuote = false, first = true;
            int next;
            while ((next = reader.Read()) != -1)
            {
                char c = (char)next;
                if (first) { first = false; if (c == '\uFEFF') continue; }
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                        else { quoted = false; closedQuote = true; }
                    }
                    else field.Append(c);
                    continue;
                }
                if (c == '#' && !closedQuote && row.Count == 0 && string.IsNullOrWhiteSpace(field.ToString()))
                {
                    while (reader.Peek() != -1 && reader.Peek() != '\r' && reader.Peek() != '\n') reader.Read();
                    field.Clear();
                    continue;
                }
                if (c == ',' || c == '\r' || c == '\n')
                {
                    row.Add(field.ToString()); field.Clear(); closedQuote = false;
                    if (c != ',')
                    {
                        if (c == '\r' && reader.Peek() == '\n') reader.Read();
                        if (row.Exists(value => !string.IsNullOrWhiteSpace(value))) records.Add(row);
                        row = new List<string>();
                    }
                }
                else if (closedQuote)
                {
                    if (!char.IsWhiteSpace(c)) throw new FormatException("CSV 引號後必須為分隔符號或換行。");
                }
                else if (c == '"')
                {
                    if (field.Length != 0) throw new FormatException("CSV 欄位內的引號必須以雙引號包覆並跳脫。");
                    quoted = true;
                }
                else field.Append(c);
            }
            if (quoted) throw new FormatException("CSV 有未閉合的引號，請修正檔案後再匯入。");
            if (field.Length > 0 || closedQuote || row.Count > 0)
            {
                row.Add(field.ToString());
                if (row.Exists(value => !string.IsNullOrWhiteSpace(value))) records.Add(row);
            }
            return records;
        }
    }
}
