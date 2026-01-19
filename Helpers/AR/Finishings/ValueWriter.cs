using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace YD_RevitTools.LicenseManager.Helpers.AR.Finishings
{
    public class ProcessingResults
    {
        public int RoomsProcessed { get; set; } = 0;
        public int GeometryUpdated { get; set; } = 0;
        public int RoomParametersUpdated { get; set; } = 0;
        public List<(string roomNumber, string message)> Errors { get; } = new List<(string, string)>();

        public void AddError(string roomNumber, string message)
        {
            Errors.Add((roomNumber, message));
        }

        public int TotalSuccessCount => GeometryUpdated + RoomParametersUpdated;
    }

    public class ValueWriter
    {
        private readonly UIDocument _uidoc;
        private Document Doc => _uidoc.Document;

        const string GROUP_NAME = "AR_Finishings";
        const string P_RoomNames = "AR_RoomNames";
        const string P_RoomNumbers = "AR_RoomNumbers";
        const string P_RoomId = "AR_RoomId";
        const string P_Summary = "AR_Summary";

        // 快取已處理的房間，避免重複處理
        private readonly HashSet<ElementId> _processedRooms = new HashSet<ElementId>();

        public ValueWriter(UIDocument uidoc) { _uidoc = uidoc; }

        public void UpdateValues(FinishSettings settings)
        {
            if (!settings.SetValuesForGeometry && !settings.SetValuesForRooms)
                return; // 沒有要執行的操作

            var rooms = GetValidRooms(settings.TargetRoomIds);
            var results = new ProcessingResults();

            foreach (var room in rooms)
            {
                if (_processedRooms.Contains(room.Id))
                    continue; // 跳過已處理的房間

                try
                {
                    bool roomSuccess = false;

                    if (settings.SetValuesForGeometry)
                    {
                        WriteRoomValuesToElements(room);
                        results.GeometryUpdated++;
                        roomSuccess = true;
                    }

                    if (settings.SetValuesForRooms)
                    {
                        WriteGeometrySummaryToRoom(room);
                        results.RoomParametersUpdated++;
                        roomSuccess = true;
                    }

                    if (roomSuccess)
                    {
                        results.RoomsProcessed++;
                        _processedRooms.Add(room.Id);
                    }
                }
                catch (Exception ex)
                {
                    results.AddError(room.Number, ex.Message);
                }
            }

            ShowUpdateResults(results);
        }

        private IEnumerable<Room> GetValidRooms(IList<ElementId> targetRoomIds)
        {
            if (targetRoomIds != null && targetRoomIds.Count > 0)
            {
                return targetRoomIds
                    .Select(id => Doc.GetElement(id))
                    .OfType<Room>()
                    .Where(r => r.Area > 0);
            }

            return new FilteredElementCollector(Doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0);
        }

        private void ShowUpdateResults(ProcessingResults results)
        {
            var message = new StringBuilder();
            message.AppendLine($"參數更新完成:");
            message.AppendLine($"- 處理房間數: {results.RoomsProcessed}");
            if (results.GeometryUpdated > 0)
                message.AppendLine($"- 幾何元素參數更新: {results.GeometryUpdated}");
            if (results.RoomParametersUpdated > 0)
                message.AppendLine($"- 房間參數更新: {results.RoomParametersUpdated}");

            if (results.Errors.Any())
            {
                message.AppendLine($"\n錯誤數量: {results.Errors.Count}");
                message.AppendLine("\n錯誤詳情:");

                foreach (var error in results.Errors.Take(5))
                {
                    message.AppendLine($"- 房間 {error.roomNumber}: {error.message}");
                }

                if (results.Errors.Count > 5)
                {
                    message.AppendLine($"... 還有 {results.Errors.Count - 5} 個錯誤");
                }
            }

            TaskDialog.Show("參數更新結果", message.ToString());
        }

        void WriteRoomValuesToElements(Room room)
        {
            // 優化：只查詢特定類別的元素，而非所有元素
            var categories = new BuiltInCategory[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_GenericModel
            };

            var elems = new List<Element>();
            foreach (var cat in categories)
            {
                var catElems = new FilteredElementCollector(Doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(e => (e.LookupParameter(P_RoomId)?.AsInteger() ?? 0) == room.Id.Value);
                elems.AddRange(catElems);
            }

            foreach (var e in elems)
            {
                try
                {
                    var roomNumberParam = e.LookupParameter(P_RoomNumbers);
                    var roomNameParam = e.LookupParameter(P_RoomNames);
                    
                    if (roomNumberParam != null && !roomNumberParam.IsReadOnly)
                        roomNumberParam.Set(room.Number ?? "");
                    
                    if (roomNameParam != null && !roomNameParam.IsReadOnly)
                        roomNameParam.Set(room.Name ?? "");
                }
                catch (Exception ex)
                {
                    // 記錄單個元素的錯誤，但不中斷整個處理流程
                    System.Diagnostics.Debug.WriteLine($"設定元素 {e.Id} 參數失敗: {ex.Message}");
                }
            }
        }

        void WriteGeometrySummaryToRoom(Room room)
        {
            var categories = new BuiltInCategory[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_GenericModel
            };

            var elems = new List<Element>();
            foreach (var cat in categories)
            {
                var catElems = new FilteredElementCollector(Doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(e => (e.LookupParameter(P_RoomId)?.AsInteger() ?? 0) == room.Id.Value);
                elems.AddRange(catElems);
            }

            if (!elems.Any())
            {
                var summaryParam = room.LookupParameter(P_Summary);
                if (summaryParam != null && !summaryParam.IsReadOnly)
                    summaryParam.Set("無關聯的裝修元素");
                return;
            }

            var groups = elems.GroupBy(e => GetElementDisplayName(e));
            var sb = new StringBuilder();
            sb.AppendLine($"房間 {room.Number} 裝修摘要:");
            
            foreach (var g in groups)
            {
                double areaSum = 0;
                foreach (var e in g)
                {
                    try
                    {
                        var pArea = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)
                                 ?? e.get_Parameter(BuiltInParameter.HOST_PERIMETER_COMPUTED);
                        if (pArea != null) areaSum += pArea.AsDouble();
                    }
                    catch
                    {
                        // 忽略無法取得面積的元素
                    }
                }
                
                sb.AppendLine($"- {g.Key}: {g.Count()} 個, 面積 {areaSum:F2} ft²");
            }
            
            var summaryParameter = room.LookupParameter(P_Summary);
            if (summaryParameter != null && !summaryParameter.IsReadOnly)
                summaryParameter.Set(sb.ToString());
        }

        private string GetElementDisplayName(Element element)
        {
            try
            {
                var categoryName = element.Category?.Name ?? "未知類別";
                var typeName = Doc.GetElement(element.GetTypeId())?.Name ?? "未知類型";
                return $"{categoryName} | {typeName}";
            }
            catch
            {
                return "未知元素";
            }
        }

        // --- Shared Parameters with ForgeTypeId (Revit 2024+) ---
        /// <summary>
        /// 確保共享參數存在並綁定到相應類別
        /// 注意：此方法必須在 Transaction 內部調用
        /// </summary>
        public void EnsureSharedParameters()
        {
            var app = Doc.Application;
            string spFile = app.SharedParametersFilename;
            if (string.IsNullOrWhiteSpace(spFile) || !File.Exists(spFile))
            {
                spFile = Path.Combine(Path.GetTempPath(), "AR_Finishings_SharedParameters.txt");
                if (!File.Exists(spFile)) File.WriteAllText(spFile, "# AR Finishings Shared Parameters");
                app.SharedParametersFilename = spFile;
            }

            var defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                throw new InvalidOperationException("無法開啟共享參數檔案");
            }

            var group = defFile.Groups.get_Item(GROUP_NAME) ?? defFile.Groups.Create(GROUP_NAME);

            var defRoomId = group.Definitions.get_Item(P_RoomId)
                ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(P_RoomId, SpecTypeId.Int.Integer));
            var defRoomNames = group.Definitions.get_Item(P_RoomNames)
                ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(P_RoomNames, SpecTypeId.String.Text));
            var defRoomNumbers = group.Definitions.get_Item(P_RoomNumbers)
                ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(P_RoomNumbers, SpecTypeId.String.Text));
            var defSummary = group.Definitions.get_Item(P_Summary)
                ?? group.Definitions.Create(new ExternalDefinitionCreationOptions(P_Summary, SpecTypeId.String.Text));

            // 綁定參數到類別（必須在 Transaction 內執行）
            // finish elements
            var catsFinish = new CategorySet();
            catsFinish.Insert(Doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls));
            catsFinish.Insert(Doc.Settings.Categories.get_Item(BuiltInCategory.OST_Floors));
            catsFinish.Insert(Doc.Settings.Categories.get_Item(BuiltInCategory.OST_Ceilings));
            catsFinish.Insert(Doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel));

            var inst = app.Create.NewInstanceBinding(catsFinish);
            var map = Doc.ParameterBindings;
            map.Insert(defRoomId, inst, GroupTypeId.IdentityData);
            map.Insert(defRoomNames, inst, GroupTypeId.IdentityData);
            map.Insert(defRoomNumbers, inst, GroupTypeId.IdentityData);

            // rooms
            var catsRoom = new CategorySet();
            catsRoom.Insert(Doc.Settings.Categories.get_Item(BuiltInCategory.OST_Rooms));
            var instRoom = app.Create.NewInstanceBinding(catsRoom);
            map.Insert(defSummary, instRoom, GroupTypeId.IdentityData);
        }
    }
}
