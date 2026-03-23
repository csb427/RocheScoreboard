using System;
using System.IO;
using System.Text.Json;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services
{
    public static class MatchStorage
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true
        };

        public static string GetAutoSavePath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Roche_Scoreboard"
            );
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "autosave.json");
        }

        public static void SaveAuto(SerializableState state)
        {
            string path = GetAutoSavePath();
            string json = JsonSerializer.Serialize(state, Options);
            File.WriteAllText(path, json);
        }

        public static SerializableState? LoadAuto()
        {
            string path = GetAutoSavePath();
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<SerializableState>(json, Options);
            }
            catch
            {
                return null;
            }
        }
    }
}
