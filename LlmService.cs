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

        public void StartServer(AppSettings settings)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string serverExePath = Path.Combine(baseDir, "llama-server.exe");
            
            if (!File.Exists(serverExePath))
            {
                string fallbackPath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\Release\llama-server.exe"));
                if (File.Exists(fallbackPath))
                {
                    serverExePath = fallbackPath;
                }
                else
                {
                    Log("llama-server.exe not found.");
                    return; 
                }
            }

            try
            {
                string arguments = $"-m \"{settings.ModelPath}\" -c {settings.ContextSize} --host 127.0.0.1 --port 8080";
                
                if (settings.UseNpu)
                {
                    // For Intel NPU via OpenVINO, usually it's -ngl 0 or specific device flags
                    // We'll assume the user has an OpenVINO build of llama-cpp
                    arguments += " -ngl 0"; 
                }
                else
                {
                    arguments += $" -ngl {settings.GpuLayers}";
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = serverExePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                Log($"Starting server with args: {arguments}");
                _serverProcess = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log($"Failed to start llama-server: {ex.Message}");
            }
        }

        private void Log(string msg)
        {
            System.IO.File.AppendAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_debug.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + " [LLM] " + msg + "\n");
        }

        public async Task<string?> GenerateSuggestion(string typedContext, string screenContext)
        {
            try
            {
                if (screenContext.Length > 2000)
                {
                    screenContext = screenContext.Substring(0, 2000) + "...";
                }

                // Use Gemma's native instruct format with the raw /completion endpoint.
                // The /v1/chat/completions endpoint triggers Gemma 4's thinking mode
                // which wastes all tokens on internal reasoning and returns empty content.
                string screenSection = string.IsNullOrWhiteSpace(screenContext)
                    ? ""
                    : $"\nFor reference, here is some background text from the screen (DO NOT repeat this):\n{screenContext}\n";

                string gemmaPrompt = $"<start_of_turn>user\nComplete the partial sentence below with the most likely next 3-5 words. Rules:\n- Output ONLY the continuation words\n- Do NOT repeat any text the user already typed\n- Do NOT include UI elements, menus, or labels\n- Do NOT use quotes or formatting{screenSection}\nPartial sentence to complete: \"{typedContext}\"\n<end_of_turn>\n<start_of_turn>model\n";

                var payload = new
                {
                    prompt = gemmaPrompt,
                    n_predict = 15,
                    temperature = 0.3,
                    stop = new[] { "<end_of_turn>", "<start_of_turn>", "<|", "\n" }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                Log($"Sending to /completion, typed={typedContext.Length}chars, screen={screenContext.Length}chars");

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync($"{_serverUrl}/completion", content);

                string jsonResponse = await response.Content.ReadAsStringAsync();
                Log($"HTTP {(int)response.StatusCode}: {jsonResponse.Substring(0, Math.Min(300, jsonResponse.Length))}");

                if (response.IsSuccessStatusCode)
                {
                    using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                    {
                        if (doc.RootElement.TryGetProperty("content", out JsonElement contentElement))
                        {
                            string suggestion = contentElement.GetString()?.Trim() ?? "";
                            // Filter out any remaining junk tokens
                            if (suggestion.Contains("<|") || suggestion.Contains("<start_of_turn>"))
                            {
                                Log($"Filtered junk suggestion: {suggestion}");
                                return null;
                            }
                            Log($"Clean suggestion: '{suggestion}'");
                            return string.IsNullOrWhiteSpace(suggestion) ? null : suggestion;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.Message}");
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
