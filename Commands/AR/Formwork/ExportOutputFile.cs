using System;
using System.IO;

namespace YD_RevitTools.LicenseManager.Commands.AR.Formwork
{
    // Write beside the destination so cancellation/failure never truncates an existing report.
    internal sealed class ExportOutputFile : IDisposable
    {
        private readonly string _destination;
        private bool _committed;
        internal string TemporaryPath { get; }

        internal ExportOutputFile(string destination)
        {
            _destination = Path.GetFullPath(destination);
            TemporaryPath = Path.Combine(Path.GetDirectoryName(_destination),
                ".hb-formwork-" + Guid.NewGuid().ToString("N") + Path.GetExtension(_destination));
            using (new FileStream(TemporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        }

        internal void Commit()
        {
            if (_committed) throw new InvalidOperationException("Report already committed.");
            if (File.Exists(_destination)) File.Replace(TemporaryPath, _destination, null);
            else File.Move(TemporaryPath, _destination);
            _committed = true;
        }

        public void Dispose()
        {
            if (_committed) return;
            try { File.Delete(TemporaryPath); }
            catch (IOException ex) { System.Diagnostics.Debug.WriteLine("Export temporary file cleanup failed: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { System.Diagnostics.Debug.WriteLine("Export temporary file cleanup failed: " + ex.Message); }
        }
    }
}
