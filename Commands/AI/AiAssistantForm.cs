using Newtonsoft.Json;
using System.Collections.Generic;
using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YD_RevitTools.LicenseManager.Commands.AI
{
    internal class AiAssistantForm : Form
    {
        private readonly string _modelSummary;
        private readonly Autodesk.Revit.UI.ExternalCommandData _commandData;
        private readonly AiLocalClient _client = new AiLocalClient();
        private readonly AiMcpClient _mcpClient = new AiMcpClient();
        private readonly AiAgentToolRegistry _toolRegistry;
        private readonly Panel _headerPanel = new Panel();
        private readonly Panel _settingsCard = new Panel();
        private readonly ComboBox _providerBox = new ComboBox();
        private readonly TextBox _endpointBox = new TextBox();
        private readonly TextBox _tokenBox = new TextBox();
        private readonly TextBox _modelBox = new TextBox();
        private readonly CheckBox _includeSummaryBox = new CheckBox();
        private readonly CheckBox _agentModeBox = new CheckBox();
        private readonly CheckBox _enableMcpBox = new CheckBox();
        private readonly TextBox _mcpEndpointBox = new TextBox();
        private readonly TextBox _mcpTokenBox = new TextBox();
        private readonly TextBox _promptBox = new TextBox();
        private readonly TextBox _responseBox = new TextBox();
        private readonly Button _askButton = new Button();
        private readonly Button _testButton = new Button();
        private readonly Button _mcpTestButton = new Button();
        private readonly Button _mcpListButton = new Button();
        private readonly Button _saveButton = new Button();
        private readonly Button _clearButton = new Button();
        private readonly Label _statusLabel = new Label();
        private CancellationTokenSource _cts;

        private static readonly Color BackColorMain = Color.FromArgb(13, 20, 34);
        private static readonly Color CardColor = Color.FromArgb(18, 27, 45);
        private static readonly Color InputColor = Color.FromArgb(7, 12, 28);
        private static readonly Color BorderColor = Color.FromArgb(39, 54, 82);
        private static readonly Color TextColorMain = Color.FromArgb(230, 235, 245);
        private static readonly Color TextColorMuted = Color.FromArgb(150, 163, 184);
        private static readonly Color AccentColor = Color.FromArgb(16, 185, 129);
        private static readonly Color ButtonColor = Color.FromArgb(6, 95, 70);

        public AiAssistantForm(Autodesk.Revit.UI.ExternalCommandData commandData, string modelSummary)
        {
            _commandData = commandData;
            _modelSummary = modelSummary ?? string.Empty;
            _toolRegistry = new AiAgentToolRegistry(commandData);
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            Text = "HB_BIM Tools - AI 助理";
            Width = 1140;
            Height = 820;
            MinimumSize = new Size(1040, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BackColorMain;
            ForeColor = TextColorMain;
            AutoScaleMode = AutoScaleMode.Dpi;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(18),
                BackColor = BackColorMain
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            BuildHeader();

            var workspace = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = BackColorMain
            };
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 410));
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            workspace.Controls.Add(BuildLeftPanel(), 0, 0);
            workspace.Controls.Add(BuildRightPanel(), 1, 0);

            root.Controls.Add(_headerPanel, 0, 0);
            root.Controls.Add(workspace, 0, 1);
            Controls.Add(root);
        }

        private Control BuildLeftPanel()
        {
            var left = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColorMain,
                Padding = new Padding(0, 0, 10, 0),
                AutoScroll = true
            };

            BuildSettingsCard();
            left.Controls.Add(_settingsCard);
            return left;
        }

        private Control BuildRightPanel()
        {
            ConfigureTextAreas();

            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = BackColorMain,
                Padding = new Padding(0)
            };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            StylePrimaryButton(_askButton, "送出");
            _askButton.Width = 120;
            _askButton.Click += async (s, e) => await AskAsync(false);

            StyleSecondaryButton(_clearButton, "清空回應");
            _clearButton.Width = 110;
            _clearButton.Click += (s, e) => _responseBox.Clear();

            _statusLabel.AutoSize = true;
            _statusLabel.ForeColor = TextColorMuted;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Padding = new Padding(0, 8, 16, 0);
            _statusLabel.Text = "就緒";

            var actionPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = false,
                BackColor = BackColorMain,
                Padding = new Padding(0, 8, 0, 0)
            };

            actionPanel.Controls.Add(_askButton);
            actionPanel.Controls.Add(_clearButton);
            actionPanel.Controls.Add(_statusLabel);

            right.Controls.Add(CreateGroup("Agent 指令", _promptBox), 0, 0);
            right.Controls.Add(CreateGroup("執行結果 / 回應", _responseBox), 0, 1);
            right.Controls.Add(actionPanel, 0, 2);
            return right;
        }

        private void ConfigureTextAreas()
        {
            _promptBox.Multiline = true;
            _promptBox.ScrollBars = ScrollBars.Vertical;
            _promptBox.Dock = DockStyle.Fill;
            _promptBox.BorderStyle = BorderStyle.FixedSingle;
            _promptBox.BackColor = InputColor;
            _promptBox.ForeColor = TextColorMain;
            _promptBox.Font = new Font("Microsoft JhengHei UI", 10);
            _promptBox.Text = "請分析目前模型狀態，列出最值得先檢查的 BIM / MEP / COBie 問題，並建議可使用哪些 HB_BIM Tools。";

            _responseBox.Multiline = true;
            _responseBox.ScrollBars = ScrollBars.Vertical;
            _responseBox.Dock = DockStyle.Fill;
            _responseBox.ReadOnly = true;
            _responseBox.BorderStyle = BorderStyle.FixedSingle;
            _responseBox.BackColor = InputColor;
            _responseBox.ForeColor = TextColorMain;
            _responseBox.Font = new Font("Microsoft JhengHei UI", 10);
        }

        private void BuildHeader()
        {
            _headerPanel.Dock = DockStyle.Top;
            _headerPanel.Height = 78;
            _headerPanel.BackColor = BackColorMain;

            var title = new Label
            {
                Text = "本地 AI 助理",
                ForeColor = TextColorMain,
                Font = new Font("Microsoft JhengHei UI", 15, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(0, 4)
            };

            var subtitle = new Label
            {
                Text = "連接內網或本機部署模型，協助檢查模型資料與整理工具建議",
                ForeColor = TextColorMuted,
                Font = new Font("Microsoft JhengHei UI", 9),
                AutoSize = true,
                Location = new Point(1, 42)
            };

            _headerPanel.Controls.Add(title);
            _headerPanel.Controls.Add(subtitle);
        }

        private void BuildSettingsCard()
        {
            _settingsCard.Dock = DockStyle.Top;
            _settingsCard.Height = 690;
            _settingsCard.Padding = new Padding(16);
            _settingsCard.BackColor = CardColor;
            _settingsCard.Paint += PaintPanelBorder;

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 12,
                BackColor = CardColor
            };
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _providerBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _providerBox.Items.Add("vLLM / OpenAI API");
            _providerBox.SelectedIndex = 0;
            StyleComboBox(_providerBox);

            StyleInput(_endpointBox);
            StyleInput(_tokenBox);
            _tokenBox.UseSystemPasswordChar = true;
            StyleInput(_modelBox);
            StyleInput(_mcpEndpointBox);
            StyleInput(_mcpTokenBox);
            _mcpTokenBox.UseSystemPasswordChar = true;

            _includeSummaryBox.Text = "包含模型摘要";
            _includeSummaryBox.AutoSize = true;
            _includeSummaryBox.ForeColor = TextColorMain;
            _includeSummaryBox.BackColor = CardColor;
            _includeSummaryBox.Checked = true;

            _agentModeBox.Text = "Agent 模式";
            _agentModeBox.AutoSize = true;
            _agentModeBox.ForeColor = TextColorMain;
            _agentModeBox.BackColor = CardColor;
            _agentModeBox.Checked = true;

            _enableMcpBox.Text = "啟用外部 MCP";
            _enableMcpBox.AutoSize = true;
            _enableMcpBox.ForeColor = TextColorMain;
            _enableMcpBox.BackColor = CardColor;

            StyleSecondaryButton(_saveButton, "儲存設定");
            _saveButton.Width = 128;
            _saveButton.Click += (s, e) => SaveSettingsWithMessage();

            StyleSecondaryButton(_testButton, "測試連線");
            _testButton.Width = 128;
            _testButton.Click += async (s, e) => await AskAsync(true);

            StyleSecondaryButton(_mcpTestButton, "測試 MCP");
            _mcpTestButton.Width = 128;
            _mcpTestButton.Click += async (s, e) => await TestMcpAsync(false);

            StyleSecondaryButton(_mcpListButton, "工具清單");
            _mcpListButton.Width = 128;
            _mcpListButton.Click += async (s, e) => await TestMcpAsync(true);

            var endpointRow = CreateRow("API 網址 (Base URL)", _endpointBox, "請填 Base URL，例如 http://192.168.0.86:8000；若已包含 /v1 也可接受。");
            var tokenRow = CreateRow("API 金鑰 (Authorization Token)", _tokenBox, "選填。若本機服務需要授權，會以 Bearer Token 送出。");
            var modelRow = CreateRow("模型 ID (Model Name)", _modelBox, "例如 deepseek-qwen、Qwen/Qwen2.5-VL-7B-Instruct。");
            var mcpEndpointRow = CreateRow("MCP Endpoint", _mcpEndpointBox, "例如 http://127.0.0.1:3333/mcp。");
            var mcpTokenRow = CreateRow("MCP Token", _mcpTokenBox, "選填。若 MCP server 需要授權，會以 Bearer Token 送出。");

            var optionsRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                BackColor = CardColor,
                Padding = new Padding(0, 6, 0, 4)
            };
            optionsRow.Controls.Add(_includeSummaryBox);
            optionsRow.Controls.Add(_agentModeBox);

            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = CardColor,
                Padding = new Padding(0, 4, 0, 4)
            };
            buttonRow.Controls.Add(_testButton);
            buttonRow.Controls.Add(_saveButton);

            var mcpOptionsRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                BackColor = CardColor,
                Padding = new Padding(0, 6, 0, 4)
            };
            mcpOptionsRow.Controls.Add(_enableMcpBox);

            var mcpButtonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = CardColor,
                Padding = new Padding(0, 4, 0, 4)
            };
            mcpButtonRow.Controls.Add(_mcpTestButton);
            mcpButtonRow.Controls.Add(_mcpListButton);

            var title = CreateSectionTitle("本地模型設定");
            var mcpTitle = CreateSectionTitle("外部 MCP");
            var helpBox = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = TextColorMuted,
                BackColor = InputColor,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(12, 10, 12, 10),
                Font = new Font("Microsoft JhengHei UI", 9),
                Text =
                    "連線說明\r\n" +
                    "LLM 與 MCP 可分開設定。外部 MCP 工具會以 mcp. 前綴加入 Agent schema。\r\n\r\n" +
                    "建議 MCP server 僅綁定 127.0.0.1 或內網位址，並使用 Token。"
            };

            grid.Controls.Add(title, 0, 0);
            grid.Controls.Add(endpointRow, 0, 1);
            grid.Controls.Add(tokenRow, 0, 2);
            grid.Controls.Add(modelRow, 0, 3);
            grid.Controls.Add(optionsRow, 0, 4);
            grid.Controls.Add(buttonRow, 0, 5);
            grid.Controls.Add(mcpTitle, 0, 6);
            grid.Controls.Add(mcpOptionsRow, 0, 7);
            grid.Controls.Add(mcpEndpointRow, 0, 8);
            grid.Controls.Add(mcpTokenRow, 0, 9);
            grid.Controls.Add(mcpButtonRow, 0, 10);
            grid.Controls.Add(helpBox, 0, 11);
            _settingsCard.Controls.Add(grid);
        }

        private static Label CreateSectionTitle(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = TextColorMain,
                BackColor = CardColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft JhengHei UI", 11, FontStyle.Bold)
            };
        }

        private static Control CreateInfoCard()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CardColor,
                Padding = new Padding(14),
                Margin = new Padding(0, 10, 0, 0)
            };
            panel.Paint += PaintPanelBorder;

            var label = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = TextColorMuted,
                BackColor = CardColor,
                Font = new Font("Microsoft JhengHei UI", 9),
                Text =
                    "使用狀態\r\n" +
                    "建議先測試連線，再送出 Agent 指令。\r\n" +
                    "回應只作為檢查與操作建議，模型變更仍由使用者執行。"
            };
            panel.Controls.Add(label);
            return panel;
        }

        private async Task AskAsync(bool testOnly)
        {
            try
            {
                ToggleBusy(true, testOnly ? "正在測試本機 AI..." : "正在等待本機 AI 回應...");
                SaveSettings();

                _cts?.Dispose();
                _cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

                string response;
                if (testOnly)
                {
                    response = await _client.AskAsync(
                        ReadSettingsFromForm(),
                        "請使用繁體中文回答。",
                        "請用一句話回覆：本機 AI 連線正常。",
                        _cts.Token);
                }
                else if (_agentModeBox.Checked)
                {
                    response = await RunAgentAsync(_cts.Token);
                }
                else
                {
                    string systemPrompt =
                        "你是 HB_BIM Tools 的本機 BIM AI 助理。請使用繁體中文回答，回答需務實、簡潔，" +
                        "只能提出建議或檢查方向，不得聲稱已修改 Revit 模型。";
                    response = await _client.AskAsync(ReadSettingsFromForm(), systemPrompt, BuildUserPrompt(), _cts.Token);
                }

                _responseBox.Text = response;
                ToggleBusy(false, testOnly ? "連線測試完成" : "回應完成");
            }
            catch (OperationCanceledException)
            {
                ToggleBusy(false, "已逾時或取消");
                MessageBox.Show("本機 AI 回應逾時，請確認模型已載入且 endpoint 正確。", "AI 助理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                ToggleBusy(false, "連線失敗");
                MessageBox.Show($"本機 AI 連線或回應失敗：\n{ex.Message}", "AI 助理", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task TestMcpAsync(bool showToolList)
        {
            try
            {
                ToggleBusy(true, showToolList ? "正在讀取 MCP 工具清單..." : "正在測試 MCP...");
                SaveSettings();

                _cts?.Dispose();
                _cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

                AiAssistantSettings settings = ReadSettingsFromForm();
                string result = await _mcpClient.BuildToolListTextAsync(settings, _cts.Token);
                _responseBox.Text = showToolList
                    ? result
                    : "外部 MCP 連線正常。\r\n\r\n" + result;

                ToggleBusy(false, showToolList ? "MCP 工具清單完成" : "MCP 測試完成");
            }
            catch (OperationCanceledException)
            {
                ToggleBusy(false, "MCP 逾時");
                MessageBox.Show("外部 MCP 回應逾時，請確認 server 已啟動且 endpoint 正確。", "AI 助理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                ToggleBusy(false, "MCP 連線失敗");
                MessageBox.Show($"外部 MCP 連線失敗：\n{ex.Message}", "AI 助理", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string BuildUserPrompt()
        {
            string prompt = _promptBox.Text.Trim();
            if (!_includeSummaryBox.Checked)
                return prompt;

            return _modelSummary + "\n\n使用者問題：\n" + prompt;
        }

        private async Task<string> RunAgentAsync(CancellationToken cancellationToken)
        {
            AiAssistantSettings settings = ReadSettingsFromForm();
            string originalPrompt = BuildUserPrompt();
            string toolSchemaText = await BuildCombinedToolSchemaTextAsync(settings, cancellationToken);
            string systemPrompt = AiAgentPlanner.BuildSystemPrompt(toolSchemaText);

            string firstResponse = await _client.AskAsync(settings, systemPrompt, originalPrompt, cancellationToken);
            AiAgentPlan plan = AiAgentPlanner.ParsePlan(firstResponse);

            var transcript = new List<string>();
            if (!string.IsNullOrWhiteSpace(plan.Response))
                transcript.Add(plan.Response.Trim());

            var allResults = new List<AiAgentToolResult>();
            int rounds = 0;
            while (plan.ToolCalls != null && plan.ToolCalls.Count > 0 && rounds < 2)
            {
                rounds++;
                var results = new List<AiAgentToolResult>();
                foreach (AiAgentToolCall call in plan.ToolCalls)
                {
                    if (settings.EnableExternalMcp && _mcpClient.IsExternalTool(call.Tool))
                        results.Add(await _mcpClient.ExecuteAsync(settings, call, cancellationToken));
                    else
                        results.Add(_toolRegistry.Execute(call));
                }
                allResults.AddRange(results);

                transcript.Add("Agent 工具結果：\r\n" + FormatToolResults(results));

                string followUpPrompt = AiAgentPlanner.BuildFollowUpPrompt(originalPrompt, allResults);
                string followUpResponse = await _client.AskAsync(settings, systemPrompt, followUpPrompt, cancellationToken);
                plan = AiAgentPlanner.ParsePlan(followUpResponse);

                if (!string.IsNullOrWhiteSpace(plan.Response))
                    transcript.Add(plan.Response.Trim());
            }

            if (plan.ToolCalls != null && plan.ToolCalls.Count > 0)
            {
                transcript.Add("Agent 已達本次工具回合上限，剩餘工具呼叫未執行。");
            }

            transcript.Add(settings.EnableExternalMcp
                ? "\r\nMCP 狀態：已啟用外部 Streamable HTTP MCP client，並保留 HB 內建只讀工具。"
                : "\r\nMCP 狀態：目前使用 HB 內建 MCP-style tool schema；外部 MCP 未啟用。");
            return string.Join("\r\n\r\n", transcript);
        }

        private async Task<string> BuildCombinedToolSchemaTextAsync(AiAssistantSettings settings, CancellationToken cancellationToken)
        {
            var definitions = new List<AiAgentToolDefinition>(_toolRegistry.GetDefinitions());
            string mcpError = string.Empty;
            if (settings.EnableExternalMcp)
            {
                try
                {
                    definitions.AddRange(await _mcpClient.GetToolDefinitionsAsync(settings, cancellationToken));
                }
                catch (Exception ex)
                {
                    mcpError = "\r\n\r\n外部 MCP 工具清單讀取失敗：" + ex.Message;
                }
            }

            return JsonConvert.SerializeObject(definitions, Formatting.Indented) + mcpError;
        }

        private static string FormatToolResults(IEnumerable<AiAgentToolResult> results)
        {
            return string.Join(
                "\r\n",
                results.Select(result =>
                    $"- {result.Tool}: {(result.Success ? "成功" : "失敗")}\r\n{result.Message}"));
        }

        private void LoadSettings()
        {
            var settings = AiAssistantSettings.Load();
            _providerBox.SelectedItem = string.IsNullOrWhiteSpace(settings.Provider) ? "vLLM / OpenAI API" : settings.Provider;
            if (_providerBox.SelectedIndex < 0)
                _providerBox.SelectedIndex = 0;

            _endpointBox.Text = settings.Endpoint;
            _tokenBox.Text = settings.AuthorizationToken;
            _modelBox.Text = settings.Model;
            _includeSummaryBox.Checked = settings.IncludeModelSummary;
            _enableMcpBox.Checked = settings.EnableExternalMcp;
            _mcpEndpointBox.Text = settings.McpEndpoint;
            _mcpTokenBox.Text = settings.McpAuthorizationToken;
        }

        private void SaveSettingsWithMessage()
        {
            SaveSettings();
            MessageBox.Show("AI 助理設定已儲存。", "AI 助理", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SaveSettings()
        {
            ReadSettingsFromForm().Save();
        }

        private AiAssistantSettings ReadSettingsFromForm()
        {
            return new AiAssistantSettings
            {
                Provider = Convert.ToString(_providerBox.SelectedItem) ?? "vLLM / OpenAI API",
                Endpoint = _endpointBox.Text.Trim(),
                AuthorizationToken = _tokenBox.Text.Trim(),
                Model = _modelBox.Text.Trim(),
                IncludeModelSummary = _includeSummaryBox.Checked,
                EnableExternalMcp = _enableMcpBox.Checked,
                McpEndpoint = _mcpEndpointBox.Text.Trim(),
                McpAuthorizationToken = _mcpTokenBox.Text.Trim()
            };
        }

        private void ToggleBusy(bool busy, string status)
        {
            _askButton.Enabled = !busy;
            _testButton.Enabled = !busy;
            _mcpTestButton.Enabled = !busy;
            _mcpListButton.Enabled = !busy;
            _saveButton.Enabled = !busy;
            _clearButton.Enabled = !busy;
            _statusLabel.Text = status;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private static Label CreateLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = TextColorMain,
                BackColor = CardColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 2, 8, 0),
                Font = new Font("Microsoft JhengHei UI", 9, FontStyle.Bold)
            };
        }

        private static GroupBox CreateGroup(string title, Control content)
        {
            var group = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ForeColor = TextColorMain,
                BackColor = BackColorMain,
                Font = new Font("Microsoft JhengHei UI", 9, FontStyle.Bold)
            };
            group.Controls.Add(content);
            return group;
        }

        private static Control CreateRow(string label, Control input, string hint)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = CardColor,
                Padding = new Padding(0, 2, 0, 2)
            };
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            input.Dock = DockStyle.Fill;
            input.MinimumSize = new Size(0, 32);

            row.Controls.Add(CreateLabel(label), 0, 0);
            row.Controls.Add(input, 0, 1);
            return row;
        }

        private static void StyleInput(TextBox textBox)
        {
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.BackColor = InputColor;
            textBox.ForeColor = TextColorMain;
            textBox.Font = new Font("Microsoft JhengHei UI", 10);
            textBox.Margin = new Padding(0, 2, 0, 2);
            textBox.MinimumSize = new Size(0, 32);
            textBox.Height = 32;
        }

        private static void StyleComboBox(ComboBox comboBox)
        {
            comboBox.FlatStyle = FlatStyle.Flat;
            comboBox.BackColor = InputColor;
            comboBox.ForeColor = TextColorMain;
            comboBox.Font = new Font("Microsoft JhengHei UI", 10);
            comboBox.Margin = new Padding(0, 2, 0, 2);
        }

        private static void StylePrimaryButton(Button button, string text)
        {
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = AccentColor;
            button.BackColor = ButtonColor;
            button.ForeColor = Color.White;
            button.Font = new Font("Microsoft JhengHei UI", 9, FontStyle.Bold);
            button.Height = 36;
            button.MinimumSize = new Size(0, 36);
        }

        private static void StyleSecondaryButton(Button button, string text)
        {
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(24, 134, 103);
            button.BackColor = Color.FromArgb(12, 47, 64);
            button.ForeColor = TextColorMain;
            button.Font = new Font("Microsoft JhengHei UI", 9, FontStyle.Bold);
            button.Height = 36;
            button.MinimumSize = new Size(0, 36);
        }

        private static void PaintPanelBorder(object sender, PaintEventArgs e)
        {
            var panel = sender as Panel;
            if (panel == null)
                return;

            using (var pen = new Pen(BorderColor))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
