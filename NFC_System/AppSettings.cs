using System;
using System.IO;
using System.Text.Json;

namespace NFC_System
{
    /// <summary>
    /// Reads and writes settings.json next to the running .exe.
    /// All windows call AppSettings.Load() to get their COM port.
    /// </summary>
    public class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            AppContext.BaseDirectory, "settings.json");

        public string VerificationComPort  { get; set; } = "COM3";
        public string EventComPort         { get; set; } = "COM4";

        // ── Load ──────────────────────────────────────────────────────

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json)
                           ?? new AppSettings();
                }
            }
            catch { /* fall through to defaults */ }

            return new AppSettings();
        }

        // ── Save ──────────────────────────────────────────────────────

        public void Save()
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsPath, json);
        }
    }
}
