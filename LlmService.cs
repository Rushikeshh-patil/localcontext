using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LocalContextBuilder
{
    public class LlmService
    {
        private Process? _serverProcess;
        private readonly HttpClient _httpClient;
        private readonly string _serverUrl = "http://127.0.0.1:8080";

        public LlmService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public void StartServer(string modelPath)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string serverExePath = Path.Combine(baseDir, "llama-server.exe");
            
            if (!File.Exists(serverExePath))
            {
                // Fallback to searching up the tree in the Release folder
                string fallbackPath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\Release\llama-server.exe"));
                if (File.Exists(fallbackPath))
                {
                    serverExePath = fallbackPath;
                }
                else
                {
                    Console.WriteLine("llama-server.exe not found in " + serverExePath + " or " + fallbackPath);
                    return; 
                }
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = serverExePath,
                    Arguments = $"-m \"{modelPath}\" -ngl 999 -c 4096 --host 127.0.0.1 --port 8080", // Increased context for OCR
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _serverProcess = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start llama-server: {ex.Message}");
            }
        }

        public async Task<string?> GenerateSuggestion(string typedContext, string screenContext)
        {
            try
            {
                if (screenContext.Length > 2000)
                {
                    screenContext = screenContext.Substring(0, 2000) + "...";
                }

                string prompt = $@"You are a fast autocomplete predictive typing assistant.
Below is the text currently visible on the user's screen for context:
---
{screenContext}
---

The user has just typed the following:
""{typedContext}""

Predict exactly the next 3 to 5 words to complete their sentence. DO NOT include the text they already typed. DO NOT add quotes or explanations. JUST the predicted continuation.";

                var payload = new
                {
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    max_tokens = 10,
                    temperature = 0.2,
                    stop = new[] { "\n", "\r" }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync($"{_serverUrl}/v1/chat/completions", content);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                    {
                        var choices = doc.RootElement.GetProperty("choices");
                        if (choices.GetArrayLength() > 0)
                        {
                            var message = choices[0].GetProperty("message");
                            if (message.TryGetProperty("content", out JsonElement contentElement))
                            {
                                string suggestion = contentElement.GetString()?.Trim() ?? "";
                                return suggestion;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Server might not be ready
            }
            return null;
        }

        public void Cleanup()
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill();
            }
        }
    }
}
