using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Helpers.AR.AutoJoin
{
    public class AutoJoinSettings
    {
        // 接合規則
        public bool Rule_Wall_Floor_FloorCuts { get; set; } = true;   // 樓板切牆
        public bool Rule_Wall_Beam_BeamCuts { get; set; } = true;   // 梁切牆
        public bool Rule_Wall_Column_ColumnCuts { get; set; } = true; // 柱切牆
        public bool Rule_Floor_Column_ColumnCuts { get; set; } = true; // 柱切樓板
        public bool Rule_Floor_Beam_BeamCuts { get; set; } = true; // 梁切樓板

        // 自訂配對（A 切 B）
        public List<(BuiltInCategory A, BuiltInCategory B)> CustomPairs { get; } =
            new List<(BuiltInCategory, BuiltInCategory)>();

        // 模式
        public bool DryRun { get; set; } = false;   // 預覽模式（只計算）
        public bool SwitchOnly { get; set; } = false;   // 只調整順序

        // 範圍
        public bool OnlyActiveView { get; set; } = false;
        public bool OnlyUserSelection { get; set; } = false;

        // 搜尋
        public double InflateFeet { get; set; } = 1.0;

        // 近距離偵測（新功能）
        public bool DetectNearMisses { get; set; } = true;  // 偵測接近但未相交的元素
        public double ProximityToleranceMm { get; set; } = 5.0;  // 容差距離（mm）

        // 記錄
        public bool EnableCsvLog { get; set; } = false;
        public string CsvPath { get; set; } = @"C:\Temp\AutoJoinLog.csv";
    }
}
