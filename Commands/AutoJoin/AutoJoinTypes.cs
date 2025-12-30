namespace YD_RevitTools.LicenseManager.Commands.AutoJoin
{
    /// <summary>
    /// 元件類型模式
    /// </summary>
    public enum ElementTypeMode
    {
        Column,  // 柱
        Beam,    // 梁
        Floor,   // 版
        Wall,    // 牆
        All      // 全部（智慧模式）
    }

    /// <summary>
    /// 處理範圍
    /// </summary>
    public enum ProcessingScope
    {
        AllElements,  // 整個專案
        CurrentView,  // 目前視圖
        Selection     // 目前選取
    }
}

