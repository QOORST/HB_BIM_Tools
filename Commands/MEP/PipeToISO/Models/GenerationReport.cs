using System;
using System.Collections.Generic;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Models
{
    internal sealed class GenerationReport
    {
        private readonly List<string> _lines = new List<string>();
        private readonly Action<string, Exception> _logError;
        internal GenerationReport(Action<string, Exception> logError = null) { _logError = logError; }
        internal bool HasFailures { get; private set; }
        internal bool HasSuccess { get; private set; }
        internal bool Run(string name, Func<string> action)
        {
            try
            {
                string detail = action();
                _lines.Add($"成功｜{name}：{detail}");
                HasSuccess = true;
                return true;
            }
            catch (Exception ex)
            {
                try { _logError?.Invoke(name, ex); } catch { /* 記錄失敗不可遮蔽原始錯誤。 */ }
                Fail(name, ex.Message);
                return false;
            }
        }
        internal void Fail(string name, string reason)
        {
            HasFailures = true;
            _lines.Add($"失敗｜{name}：{reason}");
        }
        internal void Skip(string name, string reason) => _lines.Add($"略過｜{name}：{reason}");
        internal void Note(string name, string detail) => _lines.Add($"檢查｜{name}：{detail}");
        public override string ToString() => string.Join(Environment.NewLine, _lines);
    }
}
