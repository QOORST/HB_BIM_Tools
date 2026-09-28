using System;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    internal sealed class PipeMeasurement
    {
        internal double X0, Y0, Z0, X1, Y1, Z1, LengthMm, DiameterMm;
        internal bool IsStraight;
        internal double HorizontalMm => Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
        // 端點順序是模型曲線順序，不代表流向。垂直管沒有有限坡度。
        internal double? SlopePercent => IsStraight && HorizontalMm > 0.001 ?
            (double?)(Math.Abs(Z1 - Z0) / HorizontalMm * 100) : null;
        internal string SlopeText => !IsStraight ? "曲管不計單一坡度" : HorizontalMm <= 0.001 ? "垂直" :
            ExportFormatting.Number(SlopePercent.Value, "0.###");
        internal string Label(string code) => code + "  管徑 " + ExportFormatting.Number(DiameterMm, "0.###") +
            " mm\n模型長 " + ExportFormatting.Number(LengthMm, "0.###") + " mm";
    }
}
