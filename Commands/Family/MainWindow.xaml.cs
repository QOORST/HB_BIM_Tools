using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
namespace YD_RevitTools.LicenseManager.Commands.Family
{
    public enum FamilyParameterRequestKind
    {
        SetParameter,
        RenameTypes,
        BatchSetParameter
    }

    public sealed class TypeRenamePreviewItem
    {
        public string CurrentName { get; set; }
        public string NewName { get; set; }
        public string Status { get; set; }
                public bool IsSelected { get; set; }
        public bool CanApply => string.Equals(Status, "\u5F85\u5957\u7528", StringComparison.Ordinal);
    }

    public sealed class BatchParameterOption
    {
        public string Name { get; set; }
        public FamilyParameter Parameter { get; set; }
    }

    public sealed class BatchParameterPreviewItem
    {
        public string TypeName { get; set; }
        public string CurrentValue { get; set; }
        public string NewValue { get; set; }
        public string Status { get; set; }
                public bool IsSelected { get; set; }
        public bool CanApply => string.Equals(Status, "\u5F85\u5957\u7528", StringComparison.Ordinal);
    }

    public static class FamilyBatchParameterValue
    {
        public static bool TryParse(FamilyParameter parameter, string input, out object value, out string displayValue, out string error)
        {
            value = null;
            displayValue = input ?? string.Empty;
            error = null;

            if (parameter == null)
            {
                error = "\u672A\u9078\u53C3\u6578";
                return false;
            }

            input = (input ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(input) && parameter.StorageType != StorageType.String)
            {
                error = "\u65B0\u503C\u7A7A\u767D";
                return false;
            }

            switch (parameter.StorageType)
            {
                case StorageType.String:
                    value = input;
                    displayValue = input;
                    return true;
                case StorageType.Integer:
                    if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out var intValue))
                    {
                        error = "\u9700\u70BA\u6574\u6578";
                        return false;
                    }
                    value = intValue;
                    displayValue = intValue.ToString(CultureInfo.CurrentCulture);
                    return true;
                case StorageType.Double:
                    if (!double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out var doubleValue))
                    {
                        error = "\u9700\u70BA\u6578\u5B57";
                        return false;
                    }
                    if (IsLength(parameter))
                    {
                        value = UnitUtils.ConvertToInternalUnits(doubleValue, UnitTypeId.Millimeters);
                        displayValue = doubleValue.ToString("0.###", CultureInfo.CurrentCulture) + " mm";
                    }
                    else
                    {
                        value = doubleValue;
                        displayValue = doubleValue.ToString("0.###", CultureInfo.CurrentCulture);
                    }
                    return true;
                default:
                    error = "\u4E0D\u652F\u63F4\u6B64\u53C3\u6578\u985E\u578B";
                    return false;
            }
        }

        public static void Set(FamilyManager manager, FamilyParameter parameter, object value)
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    manager.Set(parameter, value as string ?? string.Empty);
                    break;
                case StorageType.Integer:
                    manager.Set(parameter, Convert.ToInt32(value, CultureInfo.CurrentCulture));
                    break;
                case StorageType.Double:
                    manager.Set(parameter, Convert.ToDouble(value, CultureInfo.CurrentCulture));
                    break;
                default:
                    throw new InvalidOperationException("\u4E0D\u652F\u63F4\u6B64\u53C3\u6578\u985E\u578B");
            }
        }

        public static string Format(FamilyType type, FamilyParameter parameter)
        {
            if (type == null || parameter == null)
                return string.Empty;

            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return type.AsString(parameter) ?? string.Empty;
                case StorageType.Integer:
                    return (type.AsInteger(parameter) ?? 0).ToString(CultureInfo.CurrentCulture);
                case StorageType.Double:
                    var value = type.AsDouble(parameter) ?? 0;
                    if (IsLength(parameter))
                    {
                        var mm = UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Millimeters);
                        return mm.ToString("0.###", CultureInfo.CurrentCulture) + " mm";
                    }
                    return value.ToString("0.###", CultureInfo.CurrentCulture);
                default:
                    return "\u4E0D\u652F\u63F4";
            }
        }

        public static bool IsLength(FamilyParameter parameter)
        {
            try
            {
                return parameter.Definition.GetDataType() == SpecTypeId.Length;
            }
            catch
            {
                return false;
            }
        }
    }

    public class FamilyParameterUpdateHandler : IExternalEventHandler
    {
        public FamilyParameterRequestKind RequestKind { get; set; } = FamilyParameterRequestKind.SetParameter;
        public FamilyParameter ParameterToUpdate { get; set; }
        public double ValueToSet { get; set; }
        public FamilyManager FamilyManager { get; set; }
        public List<TypeRenamePreviewItem> TypeRenameItems { get; set; } = new List<TypeRenamePreviewItem>();
        public FamilyParameter BatchParameterToUpdate { get; set; }
        public string BatchParameterValueText { get; set; }
        public List<BatchParameterPreviewItem> BatchParameterItems { get; set; } = new List<BatchParameterPreviewItem>();
        public Action AfterBatchApplied { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                if (RequestKind == FamilyParameterRequestKind.RenameTypes)
                {
                    RenameTypes(doc);
                    return;
                }

                if (RequestKind == FamilyParameterRequestKind.BatchSetParameter)
                {
                    BatchSetParameter(doc);
                    return;
                }

                using (var t = new Transaction(doc, "\u66F4\u65B0\u65CF\u53C3\u6578"))
                {
                    t.Start();
                    FamilyManager.Set(ParameterToUpdate, ValueToSet);
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("\u932F\u8AA4", "\u65CF\u7FA4\u5DE5\u5177\u57F7\u884C\u5931\u6557\uFF1A" + ex.Message);
            }
            finally
            {
                RequestKind = FamilyParameterRequestKind.SetParameter;
            }
        }

        private void RenameTypes(Document doc)
        {
            var items = TypeRenameItems?.Where(i => i.IsSelected && i.CanApply).ToList() ?? new List<TypeRenamePreviewItem>();
            if (items.Count == 0)
                return;

            var renamed = 0;
            var errors = new List<string>();
            using (var t = new Transaction(doc, "\u6279\u6B21\u8ABF\u6574\u65CF\u985E\u578B\u540D\u7A31"))
            {
                t.Start();
                foreach (var item in items)
                {
                    try
                    {
                        var type = FamilyManager.Types.Cast<FamilyType>()
                            .FirstOrDefault(x => string.Equals(x.Name, item.CurrentName, StringComparison.Ordinal));
                        if (type == null)
                        {
                            item.Status = "\u627E\u4E0D\u5230\u985E\u578B";
                            continue;
                        }

                        FamilyManager.CurrentType = type;
                        FamilyManager.RenameCurrentType(item.NewName.Trim());
                        item.CurrentName = item.NewName.Trim();
                        item.Status = "\u5DF2\u5957\u7528";
                        renamed++;
                    }
                    catch (Exception ex)
                    {
                        item.Status = "\u5931\u6557";
                        errors.Add(item.CurrentName + " -> " + item.NewName + ": " + ex.Message);
                    }
                }
                t.Commit();
            }

            AfterBatchApplied?.Invoke();
            var message = "\u5DF2\u5957\u7528 " + renamed + " \u500B\u65CF\u985E\u578B\u540D\u7A31\u3002";
            if (errors.Count > 0)
                message += "\n\n\u5931\u6557\u9805\u76EE\uFF1A\n" + string.Join("\n", errors.Take(8));
            TaskDialog.Show("\u6279\u6B21\u985E\u578B\u547D\u540D", message);
        }

        private void BatchSetParameter(Document doc)
        {
            var items = BatchParameterItems?.Where(i => i.IsSelected && i.CanApply).ToList() ?? new List<BatchParameterPreviewItem>();
            if (items.Count == 0)
                return;

            if (!FamilyBatchParameterValue.TryParse(BatchParameterToUpdate, BatchParameterValueText, out var parsedValue, out _, out var parseError))
            {
                TaskDialog.Show("\u6279\u6B21\u53C3\u6578\u8ABF\u6574", parseError);
                return;
            }

            var applied = 0;
            var errors = new List<string>();
            using (var t = new Transaction(doc, "\u6279\u6B21\u8ABF\u6574\u65CF\u985E\u578B\u53C3\u6578"))
            {
                t.Start();
                foreach (var item in items)
                {
                    try
                    {
                        var type = FamilyManager.Types.Cast<FamilyType>()
                            .FirstOrDefault(x => string.Equals(x.Name, item.TypeName, StringComparison.Ordinal));
                        if (type == null)
                        {
                            item.Status = "\u627E\u4E0D\u5230\u985E\u578B";
                            continue;
                        }

                        FamilyManager.CurrentType = type;
                        FamilyBatchParameterValue.Set(FamilyManager, BatchParameterToUpdate, parsedValue);
                        item.CurrentValue = item.NewValue;
                        item.Status = "\u5DF2\u5957\u7528";
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        item.Status = "\u5931\u6557";
                        errors.Add(item.TypeName + ": " + ex.Message);
                    }
                }
                t.Commit();
            }

            AfterBatchApplied?.Invoke();
            var message = "\u5DF2\u5957\u7528 " + applied + " \u500B\u65CF\u985E\u578B\u53C3\u6578\u503C\u3002";
            if (errors.Count > 0)
                message += "\n\n\u5931\u6557\u9805\u76EE\uFF1A\n" + string.Join("\n", errors.Take(8));
            TaskDialog.Show("\u6279\u6B21\u53C3\u6578\u8ABF\u6574", message);
        }

        public string GetName()
        {
            return "HB_BIM Tools - \u65CF\u53C3\u6578\u8207\u985E\u578B\u6279\u6B21\u5DE5\u5177";
        }
    }

    public partial class MainWindow : Window
    {
        private readonly Document _doc;
        private readonly FamilyManager _fm;
        private double sliderMin = 1;
        private double sliderMax = 10000;
        private double sliderStep = 0.1;
        private DispatcherTimer _updateTimer;
        private readonly FamilyParameterUpdateHandler _updateHandler;
        private readonly ExternalEvent _externalEvent;
        private readonly List<TypeRenamePreviewItem> _typeRenamePreviewItems = new List<TypeRenamePreviewItem>();
        private readonly List<BatchParameterPreviewItem> _batchParameterPreviewItems = new List<BatchParameterPreviewItem>();

        public MainWindow(ExternalCommandData commandData)
        {
            InitializeComponent();

            try
            {
                _doc = commandData.Application.ActiveUIDocument.Document;

                if (!_doc.IsFamilyDocument)
                {
                    MessageBox.Show("\u8ACB\u5728\u65CF\u7FA4\u7DE8\u8F2F\u5668\u4E2D\u4F7F\u7528\u6B64\u5DE5\u5177\u3002", "\u932F\u8AA4", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Close();
                    return;
                }

                _fm = _doc.FamilyManager;
                _updateHandler = new FamilyParameterUpdateHandler
                {
                    FamilyManager = _fm,
                    AfterBatchApplied = () => Dispatcher.Invoke(() =>
                    {
                        TypeLabel.Text = "\u7576\u524D\u985E\u578B\uFF1A" + (_fm.CurrentType?.Name ?? string.Empty);
                        RefreshSliders();
                        
                    })
                };
                _externalEvent = ExternalEvent.Create(_updateHandler);

                _updateTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };

                TypeLabel.Text = "\u7576\u524D\u985E\u578B\uFF1A" + (_fm.CurrentType?.Name ?? string.Empty);
                RefreshSliders();
                LoadBatchParameterOptions();
                RefreshTypeRenamePreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show("\u521D\u59CB\u5316\u5931\u6557\uFF1A" + ex.Message, "\u932F\u8AA4", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _externalEvent?.Dispose();
        }

        private void RefreshSliders()
        {
            try
            {
                MainStack.Children.Clear();

                var lengthParams = _fm.Parameters.Cast<FamilyParameter>()
                    .Where(p => p.Definition.GetDataType() == SpecTypeId.Length && !p.IsInstance)
                    .ToList();

                if (lengthParams.Count == 0)
                {
                    MainStack.Children.Add(new TextBlock
                    {
                        Text = "\u6B64\u65CF\u985E\u578B\u4E2D\u6C92\u6709\u53EF\u7DE8\u8F2F\u7684\u9577\u5EA6\u985E\u578B\u53C3\u6578\u3002",
                        Foreground = System.Windows.Media.Brushes.Gray
                    });
                    return;
                }

                foreach (var param in lengthParams)
                {
                    double valueFeet = _fm.CurrentType.AsDouble(param) ?? 0;
                    double valueMm = UnitUtils.ConvertFromInternalUnits(valueFeet, UnitTypeId.Millimeters);

                    var label = new TextBlock
                    {
                        Text = param.Definition.Name,
                        Margin = new Thickness(0, 5, 0, 2)
                    };

                    var slider = new Slider
                    {
                        Minimum = sliderMin,
                        Maximum = sliderMax,
                        TickFrequency = sliderStep,
                        Value = valueMm,
                        Margin = new Thickness(0, 0, 5, 0),
                        Width = 300
                    };

                    var valueBox = new System.Windows.Controls.TextBox
                    {
                        Text = valueMm.ToString("F2"),
                        Width = 90,
                        Margin = new Thickness(5, 0, 0, 0)
                    };

                    slider.ValueChanged += (s, e) =>
                    {
                        valueBox.Text = slider.Value.ToString("F2");
                        _updateTimer.Stop();
                        _updateTimer.Tick -= UpdateTimer_Tick;
                        _updateTimer.Tag = Tuple.Create(param, slider.Value);
                        _updateTimer.Tick += UpdateTimer_Tick;
                        _updateTimer.Start();
                    };

                    valueBox.KeyDown += (s, e) =>
                    {
                        if (e.Key == System.Windows.Input.Key.Enter)
                        {
                            if (double.TryParse(valueBox.Text, out double newValue))
                            {
                                if (newValue >= sliderMin && newValue <= sliderMax)
                                {
                                    slider.Value = newValue;
                                }
                                else
                                {
                                    MessageBox.Show("\u6578\u503C\u9700\u4ECB\u65BC " + sliderMin + " \u5230 " + sliderMax + ".", "\u8B66\u544A", MessageBoxButton.OK, MessageBoxImage.Warning);
                                    valueBox.Text = slider.Value.ToString("F2");
                                }
                            }
                            else
                            {
                                MessageBox.Show("\u8ACB\u8F38\u5165\u6709\u6548\u6578\u5B57\u3002", "\u932F\u8AA4", MessageBoxButton.OK, MessageBoxImage.Error);
                                valueBox.Text = slider.Value.ToString("F2");
                            }
                        }
                    };

                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    panel.Children.Add(slider);
                    panel.Children.Add(valueBox);

                    MainStack.Children.Add(label);
                    MainStack.Children.Add(panel);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("\u5237\u65B0\u6ED1\u687F\u5931\u6557\uFF1A" + ex.Message, "\u932F\u8AA4", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            _updateTimer.Stop();
            _updateTimer.Tick -= UpdateTimer_Tick;
            if (_updateTimer.Tag is Tuple<FamilyParameter, double> payload)
                UpdateParameter(payload.Item1, payload.Item2);
        }

        private void UpdateParameter(FamilyParameter param, double valueMm)
        {
            try
            {
                double newValFeet = UnitUtils.ConvertToInternalUnits(valueMm, UnitTypeId.Millimeters);
                _updateHandler.RequestKind = FamilyParameterRequestKind.SetParameter;
                _updateHandler.ParameterToUpdate = param;
                _updateHandler.ValueToSet = newValFeet;
                _externalEvent.Raise();
            }
            catch (Exception ex)
            {
                MessageBox.Show("\u66F4\u65B0\u53C3\u6578 '" + param.Definition.Name + "' \u5931\u6557\uFF1A" + ex.Message, "\u932F\u8AA4", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool IsBatchWritableTypeParameter(FamilyParameter parameter)
        {
            return parameter != null
                && !parameter.IsInstance
                && parameter.StorageType != StorageType.ElementId
                && !GetBoolProperty(parameter, "IsDeterminedByFormula")
                && !GetBoolProperty(parameter, "IsReporting")
                && !GetBoolProperty(parameter, "IsReadOnly");
        }

        private static bool GetBoolProperty(object target, string propertyName)
        {
            try
            {
                var property = target?.GetType().GetProperty(propertyName);
                if (property == null || property.PropertyType != typeof(bool))
                {
                    return false;
                }

                return (bool)property.GetValue(target, null);
            }
            catch
            {
                return false;
            }
        }
        private void LoadBatchParameterOptions()
        {
            var options = _fm.Parameters.Cast<FamilyParameter>()
                .Where(IsBatchWritableTypeParameter)
                .OrderBy(p => p.Definition.Name)
                .Select(p => new BatchParameterOption { Name = p.Definition.Name, Parameter = p })
                .ToList();

            BatchParameterCombo.ItemsSource = options;
            if (options.Count > 0)
                BatchParameterCombo.SelectedIndex = 0;
            else
                BatchParameterSummary.Text = "\u6C92\u6709\u53EF\u6279\u6B21\u8ABF\u6574\u7684\u985E\u578B\u53C3\u6578\u3002";
        }

        private void BatchParameter_Changed(object sender, EventArgs e)
        {
            RefreshBatchParameterPreview();
        }

        private void PreviewBatchParameter_Click(object sender, RoutedEventArgs e)
        {
            RefreshBatchParameterPreview(showEmptyMessage: true);
        }

        private void ApplyBatchParameter_Click(object sender, RoutedEventArgs e)
        {
            RefreshBatchParameterPreview();
            var targetItems = _batchParameterPreviewItems.Where(i => i.IsSelected && i.CanApply).ToList();
            if (targetItems.Count == 0)
            {
                MessageBox.Show("\u6C92\u6709\u53EF\u5957\u7528\u7684\u53C3\u6578\u8B8A\u66F4\u3002", "\u6279\u6B21\u53C3\u6578\u8ABF\u6574", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selected = BatchParameterCombo.SelectedItem as BatchParameterOption;
            if (selected == null)
                return;
            var confirm = MessageBox.Show("\u78BA\u5B9A\u8981\u5C07\u53C3\u6578 '" + selected?.Name + "' \u6279\u6B21\u5957\u7528\u5230 " + targetItems.Count + " \u500B\u65CF\u985E\u578B\uFF1F", "\u6279\u6B21\u53C3\u6578\u8ABF\u6574", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK)
                return;

            _updateHandler.RequestKind = FamilyParameterRequestKind.BatchSetParameter;
            _updateHandler.BatchParameterToUpdate = selected.Parameter;
            _updateHandler.BatchParameterValueText = BatchParameterValueBox.Text;
            _updateHandler.BatchParameterItems = targetItems;
            _externalEvent.Raise();
        }

        private void RefreshBatchParameterPreview(bool showEmptyMessage = false)
        {
            if (_fm == null || BatchParameterPreviewList == null)
                return;

            _batchParameterPreviewItems.Clear();
            var selected = BatchParameterCombo.SelectedItem as BatchParameterOption;
            if (BatchParameterUnitText != null)
                BatchParameterUnitText.Text = selected != null && FamilyBatchParameterValue.IsLength(selected.Parameter) ? "mm" : string.Empty;
            var types = _fm.Types.Cast<FamilyType>().OrderBy(t => t.Name).ToList();
            if (selected == null)
            {
                BatchParameterPreviewList.ItemsSource = null;
                return;
            }

            var isValid = FamilyBatchParameterValue.TryParse(selected.Parameter, BatchParameterValueBox.Text, out _, out var newDisplayValue, out var error);
            foreach (var type in types)
            {
                var current = FamilyBatchParameterValue.Format(type, selected.Parameter);
                var status = !isValid
                    ? error
                    : (string.Equals(current, newDisplayValue, StringComparison.Ordinal) ? "\u672A\u8B8A\u66F4" : "\u5F85\u5957\u7528");
                _batchParameterPreviewItems.Add(new BatchParameterPreviewItem
                {
                    TypeName = type.Name,
                    CurrentValue = current,
                    NewValue = isValid ? newDisplayValue : BatchParameterValueBox.Text,
                    Status = status,
                    IsSelected = string.Equals(status, "\u5F85\u5957\u7528", StringComparison.Ordinal)
                });
            }

            BatchParameterPreviewList.ItemsSource = null;
            BatchParameterPreviewList.ItemsSource = _batchParameterPreviewItems;
            var changed = _batchParameterPreviewItems.Count(i => i.CanApply);
            var selectedCount = _batchParameterPreviewItems.Count(i => i.IsSelected && i.CanApply);
            var blocked = _batchParameterPreviewItems.Count(i => !i.CanApply && !string.Equals(i.Status, "\u672A\u8B8A\u66F4", StringComparison.Ordinal));
            BatchParameterSummary.Text = "\u65CF\u985E\u578B\uFF1A" + types.Count + "\uFF0C\u53EF\u5957\u7528\uFF1A" + changed + "\uFF0C\u5DF2\u52FE\u9078\uFF1A" + selectedCount + "\uFF0C\u9700\u6AA2\u67E5\uFF1A" + blocked + "\u3002";

            if (showEmptyMessage && changed == 0)
                MessageBox.Show("\u6C92\u6709\u53EF\u5957\u7528\u7684\u53C3\u6578\u8B8A\u66F4\u3002", "\u6279\u6B21\u53C3\u6578\u8ABF\u6574", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void TypeAffix_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshTypeRenamePreview();
        }

        private void PreviewTypeRename_Click(object sender, RoutedEventArgs e)
        {
            RefreshTypeRenamePreview(showEmptyMessage: true);
        }

        private void ApplyTypeRename_Click(object sender, RoutedEventArgs e)
        {
            RefreshTypeRenamePreview();
            var targetItems = _typeRenamePreviewItems.Where(i => i.IsSelected && i.CanApply).ToList();
            if (targetItems.Count == 0)
            {
                MessageBox.Show("\u6C92\u6709\u53EF\u5957\u7528\u7684\u985E\u578B\u540D\u7A31\u8B8A\u66F4\u3002", "\u6279\u6B21\u985E\u578B\u547D\u540D", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show("\u78BA\u5B9A\u8981\u6279\u6B21\u8ABF\u6574 " + targetItems.Count + " \u500B\u65CF\u985E\u578B\uFF1F", "\u6279\u6B21\u985E\u578B\u547D\u540D", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK)
                return;

            _updateHandler.RequestKind = FamilyParameterRequestKind.RenameTypes;
            _updateHandler.TypeRenameItems = targetItems;
            _externalEvent.Raise();
        }

        private void RefreshTypeRenamePreview(bool showEmptyMessage = false)
        {
            if (_fm == null || TypeRenamePreviewList == null)
                return;

            var prefix = TypePrefixBox?.Text ?? string.Empty;
            var suffix = TypeSuffixBox?.Text ?? string.Empty;
            var findText = TypeFindBox?.Text ?? string.Empty;
            var replaceText = TypeReplaceBox?.Text ?? string.Empty;
            var types = _fm.Types.Cast<FamilyType>().OrderBy(t => t.Name).ToList();
            _typeRenamePreviewItems.Clear();

            foreach (var type in types)
            {
                var currentName = type.Name ?? string.Empty;
                var baseName = string.IsNullOrEmpty(findText) ? currentName : currentName.Replace(findText, replaceText);
                var newName = (prefix + baseName + suffix).Trim();
                var status = GetTypeRenameStatus(currentName, newName, types);
                _typeRenamePreviewItems.Add(new TypeRenamePreviewItem
                {
                    CurrentName = currentName,
                    NewName = newName,
                    Status = status,
                    IsSelected = string.Equals(status, "\u5F85\u5957\u7528", StringComparison.Ordinal)
                });
            }

            TypeRenamePreviewList.ItemsSource = null;
            TypeRenamePreviewList.ItemsSource = _typeRenamePreviewItems;
            var changed = _typeRenamePreviewItems.Count(i => i.CanApply);
            var selectedCount = _typeRenamePreviewItems.Count(i => i.IsSelected && i.CanApply);
            var blocked = _typeRenamePreviewItems.Count(i => !i.CanApply && !string.Equals(i.Status, "\u672A\u8B8A\u66F4", StringComparison.Ordinal));
            TypeRenameSummary.Text = "\u65CF\u985E\u578B\uFF1A" + types.Count + "\uFF0C\u53EF\u5957\u7528\uFF1A" + changed + "\uFF0C\u5DF2\u52FE\u9078\uFF1A" + selectedCount + "\uFF0C\u9700\u6AA2\u67E5\uFF1A" + blocked + ".";

            if (showEmptyMessage && changed == 0)
                MessageBox.Show("\u76EE\u524D\u6C92\u6709\u53EF\u9810\u89BD\u7684\u985E\u578B\u540D\u7A31\u8B8A\u66F4\u3002", "\u6279\u6B21\u985E\u578B\u547D\u540D", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static string GetTypeRenameStatus(string currentName, string newName, List<FamilyType> allTypes)
        {
            if (string.Equals(currentName, newName, StringComparison.Ordinal))
                return "\u672A\u8B8A\u66F4";
            if (string.IsNullOrWhiteSpace(newName))
                return "\u540D\u7A31\u7A7A\u767D";
            if (HasInvalidRevitNameChars(newName))
                return "\u975E\u6CD5\u5B57\u5143";
            if (allTypes.Any(t => !string.Equals(t.Name, currentName, StringComparison.Ordinal) && string.Equals(t.Name, newName, StringComparison.OrdinalIgnoreCase)))
                return "\u5DF2\u5B58\u5728";
            return "\u5F85\u5957\u7528";
        }

        private static bool HasInvalidRevitNameChars(string name)
        {
            return (name ?? string.Empty).IndexOfAny(new[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' }) >= 0;
        }

        private void TypeRenameSelection_Changed(object sender, RoutedEventArgs e)
        {
            UpdateTypeRenameSummary();
        }

        private void BatchParameterSelection_Changed(object sender, RoutedEventArgs e)
        {
            UpdateBatchParameterSummary();
        }

        private void UpdateTypeRenameSummary()
        {
            if (TypeRenameSummary == null)
                return;

            var total = _typeRenamePreviewItems.Count;
            var changed = _typeRenamePreviewItems.Count(i => i.CanApply);
            var selectedCount = _typeRenamePreviewItems.Count(i => i.IsSelected && i.CanApply);
            var blocked = _typeRenamePreviewItems.Count(i => !i.CanApply && !string.Equals(i.Status, "\u672A\u8B8A\u66F4", StringComparison.Ordinal));
            TypeRenameSummary.Text = "\u65CF\u985E\u578B\uFF1A" + total + "\uFF0C\u53EF\u5957\u7528\uFF1A" + changed + "\uFF0C\u5DF2\u52FE\u9078\uFF1A" + selectedCount + "\uFF0C\u9700\u6AA2\u67E5\uFF1A" + blocked + ".";
        }

        private void UpdateBatchParameterSummary()
        {
            if (BatchParameterSummary == null)
                return;

            var total = _batchParameterPreviewItems.Count;
            var changed = _batchParameterPreviewItems.Count(i => i.CanApply);
            var selectedCount = _batchParameterPreviewItems.Count(i => i.IsSelected && i.CanApply);
            var blocked = _batchParameterPreviewItems.Count(i => !i.CanApply && !string.Equals(i.Status, "\u672A\u8B8A\u66F4", StringComparison.Ordinal));
            BatchParameterSummary.Text = "\u65CF\u985E\u578B\uFF1A" + total + "\uFF0C\u53EF\u5957\u7528\uFF1A" + changed + "\uFF0C\u5DF2\u52FE\u9078\uFF1A" + selectedCount + "\uFF0C\u9700\u6AA2\u67E5\uFF1A" + blocked + "\u3002";
        }
        private void ExportTypeRenameCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_typeRenamePreviewItems.Count == 0)
                RefreshTypeRenamePreview();

            ExportCsv("family_type_rename_result.csv", "\u5957\u7528,\u76EE\u524D\u540D\u7A31,\u65B0\u540D\u7A31,\u72C0\u614B", _typeRenamePreviewItems.Select(i => new[]
            {
                i.IsSelected ? "Y" : "N",
                i.CurrentName,
                i.NewName,
                i.Status
            }));
        }

        private void ExportBatchParameterCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_batchParameterPreviewItems.Count == 0)
                RefreshBatchParameterPreview();

            ExportCsv("family_parameter_batch_result.csv", "\u5957\u7528,\u985E\u578B,\u76EE\u524D\u503C,\u65B0\u503C,\u72C0\u614B", _batchParameterPreviewItems.Select(i => new[]
            {
                i.IsSelected ? "Y" : "N",
                i.TypeName,
                i.CurrentValue,
                i.NewValue,
                i.Status
            }));
        }

        private void ExportCsv(string defaultFileName, string header, IEnumerable<string[]> rows)
        {
            var rowList = rows?.ToList() ?? new List<string[]>();
            if (rowList.Count == 0)
            {
                MessageBox.Show("\u6C92\u6709\u53EF\u532F\u51FA\u7684\u9810\u89BD\u8CC7\u6599\u3002", "\u532F\u51FA CSV", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "\u532F\u51FA CSV",
                FileName = defaultFileName,
                Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var builder = new StringBuilder();
            builder.AppendLine(header);
            foreach (var row in rowList)
                builder.AppendLine(string.Join(",", row.Select(CsvEscape)));

            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            MessageBox.Show("\u5DF2\u532F\u51FA CSV\uFF1A" + dialog.FileName, "\u532F\u51FA CSV", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static string CsvEscape(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = new SliderSettingsWindow(sliderMin, sliderMax, sliderStep);
                if (settings.ShowDialog() == true)
                {
                    sliderMin = settings.Min;
                    sliderMax = settings.Max;
                    sliderStep = settings.Step;
                    RefreshSliders();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("\u958B\u555F\u8A2D\u5B9A\u5931\u6557\uFF1A" + ex.Message, "\u932F\u8AA4", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TypeLabel.Text = "\u7576\u524D\u985E\u578B\uFF1A" + (_fm.CurrentType?.Name ?? string.Empty);
                RefreshSliders();
                LoadBatchParameterOptions();
                
                MessageBox.Show("\u5DF2\u5237\u65B0\u3002", "\u5B8C\u6210", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("\u5237\u65B0\u5931\u6557\uFF1A" + ex.Message, "\u932F\u8AA4", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _updateTimer?.Stop();
            _externalEvent?.Dispose();
            base.OnClosing(e);
        }
    }
}