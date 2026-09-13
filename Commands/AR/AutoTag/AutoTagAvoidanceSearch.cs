using System;
using System.Collections.Generic;

namespace YD_RevitTools.LicenseManager.Commands.AR.AutoTag
{
    internal static class AutoTagAvoidanceSearch
    {
        internal struct Offset
        {
            public double X;
            public double Y;
        }

        // Paper-space search. Exhaust the requested half-plane before considering other sides.
        internal static IEnumerable<Offset> GetOffsets(AutoTagPlacement placement, double maxPaperMm, bool crossSide, bool leader)
        {
            if (placement == AutoTagPlacement.Center || double.IsNaN(maxPaperMm) || double.IsInfinity(maxPaperMm) || maxPaperMm <= 0)
                yield break;
            double limit = leader ? maxPaperMm : Math.Min(maxPaperMm, 3.0);
            var directions = new[] { new Offset { Y=1 }, new Offset { X=1 }, new Offset { Y=-1 }, new Offset { X=-1 },
                new Offset { X=1,Y=1 }, new Offset { X=-1,Y=1 }, new Offset { X=1,Y=-1 }, new Offset { X=-1,Y=-1 } };
            for (int pass=0; pass < (crossSide ? 2 : 1); pass++)
                for (int ring=1; ring<=4; ring++)
                    foreach (var d in directions)
                    {
                        bool same = placement == AutoTagPlacement.Above ? d.Y >= 0 :
                            placement == AutoTagPlacement.Below ? d.Y <= 0 :
                            placement == AutoTagPlacement.Left ? d.X <= 0 : d.X >= 0;
                        if (same != (pass == 0)) continue;
                        double distance = limit * ring / 4.0 / Math.Sqrt(d.X*d.X+d.Y*d.Y);
                        yield return new Offset { X=d.X*distance, Y=d.Y*distance };
                    }
        }
    }
}
