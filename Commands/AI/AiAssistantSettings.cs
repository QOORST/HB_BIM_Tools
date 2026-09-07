using Newtonsoft.Json;
using System;
using System.IO;

namespace YD_RevitTools.LicenseManager.Commands.AI
{
    internal class AiAssistantSettings
    {
        public string Provider { get; set; } = "vLLM / OpenAI API";
        public string Endpoint { get; set; } = "http://localhost:8000";
        public string AuthorizationToken { get; set; } = string.Empty;
        public string Model { get; set; } = "deepseek-qwen";
        public bool IncludeModelSummary { get; set; } = true;
        public bool EnableExternalMcp { get; set; } = false;
        public string McpEndpoint { get; set; } = "http://127.0.0.1:3333/mcp";
        public string McpAuthorizationToken { get; set; } = string.Empty;

        public static string SettingsDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HB_BIM",
                    "AiAssistant");
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(SettingsDirectory, "settings.json"); }
        }

        public static AiAssistantSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new AiAssistantSettings();

                string json = File.ReadAllText(SettingsPath);
                var settings = JsonConvert.DeserializeObject<AiAssistantSettings>(json) ?? new AiAssistantSettings();
                if (string.Equals(settings.Provider, "Ollama", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(settings.Provider, "OpenAI Compatible", StringComparison.OrdinalIgnoreCase))
                {
                    settings.Provider = "vLLM / OpenAI API";
                    if (string.IsNullOrWhiteSpace(settings.Endpoint) || settings.Endpoint.Contains(":11434"))
                        settings.Endpoint = "http://localhost:8000";
                    if (string.IsNullOrWhiteSpace(settings.Model) || settings.Model.Equals("llama3.1", StringComparison.OrdinalIgnoreCase))
                        settings.Model = "deepseek-qwen";
                }

                return settings;
            }
            catch
            {
                return new AiAssistantSettings();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }
}
