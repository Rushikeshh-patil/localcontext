using System;
using System.IO;
using System.Text.Json;

namespace LocalContextBuilder
{
    public class AppSettings
    {
        public string ModelPath { get; set; } = @"C:\Users\rush\.ollama\models\gemma-4-E4B-it-UD-Q8_K_XL.gguf";
        public bool UseNpu { get; set; } = false;
        public int GpuLayers { get; set; } = 999;
        public int ContextSize { get; set; } = 4096;

        private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch { }
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
