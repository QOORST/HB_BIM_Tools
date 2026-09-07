using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using YD_RevitTools.LicenseManager.Helpers;

namespace YD_RevitTools.LicenseManager.Commands.AI
{
    internal class AiAgentToolRegistry
    {
        private readonly ExternalCommandData _commandData;

        private static readonly string[] KnownToolKeys =
        {
            "mep_check",
            "bim_standard_audit",
            "model_data_manager",
            "cobie_field_manager",
            "cobie_export",
            "cobie_standard_export",
            "schedule_export",
            "clarification_deck",
            "pipe_sleeve",
            "pipe_sleeve_manager",
            "pipe_center_align",
            "auto_avoid",
            "mep_up_down_offset",
            "family_parameter_rename",
            "license",
            "check_update"
        };

        public AiAgentToolRegistry(ExternalCommandData commandData)
        {
            _commandData = commandData;
        }

        public string BuildToolSchemaText()
        {
            return JsonConvert.SerializeObject(GetDefinitions(), Formatting.Indented);
        }

        public IReadOnlyList<AiAgentToolDefinition> GetDefinitions()
        {
            return new List<AiAgentToolDefinition>
            {
                new AiAgentToolDefinition
                {
                    Name = "hb.get_model_summary",
                    RiskLevel = "read",
                    Description = "取得目前 Revit 文件摘要。只讀，不修改模型。",
                    ArgumentsSchema = "{}"
                },
                new AiAgentToolDefinition
                {
                    Name = "hb.get_selection_summary",
                    RiskLevel = "read",
                    Description = "取得目前選取元素的類別、名稱與 ElementId 摘要。只讀，不修改模型。",
                    ArgumentsSchema = "{}"
                },
                new AiAgentToolDefinition
                {
                    Name = "hb.list_tools",
                    RiskLevel = "read",
                    Description = "列出 Agent 知道的 HB_BIM Tools 工具 key。只讀，不開啟工具。",
                    ArgumentsSchema = "{}"
                },
                new AiAgentToolDefinition
                {
                    Name = "hb.recommend_tool",
                    RiskLevel = "read",
                    Description = "將建議開啟的 HB_BIM Tools 工具加入回覆。只產生建議，不會直接執行。",
                    ArgumentsSchema = "{\"tool_key\":\"mep_check | bim_standard_audit | model_data_manager | cobie_field_manager | clarification_deck | pipe_sleeve | pipe_sleeve_manager | pipe_center_align | auto_avoid | mep_up_down_offset | schedule_export | license | check_update\", \"reason\":\"建議原因\"}"
                }
            };
        }

        public AiAgentToolResult Execute(AiAgentToolCall call)
        {
            if (call == null || string.IsNullOrWhiteSpace(call.Tool))
                return Fail("(unknown)", "工具呼叫內容為空。");

            try
            {
                switch (call.Tool.Trim())
                {
                    case "hb.get_model_summary":
                        return Ok(call.Tool, AiRevitContextBuilder.Build(_commandData));
                    case "hb.get_selection_summary":
                        return Ok(call.Tool, BuildSelectionSummary());
                    case "hb.list_tools":
                        return Ok(call.Tool, BuildAvailableToolsText());
                    case "hb.recommend_tool":
                        return RecommendTool(call);
                    default:
                        return Fail(call.Tool, $"未知或未允許的工具：{call.Tool}");
                }
            }
            catch (Exception ex)
            {
                return Fail(call.Tool, ex.Message);
            }
        }

        private AiAgentToolResult RecommendTool(AiAgentToolCall call)
        {
            string toolKey = call.Arguments?.Value<string>("tool_key") ?? string.Empty;
            string reason = call.Arguments?.Value<string>("reason") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(toolKey))
                return Fail(call.Tool, "缺少 tool_key。");

            if (!KnownToolKeys.Contains(toolKey.Trim(), StringComparer.OrdinalIgnoreCase))
                return Fail(call.Tool, $"尚未登錄工具：{toolKey}");

            return Ok(call.Tool, $"建議工具：{toolKey}\n原因：{reason}\n狀態：僅建議，尚未執行。");
        }

        private string BuildSelectionSummary()
        {
            UIDocument uiDoc = _commandData?.Application?.ActiveUIDocument;
            Document doc = uiDoc?.Document;
            if (doc == null)
                return "目前沒有開啟的 Revit 文件。";

            var selectedIds = uiDoc.Selection.GetElementIds();
            if (selectedIds.Count == 0)
                return "目前沒有選取元素。";

            var sb = new StringBuilder();
            sb.AppendLine($"目前選取 {selectedIds.Count} 個元素：");
            foreach (ElementId id in selectedIds.Take(50))
            {
                Element element = doc.GetElement(id);
                if (element == null)
                    continue;

                string category = element.Category?.Name ?? "(無類別)";
                string name = string.IsNullOrWhiteSpace(element.Name) ? "(未命名)" : element.Name;
                sb.AppendLine($"- {id.GetIdValue()} | {category} | {name}");
            }

            if (selectedIds.Count > 50)
                sb.AppendLine($"... 已省略 {selectedIds.Count - 50} 個元素。");

            return sb.ToString();
        }

        private string BuildAvailableToolsText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Agent 可建議的 HB_BIM Tools：");
            foreach (string toolKey in KnownToolKeys.OrderBy(x => x))
            {
                sb.AppendLine($"- {toolKey}");
            }

            return sb.ToString();
        }

        private static AiAgentToolResult Ok(string tool, string message)
        {
            return new AiAgentToolResult { Tool = tool, Success = true, Message = message };
        }

        private static AiAgentToolResult Fail(string tool, string message)
        {
            return new AiAgentToolResult { Tool = tool, Success = false, Message = message };
        }
    }
}
