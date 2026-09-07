using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YD_RevitTools.LicenseManager.Commands.AI
{
    internal class AiMcpClient
    {
        private const string ToolPrefix = "mcp.";
        private const string ProtocolVersion = "2025-06-18";
        private string _sessionId = string.Empty;
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public bool IsExternalTool(string toolName)
        {
            return !string.IsNullOrWhiteSpace(toolName) &&
                toolName.StartsWith(ToolPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IReadOnlyList<AiAgentToolDefinition>> GetToolDefinitionsAsync(
            AiAssistantSettings settings,
            CancellationToken cancellationToken)
        {
            JArray tools = await ListToolsRawAsync(settings, cancellationToken).ConfigureAwait(false);
            return tools
                .OfType<JObject>()
                .Select(tool =>
                {
                    string name = tool.Value<string>("name") ?? string.Empty;
                    string description = tool.Value<string>("description") ?? "外部 MCP 工具。";
                    string schema = tool["inputSchema"]?.ToString(Formatting.None) ?? "{}";

                    return new AiAgentToolDefinition
                    {
                        Name = ToolPrefix + name,
                        Description = description,
                        ArgumentsSchema = schema,
                        RiskLevel = "external"
                    };
                })
                .Where(tool => !string.IsNullOrWhiteSpace(tool.Name) && tool.Name.Length > ToolPrefix.Length)
                .ToList();
        }

        public async Task<string> BuildToolListTextAsync(
            AiAssistantSettings settings,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<AiAgentToolDefinition> tools = await GetToolDefinitionsAsync(settings, cancellationToken).ConfigureAwait(false);
            if (tools.Count == 0)
                return "外部 MCP server 已連線，但沒有回傳工具。";

            var sb = new StringBuilder();
            sb.AppendLine($"外部 MCP 工具清單：{tools.Count} 個");
            foreach (AiAgentToolDefinition tool in tools)
            {
                sb.AppendLine($"- {tool.Name}: {tool.Description}");
            }

            return sb.ToString();
        }

        public async Task<AiAgentToolResult> ExecuteAsync(
            AiAssistantSettings settings,
            AiAgentToolCall call,
            CancellationToken cancellationToken)
        {
            string toolName = call?.Tool ?? string.Empty;
            if (!IsExternalTool(toolName))
                return Fail(toolName, "不是外部 MCP 工具。");

            string mcpToolName = toolName.Substring(ToolPrefix.Length);
            try
            {
                JObject result = await SendRequestAsync(
                    settings,
                    "tools/call",
                    new JObject
                    {
                        ["name"] = mcpToolName,
                        ["arguments"] = call.Arguments ?? new JObject()
                    },
                    cancellationToken).ConfigureAwait(false);

                return Ok(toolName, FormatToolCallResult(result));
            }
            catch (Exception ex)
            {
                return Fail(toolName, ex.Message);
            }
        }

        private async Task<JArray> ListToolsRawAsync(AiAssistantSettings settings, CancellationToken cancellationToken)
        {
            await InitializeAsync(settings, cancellationToken).ConfigureAwait(false);
            JObject result = await SendRequestAsync(settings, "tools/list", new JObject(), cancellationToken).ConfigureAwait(false);
            return result["tools"] as JArray ?? new JArray();
        }

        private async Task InitializeAsync(AiAssistantSettings settings, CancellationToken cancellationToken)
        {
            _sessionId = string.Empty;
            JObject initializeParams = new JObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject
                {
                    ["name"] = "HB_BIM Tools",
                    ["version"] = "2.5.6"
                }
            };

            await SendRequestAsync(settings, "initialize", initializeParams, cancellationToken).ConfigureAwait(false);
            await SendNotificationAsync(settings, "notifications/initialized", cancellationToken).ConfigureAwait(false);
        }

        private async Task<JObject> SendRequestAsync(
            AiAssistantSettings settings,
            string method,
            JObject parameters,
            CancellationToken cancellationToken)
        {
            JObject body = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = Guid.NewGuid().ToString("N"),
                ["method"] = method,
                ["params"] = parameters ?? new JObject()
            };

            JObject response = await PostJsonRpcAsync(settings, body, cancellationToken).ConfigureAwait(false);
            if (response["error"] != null)
                throw new InvalidOperationException(response["error"].ToString(Formatting.None));

            return response["result"] as JObject ?? new JObject();
        }

        private async Task SendNotificationAsync(
            AiAssistantSettings settings,
            string method,
            CancellationToken cancellationToken)
        {
            JObject body = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method
            };

            await PostJsonRpcAsync(settings, body, cancellationToken).ConfigureAwait(false);
        }

        private async Task<JObject> PostJsonRpcAsync(
            AiAssistantSettings settings,
            JObject body,
            CancellationToken cancellationToken)
        {
            string endpoint = settings.McpEndpoint?.Trim();
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException("MCP Endpoint 未設定。");

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Accept.ParseAdd("text/event-stream");
                request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
                if (!string.IsNullOrWhiteSpace(_sessionId))
                    request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);

                if (!string.IsNullOrWhiteSpace(settings.McpAuthorizationToken))
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.McpAuthorizationToken.Trim());

                request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");

                using (HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException($"MCP HTTP {(int)response.StatusCode}: {content}");

                    if (response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string> sessionValues))
                        _sessionId = sessionValues.FirstOrDefault() ?? _sessionId;

                    if (string.IsNullOrWhiteSpace(content))
                        return new JObject();

                    return ParseResponse(content);
                }
            }
        }

        private static JObject ParseResponse(string content)
        {
            string trimmed = content.Trim();
            if (trimmed.StartsWith("{"))
                return JObject.Parse(trimmed);

            foreach (string line in trimmed.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    continue;

                string data = line.Substring(5).Trim();
                if (string.IsNullOrWhiteSpace(data) || data == "[DONE]")
                    continue;

                if (data.StartsWith("{"))
                    return JObject.Parse(data);
            }

            throw new InvalidOperationException("MCP 回應不是有效 JSON 或 SSE data JSON。");
        }

        private static string FormatToolCallResult(JObject result)
        {
            JArray content = result["content"] as JArray;
            if (content == null || content.Count == 0)
                return result.ToString(Formatting.Indented);

            var messages = new List<string>();
            foreach (JObject item in content.OfType<JObject>())
            {
                string type = item.Value<string>("type") ?? string.Empty;
                if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
                    messages.Add(item.Value<string>("text") ?? string.Empty);
                else
                    messages.Add(item.ToString(Formatting.None));
            }

            return string.Join("\r\n", messages.Where(x => !string.IsNullOrWhiteSpace(x)));
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
