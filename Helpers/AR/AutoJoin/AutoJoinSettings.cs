using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace YD_RevitTools.LicenseManager.Helpers.AR.AutoJoin
{
    public class AutoJoinSettings
    {
        // ���سW�h
        public bool Rule_Wall_Floor_FloorCuts { get; set; } = true;   // �ӪO����
        public bool Rule_Wall_Beam_BeamCuts { get; set; } = true;   // �����
        public bool Rule_Floor_Column_ColumnCuts { get; set; } = true; // �W���ӪO

        // �ۭq�t��]A �� B�^
        public List<(BuiltInCategory A, BuiltInCategory B)> CustomPairs { get; } =
            new List<(BuiltInCategory, BuiltInCategory)>();

        // �Ҧ�
        public bool DryRun { get; set; } = false;   // ���ʼҫ��u�έp
        public bool SwitchOnly { get; set; } = false;   // �u��������

        // �d��
        public bool OnlyActiveView { get; set; } = false;
        public bool OnlyUserSelection { get; set; } = false;

        // �j�M
        public double InflateFeet { get; set; } = 1.0;

        // �O��
        public bool EnableCsvLog { get; set; } = false;
        public string CsvPath { get; set; } = @"C:\Temp\AutoJoinLog.csv";
    }
}
