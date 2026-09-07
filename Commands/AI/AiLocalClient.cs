using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YD_RevitTools.LicenseManager.Commands.AI
{
    internal class AiLocalClient
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        };

        public async Task<string> AskAsync(AiAssistantSettings settings, string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            return await AskOpenAiCompatibleAsync(settings, systemPrompt, userPrompt, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> AskOpenAiCompatibleAsync(AiAssistantSettings settings, string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            string baseUrl = NormalizeBaseUrl(settings.Endpoint);
            string url = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? baseUrl + "/chat/completions"
                : baseUrl + "/v1/chat/completions";
            var payload = new
            {
                model = settings.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.2
            };

            using (var request = CreateJsonRequest(HttpMethod.Post, url, payload, settings.AuthorizationToken))
            using (var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"本機 AI 回應失敗：{(int)response.StatusCode} {response.ReasonPhrase}\n{body}");

                JObject json = JObject.Parse(body);
                return json["choices"]?[0]?["message"]?.Value<string>("content") ?? body;
            }
        }

        private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string url, object payload, string authorizationToken)
        {
            string json = JsonConvert.SerializeObject(payload);
            var request = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(authorizationToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authorizationToken.Trim());
            }

            return request;
        }

        private static string NormalizeBaseUrl(string endpoint)
        {
            string value = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434" : endpoint.Trim();
            return value.TrimEnd('/');
        }
    }
}
