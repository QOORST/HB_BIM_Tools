using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace HB.CadRevision
{
    public sealed class BatchProbe
    {
        [CommandMethod("HBCADPROBE", CommandFlags.Modal)]
        public void Execute()
        {
            var manifest=Environment.GetEnvironmentVariable("HB_CAD_PROBE_MANIFEST");
            var output=Environment.GetEnvironmentVariable("HB_CAD_PROBE_OUTPUT");
            if(string.IsNullOrWhiteSpace(manifest)||string.IsNullOrWhiteSpace(output)) return;
            Directory.CreateDirectory(output);
            var log=new StringBuilder();
            foreach(var path in File.ReadAllLines(manifest).Where(File.Exists))
            {
                log.AppendLine("FILE: "+path);
                try
                {
                    using(var db=new Database(false,true))
                    {
                        db.ReadDwgFile(path,FileOpenMode.OpenForReadAndReadShare,false,null);
                        db.CloseInput(true);
                        log.AppendLine("INSUNITS: "+db.Insunits);
                        using(var tx=db.TransactionManager.StartOpenCloseTransaction())
                        {
                            var bt=(BlockTable)tx.GetObject(db.BlockTableId,OpenMode.ForRead);
                            var ms=(BlockTableRecord)tx.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
                            var counts=new Dictionary<string,int>();
                            foreach(ObjectId id in ms)
                            {
                                var e=tx.GetObject(id,OpenMode.ForRead) as Entity;
                                if(e==null) continue;
                                var key=e.GetType().Name+" | "+e.Layer;
                                counts[key]=counts.TryGetValue(key,out var n)?n+1:1;
                                if(e is BlockReference br)
                                {
                                    var block=(BlockTableRecord)tx.GetObject(br.BlockTableRecord,OpenMode.ForRead);
                                    if(block.IsFromExternalReference) log.AppendLine("XREF: "+block.PathName+" | position="+br.Position+" | scale="+br.ScaleFactors+" | rotation="+br.Rotation);
                                }
                            }
                            foreach(var c in counts.OrderByDescending(x=>x.Value)) log.AppendLine(c.Value+" | "+c.Key);
                        }
                    }
                    var data=DwgReader.Read(path,new HashSet<string>());
                    log.AppendLine("SUPPORTED SEGMENTS: "+data.Segments.Count);
                    if(data.Segments.Count>0) log.AppendLine("BOUNDS mm: "+data.Segments.Min(s=>Math.Min(s.X1,s.X2))+", "+data.Segments.Min(s=>Math.Min(s.Y1,s.Y2))+" -> "+data.Segments.Max(s=>Math.Max(s.X1,s.X2))+", "+data.Segments.Max(s=>Math.Max(s.Y1,s.Y2)));
                    foreach(var c in data.Skipped) log.AppendLine("SKIPPED: "+c.Key+"="+c.Value);
                }
                catch(System.Exception ex) { log.AppendLine("ERROR: "+ex.Message); }
                File.WriteAllText(Path.Combine(output,"probe.txt"),log.ToString(),new UTF8Encoding(true));
            }
        }
    }
}
