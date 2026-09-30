using System;
namespace HB.CadRevision
{
    internal static class PlanProjection
    {
        internal static bool AcceptLine(double z1,double z2,bool enabled)
        {
            if(double.IsNaN(z1)||double.IsNaN(z2)||double.IsInfinity(z1)||double.IsInfinity(z2))return false;
            return enabled?Math.Abs(z1-z2)<=0.01:Math.Abs(z1)<=0.01&&Math.Abs(z2)<=0.01;
        }
    }
}
