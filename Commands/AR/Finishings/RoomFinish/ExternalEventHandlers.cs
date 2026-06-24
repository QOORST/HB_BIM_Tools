using Autodesk.Revit.UI;
using System;
using YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish.UI;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings.RoomFinish
{
    /// <summary>
    /// 同步模型 — 讀取 Revit 房間現況參數並回填到 UI
    /// </summary>
    public class SyncRoomsHandler : IExternalEventHandler
    {
        public MainWindow Window { get; set; }

        public void Execute(UIApplication app)
        {
            try { Window?.SyncRoomsInternal(); }
            catch (Exception ex) { TaskDialog.Show("同步失敗", ex.Message); }
        }

        public string GetName() => "AR_SyncRooms";
    }

    /// <summary>
    /// 套用設定並更新粉刷面（含重建幾何）
    /// </summary>
    public class ApplyAndUpdateHandler : IExternalEventHandler
    {
        public MainWindow Window { get; set; }

        public void Execute(UIApplication app)
        {
            try { Window?.ApplyAndUpdateInternal(); }
            catch (Exception ex) { TaskDialog.Show("套用失敗", ex.Message); }
        }

        public string GetName() => "AR_ApplyAndUpdate";
    }

    /// <summary>
    /// 模型選房 — 呼叫 PickObjects 讓使用者在 Revit 視圖點選房間
    /// </summary>
    public class PickRoomsHandler : IExternalEventHandler
    {
        public MainWindow Window { get; set; }

        public void Execute(UIApplication app)
        {
            Window?.PickRoomsInternal();
        }

        public string GetName() => "AR_PickRooms";
    }

    /// <summary>
    /// 清單回查模型 — 將目前清單選取房間反選到 Revit 模型並縮放定位
    /// </summary>
    public class FocusRoomsHandler : IExternalEventHandler
    {
        public MainWindow Window { get; set; }

        public void Execute(UIApplication app)
        {
            try { Window?.FocusRoomsInternal(); }
            catch (Exception ex) { TaskDialog.Show("回查模型失敗", ex.Message); }
        }

        public string GetName() => "AR_FocusRooms";
    }

    /// <summary>
    /// 僅更新共享參數（不重建幾何）
    /// </summary>
    public class UpdateValuesOnlyHandler : IExternalEventHandler
    {
        public MainWindow Window { get; set; }

        public void Execute(UIApplication app)
        {
            try { Window?.UpdateValuesOnlyInternal(); }
            catch (Exception ex) { TaskDialog.Show("更新參數失敗", ex.Message); }
        }

        public string GetName() => "AR_UpdateValuesOnly";
    }

    /// <summary>
    /// 自動接合牆面
    /// </summary>
    public class AutoJoinWallsHandler : IExternalEventHandler
    {
        public MainWindow Window { get; set; }

        public void Execute(UIApplication app)
        {
            try { Window?.AutoJoinWallsInternal(); }
            catch (Exception ex) { TaskDialog.Show("接合牆面失敗", ex.Message); }
        }

        public string GetName() => "AR_AutoJoinWalls";
    }

    /// <summary>
    /// 結構牆端點對齊至結構柱邊緣
    /// </summary>
    public class AlignWallsToColumnsHandler : IExternalEventHandler
    {
        public MainWindow Window { get; set; }

        public void Execute(UIApplication app)
        {
            try { Window?.AlignWallsToColumnsInternal(); }
            catch (Exception ex) { TaskDialog.Show("對齊柱邊失敗", ex.Message); }
        }

        public string GetName() => "AR_AlignWallsToColumns";
    }

    public class ToggleFinishWallJoinsHandler : IExternalEventHandler
    {
        public MainWindow Window { get; set; }

        public void Execute(UIApplication app)
        {
            try { Window?.ToggleFinishWallJoinsInternal(); }
            catch (Exception ex) { TaskDialog.Show("切換端點接合失敗", ex.Message); }
        }

        public string GetName() => "AR_ToggleFinishWallJoins";
    }

    public class ClearArFinishParamsHandler : IExternalEventHandler
    {
        public MainWindow Window { get; set; }

        public void Execute(UIApplication app)
        {
            try { Window?.ClearArFinishParamsInternal(); }
            catch (Exception ex) { TaskDialog.Show("清理 AR 參數失敗", ex.Message); }
        }

        public string GetName() => "AR_ClearFinishParams";
    }

    /// <summary>
    /// 依模型差異報表建立房間裝修 3D 驗算視圖。
    /// </summary>
    public class CreateCheckViewsHandler : IExternalEventHandler
    {
        public MainWindow Window { get; set; }

        public void Execute(UIApplication app)
        {
            try { Window?.CreateCheckViewsInternal(); }
            catch (Exception ex) { TaskDialog.Show("建立驗算視圖失敗", ex.Message); }
        }

        public string GetName() => "AR_CreateRoomFinishCheckViews";
    }
}

