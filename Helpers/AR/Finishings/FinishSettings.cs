using Autodesk.Revit.DB;
using System.Collections.Generic;
using System;
using System.IO;
using System.Xml.Serialization;

namespace YD_RevitTools.LicenseManager.Helpers.AR.Finishings
{
    public class FinishSettings
    {
        // 單位轉換常數
        private const double MM_TO_FEET = 304.8;

        public FloorBoundaryMode BoundaryMode { get; set; } = FloorBoundaryMode.InnerFinish;

        public ElementId SelectedFloorTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId SelectedCeilingTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId SelectedWallTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId SelectedSkirtingTypeId { get; set; } = ElementId.InvalidElementId;

        public double CeilingHeightMm { get; set; } = 3000;
        public double WallHeightMm { get; set; } = 2700;
        public double WallOffsetMm { get; set; } = 300;
        public double SkirtingHeightMm { get; set; } = 100;

        public bool GenerateGeometry { get; set; } = true;
        public bool UpdateValues { get; set; } = true;
        public bool SetValuesForGeometry { get; set; } = true;
        public bool SetValuesForRooms { get; set; } = true;

        public bool SkipDoorsForSkirting { get; set; } = true;
        public bool SkipWindowsForSkirting { get; set; } = true;
        public bool SkipOpeningsForWalls { get; set; } = true;
        public bool AutoJoinWalls { get; set; } = false;

        public IList<ElementId> TargetRoomIds { get; set; } = null;

        /// <summary>
        /// 從毫米轉換為Revit內部單位（英尺）
        /// </summary>
        public double MmToInternalUnits(double mm) => mm / MM_TO_FEET;

        /// <summary>
        /// 從Revit內部單位（英尺）轉換為毫米
        /// </summary>
        public double InternalUnitsToMm(double internalUnits) => internalUnits * MM_TO_FEET;

        // 設定管理
        public static string GetSettingsPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var settingsDir = Path.Combine(appDataPath, "AR_Finishings");
            if (!Directory.Exists(settingsDir))
                Directory.CreateDirectory(settingsDir);
            return Path.Combine(settingsDir, "settings.xml");
        }

        public void SaveToFile()
        {
            try
            {
                var serializer = new XmlSerializer(typeof(FinishSettings));
                using (var writer = new StreamWriter(GetSettingsPath()))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"儲存設定失敗: {ex.Message}");
            }
        }

        public static FinishSettings LoadFromFile()
        {
            try
            {
                var settingsPath = GetSettingsPath();
                if (!File.Exists(settingsPath))
                    return new FinishSettings();

                var serializer = new XmlSerializer(typeof(FinishSettings));
                using (var reader = new StreamReader(settingsPath))
                {
                    return (FinishSettings)serializer.Deserialize(reader);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"載入設定失敗: {ex.Message}");
                return new FinishSettings();
            }
        }

        public FinishSettings Clone()
        {
            return new FinishSettings
            {
                BoundaryMode = this.BoundaryMode,
                SelectedFloorTypeId = this.SelectedFloorTypeId,
                SelectedCeilingTypeId = this.SelectedCeilingTypeId,
                SelectedWallTypeId = this.SelectedWallTypeId,
                SelectedSkirtingTypeId = this.SelectedSkirtingTypeId,
                CeilingHeightMm = this.CeilingHeightMm,
                WallHeightMm = this.WallHeightMm,
                WallOffsetMm = this.WallOffsetMm,
                SkirtingHeightMm = this.SkirtingHeightMm,
                GenerateGeometry = this.GenerateGeometry,
                UpdateValues = this.UpdateValues,
                SetValuesForGeometry = this.SetValuesForGeometry,
                SetValuesForRooms = this.SetValuesForRooms,
                SkipDoorsForSkirting = this.SkipDoorsForSkirting,
                SkipWindowsForSkirting = this.SkipWindowsForSkirting,
                SkipOpeningsForWalls = this.SkipOpeningsForWalls,
                AutoJoinWalls = this.AutoJoinWalls,
                TargetRoomIds = this.TargetRoomIds
            };
        }
    }
}
