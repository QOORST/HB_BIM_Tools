using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using YD_RevitTools.LicenseManager.Helpers.AR.Finishings;

namespace YD_RevitTools.LicenseManager.UI.Finishings
{
    public class RoomSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem.Category != null && elem.Category.Id.Value == (int)BuiltInCategory.OST_Rooms;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    public partial class MainWindow : Window
    {
        private readonly UIDocument _uiDoc;
        private IList<ElementId> _pickedRoomIds = new List<ElementId>();
        public FinishSettings ViewModel { get; private set; }

        public MainWindow(UIDocument uiDoc)
        {
            InitializeComponent();
            _uiDoc = uiDoc;
            LoadTypes();
            LoadSettings();

            btnGenerate.Click += (s, e) => { 
                if (ValidateInputs()) 
                { 
                    CollectViewModel(true); 
                    SaveSettings(); 
                    DialogResult = true; 
                    Close(); 
                } 
            };
            btnUpdateValues.Click += (s, e) => { 
                if (ValidateInputs()) 
                { 
                    CollectViewModel(false); 
                    SaveSettings(); 
                    DialogResult = true; 
                    Close(); 
                } 
            };
            btnPickRooms.Click += (s, e) => PickRooms();
            btnAutoJoinWalls.Click += (s, e) => AutoJoinWalls();
            
            // 加入取消按鈕處理
            this.KeyDown += (s, e) => 
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    DialogResult = false;
                    Close();
                }
            };
        }

        void LoadTypes()
        {
            var doc = _uiDoc.Document;
            
            // 載入地板類型
            var floorTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .OrderBy(t => t.Name)
                .ToList();
            cmbFloors.ItemsSource = floorTypes;
            cmbFloors.DisplayMemberPath = "Name";
            
            // 載入天花板類型
            var ceilingTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(CeilingType))
                .Cast<CeilingType>()
                .OrderBy(t => t.Name)
                .ToList();
            cmbCeilings.ItemsSource = ceilingTypes;
            cmbCeilings.DisplayMemberPath = "Name";
            
            // 載入牆類型
            var wallTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .OrderBy(t => t.Name)
                .ToList();
            cmbWalls.ItemsSource = wallTypes;
            cmbWalls.DisplayMemberPath = "Name";
            
            // 載入踢腳板類型（使用牆類型）
            cmbSkirtings.ItemsSource = wallTypes;
            cmbSkirtings.DisplayMemberPath = "Name";
            
            cmbBoundary.SelectedIndex = 0;
        }

        void PickRooms()
        {
            rbPickRooms.IsChecked = true;
            try
            {
                var refs = _uiDoc.Selection.PickObjects(ObjectType.Element, new RoomSelectionFilter(), "請選擇房間");
                _pickedRoomIds = refs.Select(r => r.ElementId).ToList();
                txtPickedCount.Text = $"已選擇: {_pickedRoomIds.Count} 個房間";
            }
            catch { /* cancelled */ }
        }

        void CollectViewModel(bool generateGeometry)
        {
            var vm = new FinishSettings();
            vm.GenerateGeometry = generateGeometry;
            vm.UpdateValues = !generateGeometry || chkSetValuesGeom.IsChecked == true || chkSetValuesRooms.IsChecked == true;
            vm.SetValuesForGeometry = chkSetValuesGeom.IsChecked == true;
            vm.SetValuesForRooms = chkSetValuesRooms.IsChecked == true;
            vm.SkipDoorsForSkirting = chkSkipDoor.IsChecked == true;
            vm.SkipWindowsForSkirting = chkSkipWindow.IsChecked == true;
            vm.SkipOpeningsForWalls = chkSkipOpeningsForWalls.IsChecked == true;

            vm.SelectedFloorTypeId = (cmbFloors.SelectedItem as ElementType)?.Id ?? ElementId.InvalidElementId;
            vm.SelectedCeilingTypeId = (cmbCeilings.SelectedItem as ElementType)?.Id ?? ElementId.InvalidElementId;
            vm.SelectedWallTypeId = (cmbWalls.SelectedItem as ElementType)?.Id ?? ElementId.InvalidElementId;
            vm.SelectedSkirtingTypeId = (cmbSkirtings.SelectedItem as ElementType)?.Id ?? ElementId.InvalidElementId;

            if (cmbBoundary.SelectedItem is System.Windows.Controls.ComboBoxItem ci)
            {
                var tag = (string)ci.Tag;
                vm.BoundaryMode = tag == "Centerline" ? FloorBoundaryMode.Centerline :
                                  tag == "OuterFinish" ? FloorBoundaryMode.OuterFinish :
                                  FloorBoundaryMode.InnerFinish;
            }

            double.TryParse(txtCeilingHeight.Text, out double ceilMm);
            double.TryParse(txtWallOffset.Text, out double wallOffsetMm);
            double.TryParse(txtSkirtingHeight.Text, out double skMm);
            vm.CeilingHeightMm = ceilMm;
            vm.WallOffsetMm = wallOffsetMm; // 儲存偏移值
            vm.WallHeightMm = Math.Max(100, ceilMm + wallOffsetMm); // 牆高 = 天花板高度 + 偏移值，最少100mm
            vm.SkirtingHeightMm = skMm;

            vm.TargetRoomIds = (rbPickRooms.IsChecked == true && _pickedRoomIds.Any()) ? _pickedRoomIds : null;
            ViewModel = vm;
        }

        private void LoadSettings()
        {
            try
            {
                var settings = FinishSettings.LoadFromFile();
                txtCeilingHeight.Text = settings.CeilingHeightMm.ToString();
                txtWallOffset.Text = settings.WallOffsetMm.ToString();
                txtSkirtingHeight.Text = settings.SkirtingHeightMm.ToString();
                chkSetValuesGeom.IsChecked = settings.SetValuesForGeometry;
                chkSetValuesRooms.IsChecked = settings.SetValuesForRooms;
                chkSkipDoor.IsChecked = settings.SkipDoorsForSkirting;
                chkSkipWindow.IsChecked = settings.SkipWindowsForSkirting;
                chkSkipOpeningsForWalls.IsChecked = settings.SkipOpeningsForWalls;
                
                // 設定邊界模式
                switch (settings.BoundaryMode)
                {
                    case FloorBoundaryMode.Centerline:
                        cmbBoundary.SelectedIndex = 1;
                        break;
                    case FloorBoundaryMode.OuterFinish:
                        cmbBoundary.SelectedIndex = 2;
                        break;
                    default:
                        cmbBoundary.SelectedIndex = 0;
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"載入設定失敗: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                if (ViewModel != null)
                {
                    ViewModel.SaveToFile();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"儲存設定失敗: {ex.Message}");
            }
        }

        private bool ValidateInputs()
        {
            var errors = new List<string>();

            // 驗證數值輸入
            if (!double.TryParse(txtCeilingHeight.Text, out double ceilHeight) || ceilHeight <= 0)
                errors.Add("天花板高度必須是正數");

            if (!double.TryParse(txtWallOffset.Text, out double wallOffset))
                errors.Add("牆面偏移量必須是有效數值");
            else if (wallOffset < 0)
                errors.Add("牆面偏移量不能是負數");

            if (!double.TryParse(txtSkirtingHeight.Text, out double skirtHeight) || skirtHeight <= 0)
                errors.Add("踢腳板高度必須是正數");

            // 檢查是否至少選擇了一個類型
            if (cmbFloors.SelectedItem == null && 
                cmbCeilings.SelectedItem == null && 
                cmbWalls.SelectedItem == null && 
                cmbSkirtings.SelectedItem == null)
            {
                errors.Add("請至少選擇一種裝修類型");
            }

            if (errors.Any())
            {
                var message = "輸入驗證失敗:\n" + string.Join("\n", errors);
                System.Windows.MessageBox.Show(message, "輸入錯誤", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void AutoJoinWalls()
        {
            try
            {
                // 提示用戶這個功能需要在生成牆面後執行
                var result = System.Windows.MessageBox.Show(
                    "自動接合牆面功能將嘗試接合目標房間內的相鄰牆面。\n" +
                    "此功能適用於已生成的裝修牆面。\n\n" +
                    "是否繼續？", 
                    "自動接合牆面", 
                    System.Windows.MessageBoxButton.YesNo, 
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    // 收集設定用於自動接合
                    var settings = new FinishSettings();
                    settings.AutoJoinWalls = true;
                    settings.GenerateGeometry = false;
                    
                    // 設定目標房間
                    if (rbPickRooms.IsChecked == true && _pickedRoomIds?.Any() == true)
                    {
                        settings.TargetRoomIds = _pickedRoomIds;
                    }
                    else
                    {
                        settings.TargetRoomIds = null; // 所有房間
                    }

                    // 執行自動接合
                    var doc = _uiDoc.Document;
                    using (var t = new Transaction(doc, "自動接合牆面"))
                    {
                        var status = t.Start();
                        if (status != TransactionStatus.Started)
                        {
                            System.Windows.MessageBox.Show("無法啟動交易", "錯誤",
                                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                            return;
                        }

                        var generator = new GeometryGenerator(_uiDoc);
                        var joinResults = generator.AutoJoinExistingWalls(settings.TargetRoomIds);

                        var commitStatus = t.Commit();
                        if (commitStatus != TransactionStatus.Committed)
                        {
                            System.Windows.MessageBox.Show($"交易提交失敗: {commitStatus}", "錯誤",
                                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                            return;
                        }
                        
                        // 顯示結果
                        var message = $"自動接合完成:\n成功接合 {joinResults.SuccessCount}/{joinResults.TotalAttempts} 次";
                        if (joinResults.Errors.Any())
                        {
                            message += $"\n\n錯誤:\n{string.Join("\n", joinResults.Errors.Take(5))}";
                            if (joinResults.Errors.Count > 5)
                                message += $"\n... 還有 {joinResults.Errors.Count - 5} 個錯誤";
                        }
                        
                        System.Windows.MessageBox.Show(message, "自動接合結果", 
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"自動接合牆面時發生錯誤: {ex.Message}", "錯誤", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
