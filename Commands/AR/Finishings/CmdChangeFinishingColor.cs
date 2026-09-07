using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Commands.AR.Formwork;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.AR.Finishings
{
    /// <summary>
    /// AR 裝修工具 - 更換裝修面顏色
    /// 允許用戶選擇已創建的一般模型裝修面，並更換材質顏色
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdChangeFinishingColor : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;

            try
            {
                Debug.WriteLine("🎨 開始執行「更換裝修面顏色」命令");

                // 1. 選擇要更換顏色的裝修面（一般模型）
                var selectedElements = SelectFinishingElements(uidoc);
                if (selectedElements == null || selectedElements.Count == 0)
                {
                    TaskDialog.Show("提示", "未選擇任何裝修面。");
                    return Result.Cancelled;
                }

                Debug.WriteLine($"✅ 已選擇 {selectedElements.Count} 個裝修面");

                // 2. 顯示材質選擇對話框
                var dlg = new MaterialPickerDialog(doc);
                dlg.Title = "選擇新的材質";
                new System.Windows.Interop.WindowInteropHelper(dlg) { Owner = uiapp.MainWindowHandle };
                var ok = dlg.ShowDialog();
                if (ok != true)
                {
                    return Result.Cancelled;
                }

                var newMaterial = dlg.SelectedMaterial;
                if (newMaterial == null)
                {
                    TaskDialog.Show("錯誤", "未選擇材質。");
                    return Result.Cancelled;
                }

                Debug.WriteLine($"✅ 選擇的新材質: {newMaterial.Name}");

                // 3. 更換顏色
                int successCount = 0;
                using (var trans = new Transaction(doc, "更換裝修面顏色"))
                {
                    trans.Start();

                    foreach (var elementId in selectedElements)
                    {
                        try
                        {
                            var element = doc.GetElement(elementId);
                            if (element == null) continue;

                            // 更新材質參數
                            UpdateMaterialParameter(element, newMaterial);

                            // 更新視圖覆蓋（使用 VisualEffectsManager）
                            VisualEffectsManager.SetFormworkMaterialAndColor(doc, elementId, newMaterial, transparency: 0);

                            successCount++;
                            Debug.WriteLine($"✅ 成功更換元素 {elementId.GetIdValue()} 的顏色");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"❌ 更換元素 {elementId.GetIdValue()} 顏色失敗: {ex.Message}");
                        }
                    }

                    trans.Commit();
                }

                // 4. 顯示結果
                TaskDialog.Show("更換顏色完成",
                    $"成功更換 {successCount}/{selectedElements.Count} 個裝修面的顏色\n" +
                    $"新材質: {newMaterial.Name}");

                Debug.WriteLine($"✅ 更換顏色完成: {successCount}/{selectedElements.Count}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Debug.WriteLine($"❌ 更換裝修面顏色失敗: {ex}");
                TaskDialog.Show("錯誤", $"執行失敗: {ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// 選擇裝修面元素（支援多選）
        /// </summary>
        private List<ElementId> SelectFinishingElements(UIDocument uidoc)
        {
            var doc = uidoc.Document;
            var selectedIds = new List<ElementId>();

            try
            {
                // 使用過濾器只選擇一般模型（DirectShape）
                var filter = new FinishingElementFilter();

                var selection = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    filter,
                    "選擇要更換顏色的裝修面（可多選，按 ESC 結束）");

                selectedIds = selection.Select(r => r.ElementId).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // 用戶取消選擇
                return null;
            }

            return selectedIds;
        }

        /// <summary>
        /// 更新元素的材質參數
        /// </summary>
        private void UpdateMaterialParameter(Element element, Material newMaterial)
        {
            // 更新共用參數中的材質資訊
            var materialParam = element.LookupParameter(SharedParams.P_MaterialName);
            if (materialParam != null && !materialParam.IsReadOnly)
            {
                materialParam.Set(newMaterial.Name);
                Debug.WriteLine($"✅ 已更新材料名稱參數: {newMaterial.Name}");
            }
            else
            {
                Debug.WriteLine($"⚠️ 無法更新材料名稱參數（參數不存在或唯讀）");
            }
        }
    }

    /// <summary>
    /// 裝修面元素過濾器（只選擇一般模型 DirectShape）
    /// </summary>
    public class FinishingElementFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            // 只允許裝修工具建立的 DirectShape，避免誤選模板元素
            if (elem is DirectShape ds)
            {
                if (elem.Category?.Id.GetIdValue() == (long)BuiltInCategory.OST_GenericModel
                    && ds.ApplicationId == "HB_BIM_Finishings")
                {
                    Debug.WriteLine($"✅ 允許選擇裝修面: {elem.Name} (ID: {elem.Id.GetIdValue()})");
                    return true;
                }
            }

            Debug.WriteLine($"❌ 不允許選擇: {elem.Name} (ID: {elem.Id.GetIdValue()}, Category: {elem.Category?.Name})");
            return false;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    /// <summary>
    /// 材質選擇對話框
    /// </summary>
    public class MaterialPickerDialog : System.Windows.Window
    {
        private Document _doc;
        public Material SelectedMaterial { get; private set; }

        public MaterialPickerDialog(Document doc)
        {
            _doc = doc;
            Title = "選擇材質";
            Width = 400;
            Height = 500;
            WindowStyle = System.Windows.WindowStyle.ToolWindow;
            ResizeMode = System.Windows.ResizeMode.NoResize;
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

            BuildUI();
        }

        private void BuildUI()
        {
            var grid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(10) };
            Content = grid;

            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            // 標題
            var label = new System.Windows.Controls.Label
            {
                Content = "選擇新的材質：",
                FontSize = 14,
                FontWeight = System.Windows.FontWeights.Bold
            };
            System.Windows.Controls.Grid.SetRow(label, 0);
            grid.Children.Add(label);

            // 材質列表
            var listBox = new System.Windows.Controls.ListBox
            {
                Margin = new System.Windows.Thickness(0, 10, 0, 10)
            };
            System.Windows.Controls.Grid.SetRow(listBox, 1);
            grid.Children.Add(listBox);

            // 載入所有材質
            var materials = new FilteredElementCollector(_doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .OrderBy(m => m.Name)
                .ToList();

            foreach (var mat in materials)
            {
                listBox.Items.Add(new MaterialListItem { Material = mat, DisplayName = mat.Name });
            }

            listBox.DisplayMemberPath = "DisplayName";

            // 按鈕面板
            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            System.Windows.Controls.Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            var okButton = new System.Windows.Controls.Button
            {
                Content = "確定",
                Width = 80,
                Height = 30,
                Margin = new System.Windows.Thickness(0, 0, 10, 0)
            };
            okButton.Click += (s, e) =>
            {
                var selectedItem = listBox.SelectedItem as MaterialListItem;
                if (selectedItem != null)
                {
                    SelectedMaterial = selectedItem.Material;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    System.Windows.MessageBox.Show("請選擇一個材質。", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 80,
                Height = 30
            };
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };
            buttonPanel.Children.Add(cancelButton);
        }

        private class MaterialListItem
        {
            public Material Material { get; set; }
            public string DisplayName { get; set; }
        }
    }
}
