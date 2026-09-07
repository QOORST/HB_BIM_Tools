using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using YDBIM.AutoDimension.Core;

#nullable enable

namespace YDBIM.AutoDimension.App
{

[Transaction(TransactionMode.Manual)]
public sealed class RoomContentCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            UIDocument? uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc is null)
            {
                message = "目前沒有開啟中的 Revit 文件。";
                return Result.Failed;
            }

            Document doc = uiDoc.Document;
            View view = doc.ActiveView;
            var roomService = new RoomAnnotationService();

            using var tx = new Transaction(doc, "更新房間內容");
            tx.Start();

            RoomAnnotationResult result = roomService.UpdateRoomContent(doc, view);
            if (result.Changed == 0)
            {
                tx.RollBack();
                string reason = result.Processed == 0
                    ? "目前視圖或樓層找不到可更新的 Room。請確認 Room 已放置、已封閉，且目前視圖有對應樓層。"
                    : $"找到 {result.Processed} 個 Room，但內容已是最新，或必要參數為唯讀/不存在。";
                TaskDialog.Show("HB_BIM Auto Dimension", reason);
                return Result.Cancelled;
            }

            tx.Commit();
            TaskDialog.Show("HB_BIM Auto Dimension", $"已更新 {result.Changed} / {result.Processed} 個房間內容。");
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("HB_BIM Auto Dimension", $"房間內容更新失敗：{ex.Message}");
            return Result.Failed;
        }
    }
}
}


