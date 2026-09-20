using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;

namespace YDBIM.AutoDimension.Core
{
    internal sealed class DimensionTextPreview : IDirectContext3DServer, IDisposable
    {
        internal sealed class Box
        {
            internal XYZ Center, Along, Across;
            internal double Width, Height;
            internal bool Conflict;
            internal XYZ[] Corners => new[] { Center-Along*Width/2-Across*Height/2,
                Center+Along*Width/2-Across*Height/2, Center+Along*Width/2+Across*Height/2, Center-Along*Width/2+Across*Height/2 };
        }
        private readonly Guid id = Guid.NewGuid();
        private readonly Document document;
        private readonly ElementId viewId;
        private readonly List<Box> boxes;
        private MultiServerService service;
        private bool registered;
        internal string RenderError { get; private set; }
        internal bool Rendered { get; private set; }
        internal DimensionTextPreview(View view, List<Box> boxes)
        { document = view.Document; viewId = view.Id; this.boxes = boxes; }
        internal void Start()
        {
            service = (MultiServerService)ExternalServiceRegistry.GetService(GetServiceId());
            service.AddServer(this);
            registered = true;
            var active = service.GetActiveServerIds().ToList(); active.Add(id); service.SetActiveServers(active);
        }
        public Guid GetServerId() => id;
        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
        public string GetName() => "HB_BIM dimension text preview";
        public string GetDescription() => "Temporary dimension text bounds";
        public string GetVendorId() => "HBBM";
        public string GetApplicationId() => "HB_BIM";
        public string GetSourceId() => id.ToString();
        public bool UsesHandles() => false;
        public bool UseInTransparentPass(View view) => false;
        public bool CanExecute(View view) => view != null && view.Document.Equals(document) && view.Id == viewId;
        public Outline GetBoundingBox(View view)
        {
            var points = boxes.SelectMany(b => b.Corners).ToList();
            if (points.Count == 0) return new Outline(XYZ.Zero, new XYZ(.001,.001,.001));
            return new Outline(new XYZ(points.Min(p=>p.X)-.001,points.Min(p=>p.Y)-.001,points.Min(p=>p.Z)-.001),
                new XYZ(points.Max(p=>p.X)+.001,points.Max(p=>p.Y)+.001,points.Max(p=>p.Z)+.001));
        }
        public void RenderScene(View view, DisplayStyle style)
        {
            try
            {
                // Small batches keep the index buffers inside the 16-bit vertex index limit.
                for (int start=0; start<boxes.Count; start+=1000)
                {
                    var batch=boxes.Skip(start).Take(1000).ToList();
                    int count=batch.Count*4;
                    using(var vertices=new VertexBuffer(count*VertexPositionColored.GetSizeInFloats()))
                    using(var indices=new IndexBuffer(count*2))
                    using(var format=new VertexFormat(VertexFormatBits.PositionColored))
                    using(var effect=new EffectInstance(VertexFormatBits.PositionColored))
                    {
                        vertices.Map(count*VertexPositionColored.GetSizeInFloats());
                        var stream=vertices.GetVertexStreamPositionColored();
                        foreach(var box in batch)
                            foreach(var p in box.Corners)
                                stream.AddVertex(new VertexPositionColored(p,box.Conflict ? new ColorWithTransparency(230,60,60,0) : new ColorWithTransparency(30,200,100,0)));
                        vertices.Unmap();
                        indices.Map(count*2); var lines=indices.GetIndexStreamLine();
                        for(int b=0;b<batch.Count;b++) for(int e=0;e<4;e++) lines.AddLine(new IndexLine(b*4+e,b*4+(e+1)%4));
                        indices.Unmap();
                        DrawContext.FlushBuffer(vertices,count,indices,count*2,format,effect,PrimitiveType.LineList,0,count);
                    }
                }
                Rendered=true;
            }
            catch(Exception ex) { RenderError=ex.Message; }
        }
        public void Dispose()
        {
            if(service==null || !registered) return;
            try { service.SetActiveServers(service.GetActiveServerIds().Where(x=>x!=id).ToList()); }
            finally { service.RemoveServer(id); registered=false; service=null; }
        }
    }
}
