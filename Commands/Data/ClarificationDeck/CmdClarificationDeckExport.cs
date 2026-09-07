using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using A = DocumentFormat.OpenXml.Drawing;
using DrawPoint = System.Drawing.Point;
using DrawRectangle = System.Drawing.Rectangle;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using WinForm = System.Windows.Forms.Form;
using WinTextBox = System.Windows.Forms.TextBox;
using SysColor = System.Drawing.Color;

namespace YD_RevitTools.LicenseManager.Commands.Data.ClarificationDeck
{
    [Transaction(TransactionMode.Manual)]
    public class CmdClarificationDeckExport : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null)
            {
                message = "No active Revit document.";
                return Result.Failed;
            }

            if (!LicenseManager.Instance.HasFeatureAccess("Data.ClarificationDeckExport"))
            {
                TaskDialog.Show("釋疑簡報", "此功能需要 Standard 或 Professional 授權。");
                return Result.Cancelled;
            }

            try
            {
                using (var dialog = new ClarificationDeckDialog(doc))
                {
                    if (dialog.ShowDialog() != DialogResult.OK)
                        return Result.Cancelled;

                    dialog.Options.SlideIndex = ClarificationDeckBuilder.Build(dialog.Options);
                    ClarificationDeckRecordStore.UpsertFromOptions(dialog.Options);

                    var result = MessageBox.Show(
                        "釋疑簡報已產出。\n\n是：開啟簡報\n否：開啟資料夾\n取消：關閉",
                        "釋疑簡報",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = dialog.Options.OutputPath,
                            UseShellExecute = true
                        });
                    else if (result == DialogResult.No)
                        Process.Start("explorer.exe", "/select,\"" + dialog.Options.OutputPath + "\"");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("釋疑簡報", "產出失敗：\n" + ex.Message);
                return Result.Failed;
            }
        }
    }

    internal sealed class ClarificationDeckOptions
    {
        public string ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string ProjectPath { get; set; }
        public string RevitVersion { get; set; }
        public string ItemNo { get; set; }
        public string Title { get; set; }
        public string SystemName { get; set; }
        public DateTime IssueDate { get; set; }
        public string Description { get; set; }
        public string Reply { get; set; }
        public string Handling { get; set; }
        public string TrackStatus { get; set; }
        public string OverviewImagePath { get; set; }
        public string DetailImagePath { get; set; }
        public string ThreeDImagePath { get; set; }
        public string ExtraImagePath { get; set; }
        public string OutputPath { get; set; }
        public string PageNo { get; set; }
        public bool AppendToProjectDeck { get; set; }
        public string ExportMode { get; set; }
        public string TemplateMode { get; set; }
        public int SlideIndex { get; set; }
        public int Revision { get; set; }
        public bool IsCurrent { get; set; }
    }

    internal enum DuplicateItemDecision
    {
        Cancel,
        SaveAsCurrentRevision,
        SaveAsAdditionalVersion
    }

    internal sealed class ClarificationDeckDialog : WinForm
    {
        private readonly WinTextBox _itemNo = new WinTextBox();
        private readonly WinTextBox _title = new WinTextBox();
        private readonly WinTextBox _systemName = new WinTextBox();
        private readonly DateTimePicker _issueDate = new DateTimePicker();
        private readonly WinTextBox _description = new WinTextBox();
        private readonly WinTextBox _reply = new WinTextBox();
        private readonly WinTextBox _handling = new WinTextBox();
        private readonly WinTextBox _trackStatus = new WinTextBox();
        private readonly List<Button> _progressButtons = new List<Button>();
        private readonly WinTextBox _overviewImage = new WinTextBox();
        private readonly WinTextBox _detailImage = new WinTextBox();
        private readonly WinTextBox _threeDImage = new WinTextBox();
        private readonly WinTextBox _extraImage = new WinTextBox();
        private readonly WinTextBox _outputPath = new WinTextBox();
        private readonly WinTextBox _pageNo = new WinTextBox();
        private readonly CheckBox _appendToProjectDeck = new CheckBox();
        private readonly System.Windows.Forms.ComboBox _templateMode = new System.Windows.Forms.ComboBox();
        private readonly ToolTip _toolTip = new ToolTip();
        private readonly string _projectId;
        private readonly string _projectName;
        private readonly string _projectPath;
        private readonly string _revitVersion;
        private string _lastGeneratedItemNo;
        private bool _loadingRecord;
        private ImageSlotCard[] _imageCards;

        public ClarificationDeckOptions Options { get; private set; }

        public ClarificationDeckDialog(Document doc)
        {
            Text = "釋疑簡報快速產出";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 850);
            Size = new Size(1040, 900);
            Font = new Font("Microsoft JhengHei UI", 9.5F);
            BackColor = SysColor.White;

            _projectName = doc.ProjectInformation?.Name;
            if (string.IsNullOrWhiteSpace(_projectName))
                _projectName = doc.Title;
            _projectPath = doc.PathName;
            _projectId = CreateProjectId(doc, _projectName, _projectPath);
            _revitVersion = doc.Application?.VersionNumber ?? string.Empty;

            _title.Text = _projectName;
            _systemName.Text = "PP";
            _issueDate.Value = DateTime.Today;
            _pageNo.Text = "1";
            _appendToProjectDeck.Checked = true;
            _templateMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _templateMode.Items.AddRange(new object[] { "內部檢查", "正式釋疑" });
            _templateMode.SelectedIndex = 0;
            _templateMode.SelectedIndexChanged += (s, e) =>
            {
                _outputPath.Text = CreateDefaultOutputPath();
                RefreshDefaultItemNo(false);
            };
            _systemName.TextChanged += (s, e) => RefreshDefaultItemNo(false);

            RefreshDefaultItemNo(true);
            _outputPath.Text = CreateDefaultOutputPath();

            BuildLayout();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 12,
                Padding = new Padding(16),
                BackColor = SysColor.White
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));

            AddRow(root, 0, "項次", _itemNo);
            AddRow(root, 1, "標題", _title);
            AddRow(root, 2, "系統", _systemName);
            AddDateRow(root, 3);
            AddTemplateRow(root, 4);
            AddMultilineRow(root, 5, "問題處/釋疑說明", _description, 78);
            AddMultilineRow(root, 6, "修正方式/建議回覆", _reply, 64);
            AddMultilineRow(root, 7, "修正情形/辦理情形", _handling, 64);
            AddProgressRow(root, 8);
            AddImageSection(root, 9);
            AddAppendModeRow(root, 10);
            AddOutputRow(root, 11);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 16, 8)
            };

            var ok = BuildButton("產出簡報", 110);
            ok.Click += (s, e) => Accept();
            var history = BuildButton("歷史紀錄", 100);
            history.Click += (s, e) => ShowHistory();
            var cancel = BuildButton("取消", 86);
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(history);
            buttons.Controls.Add(cancel);

            Controls.Add(root);
            Controls.Add(buttons);
        }

        private static Button BuildButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 34,
                Margin = new Padding(8, 0, 0, 0),
                BackColor = SysColor.FromArgb(31, 108, 159),
                ForeColor = SysColor.White,
                FlatStyle = FlatStyle.Flat
            };
        }

        private static void AddLabel(TableLayoutPanel root, int row, string text)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                Padding = new Padding(0, 6, 0, 6)
            }, 0, row);
        }

        private static void AddRow(TableLayoutPanel root, int row, string label, WinTextBox box)
        {
            AddLabel(root, row, label);
            box.Dock = DockStyle.Fill;
            root.Controls.Add(box, 1, row);
            root.SetColumnSpan(box, 2);
        }

        private void AddDateRow(TableLayoutPanel root, int row)
        {
            AddLabel(root, row, "日期");
            _issueDate.Format = DateTimePickerFormat.Custom;
            _issueDate.CustomFormat = "yyyy/MM/dd";
            _issueDate.Dock = DockStyle.Left;
            root.Controls.Add(_issueDate, 1, row);
            root.SetColumnSpan(_issueDate, 2);
        }

        private void AddTemplateRow(TableLayoutPanel root, int row)
        {
            AddLabel(root, row, "簡報樣板");
            _templateMode.Dock = DockStyle.Left;
            _templateMode.Width = 160;
            root.Controls.Add(_templateMode, 1, row);
            root.SetColumnSpan(_templateMode, 2);
        }

        private static void AddMultilineRow(TableLayoutPanel root, int row, string label, WinTextBox box, int height)
        {
            AddLabel(root, row, label);
            box.Multiline = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.Height = height;
            box.Dock = DockStyle.Fill;
            root.Controls.Add(box, 1, row);
            root.SetColumnSpan(box, 2);
        }

        private void AddProgressRow(TableLayoutPanel root, int row)
        {
            AddLabel(root, row, "進度");
            _trackStatus.Visible = false;

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 2, 0, 2)
            };

            AddProgressButton(panel, "待進行");
            AddProgressButton(panel, "進行中");
            AddProgressButton(panel, "完成");
            root.Controls.Add(panel, 1, row);
            root.SetColumnSpan(panel, 2);

            SetProgress(string.IsNullOrWhiteSpace(_trackStatus.Text) ? "待進行" : _trackStatus.Text);
        }

        private void AddProgressButton(FlowLayoutPanel panel, string text)
        {
            var button = BuildButton(text, 86);
            button.Height = 30;
            button.Margin = new Padding(0, 0, 8, 0);
            button.Tag = text;
            button.Click += (s, e) => SetProgress(text);
            _progressButtons.Add(button);
            panel.Controls.Add(button);
        }

        private void SetProgress(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "待進行" : value.Trim();
            _trackStatus.Text = normalized;

            foreach (var button in _progressButtons)
            {
                var selected = string.Equals(button.Tag as string, normalized, StringComparison.Ordinal);
                button.BackColor = selected
                    ? SysColor.FromArgb(21, 158, 82)
                    : SysColor.FromArgb(31, 108, 159);
            }
        }

        private void SetTemplateMode(string templateMode)
        {
            var display = string.Equals(templateMode, "FormalClarification", StringComparison.OrdinalIgnoreCase)
                ? "正式釋疑"
                : "內部檢查";
            var index = _templateMode.Items.IndexOf(display);
            _templateMode.SelectedIndex = index >= 0 ? index : 0;
        }

        private void RefreshDefaultItemNo(bool force)
        {
            if (_loadingRecord)
                return;

            var current = _itemNo.Text.Trim();
            if (!force &&
                !string.IsNullOrWhiteSpace(current) &&
                !string.Equals(current, _lastGeneratedItemNo, StringComparison.OrdinalIgnoreCase))
                return;

            var next = IsFormalTemplate
                ? ClarificationDeckRecordStore.GetNextFormalClarificationItemNo(_projectId, _systemName.Text)
                : ClarificationDeckRecordStore.GetNextQuestionItemNo(_projectId);

            _itemNo.Text = next;
            _lastGeneratedItemNo = next;
        }

        private void AddImageSection(TableLayoutPanel root, int row)
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 235));

            var group = new GroupBox
            {
                Text = "圖片",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1
            };

            for (var i = 0; i < 4; i++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _imageCards = new[]
            {
                new ImageSlotCard("平面總圖", _overviewImage, BrowseImage, CaptureWindowSelection, _toolTip),
                new ImageSlotCard("局部放大", _detailImage, BrowseImage, CaptureWindowSelection, _toolTip),
                new ImageSlotCard("3D 視圖", _threeDImage, BrowseImage, CaptureWindowSelection, _toolTip),
                new ImageSlotCard("補充圖片", _extraImage, BrowseImage, CaptureWindowSelection, _toolTip)
            };

            for (var i = 0; i < _imageCards.Length; i++)
                grid.Controls.Add(_imageCards[i], i, 0);

            group.Controls.Add(grid);
            root.Controls.Add(group, 0, row);
            root.SetColumnSpan(group, 3);
        }

        private void AddOutputRow(TableLayoutPanel root, int row)
        {
            AddLabel(root, row, "輸出");
            _outputPath.Dock = DockStyle.Fill;
            root.Controls.Add(_outputPath, 1, row);
            var browse = BuildButton("另存", 76);
            browse.Click += (s, e) => BrowseOutput();
            root.Controls.Add(browse, 2, row);
        }

        private void AddAppendModeRow(TableLayoutPanel root, int row)
        {
            AddLabel(root, row, "模式");
            _appendToProjectDeck.Text = "追加到專案總簡報";
            _appendToProjectDeck.Dock = DockStyle.Left;
            _appendToProjectDeck.AutoSize = true;
            _appendToProjectDeck.CheckedChanged += (s, e) => _outputPath.Text = CreateDefaultOutputPath();
            root.Controls.Add(_appendToProjectDeck, 1, row);
            root.SetColumnSpan(_appendToProjectDeck, 2);
        }

        private void BrowseImage(WinTextBox box)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";
                if (dialog.ShowDialog() == DialogResult.OK)
                    SetImagePath(box, dialog.FileName);
            }
        }

        private void CaptureWindowSelection(WinTextBox target)
        {
            Hide();
            try
            {
                using (var capture = new WindowSelectionCaptureForm())
                {
                    if (capture.ShowDialog() == DialogResult.OK && File.Exists(capture.CapturedPath))
                        SetImagePath(target, capture.CapturedPath);
                }
            }
            finally
            {
                Show();
                Activate();
            }
        }

        private void SetImagePath(WinTextBox target, string imagePath)
        {
            target.Text = imagePath;
            RefreshImageCards();
        }

        private void RefreshImageCards()
        {
            if (_imageCards == null)
                return;

            foreach (var card in _imageCards)
                card.RefreshPreview();
        }

        private void BrowseOutput()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "PowerPoint (*.pptx)|*.pptx";
                dialog.DefaultExt = "pptx";
                dialog.FileName = Path.GetFileName(_outputPath.Text);
                if (dialog.ShowDialog() == DialogResult.OK)
                    _outputPath.Text = dialog.FileName;
            }
        }

        private void ShowHistory()
        {
            using (var dialog = new ClarificationDeckHistoryDialog(_projectId))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedRecord != null)
                    LoadRecord(dialog.SelectedRecord);
            }
        }

        private void LoadRecord(ClarificationDeckRecord record)
        {
            _loadingRecord = true;
            try
            {
                _itemNo.Text = record.ItemNo;
                _title.Text = record.Title;
                _systemName.Text = record.SystemName;
                if (record.IssueDate != default(DateTime))
                    _issueDate.Value = record.IssueDate;
                _description.Text = record.Description;
                _reply.Text = record.Reply;
                _handling.Text = record.Handling;
                SetProgress(record.Progress);
                SetTemplateMode(record.TemplateMode);
                _overviewImage.Text = record.OverviewImagePath;
                _detailImage.Text = record.DetailImagePath;
                _threeDImage.Text = record.ThreeDImagePath;
                _extraImage.Text = record.ExtraImagePath;
                _lastGeneratedItemNo = null;
            }
            finally
            {
                _loadingRecord = false;
            }

            _outputPath.Text = CreateDefaultOutputPath();
            RefreshImageCards();
        }

        private string CreateDefaultOutputPath()
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var prefix = IsFormalTemplate ? "正式釋疑簡報" : "釋疑簡報";
            if (_appendToProjectDeck.Checked)
                return Path.Combine(folder, $"{prefix}_{SanitizeFileName(_projectName)}.pptx");

            var itemSuffix = string.IsNullOrWhiteSpace(_itemNo.Text) ? string.Empty : "_" + SanitizeFileName(_itemNo.Text.Trim());
            return Path.Combine(folder, $"{prefix}{itemSuffix}_{DateTime.Now:yyyyMMdd_HHmm}.pptx");
        }

        private bool IsFormalTemplate => string.Equals(_templateMode.SelectedItem as string, "正式釋疑", StringComparison.Ordinal);

        private string SelectedTemplateMode => IsFormalTemplate ? "FormalClarification" : "InternalReview";

        private static string CreateProjectId(Document doc, string projectName, string projectPath)
        {
            if (!string.IsNullOrWhiteSpace(projectPath))
                return "PATH:" + NormalizeProjectPath(projectPath);

            var uniqueId = doc.ProjectInformation?.UniqueId;
            if (!string.IsNullOrWhiteSpace(uniqueId))
                return "UNSAVED:" + uniqueId + "|" + (doc.Title ?? projectName ?? string.Empty);

            return "UNSAVED:" + (doc.Title ?? projectName ?? string.Empty);
        }

        private static string NormalizeProjectPath(string projectPath)
        {
            try
            {
                return Path.GetFullPath(projectPath).Trim().ToUpperInvariant();
            }
            catch
            {
                return projectPath.Trim().ToUpperInvariant();
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                value = "未命名專案";

            foreach (var invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value;
        }

        private void Accept()
        {
            if (string.IsNullOrWhiteSpace(_itemNo.Text))
            {
                MessageBox.Show("請填寫項次。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _itemNo.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(_title.Text))
            {
                MessageBox.Show("請填寫標題。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _title.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(_description.Text))
            {
                MessageBox.Show("請填寫問題處。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _description.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(_outputPath.Text))
            {
                MessageBox.Show("請指定輸出檔案。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _outputPath.Focus();
                return;
            }

            var imageBoxes = new[] { _overviewImage, _detailImage, _threeDImage, _extraImage };
            var missingImage = imageBoxes.FirstOrDefault(box => !string.IsNullOrWhiteSpace(box.Text) && !File.Exists(box.Text));
            if (missingImage != null)
            {
                MessageBox.Show("圖片檔案不存在，請重新選取或清除該欄位。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var outputDir = Path.GetDirectoryName(_outputPath.Text);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var outputPath = _outputPath.Text.Trim();
            if (File.Exists(outputPath) && IsFileLocked(outputPath))
            {
                MessageBox.Show("輸出簡報目前可能已被 PowerPoint 開啟，請先關閉該檔案後再產出。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var existing = ClarificationDeckRecordStore.FindLatest(_projectId, _itemNo.Text.Trim());
            var isCurrent = true;
            if (existing != null)
            {
                var duplicateResult = DuplicateItemDialog.Show(this, existing.ItemNo);

                if (duplicateResult == DuplicateItemDecision.Cancel)
                    return;

                isCurrent = duplicateResult == DuplicateItemDecision.SaveAsCurrentRevision;
            }

            Options = new ClarificationDeckOptions
            {
                ProjectId = _projectId,
                ProjectName = _projectName,
                ProjectPath = _projectPath,
                RevitVersion = _revitVersion,
                ItemNo = _itemNo.Text.Trim(),
                Title = _title.Text.Trim(),
                SystemName = _systemName.Text.Trim(),
                IssueDate = _issueDate.Value,
                Description = _description.Text.Trim(),
                Reply = _reply.Text.Trim(),
                Handling = _handling.Text.Trim(),
                TrackStatus = _trackStatus.Text.Trim(),
                OverviewImagePath = _overviewImage.Text.Trim(),
                DetailImagePath = _detailImage.Text.Trim(),
                ThreeDImagePath = _threeDImage.Text.Trim(),
                ExtraImagePath = _extraImage.Text.Trim(),
                OutputPath = _outputPath.Text.Trim(),
                PageNo = _pageNo.Text.Trim(),
                AppendToProjectDeck = _appendToProjectDeck.Checked,
                ExportMode = _appendToProjectDeck.Checked ? "AppendDeck" : "SingleFile",
                TemplateMode = SelectedTemplateMode,
                Revision = ClarificationDeckRecordStore.GetNextRevision(_projectId, _itemNo.Text.Trim()),
                IsCurrent = isCurrent
            };

            DialogResult = DialogResult.OK;
        }

        private static bool IsFileLocked(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }
    }

    internal sealed class DuplicateItemDialog : WinForm
    {
        private DuplicateItemDecision _decision = DuplicateItemDecision.Cancel;

        private DuplicateItemDialog(string itemNo)
        {
            Text = "項次已存在";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Size = new Size(470, 190);
            Font = new Font("Microsoft JhengHei UI", 9.5F);

            var label = new Label
            {
                Text = $"此專案已有項次 {itemNo} 的紀錄，請選擇本次產出方式。",
                Dock = DockStyle.Top,
                Height = 58,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 12, 16, 0)
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 72,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12, 16, 12, 12)
            };

            var cancel = BuildDialogButton("取消", 82);
            cancel.Click += (s, e) => CloseWith(DuplicateItemDecision.Cancel);
            var addVersion = BuildDialogButton("新增版本", 104);
            addVersion.Click += (s, e) => CloseWith(DuplicateItemDecision.SaveAsAdditionalVersion);
            var updateCurrent = BuildDialogButton("更新有效版", 116);
            updateCurrent.Click += (s, e) => CloseWith(DuplicateItemDecision.SaveAsCurrentRevision);

            buttons.Controls.Add(cancel);
            buttons.Controls.Add(addVersion);
            buttons.Controls.Add(updateCurrent);
            Controls.Add(label);
            Controls.Add(buttons);
        }

        public static DuplicateItemDecision Show(IWin32Window owner, string itemNo)
        {
            using (var dialog = new DuplicateItemDialog(itemNo))
            {
                dialog.ShowDialog(owner);
                return dialog._decision;
            }
        }

        private static Button BuildDialogButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 34,
                Margin = new Padding(8, 0, 0, 0),
                BackColor = SysColor.FromArgb(31, 108, 159),
                ForeColor = SysColor.White,
                FlatStyle = FlatStyle.Flat
            };
        }

        private void CloseWith(DuplicateItemDecision decision)
        {
            _decision = decision;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal sealed class ClarificationDeckRecord
    {
        public string Id { get; set; }
        public string ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string ProjectPath { get; set; }
        public string RevitVersion { get; set; }
        public string ItemNo { get; set; }
        public string Title { get; set; }
        public string SystemName { get; set; }
        public DateTime IssueDate { get; set; }
        public string Description { get; set; }
        public string Reply { get; set; }
        public string Handling { get; set; }
        public string Progress { get; set; }
        public string OutputPath { get; set; }
        public string ExportMode { get; set; }
        public string TemplateMode { get; set; }
        public int SlideIndex { get; set; }
        public int Revision { get; set; }
        public bool IsCurrent { get; set; }
        public string OverviewImagePath { get; set; }
        public string DetailImagePath { get; set; }
        public string ThreeDImagePath { get; set; }
        public string ExtraImagePath { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string CreatedBy { get; set; }
    }

    internal static class ClarificationDeckRecordStore
    {
        private static readonly object SyncRoot = new object();
        private static bool _sqliteInitialized;

        public static string StorePath
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "YD_RevitTools", "ClarificationDeck", "clarification_records.sqlite");
            }
        }

        private static string LegacyJsonStorePath
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "YD_RevitTools", "ClarificationDeck", "clarification_records.json");
            }
        }

        public static void UpsertFromOptions(ClarificationDeckOptions options)
        {
            var now = DateTime.Now;
            var record = new ClarificationDeckRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                ProjectId = options.ProjectId,
                ProjectName = options.ProjectName,
                ProjectPath = options.ProjectPath,
                RevitVersion = options.RevitVersion,
                ItemNo = options.ItemNo,
                Title = options.Title,
                SystemName = options.SystemName,
                IssueDate = options.IssueDate,
                Description = options.Description,
                Reply = options.Reply,
                Handling = options.Handling,
                Progress = options.TrackStatus,
                OutputPath = options.OutputPath,
                ExportMode = options.ExportMode,
                TemplateMode = NormalizeTemplateMode(options.TemplateMode),
                SlideIndex = options.SlideIndex,
                Revision = options.Revision,
                IsCurrent = options.IsCurrent,
                OverviewImagePath = options.OverviewImagePath,
                DetailImagePath = options.DetailImagePath,
                ThreeDImagePath = options.ThreeDImagePath,
                ExtraImagePath = options.ExtraImagePath,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = Environment.UserName
            };

            lock (SyncRoot)
            {
                var records = LoadAllInternal();
                if (record.IsCurrent)
                {
                    foreach (var existing in records.Where(existing => SameProject(existing.ProjectId, record.ProjectId) && string.Equals(existing.ItemNo, record.ItemNo, StringComparison.OrdinalIgnoreCase)))
                    {
                        existing.IsCurrent = false;
                        existing.UpdatedAt = now;
                    }
                }

                records.Add(record);
                SaveAllInternal(records);
            }
        }

        public static ClarificationDeckRecord FindLatest(string projectId, string itemNo)
        {
            return LoadAll()
                .Where(record => SameProject(record.ProjectId, projectId) && string.Equals(record.ItemNo, itemNo, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(record => record.CreatedAt)
                .FirstOrDefault();
        }

        public static List<ClarificationDeckRecord> LoadForProject(string projectId)
        {
            return LoadAll()
                .Where(record => SameProject(record.ProjectId, projectId))
                .OrderByDescending(record => record.CreatedAt)
                .ToList();
        }

        public static int GetNextRevision(string projectId, string itemNo)
        {
            var maxRevision = LoadAll()
                .Where(record => SameProject(record.ProjectId, projectId) && string.Equals(record.ItemNo, itemNo, StringComparison.OrdinalIgnoreCase))
                .Select(record => record.Revision)
                .DefaultIfEmpty(0)
                .Max();

            return maxRevision + 1;
        }

        public static bool UpdateProgress(string recordId, string progress)
        {
            if (string.IsNullOrWhiteSpace(recordId))
                return false;

            lock (SyncRoot)
            {
                var records = LoadAllInternal();
                var record = records.FirstOrDefault(item => string.Equals(item.Id, recordId, StringComparison.OrdinalIgnoreCase));
                if (record == null)
                    return false;

                record.Progress = progress;
                record.UpdatedAt = DateTime.Now;
                SaveAllInternal(records);
                return true;
            }
        }

        public static bool UpdateRecordContent(string recordId, string description, string reply, string handling, string progress)
        {
            if (string.IsNullOrWhiteSpace(recordId))
                return false;

            lock (SyncRoot)
            {
                var records = LoadAllInternal();
                var record = records.FirstOrDefault(item => string.Equals(item.Id, recordId, StringComparison.OrdinalIgnoreCase));
                if (record == null)
                    return false;

                record.Description = description ?? string.Empty;
                record.Reply = reply ?? string.Empty;
                record.Handling = handling ?? string.Empty;
                record.Progress = progress ?? string.Empty;
                record.UpdatedAt = DateTime.Now;
                SaveAllInternal(records);
                return true;
            }
        }

        public static bool DeleteRecord(string recordId)
        {
            if (string.IsNullOrWhiteSpace(recordId))
                return false;

            lock (SyncRoot)
            {
                var records = LoadAllInternal();
                var removed = records.RemoveAll(record => string.Equals(record.Id, recordId, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                    return false;

                SaveAllInternal(records);
                return true;
            }
        }

        public static int DeleteForProject(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return 0;

            lock (SyncRoot)
            {
                var records = LoadAllInternal();
                var removed = records.RemoveAll(record => SameProject(record.ProjectId, projectId));
                if (removed == 0)
                    return 0;

                SaveAllInternal(records);
                return removed;
            }
        }

        public static string GetNextQuestionItemNo(string projectId)
        {
            var maxNo = LoadForProject(projectId)
                .Select(record => TryParseQuestionNo(record.ItemNo))
                .DefaultIfEmpty(0)
                .Max();

            return "Q" + (maxNo + 1).ToString();
        }

        public static string GetNextFormalClarificationItemNo(string projectId, string systemName)
        {
            var prefix = NormalizeSystemPrefix(systemName);
            var maxNo = LoadForProject(projectId)
                .Where(record => string.Equals(NormalizeTemplateMode(record.TemplateMode), "FormalClarification", StringComparison.OrdinalIgnoreCase))
                .Where(record => string.Equals(NormalizeSystemPrefix(record.SystemName), prefix, StringComparison.OrdinalIgnoreCase))
                .Select(record => TryParseFormalItemNo(record.ItemNo, prefix))
                .DefaultIfEmpty(0)
                .Max();

            return prefix + "-" + (maxNo + 1).ToString("000");
        }

        private static List<ClarificationDeckRecord> LoadAll()
        {
            lock (SyncRoot)
                return LoadAllInternal();
        }

        private static List<ClarificationDeckRecord> LoadAllInternal()
        {
            EnsureDatabase();

            using (var connection = OpenConnection())
            {
                connection.Open();
                if (GetRecordCount(connection) == 0)
                    MigrateLegacyJson(connection);

                var records = new List<ClarificationDeckRecord>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT Id, ProjectId, ProjectName, ProjectPath, RevitVersion, ItemNo, Title, SystemName, IssueDate,
       Description, Reply, Handling, Progress, OutputPath, ExportMode, TemplateMode, SlideIndex, Revision, IsCurrent,
       OverviewImagePath, DetailImagePath, ThreeDImagePath, ExtraImagePath, CreatedAt, UpdatedAt, CreatedBy
FROM ClarificationRecords
ORDER BY CreatedAt DESC;";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            records.Add(ReadRecord(reader));
                    }
                }

                NormalizeRecords(records);
                return records;
            }
        }

        private static void NormalizeRecords(List<ClarificationDeckRecord> records)
        {
            foreach (var record in records.Where(record => string.IsNullOrWhiteSpace(record.Id)))
                record.Id = Guid.NewGuid().ToString("N");

            foreach (var record in records)
                record.TemplateMode = NormalizeTemplateMode(record.TemplateMode);

            foreach (var record in records.Where(record => record.Revision <= 0))
                record.Revision = 1;

            foreach (var group in records.GroupBy(record => (record.ProjectId ?? string.Empty) + "|" + (record.ItemNo ?? string.Empty).ToUpperInvariant()))
            {
                if (group.Any(record => record.IsCurrent))
                    continue;

                var latest = group.OrderByDescending(record => record.CreatedAt).FirstOrDefault();
                if (latest != null)
                    latest.IsCurrent = true;
            }
        }

        private static void SaveAllInternal(List<ClarificationDeckRecord> records)
        {
            EnsureDatabase();
            NormalizeRecords(records);

            using (var connection = OpenConnection())
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    using (var delete = connection.CreateCommand())
                    {
                        delete.Transaction = transaction;
                        delete.CommandText = "DELETE FROM ClarificationRecords;";
                        delete.ExecuteNonQuery();
                    }

                    foreach (var record in records)
                        InsertRecord(connection, transaction, record);

                    transaction.Commit();
                }
            }
        }

        private static void EnsureDatabase()
        {
            var directory = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            if (!_sqliteInitialized)
            {
                SQLitePCL.Batteries_V2.Init();
                _sqliteInitialized = true;
            }

            using (var connection = OpenConnection())
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
CREATE TABLE IF NOT EXISTS ClarificationRecords (
    Id TEXT PRIMARY KEY,
    ProjectId TEXT,
    ProjectName TEXT,
    ProjectPath TEXT,
    RevitVersion TEXT,
    ItemNo TEXT,
    Title TEXT,
    SystemName TEXT,
    IssueDate TEXT,
    Description TEXT,
    Reply TEXT,
    Handling TEXT,
    Progress TEXT,
    OutputPath TEXT,
    ExportMode TEXT,
    TemplateMode TEXT,
    SlideIndex INTEGER,
    Revision INTEGER,
    IsCurrent INTEGER,
    OverviewImagePath TEXT,
    DetailImagePath TEXT,
    ThreeDImagePath TEXT,
    ExtraImagePath TEXT,
    CreatedAt TEXT,
    UpdatedAt TEXT,
    CreatedBy TEXT
);";
                    command.ExecuteNonQuery();
                }

                EnsureColumn(connection, "TemplateMode", "TEXT");

                using (var index = connection.CreateCommand())
                {
                    index.CommandText = "CREATE INDEX IF NOT EXISTS IX_ClarificationRecords_Project_Item ON ClarificationRecords(ProjectId, ItemNo);";
                    index.ExecuteNonQuery();
                }
            }
        }

        private static SqliteConnection OpenConnection()
        {
            return new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = StorePath
            }.ToString());
        }

        private static int GetRecordCount(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM ClarificationRecords;";
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        private static void EnsureColumn(SqliteConnection connection, string columnName, string columnType)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(ClarificationRecords);";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                            return;
                    }
                }
            }

            using (var alter = connection.CreateCommand())
            {
                alter.CommandText = $"ALTER TABLE ClarificationRecords ADD COLUMN {columnName} {columnType};";
                alter.ExecuteNonQuery();
            }
        }

        private static void MigrateLegacyJson(SqliteConnection connection)
        {
            if (!File.Exists(LegacyJsonStorePath))
                return;

            List<ClarificationDeckRecord> records;
            try
            {
                var json = File.ReadAllText(LegacyJsonStorePath, Encoding.UTF8);
                records = JsonConvert.DeserializeObject<List<ClarificationDeckRecord>>(json) ?? new List<ClarificationDeckRecord>();
            }
            catch
            {
                return;
            }

            NormalizeRecords(records);
            using (var transaction = connection.BeginTransaction())
            {
                foreach (var record in records)
                    InsertRecord(connection, transaction, record);

                transaction.Commit();
            }
        }

        private static ClarificationDeckRecord ReadRecord(SqliteDataReader reader)
        {
            return new ClarificationDeckRecord
            {
                Id = ReadString(reader, 0),
                ProjectId = ReadString(reader, 1),
                ProjectName = ReadString(reader, 2),
                ProjectPath = ReadString(reader, 3),
                RevitVersion = ReadString(reader, 4),
                ItemNo = ReadString(reader, 5),
                Title = ReadString(reader, 6),
                SystemName = ReadString(reader, 7),
                IssueDate = ReadDateTime(reader, 8),
                Description = ReadString(reader, 9),
                Reply = ReadString(reader, 10),
                Handling = ReadString(reader, 11),
                Progress = ReadString(reader, 12),
                OutputPath = ReadString(reader, 13),
                ExportMode = ReadString(reader, 14),
                TemplateMode = NormalizeTemplateMode(ReadString(reader, 15)),
                SlideIndex = ReadInt(reader, 16),
                Revision = ReadInt(reader, 17),
                IsCurrent = ReadInt(reader, 18) == 1,
                OverviewImagePath = ReadString(reader, 19),
                DetailImagePath = ReadString(reader, 20),
                ThreeDImagePath = ReadString(reader, 21),
                ExtraImagePath = ReadString(reader, 22),
                CreatedAt = ReadDateTime(reader, 23),
                UpdatedAt = ReadDateTime(reader, 24),
                CreatedBy = ReadString(reader, 25)
            };
        }

        private static void InsertRecord(SqliteConnection connection, SqliteTransaction transaction, ClarificationDeckRecord record)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT OR REPLACE INTO ClarificationRecords (
    Id, ProjectId, ProjectName, ProjectPath, RevitVersion, ItemNo, Title, SystemName, IssueDate,
    Description, Reply, Handling, Progress, OutputPath, ExportMode, TemplateMode, SlideIndex, Revision, IsCurrent,
    OverviewImagePath, DetailImagePath, ThreeDImagePath, ExtraImagePath, CreatedAt, UpdatedAt, CreatedBy
) VALUES (
    $Id, $ProjectId, $ProjectName, $ProjectPath, $RevitVersion, $ItemNo, $Title, $SystemName, $IssueDate,
    $Description, $Reply, $Handling, $Progress, $OutputPath, $ExportMode, $TemplateMode, $SlideIndex, $Revision, $IsCurrent,
    $OverviewImagePath, $DetailImagePath, $ThreeDImagePath, $ExtraImagePath, $CreatedAt, $UpdatedAt, $CreatedBy
);";

                AddParameter(command, "$Id", record.Id);
                AddParameter(command, "$ProjectId", record.ProjectId);
                AddParameter(command, "$ProjectName", record.ProjectName);
                AddParameter(command, "$ProjectPath", record.ProjectPath);
                AddParameter(command, "$RevitVersion", record.RevitVersion);
                AddParameter(command, "$ItemNo", record.ItemNo);
                AddParameter(command, "$Title", record.Title);
                AddParameter(command, "$SystemName", record.SystemName);
                AddParameter(command, "$IssueDate", ToStorageDate(record.IssueDate));
                AddParameter(command, "$Description", record.Description);
                AddParameter(command, "$Reply", record.Reply);
                AddParameter(command, "$Handling", record.Handling);
                AddParameter(command, "$Progress", record.Progress);
                AddParameter(command, "$OutputPath", record.OutputPath);
                AddParameter(command, "$ExportMode", record.ExportMode);
                AddParameter(command, "$TemplateMode", NormalizeTemplateMode(record.TemplateMode));
                AddParameter(command, "$SlideIndex", record.SlideIndex);
                AddParameter(command, "$Revision", record.Revision);
                AddParameter(command, "$IsCurrent", record.IsCurrent ? 1 : 0);
                AddParameter(command, "$OverviewImagePath", record.OverviewImagePath);
                AddParameter(command, "$DetailImagePath", record.DetailImagePath);
                AddParameter(command, "$ThreeDImagePath", record.ThreeDImagePath);
                AddParameter(command, "$ExtraImagePath", record.ExtraImagePath);
                AddParameter(command, "$CreatedAt", ToStorageDate(record.CreatedAt));
                AddParameter(command, "$UpdatedAt", ToStorageDate(record.UpdatedAt));
                AddParameter(command, "$CreatedBy", record.CreatedBy);
                command.ExecuteNonQuery();
            }
        }

        private static void AddParameter(SqliteCommand command, string name, object value)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        private static string ReadString(SqliteDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? string.Empty : reader.GetString(index);
        }

        private static int ReadInt(SqliteDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? 0 : reader.GetInt32(index);
        }

        private static DateTime ReadDateTime(SqliteDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
                return default(DateTime);

            return DateTime.TryParse(reader.GetString(index), out var value) ? value : default(DateTime);
        }

        private static string ToStorageDate(DateTime value)
        {
            return value == default(DateTime) ? string.Empty : value.ToString("O");
        }

        private static string NormalizeTemplateMode(string value)
        {
            if (string.Equals(value, "FormalClarification", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "正式釋疑", StringComparison.OrdinalIgnoreCase))
                return "FormalClarification";

            return "InternalReview";
        }

        private static bool SameProject(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static int TryParseQuestionNo(string itemNo)
        {
            if (string.IsNullOrWhiteSpace(itemNo))
                return 0;

            itemNo = itemNo.Trim();
            if (!itemNo.StartsWith("Q", StringComparison.OrdinalIgnoreCase))
                return 0;

            var numberText = itemNo.Substring(1);
            return int.TryParse(numberText, out var number) && number > 0 ? number : 0;
        }

        private static int TryParseFormalItemNo(string itemNo, string expectedPrefix)
        {
            if (string.IsNullOrWhiteSpace(itemNo) || string.IsNullOrWhiteSpace(expectedPrefix))
                return 0;

            var value = itemNo.Trim();
            var separatorIndex = value.LastIndexOf('-');
            if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
                return 0;

            var prefix = NormalizeSystemPrefix(value.Substring(0, separatorIndex));
            if (!string.Equals(prefix, expectedPrefix, StringComparison.OrdinalIgnoreCase))
                return 0;

            var numberText = value.Substring(separatorIndex + 1);
            return int.TryParse(numberText, out var number) && number > 0 ? number : 0;
        }

        private static string NormalizeSystemPrefix(string systemName)
        {
            var value = string.IsNullOrWhiteSpace(systemName) ? "SYS" : systemName.Trim().ToUpperInvariant();
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                    builder.Append(ch);
            }

            return builder.Length > 0 ? builder.ToString() : "SYS";
        }
    }

    internal sealed class ClarificationDeckHistoryDialog : WinForm
    {
        private readonly string _projectId;
        private readonly DataGridView _grid = new DataGridView();
        private readonly System.Windows.Forms.ComboBox _progressFilter = new System.Windows.Forms.ComboBox();
        private readonly WinTextBox _searchBox = new WinTextBox();
        private List<ClarificationDeckRecord> _records = new List<ClarificationDeckRecord>();

        public ClarificationDeckHistoryDialog(string projectId)
        {
            _projectId = projectId;
            Text = "釋疑紀錄";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(1120, 560);
            Size = new Size(1240, 620);
            Font = new Font("Microsoft JhengHei UI", 9.5F);
            BackColor = SysColor.White;

            BuildLayout();
            LoadRecords();
        }

        public ClarificationDeckRecord SelectedRecord { get; private set; }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(12),
                BackColor = SysColor.White
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

            var filters = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 4, 0, 4)
            };

            filters.Controls.Add(new Label
            {
                Text = "搜尋",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 7, 4, 0)
            });

            _searchBox.Width = 260;
            _searchBox.Margin = new Padding(0, 4, 16, 0);
            _searchBox.TextChanged += (s, e) => ApplyFilters();
            filters.Controls.Add(_searchBox);

            filters.Controls.Add(new Label
            {
                Text = "進度",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 7, 4, 0)
            });

            _progressFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _progressFilter.Width = 130;
            _progressFilter.Margin = new Padding(0, 4, 16, 0);
            _progressFilter.Items.AddRange(new object[] { "全部", "待進行", "進行中", "完成" });
            _progressFilter.SelectedIndexChanged += (s, e) => ApplyFilters();
            _progressFilter.SelectedIndex = 0;
            filters.Controls.Add(_progressFilter);

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.ReadOnly = true;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.RowHeadersVisible = false;
            _grid.CellDoubleClick += (s, e) => LoadSelectedRecord();

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 12, 8)
            };

            var close = BuildHistoryButton("關閉", 86);
            close.Click += (s, e) => DialogResult = DialogResult.Cancel;
            var load = BuildHistoryButton("載入", 86);
            load.Click += (s, e) => LoadSelectedRecord();
            var edit = BuildHistoryButton("編輯", 86);
            edit.Click += (s, e) => EditSelectedRecord();
            var openFile = BuildHistoryButton("開啟簡報", 100);
            openFile.Click += (s, e) => OpenSelectedFile();
            var openFolder = BuildHistoryButton("開啟資料夾", 110);
            openFolder.Click += (s, e) => OpenSelectedFolder();
            var exportExcel = BuildHistoryButton("匯出Excel", 104);
            exportExcel.Click += (s, e) => ExportExcel();
            var updateDeck = BuildHistoryButton("更新PPT", 104);
            updateDeck.Click += (s, e) => UpdateSelectedDeckSlide();
            var deleteRecord = BuildHistoryButton("刪除紀錄", 92);
            deleteRecord.BackColor = SysColor.FromArgb(153, 74, 59);
            deleteRecord.Click += (s, e) => DeleteSelectedRecord();
            var clearProject = BuildHistoryButton("清除本專案", 104);
            clearProject.BackColor = SysColor.FromArgb(153, 74, 59);
            clearProject.Click += (s, e) => ClearProjectRecords();
            var done = BuildHistoryButton("完成", 86);
            done.BackColor = SysColor.FromArgb(21, 158, 82);
            done.Click += (s, e) => UpdateSelectedProgress("完成");
            var inProgress = BuildHistoryButton("進行中", 92);
            inProgress.Click += (s, e) => UpdateSelectedProgress("進行中");
            var pending = BuildHistoryButton("待進行", 92);
            pending.Click += (s, e) => UpdateSelectedProgress("待進行");

            buttons.Controls.Add(close);
            buttons.Controls.Add(load);
            buttons.Controls.Add(edit);
            buttons.Controls.Add(openFile);
            buttons.Controls.Add(openFolder);
            buttons.Controls.Add(exportExcel);
            buttons.Controls.Add(updateDeck);
            buttons.Controls.Add(deleteRecord);
            buttons.Controls.Add(clearProject);
            buttons.Controls.Add(done);
            buttons.Controls.Add(inProgress);
            buttons.Controls.Add(pending);

            root.Controls.Add(filters, 0, 0);
            root.Controls.Add(_grid, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);
        }

        private void LoadRecords()
        {
            _records = ClarificationDeckRecordStore.LoadForProject(_projectId);
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var rows = GetFilteredRecords().Select(record => new HistoryRow
            {
                Id = record.Id,
                CreatedAt = record.CreatedAt.ToString("yyyy/MM/dd HH:mm"),
                ItemNo = record.ItemNo,
                Title = record.Title,
                SystemName = record.SystemName,
                TemplateName = GetTemplateDisplayName(record.TemplateMode),
                Progress = record.Progress,
                Revision = record.Revision,
                CurrentStatus = record.IsCurrent ? "有效" : "歷史",
                SlideIndex = record.SlideIndex,
                OutputPath = record.OutputPath
            }).ToList();

            _grid.DataSource = rows;

            SetHeader("CreatedAt", "產出時間");
            SetHeader("ItemNo", "項次");
            SetHeader("Title", "標題");
            SetHeader("SystemName", "系統");
            SetHeader("TemplateName", "樣板");
            SetHeader("Progress", "進度");
            SetHeader("Revision", "版本");
            SetHeader("CurrentStatus", "狀態");
            SetHeader("SlideIndex", "頁次");
            SetHeader("OutputPath", "輸出檔案");

            foreach (DataGridViewColumn column in _grid.Columns)
                column.SortMode = DataGridViewColumnSortMode.NotSortable;

            if (_grid.Columns.Contains("Id"))
                _grid.Columns["Id"].Visible = false;
        }

        private List<ClarificationDeckRecord> GetFilteredRecords()
        {
            var keyword = (_searchBox.Text ?? string.Empty).Trim();
            var progress = _progressFilter.SelectedItem as string ?? "全部";

            return _records.Where(record =>
                (progress == "全部" || string.Equals(record.Progress, progress, StringComparison.Ordinal)) &&
                (string.IsNullOrWhiteSpace(keyword) ||
                 ContainsText(record.ItemNo, keyword) ||
                 ContainsText(record.Title, keyword) ||
                 ContainsText(record.SystemName, keyword) ||
                 ContainsText(record.Description, keyword) ||
                 ContainsText(record.Reply, keyword) ||
                 ContainsText(record.Handling, keyword)))
                .ToList();
        }

        private static bool ContainsText(string value, string keyword)
        {
            return !string.IsNullOrWhiteSpace(value) && value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetTemplateDisplayName(string templateMode)
        {
            return string.Equals(templateMode, "FormalClarification", StringComparison.OrdinalIgnoreCase)
                ? "正式釋疑"
                : "內部檢查";
        }

        private void SetHeader(string columnName, string header)
        {
            if (_grid.Columns.Contains(columnName))
                _grid.Columns[columnName].HeaderText = header;
        }

        private ClarificationDeckRecord GetSelectedRecord()
        {
            var row = _grid.CurrentRow?.DataBoundItem as HistoryRow;
            if (row == null || string.IsNullOrWhiteSpace(row.Id))
                return null;

            return _records.FirstOrDefault(record => string.Equals(record.Id, row.Id, StringComparison.OrdinalIgnoreCase));
        }

        private void LoadSelectedRecord()
        {
            SelectedRecord = GetSelectedRecord();
            if (SelectedRecord == null)
            {
                MessageBox.Show("請先選取一筆紀錄。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
        }

        private void OpenSelectedFile()
        {
            var record = GetSelectedRecord();
            if (record == null)
                return;

            if (!File.Exists(record.OutputPath))
            {
                MessageBox.Show("找不到簡報檔案。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = record.OutputPath,
                UseShellExecute = true
            });
        }

        private void OpenSelectedFolder()
        {
            var record = GetSelectedRecord();
            if (record == null)
                return;

            if (File.Exists(record.OutputPath))
                Process.Start("explorer.exe", "/select,\"" + record.OutputPath + "\"");
            else
            {
                var folder = Path.GetDirectoryName(record.OutputPath);
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                    Process.Start("explorer.exe", "\"" + folder + "\"");
                else
                    MessageBox.Show("找不到輸出資料夾。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void UpdateSelectedProgress(string progress)
        {
            var record = GetSelectedRecord();
            if (record == null)
            {
                MessageBox.Show("請先選取一筆紀錄。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!ClarificationDeckRecordStore.UpdateProgress(record.Id, progress))
            {
                MessageBox.Show("更新進度失敗，請重新開啟歷史紀錄後再試。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            LoadRecords();
        }

        private void EditSelectedRecord()
        {
            var record = GetSelectedRecord();
            if (record == null)
            {
                MessageBox.Show("請先選取一筆紀錄。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new ClarificationRecordEditDialog(record))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                if (!ClarificationDeckRecordStore.UpdateRecordContent(record.Id, dialog.Description, dialog.Reply, dialog.Handling, dialog.Progress))
                {
                    MessageBox.Show("更新紀錄失敗，請重新開啟歷史紀錄後再試。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            LoadRecords();
        }

        private void DeleteSelectedRecord()
        {
            var record = GetSelectedRecord();
            if (record == null)
            {
                MessageBox.Show("請先選取一筆紀錄。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"將刪除歷史紀錄：\n{record.ItemNo}　{record.Title}\n\n此動作只會清除追蹤紀錄，不會刪除已產出的 PPT 檔案。是否繼續？",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            if (!ClarificationDeckRecordStore.DeleteRecord(record.Id))
            {
                MessageBox.Show("刪除紀錄失敗，請重新開啟歷史紀錄後再試。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            LoadRecords();
        }

        private void ClearProjectRecords()
        {
            if (_records.Count == 0)
            {
                MessageBox.Show("目前專案沒有可清除的歷史紀錄。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"將清除目前專案的 {_records.Count} 筆釋疑歷史紀錄。\n\n此動作只會清除追蹤紀錄，不會刪除已產出的 PPT 檔案，也不會影響其他專案。是否繼續？",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            var removed = ClarificationDeckRecordStore.DeleteForProject(_projectId);
            LoadRecords();
            MessageBox.Show($"已清除 {removed} 筆本專案歷史紀錄。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateSelectedDeckSlide()
        {
            var record = GetSelectedRecord();
            if (record == null)
            {
                MessageBox.Show("請先選取一筆紀錄。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (record.SlideIndex <= 0)
            {
                MessageBox.Show("此紀錄沒有有效的 PPT 頁次，無法更新指定投影片。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(record.OutputPath) || !File.Exists(record.OutputPath))
            {
                MessageBox.Show("找不到原始簡報檔案。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"將以目前紀錄內容更新：\n{Path.GetFileName(record.OutputPath)}\n第 {record.SlideIndex} 頁\n\n是否繼續？",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                ClarificationDeckBuilder.ReplaceSlide(CreateOptions(record), record.SlideIndex);
                MessageBox.Show("指定投影片已更新。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("更新指定投影片失敗：\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static ClarificationDeckOptions CreateOptions(ClarificationDeckRecord record)
        {
            return new ClarificationDeckOptions
            {
                ProjectId = record.ProjectId,
                ProjectName = record.ProjectName,
                ProjectPath = record.ProjectPath,
                RevitVersion = record.RevitVersion,
                ItemNo = record.ItemNo,
                Title = record.Title,
                SystemName = record.SystemName,
                IssueDate = record.IssueDate,
                Description = record.Description,
                Reply = record.Reply,
                Handling = record.Handling,
                TrackStatus = record.Progress,
                OverviewImagePath = record.OverviewImagePath,
                DetailImagePath = record.DetailImagePath,
                ThreeDImagePath = record.ThreeDImagePath,
                ExtraImagePath = record.ExtraImagePath,
                OutputPath = record.OutputPath,
                PageNo = record.SlideIndex > 0 ? record.SlideIndex.ToString() : string.Empty,
                AppendToProjectDeck = true,
                ExportMode = record.ExportMode,
                TemplateMode = record.TemplateMode,
                SlideIndex = record.SlideIndex,
                Revision = record.Revision,
                IsCurrent = record.IsCurrent
            };
        }

        private void ExportExcel()
        {
            var records = GetFilteredRecords();
            if (records.Count == 0)
            {
                MessageBox.Show("目前篩選條件沒有可匯出的紀錄。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "Excel Workbook (*.xlsx)|*.xlsx";
                dialog.DefaultExt = "xlsx";
                dialog.FileName = "釋疑追蹤表_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx";

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    ClarificationTrackingExcelExporter.Export(dialog.FileName, records);
                    var result = MessageBox.Show(
                        "追蹤表已匯出。\n\n是否開啟檔案？",
                        Text,
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = dialog.FileName,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("匯出 Excel 失敗：\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static Button BuildHistoryButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 34,
                Margin = new Padding(8, 0, 0, 0),
                BackColor = SysColor.FromArgb(31, 108, 159),
                ForeColor = SysColor.White,
                FlatStyle = FlatStyle.Flat
            };
        }

        private sealed class HistoryRow
        {
            public string Id { get; set; }
            public string CreatedAt { get; set; }
            public string ItemNo { get; set; }
            public string Title { get; set; }
            public string SystemName { get; set; }
            public string TemplateName { get; set; }
            public string Progress { get; set; }
            public int Revision { get; set; }
            public string CurrentStatus { get; set; }
            public int SlideIndex { get; set; }
            public string OutputPath { get; set; }
        }
    }

    internal sealed class ClarificationRecordEditDialog : WinForm
    {
        private readonly WinTextBox _description = new WinTextBox();
        private readonly WinTextBox _reply = new WinTextBox();
        private readonly WinTextBox _handling = new WinTextBox();
        private readonly System.Windows.Forms.ComboBox _progress = new System.Windows.Forms.ComboBox();

        public string Description => _description.Text.Trim();
        public string Reply => _reply.Text.Trim();
        public string Handling => _handling.Text.Trim();
        public string Progress => _progress.SelectedItem as string ?? string.Empty;

        public ClarificationRecordEditDialog(ClarificationDeckRecord record)
        {
            Text = "編輯釋疑紀錄";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(720, 560);
            Size = new Size(780, 620);
            Font = new Font("Microsoft JhengHei UI", 9.5F);
            BackColor = SysColor.White;

            _description.Text = record.Description;
            _reply.Text = record.Reply;
            _handling.Text = record.Handling;

            BuildLayout(record);
            SetProgress(record.Progress);
        }

        private void BuildLayout(ClarificationDeckRecord record)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 6,
                ColumnCount = 2,
                Padding = new Padding(16),
                BackColor = SysColor.White
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

            var summary = new Label
            {
                Text = $"{record.ItemNo}　{record.Title}",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
            };
            root.Controls.Add(summary, 0, 0);
            root.SetColumnSpan(summary, 2);

            AddMultilineEditRow(root, 1, "問題處", _description);
            AddMultilineEditRow(root, 2, "修正方式", _reply);
            AddMultilineEditRow(root, 3, "修正情形", _handling);

            root.Controls.Add(BuildEditLabel("進度"), 0, 4);
            _progress.DropDownStyle = ComboBoxStyle.DropDownList;
            _progress.Items.AddRange(new object[] { "待進行", "進行中", "完成" });
            _progress.Dock = DockStyle.Left;
            _progress.Width = 140;
            root.Controls.Add(_progress, 1, 4);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 10, 0, 0)
            };

            var save = BuildEditButton("儲存", 88);
            save.Click += (s, e) => Accept();
            var cancel = BuildEditButton("取消", 88);
            cancel.BackColor = SysColor.FromArgb(118, 128, 139);
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 5);
            root.SetColumnSpan(buttons, 2);

            Controls.Add(root);
        }

        private void Accept()
        {
            if (string.IsNullOrWhiteSpace(_description.Text))
            {
                MessageBox.Show("請填寫問題處。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _description.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
        }

        private void SetProgress(string progress)
        {
            var normalized = string.IsNullOrWhiteSpace(progress) ? "待進行" : progress.Trim();
            var index = _progress.Items.IndexOf(normalized);
            _progress.SelectedIndex = index >= 0 ? index : 0;
        }

        private static void AddMultilineEditRow(TableLayoutPanel root, int row, string label, WinTextBox box)
        {
            root.Controls.Add(BuildEditLabel(label), 0, row);
            box.Multiline = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.Dock = DockStyle.Fill;
            root.Controls.Add(box, 1, row);
        }

        private static Label BuildEditLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                Padding = new Padding(0, 6, 0, 6)
            };
        }

        private static Button BuildEditButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 34,
                Margin = new Padding(8, 0, 0, 0),
                BackColor = SysColor.FromArgb(31, 108, 159),
                ForeColor = SysColor.White,
                FlatStyle = FlatStyle.Flat
            };
        }
    }

    internal static class ClarificationTrackingExcelExporter
    {
        public static void Export(string outputPath, IList<ClarificationDeckRecord> records)
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            using (var document = SpreadsheetDocument.Create(outputPath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new S.Workbook();

                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new S.SheetData();
                worksheetPart.Worksheet = new S.Worksheet(CreateColumns(), sheetData);

                sheetData.Append(CreateRow(new[]
                {
                    "項次",
                    "標題",
                    "系統",
                    "樣板",
                    "日期",
                    "問題處",
                    "修正方式",
                    "修正情形",
                    "進度",
                    "版本",
                    "狀態",
                    "PPT頁次",
                    "PPT檔案",
                    "建立時間",
                    "更新時間",
                    "建立人"
                }));

                foreach (var record in records)
                {
                    sheetData.Append(CreateRow(new[]
                    {
                        record.ItemNo,
                        record.Title,
                        record.SystemName,
                        GetTemplateDisplayName(record.TemplateMode),
                        record.IssueDate == default(DateTime) ? string.Empty : record.IssueDate.ToString("yyyy/MM/dd"),
                        record.Description,
                        record.Reply,
                        record.Handling,
                        record.Progress,
                        record.Revision.ToString(),
                        record.IsCurrent ? "有效" : "歷史",
                        record.SlideIndex > 0 ? record.SlideIndex.ToString() : string.Empty,
                        record.OutputPath,
                        record.CreatedAt == default(DateTime) ? string.Empty : record.CreatedAt.ToString("yyyy/MM/dd HH:mm"),
                        record.UpdatedAt == default(DateTime) ? string.Empty : record.UpdatedAt.ToString("yyyy/MM/dd HH:mm"),
                        record.CreatedBy
                    }));
                }

                var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());
                sheets.Append(new S.Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = "釋疑追蹤"
                });

                workbookPart.Workbook.Save();
                worksheetPart.Worksheet.Save();
            }
        }

        private static S.Columns CreateColumns()
        {
            var widths = new[] { 10D, 28D, 10D, 12D, 12D, 38D, 38D, 38D, 12D, 8D, 10D, 10D, 46D, 18D, 18D, 14D };
            var columns = new S.Columns();
            for (var i = 0; i < widths.Length; i++)
            {
                columns.Append(new S.Column
                {
                    Min = (uint)(i + 1),
                    Max = (uint)(i + 1),
                    Width = widths[i],
                    CustomWidth = true
                });
            }

            return columns;
        }

        private static S.Row CreateRow(IEnumerable<string> values)
        {
            var row = new S.Row();
            foreach (var value in values)
                row.Append(CreateTextCell(value));
            return row;
        }

        private static S.Cell CreateTextCell(string value)
        {
            return new S.Cell
            {
                DataType = S.CellValues.InlineString,
                InlineString = new S.InlineString(new S.Text(value ?? string.Empty)
                {
                    Space = SpaceProcessingModeValues.Preserve
                })
            };
        }

        private static string GetTemplateDisplayName(string templateMode)
        {
            return string.Equals(templateMode, "FormalClarification", StringComparison.OrdinalIgnoreCase)
                ? "正式釋疑"
                : "內部檢查";
        }
    }

    internal sealed class ImageSlotCard : UserControl
    {
        private readonly WinTextBox _pathBox;
        private readonly PictureBox _preview = new PictureBox();
        private readonly Label _fileName = new Label();
        private readonly ToolTip _toolTip;

        public ImageSlotCard(
            string title,
            WinTextBox pathBox,
            Action<WinTextBox> browseImage,
            Action<WinTextBox> captureWindow,
            ToolTip toolTip)
        {
            _pathBox = pathBox;
            _toolTip = toolTip;

            Dock = DockStyle.Fill;
            Padding = new Padding(6);
            BackColor = SysColor.White;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 4,
                ColumnCount = 1
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold)
            };

            _preview.Dock = DockStyle.Fill;
            _preview.BackColor = SysColor.FromArgb(245, 247, 250);
            _preview.BorderStyle = BorderStyle.FixedSingle;
            _preview.SizeMode = PictureBoxSizeMode.Zoom;

            _fileName.Dock = DockStyle.Fill;
            _fileName.TextAlign = ContentAlignment.MiddleCenter;
            _fileName.AutoEllipsis = true;
            _fileName.ForeColor = SysColor.FromArgb(82, 91, 102);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 2, 0, 0)
            };

            var capture = BuildSmallButton("窗選");
            capture.Click += (s, e) => captureWindow(_pathBox);
            var browse = BuildSmallButton("選圖");
            browse.Click += (s, e) => browseImage(_pathBox);
            var clear = BuildSmallButton("清除");
            clear.BackColor = SysColor.FromArgb(118, 128, 139);
            clear.Click += (s, e) =>
            {
                _pathBox.Text = string.Empty;
                RefreshPreview();
            };

            buttons.Controls.Add(capture);
            buttons.Controls.Add(browse);
            buttons.Controls.Add(clear);

            root.Controls.Add(titleLabel, 0, 0);
            root.Controls.Add(_preview, 0, 1);
            root.Controls.Add(_fileName, 0, 2);
            root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);

            RefreshPreview();
        }

        public void RefreshPreview()
        {
            var oldImage = _preview.Image;
            _preview.Image = null;
            oldImage?.Dispose();

            var path = _pathBox.Text;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _fileName.Text = "尚未選取";
                _toolTip.SetToolTip(_fileName, string.Empty);
                return;
            }

            try
            {
                using (var source = System.Drawing.Image.FromFile(path))
                    _preview.Image = new Bitmap(source);

                _fileName.Text = Path.GetFileName(path);
                _toolTip.SetToolTip(_fileName, path);
                _toolTip.SetToolTip(_preview, path);
            }
            catch
            {
                _fileName.Text = "圖片讀取失敗";
                _toolTip.SetToolTip(_fileName, path);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var oldImage = _preview.Image;
                _preview.Image = null;
                oldImage?.Dispose();
            }

            base.Dispose(disposing);
        }

        private static Button BuildSmallButton(string text)
        {
            return new Button
            {
                Text = text,
                Width = 66,
                Height = 28,
                Margin = new Padding(3, 0, 3, 0),
                BackColor = SysColor.FromArgb(31, 108, 159),
                ForeColor = SysColor.White,
                FlatStyle = FlatStyle.Flat
            };
        }
    }

    internal sealed class WindowSelectionCaptureForm : WinForm
    {
        private DrawPoint _startPoint;
        private DrawRectangle _selection;
        private bool _dragging;

        public WindowSelectionCaptureForm()
        {
            Bounds = SystemInformation.VirtualScreen;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            BackColor = SysColor.Black;
            Opacity = 0.22;
        }

        public string CapturedPath { get; private set; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
                DialogResult = DialogResult.Cancel;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            _dragging = true;
            _startPoint = e.Location;
            _selection = DrawRectangle.Empty;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging)
                return;

            _selection = NormalizeRectangle(_startPoint, e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging || e.Button != MouseButtons.Left)
                return;

            _dragging = false;
            _selection = NormalizeRectangle(_startPoint, e.Location);

            if (_selection.Width < 8 || _selection.Height < 8)
            {
                DialogResult = DialogResult.Cancel;
                return;
            }

            CapturedPath = CaptureSelection(_selection);
            DialogResult = DialogResult.OK;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_selection.IsEmpty)
                return;

            using (var pen = new Pen(SysColor.FromArgb(0, 174, 239), 3))
            using (var fill = new SolidBrush(SysColor.FromArgb(60, 255, 255, 255)))
            {
                e.Graphics.FillRectangle(fill, _selection);
                e.Graphics.DrawRectangle(pen, _selection);
            }
        }

        private string CaptureSelection(DrawRectangle localSelection)
        {
            var screenSelection = new DrawRectangle(
                Bounds.Left + localSelection.Left,
                Bounds.Top + localSelection.Top,
                localSelection.Width,
                localSelection.Height);

            Hide();
            Application.DoEvents();
            System.Threading.Thread.Sleep(80);

            var folder = Path.Combine(Path.GetTempPath(), "YD_RevitTools_ClarificationDeck");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Capture_{DateTime.Now:yyyyMMdd_HHmmssfff}.png");

            using (var bitmap = new Bitmap(screenSelection.Width, screenSelection.Height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(screenSelection.Location, DrawPoint.Empty, screenSelection.Size);
                bitmap.Save(path, ImageFormat.Png);
            }

            return path;
        }

        private static DrawRectangle NormalizeRectangle(DrawPoint start, DrawPoint end)
        {
            return new DrawRectangle(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Abs(start.X - end.X),
                Math.Abs(start.Y - end.Y));
        }
    }

    internal static class ClarificationDeckBuilder
    {
        private const long EmuPerInch = 914400;

        public static int Build(ClarificationDeckOptions options)
        {
            string backupPath = null;
            try
            {
                if (File.Exists(options.OutputPath))
                {
                    EnsureWritableFile(options.OutputPath);
                    backupPath = CreateBackup(options.OutputPath);
                }

                var slideIndex = options.AppendToProjectDeck && File.Exists(options.OutputPath)
                    ? AppendSlide(options)
                    : CreateNewDeck(options);

                ValidateDeck(options.OutputPath);
                return slideIndex;
            }
            catch
            {
                RestoreBackup(backupPath, options.OutputPath);
                throw;
            }
        }

        public static void ReplaceSlide(ClarificationDeckOptions options, int slideIndex)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (slideIndex <= 0)
                throw new InvalidOperationException("投影片頁次無效。");

            if (string.IsNullOrWhiteSpace(options.OutputPath) || !File.Exists(options.OutputPath))
                throw new FileNotFoundException("找不到簡報檔案。", options.OutputPath);

            string backupPath = null;
            try
            {
                EnsureWritableFile(options.OutputPath);
                backupPath = CreateBackup(options.OutputPath);
                ReplaceSlideCore(options, slideIndex);
                ValidateDeck(options.OutputPath);
            }
            catch
            {
                RestoreBackup(backupPath, options.OutputPath);
                throw;
            }
        }

        private static int CreateNewDeck(ClarificationDeckOptions options)
        {
            if (File.Exists(options.OutputPath))
                File.Delete(options.OutputPath);

            return CreateDeck(options);
        }

        private static void EnsureWritableFile(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException("輸出簡報目前可能已被 PowerPoint 開啟，請先關閉該檔案後再產出。", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException("輸出簡報目前無法寫入，請確認檔案權限或關閉正在使用該檔案的程式。", ex);
            }
        }

        private static string CreateBackup(string outputPath)
        {
            var folder = Path.Combine(Path.GetDirectoryName(outputPath) ?? string.Empty, "_ClarificationDeckBackups");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var fileName = Path.GetFileNameWithoutExtension(outputPath);
            var backupPath = Path.Combine(folder, $"{fileName}_{DateTime.Now:yyyyMMdd_HHmmss}.bak.pptx");
            File.Copy(outputPath, backupPath, true);
            return backupPath;
        }

        private static void RestoreBackup(string backupPath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
                return;

            try
            {
                File.Copy(backupPath, outputPath, true);
            }
            catch
            {
                // Keep the original generation/validation error visible to the user.
            }
        }

        private static void ValidateDeck(string outputPath)
        {
            using (var document = PresentationDocument.Open(outputPath, false))
            {
                var relationshipErrors = ValidatePresentationRelationships(document).ToList();
                var errors = new OpenXmlValidator()
                    .Validate(document)
                    .Take(5)
                    .Select(error => error.Description)
                    .ToList();

                errors.InsertRange(0, relationshipErrors);
                if (errors.Count > 0)
                    throw new InvalidOperationException("簡報產出後驗證未通過：\n" + string.Join("\n", errors));
            }
        }

        private static IEnumerable<string> ValidatePresentationRelationships(PresentationDocument document)
        {
            var presentationPart = document.PresentationPart;
            if (presentationPart == null)
            {
                yield return "簡報缺少 PresentationPart。";
                yield break;
            }

            foreach (var slidePart in presentationPart.SlideParts)
            {
                if (slidePart.SlideLayoutPart == null)
                    yield return "投影片缺少 SlideLayout 關聯。";
            }

            foreach (var masterPart in presentationPart.SlideMasterParts)
            {
                foreach (var layoutPart in masterPart.SlideLayoutParts)
                {
                    if (layoutPart.SlideMasterPart == null)
                        yield return "SlideLayout 缺少 SlideMaster 反向關聯。";
                }
            }
        }

        private static int CreateDeck(ClarificationDeckOptions options)
        {
            using (var document = PresentationDocument.Create(options.OutputPath, PresentationDocumentType.Presentation))
            {
                var presentationPart = document.AddPresentationPart();
                presentationPart.Presentation = new P.Presentation();

                var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();
                var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
                var themePart = slideMasterPart.AddNewPart<ThemePart>();
                themePart.Theme = CreateTheme();
                themePart.Theme.Save();
                slideLayoutPart.SlideLayout = CreateSlideLayout();
                slideLayoutPart.SlideLayout.Save();
                slideMasterPart.SlideMaster = CreateSlideMaster(slideMasterPart.GetIdOfPart(slideLayoutPart));
                slideMasterPart.SlideMaster.Save();
                EnsureSlideLayoutMasterRelationship(slideLayoutPart, slideMasterPart);

                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.AddPart(slideLayoutPart);
                slidePart.Slide = CreateSlide(options, slidePart);
                slidePart.Slide.Save();

                var slideMasterIdList = new P.SlideMasterIdList();
                slideMasterIdList.Append(new P.SlideMasterId
                {
                    Id = 2147483648U,
                    RelationshipId = presentationPart.GetIdOfPart(slideMasterPart)
                });

                var slideIdList = new P.SlideIdList();
                var slideRelId = presentationPart.GetIdOfPart(slidePart);
                slideIdList.Append(new P.SlideId { Id = 256U, RelationshipId = slideRelId });

                presentationPart.Presentation.Append(slideMasterIdList);
                presentationPart.Presentation.Append(slideIdList);
                presentationPart.Presentation.Append(new P.SlideSize
                {
                    Cx = 12192000,
                    Cy = 6858000,
                    Type = P.SlideSizeValues.Screen16x9
                });
                presentationPart.Presentation.Append(new P.NotesSize
                {
                    Cx = 6858000,
                    Cy = 9144000
                });
                presentationPart.Presentation.Save();
            }

            return 1;
        }

        private static int AppendSlide(ClarificationDeckOptions options)
        {
            using (var document = PresentationDocument.Open(options.OutputPath, true))
            {
                var presentationPart = document.PresentationPart;
                if (presentationPart == null)
                    throw new InvalidOperationException("簡報檔案缺少 PresentationPart。");

                EnsurePresentationInfrastructure(presentationPart);

                var slidePart = presentationPart.AddNewPart<SlidePart>();
                var layoutPart = GetFirstSlideLayoutPart(presentationPart);
                if (layoutPart != null)
                    slidePart.AddPart(layoutPart);

                slidePart.Slide = CreateSlide(options, slidePart);
                slidePart.Slide.Save();

                var slideIdList = presentationPart.Presentation.SlideIdList;
                if (slideIdList == null)
                {
                    slideIdList = new P.SlideIdList();
                    presentationPart.Presentation.Append(slideIdList);
                }

                var nextId = slideIdList.Elements<P.SlideId>()
                    .Select(slideId => slideId.Id?.Value ?? 255U)
                    .DefaultIfEmpty(255U)
                    .Max() + 1U;

                slideIdList.Append(new P.SlideId
                {
                    Id = nextId,
                    RelationshipId = presentationPart.GetIdOfPart(slidePart)
                });

                presentationPart.Presentation.Save();
                return slideIdList.Elements<P.SlideId>().Count();
            }
        }

        private static void ReplaceSlideCore(ClarificationDeckOptions options, int slideIndex)
        {
            using (var document = PresentationDocument.Open(options.OutputPath, true))
            {
                var presentationPart = document.PresentationPart;
                if (presentationPart == null)
                    throw new InvalidOperationException("簡報檔案缺少 PresentationPart。");

                EnsurePresentationInfrastructure(presentationPart);

                var slideIdList = presentationPart.Presentation.SlideIdList;
                if (slideIdList == null)
                    throw new InvalidOperationException("簡報沒有可更新的投影片。");

                var slideId = slideIdList.Elements<P.SlideId>().ElementAtOrDefault(slideIndex - 1);
                if (slideId == null || slideId.RelationshipId == null)
                    throw new InvalidOperationException("找不到指定頁次的投影片。");

                var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId);
                if (slidePart == null)
                    throw new InvalidOperationException("找不到指定頁次的投影片內容。");

                var oldImageParts = slidePart.ImageParts.ToList();
                foreach (var imagePart in oldImageParts)
                    slidePart.DeletePart(imagePart);

                if (slidePart.SlideLayoutPart == null)
                {
                    var layoutPart = GetFirstSlideLayoutPart(presentationPart);
                    if (layoutPart != null)
                        slidePart.AddPart(layoutPart);
                }

                slidePart.Slide = CreateSlide(options, slidePart);
                slidePart.Slide.Save();
                presentationPart.Presentation.Save();
            }
        }

        private static void EnsurePresentationInfrastructure(PresentationPart presentationPart)
        {
            if (presentationPart.Presentation.SlideMasterIdList != null && presentationPart.SlideMasterParts.Any())
            {
                foreach (var masterPart in presentationPart.SlideMasterParts)
                {
                    foreach (var layoutPart in masterPart.SlideLayoutParts)
                        EnsureSlideLayoutMasterRelationship(layoutPart, masterPart);
                }

                return;
            }

            var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();
            var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
            var themePart = slideMasterPart.AddNewPart<ThemePart>();
            themePart.Theme = CreateTheme();
            themePart.Theme.Save();
            slideLayoutPart.SlideLayout = CreateSlideLayout();
            slideLayoutPart.SlideLayout.Save();
            slideMasterPart.SlideMaster = CreateSlideMaster(slideMasterPart.GetIdOfPart(slideLayoutPart));
            slideMasterPart.SlideMaster.Save();
            EnsureSlideLayoutMasterRelationship(slideLayoutPart, slideMasterPart);

            var masterRelId = presentationPart.GetIdOfPart(slideMasterPart);
            var slideMasterIdList = new P.SlideMasterIdList();
            slideMasterIdList.Append(new P.SlideMasterId
            {
                Id = 2147483648U,
                RelationshipId = masterRelId
            });

            presentationPart.Presentation.InsertAt(slideMasterIdList, 0);
        }

        private static void EnsureSlideLayoutMasterRelationship(SlideLayoutPart slideLayoutPart, SlideMasterPart slideMasterPart)
        {
            if (slideLayoutPart == null || slideMasterPart == null)
                return;

            if (!slideLayoutPart.Parts.Any(part => ReferenceEquals(part.OpenXmlPart, slideMasterPart)))
                slideLayoutPart.AddPart(slideMasterPart);
        }

        private static SlideLayoutPart GetFirstSlideLayoutPart(PresentationPart presentationPart)
        {
            foreach (var masterPart in presentationPart.SlideMasterParts)
            {
                var layout = masterPart.SlideLayoutParts.FirstOrDefault();
                if (layout != null)
                {
                    EnsureSlideLayoutMasterRelationship(layout, masterPart);
                    return layout;
                }
            }

            return presentationPart.SlideParts
                .SelectMany(slidePart => slidePart.SlideLayoutPart == null ? Enumerable.Empty<SlideLayoutPart>() : new[] { slidePart.SlideLayoutPart })
                .FirstOrDefault();
        }

        private static P.Slide CreateSlide(ClarificationDeckOptions options, SlidePart slidePart)
        {
            var tree = new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(
                    new A.TransformGroup(
                        new A.Offset { X = 0, Y = 0 },
                        new A.Extents { Cx = 0, Cy = 0 },
                        new A.ChildOffset { X = 0, Y = 0 },
                        new A.ChildExtents { Cx = 0, Cy = 0 })));

            AddTopInfo(tree, options);
            AddMiddleTable(tree, options);
            AddImageFrame(tree);
            AddImages(tree, slidePart, options);

            return new P.Slide(
                new P.CommonSlideData(tree),
                new P.ColorMapOverride(new A.MasterColorMapping()));
        }

        private static P.SlideMaster CreateSlideMaster(string slideLayoutRelId)
        {
            var tree = new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(
                    new A.TransformGroup(
                        new A.Offset { X = 0, Y = 0 },
                        new A.Extents { Cx = 0, Cy = 0 },
                        new A.ChildOffset { X = 0, Y = 0 },
                        new A.ChildExtents { Cx = 0, Cy = 0 })));

            return new P.SlideMaster(
                new P.CommonSlideData(tree),
                new P.ColorMap
                {
                    Background1 = A.ColorSchemeIndexValues.Light1,
                    Text1 = A.ColorSchemeIndexValues.Dark1,
                    Background2 = A.ColorSchemeIndexValues.Light2,
                    Text2 = A.ColorSchemeIndexValues.Dark2,
                    Accent1 = A.ColorSchemeIndexValues.Accent1,
                    Accent2 = A.ColorSchemeIndexValues.Accent2,
                    Accent3 = A.ColorSchemeIndexValues.Accent3,
                    Accent4 = A.ColorSchemeIndexValues.Accent4,
                    Accent5 = A.ColorSchemeIndexValues.Accent5,
                    Accent6 = A.ColorSchemeIndexValues.Accent6,
                    Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                    FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
                },
                new P.SlideLayoutIdList(
                    new P.SlideLayoutId
                    {
                        Id = 2147483649U,
                        RelationshipId = slideLayoutRelId
                    }),
                new P.TextStyles(
                    new P.TitleStyle(),
                    new P.BodyStyle(),
                    new P.OtherStyle()));
        }

        private static P.SlideLayout CreateSlideLayout()
        {
            var tree = new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(
                    new A.TransformGroup(
                        new A.Offset { X = 0, Y = 0 },
                        new A.Extents { Cx = 0, Cy = 0 },
                        new A.ChildOffset { X = 0, Y = 0 },
                        new A.ChildExtents { Cx = 0, Cy = 0 })));

            return new P.SlideLayout(
                new P.CommonSlideData(tree) { Name = "Blank" },
                new P.ColorMapOverride(new A.MasterColorMapping()))
            {
                Type = P.SlideLayoutValues.Blank,
                Preserve = true
            };
        }

        private static A.Theme CreateTheme()
        {
            return new A.Theme(
                new A.ThemeElements(
                    new A.ColorScheme(
                        new A.Dark1Color(new A.SystemColor { Val = A.SystemColorValues.WindowText, LastColor = "000000" }),
                        new A.Light1Color(new A.SystemColor { Val = A.SystemColorValues.Window, LastColor = "FFFFFF" }),
                        new A.Dark2Color(new A.RgbColorModelHex { Val = "1F2933" }),
                        new A.Light2Color(new A.RgbColorModelHex { Val = "F3F5F7" }),
                        new A.Accent1Color(new A.RgbColorModelHex { Val = "1F6C9F" }),
                        new A.Accent2Color(new A.RgbColorModelHex { Val = "17A9C6" }),
                        new A.Accent3Color(new A.RgbColorModelHex { Val = "22A65A" }),
                        new A.Accent4Color(new A.RgbColorModelHex { Val = "D90F0F" }),
                        new A.Accent5Color(new A.RgbColorModelHex { Val = "F59E0B" }),
                        new A.Accent6Color(new A.RgbColorModelHex { Val = "6B7280" }),
                        new A.Hyperlink(new A.RgbColorModelHex { Val = "0563C1" }),
                        new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "954F72" }))
                    { Name = "HB_BIM" },
                    new A.FontScheme(
                        new A.MajorFont(
                            new A.LatinFont { Typeface = "Microsoft JhengHei UI" },
                            new A.EastAsianFont { Typeface = "Microsoft JhengHei UI" },
                            new A.ComplexScriptFont { Typeface = "Microsoft JhengHei UI" }),
                        new A.MinorFont(
                            new A.LatinFont { Typeface = "Microsoft JhengHei UI" },
                            new A.EastAsianFont { Typeface = "Microsoft JhengHei UI" },
                            new A.ComplexScriptFont { Typeface = "Microsoft JhengHei UI" }))
                    { Name = "HB_BIM" },
                    new A.FormatScheme(
                        new A.FillStyleList(
                            new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                            new A.GradientFill(
                                new A.GradientStopList(
                                    new A.GradientStop(new A.SchemeColor(new A.Tint { Val = 50000 }, new A.SaturationModulation { Val = 300000 }) { Val = A.SchemeColorValues.PhColor }) { Position = 0 },
                                    new A.GradientStop(new A.SchemeColor(new A.Tint { Val = 37000 }, new A.SaturationModulation { Val = 300000 }) { Val = A.SchemeColorValues.PhColor }) { Position = 35000 },
                                    new A.GradientStop(new A.SchemeColor(new A.Tint { Val = 15000 }, new A.SaturationModulation { Val = 350000 }) { Val = A.SchemeColorValues.PhColor }) { Position = 100000 }),
                                new A.LinearGradientFill { Angle = 16200000, Scaled = true }),
                            new A.GradientFill(
                                new A.GradientStopList(
                                    new A.GradientStop(new A.SchemeColor(new A.Shade { Val = 51000 }, new A.SaturationModulation { Val = 130000 }) { Val = A.SchemeColorValues.PhColor }) { Position = 0 },
                                    new A.GradientStop(new A.SchemeColor(new A.Shade { Val = 93000 }, new A.SaturationModulation { Val = 130000 }) { Val = A.SchemeColorValues.PhColor }) { Position = 100000 }),
                                new A.LinearGradientFill { Angle = 16200000, Scaled = false })),
                        new A.LineStyleList(
                            new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 9525 },
                            new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 25400 },
                            new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 38100 }),
                        new A.EffectStyleList(
                            new A.EffectStyle(new A.EffectList()),
                            new A.EffectStyle(new A.EffectList()),
                            new A.EffectStyle(new A.EffectList())),
                        new A.BackgroundFillStyleList(
                            new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                            new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                            new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })))
                    { Name = "HB_BIM" }))
            { Name = "HB_BIM" };
        }

        private static void AddTopInfo(P.ShapeTree tree, ClarificationDeckOptions o)
        {
            AddTable(
                tree,
                "釋疑資訊表",
                339717,
                311953,
                new long[] { 543724, 1195449, 546265, 4655128, 546264, 2295897, 546265, 1183574 },
                new long[] { 274320 },
                new[]
                {
                    new[]
                    {
                        "項次", o.ItemNo,
                        "標題", o.Title,
                        "系統", o.SystemName,
                        "日期", o.IssueDate.ToString("yyyy/M/d")
                    }
                },
                new[] { true, false, true, true, true, false, true, false });

        }

        private static bool IsFormalTemplate(ClarificationDeckOptions options)
        {
            return string.Equals(options?.TemplateMode, "FormalClarification", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddMiddleTable(P.ShapeTree tree, ClarificationDeckOptions o)
        {
            var headers = IsFormalTemplate(o)
                ? new[] { "釋疑說明", "建議處理/回覆", "辦理情形", "備註" }
                : new[] { "問題處", "修正方式", "修正情形", "進度" };

            var statusOrNote = o.TrackStatus;

            AddTable(
                tree,
                "釋疑內容表",
                339717,
                771340,
                new long[] { 4157850, 2777524, 3396867, 1175658 },
                new long[] { 244416, 912364 },
                new[]
                {
                    headers,
                    new[] { o.Description, o.Reply, o.Handling, statusOrNote }
                },
                new[] { false, false, false, false });
        }

        private static void AddImageFrame(P.ShapeTree tree)
        {
            AddTable(
                tree,
                "圖片區",
                339717,
                2149434,
                new long[] { 11512566 },
                new long[] { 4191989 },
                new[] { new[] { "" } },
                new[] { false });
        }

        private static void AddImages(P.ShapeTree tree, SlidePart slidePart, ClarificationDeckOptions o)
        {
            var imagePaths = new[]
            {
                o.OverviewImagePath,
                o.DetailImagePath,
                o.ThreeDImagePath,
                o.ExtraImagePath
            }.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).ToList();

            if (imagePaths.Count == 0)
            {
                AddText(tree, 3225941, 3737945, 5740674, 1015663, "圖片區\n( 最多 4 張 )", 18, "000000", false, A.TextAnchoringTypeValues.Center);
                return;
            }

            var slots = CreateImageSlots(imagePaths);

            foreach (var slot in slots.Where(slot => !string.IsNullOrWhiteSpace(slot.Path)))
                AddPictureOrPlaceholder(tree, slidePart, slot, "");
        }

        private static ImageSlot[] CreateImageSlots(IList<string> imagePaths)
        {
            const long tableImageX = 339717;
            const long tableImageY = 2149434;
            const long tableImageW = 11512566;
            const long tableImageH = 4191989;
            const long margin = 160000;
            const long gap = 140000;

            var count = Math.Min(4, imagePaths.Count);
            var columns = count == 1 ? 1 : count == 4 ? 2 : count;
            var rows = count == 4 ? 2 : 1;
            var areaX = tableImageX + margin;
            var areaY = tableImageY + margin;
            var areaW = tableImageW - margin * 2;
            var areaH = tableImageH - margin * 2;
            var cellW = (areaW - gap * (columns - 1)) / columns;
            var cellH = (areaH - gap * (rows - 1)) / rows;
            var slots = new ImageSlot[count];

            for (var i = 0; i < count; i++)
            {
                var col = i % columns;
                var row = i / columns;
                slots[i] = new ImageSlot(
                    areaX + col * (cellW + gap),
                    areaY + row * (cellH + gap),
                    cellW,
                    cellH,
                    imagePaths[i]);
            }

            return slots;
        }

        private static void AddFooter(P.ShapeTree tree, ClarificationDeckOptions options)
        {
            AddFilledRoundRect(tree, Inch(12.15), Inch(6.46), Inch(1.18), Inch(0.64), "17A9C6");
            AddText(tree, Inch(12.68), Inch(6.61), Inch(0.40), Inch(0.28), string.IsNullOrWhiteSpace(options.PageNo) ? "1" : options.PageNo, 18, "000000", false, A.TextAnchoringTypeValues.Center);
        }

        private static void AddPictureOrPlaceholder(P.ShapeTree tree, SlidePart slidePart, ImageSlot slot, string label)
        {
            if (!string.IsNullOrWhiteSpace(slot.Path) && File.Exists(slot.Path))
            {
                var relId = AddImagePart(slidePart, slot.Path);
                tree.Append(CreatePicture(relId, slot));
                return;
            }

            AddRect(tree, slot.X, slot.Y, slot.W, slot.H, "F3F5F7", "A0A7B0", 1);
            AddText(tree, slot.X, slot.Y + slot.H / 2 - Inch(0.12), slot.W, Inch(0.24), label, 14, "6B7280", false, A.TextAnchoringTypeValues.Center);
        }

        private static void AddTable(
            P.ShapeTree tree,
            string name,
            long x,
            long y,
            long[] columnWidths,
            long[] rowHeights,
            string[][] values,
            bool[] boldColumns)
        {
            long width = columnWidths.Sum();
            long height = rowHeights.Sum();

            var table = new A.Table();
            table.Append(new A.TableProperties { FirstRow = true, BandRow = false });

            var grid = new A.TableGrid();
            foreach (long columnWidth in columnWidths)
                grid.Append(new A.GridColumn { Width = columnWidth });
            table.Append(grid);

            for (int row = 0; row < rowHeights.Length; row++)
            {
                var tr = new A.TableRow { Height = rowHeights[row] };
                for (int col = 0; col < columnWidths.Length; col++)
                {
                    string text = row < values.Length && col < values[row].Length
                        ? values[row][col]
                        : string.Empty;
                    bool bold = row == 0 || (boldColumns != null && col < boldColumns.Length && boldColumns[col]);
                    var anchor = row == 1 ? A.TextAnchoringTypeValues.Top : A.TextAnchoringTypeValues.Center;
                    tr.Append(CreateTableCell(text, bold, row == 0 ? 12 : 11, anchor));
                }
                table.Append(tr);
            }

            var graphicFrame = new P.GraphicFrame(
                new P.NonVisualGraphicFrameProperties(
                    new P.NonVisualDrawingProperties { Id = NextId(), Name = name },
                    new P.NonVisualGraphicFrameDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.Transform(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = width, Cy = height }),
                new A.Graphic(
                    new A.GraphicData(table)
                    {
                        Uri = "http://schemas.openxmlformats.org/drawingml/2006/table"
                    }));

            tree.Append(graphicFrame);
        }

        private static A.TableCell CreateTableCell(string text, bool bold, int fontSize, A.TextAnchoringTypeValues anchor)
        {
            var body = new A.TextBody(
                new A.BodyProperties
                {
                    Anchor = anchor,
                    Wrap = A.TextWrappingValues.Square,
                    LeftInset = 45720,
                    RightInset = 45720,
                    TopInset = 22860,
                    BottomInset = 22860
                },
                new A.ListStyle());
            body.Append(BuildParagraphs(text, fontSize, "000000", bold));

            var props = new A.TableCellProperties();
            props.Append(CreateTableBorder<A.LeftBorderLineProperties>());
            props.Append(CreateTableBorder<A.RightBorderLineProperties>());
            props.Append(CreateTableBorder<A.TopBorderLineProperties>());
            props.Append(CreateTableBorder<A.BottomBorderLineProperties>());

            return new A.TableCell(body, props);
        }

        private static T CreateTableBorder<T>() where T : OpenXmlCompositeElement, new()
        {
            var border = new T();
            border.Append(new A.SolidFill(new A.RgbColorModelHex { Val = "000000" }));
            border.SetAttribute(new OpenXmlAttribute("", "w", "", "12700"));
            return border;
        }

        private static string AddImagePart(SlidePart slidePart, string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var type = ext == ".jpg" || ext == ".jpeg" ? ImagePartType.Jpeg : ImagePartType.Png;
            var imagePart = slidePart.AddImagePart(type);
            using (var stream = File.OpenRead(path))
                imagePart.FeedData(stream);
            return slidePart.GetIdOfPart(imagePart);
        }

        private static P.Picture CreatePicture(string relId, ImageSlot slot)
        {
            var fittedSlot = FitImageToSlot(slot);

            return new P.Picture(
                new P.NonVisualPictureProperties(
                    new P.NonVisualDrawingProperties { Id = NextId(), Name = "Picture" },
                    new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.BlipFill(
                    new A.Blip { Embed = relId },
                    new A.Stretch(new A.FillRectangle())),
                new P.ShapeProperties(
                    new A.Transform2D(
                        new A.Offset { X = fittedSlot.X, Y = fittedSlot.Y },
                        new A.Extents { Cx = fittedSlot.W, Cy = fittedSlot.H }),
                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
        }

        private static ImageSlot FitImageToSlot(ImageSlot slot)
        {
            try
            {
                using (var image = System.Drawing.Image.FromFile(slot.Path))
                {
                    if (image.Width <= 0 || image.Height <= 0)
                        return slot;

                    var imageRatio = (double)image.Width / image.Height;
                    var slotRatio = (double)slot.W / slot.H;

                    long width;
                    long height;

                    if (imageRatio >= slotRatio)
                    {
                        width = slot.W;
                        height = (long)(slot.W / imageRatio);
                    }
                    else
                    {
                        height = slot.H;
                        width = (long)(slot.H * imageRatio);
                    }

                    return new ImageSlot(
                        slot.X + (slot.W - width) / 2,
                        slot.Y + (slot.H - height) / 2,
                        Math.Max(1, width),
                        Math.Max(1, height),
                        slot.Path);
                }
            }
            catch
            {
                return slot;
            }
        }

        private static void AddCell(P.ShapeTree tree, long x, long y, long w, long h, string text, bool bold, int fontSize, A.TextAnchoringTypeValues anchor = A.TextAnchoringTypeValues.Center)
        {
            AddRect(tree, x, y, w, h, "FFFFFF", "000000", 1);
            AddText(tree, x + Inch(0.04), y + Inch(0.02), w - Inch(0.08), h - Inch(0.04), text, fontSize, "000000", bold, anchor);
        }

        private static void AddRect(P.ShapeTree tree, long x, long y, long w, long h, string fill, string line, int lineWidth)
        {
            tree.Append(CreateShape(A.ShapeTypeValues.Rectangle, x, y, w, h, fill, line, lineWidth));
        }

        private static void AddRectangle(P.ShapeTree tree, long x, long y, long w, long h, string line, int lineWidth)
        {
            tree.Append(CreateShape(A.ShapeTypeValues.Rectangle, x, y, w, h, null, line, lineWidth));
        }

        private static void AddOval(P.ShapeTree tree, long x, long y, long w, long h, string line, int lineWidth)
        {
            tree.Append(CreateShape(A.ShapeTypeValues.Ellipse, x, y, w, h, null, line, lineWidth));
        }

        private static void AddFilledRoundRect(P.ShapeTree tree, long x, long y, long w, long h, string fill)
        {
            tree.Append(CreateShape(A.ShapeTypeValues.Rectangle, x, y, w, h, fill, fill, 1));
        }

        private static void AddFilledLabel(P.ShapeTree tree, long x, long y, long w, long h, string text, string fill)
        {
            tree.Append(CreateShape(A.ShapeTypeValues.Rectangle, x, y, w, h, fill, fill, 1));
            AddText(tree, x + Inch(0.05), y + Inch(0.02), w - Inch(0.10), h - Inch(0.04), text, 17, "FFFFFF", true, A.TextAnchoringTypeValues.Center);
        }

        private static P.Shape CreateShape(A.ShapeTypeValues preset, long x, long y, long w, long h, string fill, string line, int lineWidth)
        {
            var properties = new P.ShapeProperties(
                new A.Transform2D(new A.Offset { X = x, Y = y }, new A.Extents { Cx = w, Cy = h }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = preset });

            properties.Append(string.IsNullOrEmpty(fill)
                ? (OpenXmlElement)new A.NoFill()
                : new A.SolidFill(new A.RgbColorModelHex { Val = fill }));

            properties.Append(string.IsNullOrEmpty(line)
                ? (OpenXmlElement)new A.Outline(new A.NoFill())
                : new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = line })) { Width = lineWidth * 12700 });

            return new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = NextId(), Name = "Shape" },
                    new P.NonVisualShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                properties,
                new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph()));
        }

        private static void AddLine(P.ShapeTree tree, long x1, long y1, long x2, long y2, string color, int lineWidth, bool arrow)
        {
            var outline = new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = color })) { Width = lineWidth * 12700 };
            if (arrow)
                outline.Append(new A.TailEnd { Type = A.LineEndValues.Triangle });

            tree.Append(new P.ConnectionShape(
                new P.NonVisualConnectionShapeProperties(
                    new P.NonVisualDrawingProperties { Id = NextId(), Name = "Line" },
                    new P.NonVisualConnectorShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.ShapeProperties(
                    new A.Transform2D(
                        new A.Offset { X = Math.Min(x1, x2), Y = Math.Min(y1, y2) },
                        new A.Extents { Cx = Math.Abs(x2 - x1), Cy = Math.Abs(y2 - y1) }),
                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Line },
                    outline)));
        }

        private static void AddText(P.ShapeTree tree, long x, long y, long w, long h, string text, int fontSize, string color, bool bold, A.TextAnchoringTypeValues anchor)
        {
            var textBody = new P.TextBody(
                new A.BodyProperties { Anchor = anchor, Wrap = A.TextWrappingValues.Square },
                new A.ListStyle());
            textBody.Append(BuildParagraphs(text, fontSize, color, bold));

            tree.Append(new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = NextId(), Name = "Text" },
                    new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.ShapeProperties(
                    new A.Transform2D(new A.Offset { X = x, Y = y }, new A.Extents { Cx = w, Cy = h }),
                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                    new A.NoFill(),
                    new A.Outline(new A.NoFill())),
                textBody));
        }

        private static OpenXmlElement[] BuildParagraphs(string text, int fontSize, string color, bool bold)
        {
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0)
                lines = new[] { string.Empty };

            return lines.Select(line =>
            {
                var runProperties = new A.RunProperties
                {
                    Language = "zh-TW",
                    FontSize = fontSize * 100,
                    Bold = bold
                };
                runProperties.Append(new A.SolidFill(new A.RgbColorModelHex { Val = color }));

                return (OpenXmlElement)new A.Paragraph(
                    new A.Run(runProperties, new A.Text(line)),
                    new A.EndParagraphRunProperties { Language = "zh-TW" });
            }).ToArray();
        }

        private static long Inch(double value)
        {
            return (long)(value * EmuPerInch);
        }

        private static uint _shapeId = 10;
        private static uint NextId()
        {
            return _shapeId++;
        }

        private sealed class ImageSlot
        {
            public ImageSlot(long x, long y, long w, long h, string path)
            {
                X = x;
                Y = y;
                W = w;
                H = h;
                Path = path;
            }

            public long X { get; }
            public long Y { get; }
            public long W { get; }
            public long H { get; }
            public string Path { get; }
        }
    }
}
