using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish
{
    /// <summary>
    /// 房間裝修覆寫設定
    /// </summary>
    public class RoomFinishOverride
    {
        public long RoomId { get; set; }
        public long WallTypeId { get; set; } = -1;
        public long FloorTypeId { get; set; } = -1;
        public long CeilingTypeId { get; set; } = -1;
        public long SkirtingTypeId { get; set; } = -1;
        public double WallHeightMm { get; set; } = 3000;
        public double CeilingHeightMm { get; set; } = 2700;
    }

    /// <summary>
    /// 裝修設定類別
    /// </summary>
    public class FinishSettings
    {
        public FloorBoundaryMode BoundaryMode { get; set; } = FloorBoundaryMode.InnerFinish;

        public ElementId SelectedFloorTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId SelectedCeilingTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId SelectedWallTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId SelectedSkirtingTypeId { get; set; } = ElementId.InvalidElementId;

        public double CeilingHeightMm { get; set; } = 2700;
        public double WallHeightMm { get; set; } = 3000;
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
        public List<RoomFinishOverride> RoomOverrides { get; set; } = new List<RoomFinishOverride>();

        // 執行時略過旗標（不序列化，僅在 ApplyReplacementToModel 中設置）
        [System.Xml.Serialization.XmlIgnore]
        public HashSet<long> SkipWallForRoomIds { get; set; }
        [System.Xml.Serialization.XmlIgnore]
        public HashSet<long> SkipFloorForRoomIds { get; set; }
        [System.Xml.Serialization.XmlIgnore]
        public HashSet<long> SkipCeilingForRoomIds { get; set; }

        /// <summary>
        /// 從毫米轉換為Revit內部單位（英尺）
        /// </summary>
        public double MmToInternalUnits(double mm) => mm / 304.8;

        /// <summary>
        /// 從Revit內部單位（英尺）轉換為毫米
        /// </summary>
        public double InternalUnitsToMm(double internalUnits) => internalUnits * 304.8;

        // 設定管理
        public static string GetSettingsPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var settingsDir = Path.Combine(appDataPath, "YD_RevitTools_RoomFinish");
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
                TargetRoomIds = this.TargetRoomIds,
                RoomOverrides = this.RoomOverrides?.Select(x => new RoomFinishOverride
                {
                    RoomId = x.RoomId,
                    WallTypeId = x.WallTypeId,
                    FloorTypeId = x.FloorTypeId,
                    CeilingTypeId = x.CeilingTypeId,
                    SkirtingTypeId = x.SkirtingTypeId,
                    WallHeightMm = x.WallHeightMm,
                    CeilingHeightMm = x.CeilingHeightMm
                }).ToList() ?? new List<RoomFinishOverride>()
            };
        }

        public RoomFinishOverride GetRoomOverride(ElementId roomId)
        {
            if (roomId == null || RoomOverrides == null || !RoomOverrides.Any())
                return null;

            return RoomOverrides.FirstOrDefault(x => x.RoomId == RevitCompat.GetElementIdValue(roomId));
        }
    }
}
