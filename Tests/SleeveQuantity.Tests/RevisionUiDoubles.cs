using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Autodesk.Revit.DB { internal sealed class ViewDrafting : Element { } }
namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal static class SleeveRevisionService
    {
        internal static List<SleeveRevisionSnapshot> Baselines = new List<SleeveRevisionSnapshot>();
        internal static SleeveRevisionSnapshot Current = new SleeveRevisionSnapshot();
        internal static List<SleeveRevisionSnapshot> List(Document doc) => Baselines;
        internal static SleeveRevisionSnapshot Capture(Document doc, SleeveRevisionSnapshot baseline) => Current;
        internal static SleeveRevisionSnapshot Save(Document doc, string name) => throw new NotSupportedException("UI-only fixture");
    }
    internal static class SleeveRevisionDrawing
    {
        internal static List<ViewDrafting> Create(Document doc, SleeveRevisionSnapshot baseline, SleeveRevisionSnapshot current, ElementId sheet) =>
            throw new NotSupportedException("UI-only fixture");
    }
}
