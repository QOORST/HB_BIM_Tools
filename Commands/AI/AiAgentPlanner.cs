using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace YD_RevitTools.LicenseManager.Commands.AI
{
    internal static class AiAgentPlanner
    {
        public static AiAgentPlan ParsePlan(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return new AiAgentPlan { Response = string.Empty };

            string jsonText = ExtractJson(response.Trim());
            if (!string.IsNullOrWhiteSpace(jsonText))
            {
                try
                {
                    var plan = JsonConvert.DeserializeObject<AiAgentPlan>(jsonText);
                    if (plan != null)
                        return plan;
                }
                catch
                {
                    // Fall back to plain response below.
                }
            }

            return new AiAgentPlan
            {
                Response = response,
                ToolCalls = new List<AiAgentToolCall>()
            };
        }

        public static string BuildSystemPrompt(string toolSchemaText)
        {
            return
                "你是 HB_BIM Tools 的本機 BIM AI Agent。請使用繁體中文，務實且精簡。\n" +
                "你可以規劃工具呼叫，但目前只允許只讀工具與工具建議，不允許直接修改 Revit 模型。\n" +
                "外部 MCP 工具會以 mcp. 前綴顯示；除非工具描述明確表示只讀，否則只能用於查詢或整理建議。\n" +
                "請只回傳 JSON，不要使用 Markdown code fence。格式如下：\n" +
                "{\n" +
                "  \"response\": \"給使用者看的說明\",\n" +
                "  \"tool_calls\": [\n" +
                "    { \"tool\": \"hb.get_model_summary\", \"arguments\": {} }\n" +
                "  ]\n" +
                "}\n\n" +
                "可用 MCP-style 工具 schema：\n" + toolSchemaText;
        }

        public static string BuildFollowUpPrompt(string originalPrompt, IEnumerable<AiAgentToolResult> results)
        {
            string resultJson = JsonConvert.SerializeObject(results, Formatting.Indented);
            return
                "使用者原始需求：\n" + originalPrompt + "\n\n" +
                "工具執行結果：\n" + resultJson + "\n\n" +
                "請根據工具結果回覆最終建議。若還需要只讀工具，仍可回傳 tool_calls；否則 tool_calls 回傳空陣列。";
        }

        private static string ExtractJson(string text)
        {
            if (text.StartsWith("{") && text.EndsWith("}"))
                return text;

            Match fenced = Regex.Match(text, "```(?:json)?\\s*(\\{[\\s\\S]*?\\})\\s*```", RegexOptions.IgnoreCase);
            if (fenced.Success)
                return fenced.Groups[1].Value;

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                string candidate = text.Substring(start, end - start + 1);
                try
                {
                    JObject.Parse(candidate);
                    return candidate;
                }
                catch
                {
                    return string.Empty;
                }
            }

            return string.Empty;
        }
    }
}
