using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WinForms = System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.MEP
{
    internal enum MepRotateElementType
    {
        Pipe,
        Duct,
        Conduit
    }

    internal static class MepRotateState
    {
        public static MepRotateElementType ElementType { get; set; } = MepRotateElementType.Pipe;
        public static double AngleDeg { get; set; } = 45.0;
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdMepRotateSettings : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            using (var dlg = new MepRotateSettingsDialog())
            {
                return dlg.ShowDialog() == WinForms.DialogResult.OK ? Result.Succeeded : Result.Cancelled;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdMepRotateClockwise : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => Rotate(commandData, clockwise: true);

        internal static Result Rotate(ExternalCommandData commandData, bool clockwise)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            try
            {
                List<Element> targets = GetTargets(uiDoc, doc);
                if (!targets.Any()) return Result.Cancelled;

                double angleRad = (MepRotateState.AngleDeg * Math.PI / 180.0) * (clockwise ? -1.0 : 1.0);
                int success = 0;

                using (Transaction tx = new Transaction(doc, clockwise ? "MEP 順時針旋轉" : "MEP 逆時針旋轉"))
                {
                    tx.Start();
                    foreach (var e in targets)
                    {
                        if (TryRotateOne(doc, e, angleRad)) success++;
                    }
                    tx.Commit();
                }

                TaskDialog.Show("MEP 旋轉", $"完成旋轉：{success}/{targets.Count}");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MEP 旋轉", $"旋轉失敗：\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static List<Element> GetTargets(UIDocument uiDoc, Document doc)
        {
            var ids = uiDoc.Selection.GetElementIds();
            var selected = ids.Select(id => doc.GetElement(id)).Where(IsTypeMatch).ToList();
            if (selected.Any()) return selected;

            var refs = uiDoc.Selection.PickObjects(ObjectType.Element, new MepRotateSelectionFilter(), "選取要旋轉的元素（可複選）");
            return refs.Select(r => doc.GetElement(r)).Where(IsTypeMatch).ToList();
        }

        private static bool IsTypeMatch(Element e)
        {
            return MepRotateState.ElementType switch
            {
                MepRotateElementType.Pipe => e is Pipe,
                MepRotateElementType.Duct => e is Duct,
                MepRotateElementType.Conduit => e is Conduit,
                _ => false
            };
        }

        private static bool TryRotateOne(Document doc, Element e, double angleRad)
        {
            var lc = e.Location as LocationCurve;
            if (!(lc?.Curve is Line axis)) return false;
            ElementTransformUtils.RotateElement(doc, e.Id, axis, angleRad);
            return true;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CmdMepRotateCounterClockwise : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => CmdMepRotateClockwise.Rotate(commandData, clockwise: false);
    }

    internal class MepRotateSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return MepRotateState.ElementType switch
            {
                MepRotateElementType.Pipe => elem is Pipe,
                MepRotateElementType.Duct => elem is Duct,
                MepRotateElementType.Conduit => elem is Conduit,
                _ => false
            };
        }

        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    internal class MepRotateSettingsDialog : WinForms.Form
    {
        private readonly WinForms.RadioButton _rbPipe;
        private readonly WinForms.RadioButton _rbDuct;
        private readonly WinForms.RadioButton _rbConduit;
        private readonly WinForms.TextBox _txtAngle;

        public MepRotateSettingsDialog()
        {
            Text = "旋轉設定";
            Width = 360;
            Height = 260;
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            Font = new System.Drawing.Font("Microsoft JhengHei UI", 10f);

            Controls.Add(new WinForms.Label { Left = 24, Top = 20, Text = "元素類型", AutoSize = true });
            _rbPipe = new WinForms.RadioButton { Left = 28, Top = 48, Text = "管", AutoSize = true };
            _rbDuct = new WinForms.RadioButton { Left = 128, Top = 48, Text = "風管", AutoSize = true };
            _rbConduit = new WinForms.RadioButton { Left = 228, Top = 48, Text = "電管", AutoSize = true };
            Controls.AddRange(new WinForms.Control[] { _rbPipe, _rbDuct, _rbConduit });

            Controls.Add(new WinForms.Label { Left = 24, Top = 88, Text = "旋轉角度", AutoSize = true });
            _txtAngle = new WinForms.TextBox { Left = 110, Top = 84, Width = 130, Text = MepRotateState.AngleDeg.ToString("0.####") };
            Controls.Add(_txtAngle);
            var btn225 = new WinForms.Button { Left = 24, Top = 124, Width = 70, Text = "22.5°" };
            var btn45 = new WinForms.Button { Left = 102, Top = 124, Width = 70, Text = "45°" };
            var btn90 = new WinForms.Button { Left = 180, Top = 124, Width = 70, Text = "90°" };
            btn225.Click += (s, e) => _txtAngle.Text = "22.5";
            btn45.Click += (s, e) => _txtAngle.Text = "45";
            btn90.Click += (s, e) => _txtAngle.Text = "90";
            Controls.AddRange(new WinForms.Control[] { btn225, btn45, btn90 });

            var btnOk = new WinForms.Button { Left = 80, Top = 170, Width = 90, Text = "確定" };
            var btnCancel = new WinForms.Button { Left = 180, Top = 170, Width = 90, Text = "取消" };
            btnOk.Click += Ok_Click;
            btnCancel.Click += (s, e) => { DialogResult = WinForms.DialogResult.Cancel; Close(); };
            Controls.AddRange(new WinForms.Control[] { btnOk, btnCancel });

            switch (MepRotateState.ElementType)
            {
                case MepRotateElementType.Pipe: _rbPipe.Checked = true; break;
                case MepRotateElementType.Duct: _rbDuct.Checked = true; break;
                case MepRotateElementType.Conduit: _rbConduit.Checked = true; break;
            }
        }

        private void Ok_Click(object sender, EventArgs e)
        {
            if (!double.TryParse(_txtAngle.Text, out double angle) || angle <= 0)
            {
                WinForms.MessageBox.Show("請輸入正確旋轉角度。");
                return;
            }
            MepRotateState.AngleDeg = angle;
            if (_rbDuct.Checked) MepRotateState.ElementType = MepRotateElementType.Duct;
            else if (_rbConduit.Checked) MepRotateState.ElementType = MepRotateElementType.Conduit;
            else MepRotateState.ElementType = MepRotateElementType.Pipe;

            DialogResult = WinForms.DialogResult.OK;
            Close();
        }
    }
}
