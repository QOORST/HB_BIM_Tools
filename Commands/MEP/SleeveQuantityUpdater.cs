using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    // Runs in the triggering transaction: the source edit and its quantities share Undo/Redo.
    internal sealed class SleeveQuantityUpdater : IUpdater
    {
        private static UpdaterId _registeredId;
        private readonly UpdaterId _id;
        private SleeveQuantityUpdater(AddInId addInId) { _id = new UpdaterId(addInId, new Guid("32aff5d1-9d83-4661-a399-bba3a14a52c1")); }
        internal static void Register(AddInId addInId)
        {
            var updater = new SleeveQuantityUpdater(addInId);
            if (UpdaterRegistry.IsUpdaterRegistered(updater._id)) return;
            // Non-optional: Revit warns if a document relying on these quantities is edited without this add-in.
            UpdaterRegistry.RegisterUpdater(updater, false);
            _registeredId = updater._id;
            foreach (var category in new[] { BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_CableTray, BuiltInCategory.OST_PipeInsulations, BuiltInCategory.OST_Levels,
                BuiltInCategory.OST_Materials, BuiltInCategory.OST_ImportObjectStyles })
            {
                var filter = new ElementCategoryFilter(category);
                UpdaterRegistry.AddTrigger(updater._id, filter, Element.GetChangeTypeAny());
                UpdaterRegistry.AddTrigger(updater._id, filter, Element.GetChangeTypeElementAddition());
                UpdaterRegistry.AddTrigger(updater._id, filter, Element.GetChangeTypeElementDeletion());
            }
        }
        internal static void Unregister()
        {
            if (_registeredId != null && UpdaterRegistry.IsUpdaterRegistered(_registeredId)) UpdaterRegistry.UnregisterUpdater(_registeredId);
            _registeredId = null;
        }
        public void Execute(UpdaterData data)
        {
            var doc = data.GetDocument();
            if (doc.IsFamilyDocument || SharedParameterElement.Lookup(doc, SleeveQuantityData.Id(0)) == null) return;
            var changed = data.GetModifiedElementIds().Concat(data.GetAddedElementIds()).ToList();
            // A type, source, level, or deletion can affect other sleeves. A local instance edit cannot.
            var affected = data.GetDeletedElementIds().Count == 0 && changed.All(id => doc.GetElement(id) is FamilyInstance fi && SleeveQuantityData.Supported(fi))
                ? new HashSet<long>(changed.Select(id => id.GetIdValue())) : null;
            SleeveQuantityData.Synchronize(doc, null, affected);
        }
        public UpdaterId GetUpdaterId() => _id;
        public ChangePriority GetChangePriority() => ChangePriority.Annotations;
        public string GetUpdaterName() => "HB 套管數量表同步";
        public string GetAdditionalInformation() => "同步套管尺寸、樓層、檢核狀態與圖紙數量表；請啟用 HB_BIM Tools 後編輯。";
    }
}
