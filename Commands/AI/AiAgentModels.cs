using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace YD_RevitTools.LicenseManager.Commands.AI
{
    internal class AiAgentToolCall
    {
        [JsonProperty("tool")]
        public string Tool { get; set; }

        [JsonProperty("arguments")]
        public JObject Arguments { get; set; }
    }

    internal class AiAgentPlan
    {
        [JsonProperty("response")]
        public string Response { get; set; }

        [JsonProperty("tool_calls")]
        public List<AiAgentToolCall> ToolCalls { get; set; } = new List<AiAgentToolCall>();
    }

    internal class AiAgentToolDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string ArgumentsSchema { get; set; }
        public string RiskLevel { get; set; }
    }

    internal class AiAgentToolResult
    {
        public string Tool { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
