using System;
using System.Collections.Generic;

namespace YD_RevitTools.LicenseManager.Commands.MEP.AutoAvoid.Core
{
    /// <summary>
    /// 自動避讓參數設定（整合優化版）
    /// </summary>
    public class AvoidOptions
    {
        private double _clearanceMm = 50;
        private double _extraOffsetMm = 500;
        private int _maxTries = 4;
        private double _bendAngle = 45.0;
        private double _autoWalkDistanceMm = 0;

        /// <summary>
        /// 淨空距離（mm）：管線與障礙物的最小距離
        /// </summary>
        public double ClearanceMm 
        { 
            get => _clearanceMm;
            set => _clearanceMm = Math.Max(0, Math.Min(1000, value));
        }

        /// <summary>
        /// 額外偏移（mm）：翻彎後的額外安全距離
        /// </summary>
        public double ExtraOffsetMm 
        { 
            get => _extraOffsetMm;
            set => _extraOffsetMm = Math.Max(0, Math.Min(5000, value));
        }

        /// <summary>
        /// 彎頭角度（22.5°, 45°, 90°）
        /// </summary>
        public double BendAngle
        {
            get => _bendAngle;
            set
            {
                if (value == 22.5 || value == 45.0 || value == 90.0)
                    _bendAngle = value;
                else
                    _bendAngle = 45.0; // 預設 45°
            }
        }

        /// <summary>
        /// 自走兩側距離（mm）：障礙物兩側的額外延伸距離
        /// </summary>
        public double AutoWalkDistanceMm
        {
            get => _autoWalkDistanceMm;
            set => _autoWalkDistanceMm = Math.Max(0, Math.Min(10000, value));
        }

        // 目標元素類型
        public bool TargetPipes { get; set; } = true;
        public bool TargetDucts { get; set; } = false;
        public bool TargetConduits { get; set; } = false;

        // 障礙物類型
        public bool IncludeWalls { get; set; } = true;
        public bool IncludeFloors { get; set; } = true;
        public bool IncludeFraming { get; set; } = true;
        public bool IncludeMEP { get; set; } = true;
        public bool IncludeFittings { get; set; } = true;

        // 避讓方向
        public DirectionMode Direction { get; set; } = DirectionMode.Auto;
        
        // 進階設定
        public int MaxTries 
        { 
            get => _maxTries;
            set => _maxTries = Math.Max(1, Math.Min(10, value));
        }

        /// <summary>
        /// 驗證所有參數是否有效
        /// </summary>
        public (bool IsValid, List<string> Errors) Validate()
        {
            var errors = new List<string>();

            if (ClearanceMm < 0)
                errors.Add("淨空值不能為負數");
            if (ClearanceMm > 1000)
                errors.Add("淨空值不能超過 1000mm");
            if (ExtraOffsetMm < 0)
                errors.Add("額外偏移不能為負數");
            if (ExtraOffsetMm > 5000)
                errors.Add("額外偏移不能超過 5000mm");
            if (!TargetPipes && !TargetDucts && !TargetConduits)
                errors.Add("至少需要選擇一種目標元素類型");
            if (!IncludeWalls && !IncludeFloors && !IncludeFraming && !IncludeMEP)
                errors.Add("至少需要選擇一種障礙物類型");

            return (errors.Count == 0, errors);
        }
    }

    /// <summary>
    /// 避讓方向模式
    /// </summary>
    public enum DirectionMode
    {
        Auto,           // 自動判斷
        Up,             // 向上翻彎
        Down,           // 向下翻彎
        Left,           // 向左偏移
        Right,          // 向右偏移
        VerticalFlip    // 垂直翻彎（Dynamo 模式）
    }
}