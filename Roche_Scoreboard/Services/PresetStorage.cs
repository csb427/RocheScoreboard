using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services
{
    public static class PresetStorage
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true
        };

        private static string GetPresetsPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Roche_Scoreboard"
            );
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "team_presets.json");
        }

        private static string GetCricketPresetsPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Roche_Scoreboard"
            );
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "cricket_team_presets.json");
        }

        public static List<TeamPreset> LoadAll()
        {
            string path = GetPresetsPath();
            if (!File.Exists(path)) return new List<TeamPreset>();

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<TeamPreset>>(json, Options)
                       ?? new List<TeamPreset>();
            }
            catch
            {
                return new List<TeamPreset>();
            }
        }

        public static void SaveAll(List<TeamPreset> presets)
        {
            string path = GetPresetsPath();
            string json = JsonSerializer.Serialize(presets, Options);
            File.WriteAllText(path, json);
        }

        public static List<TeamPreset> LoadAllCricket()
        {
            string path = GetCricketPresetsPath();
            if (!File.Exists(path)) return new List<TeamPreset>();

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<TeamPreset>>(json, Options)
                       ?? new List<TeamPreset>();
            }
            catch
            {
                return new List<TeamPreset>();
            }
        }

        public static void SaveAllCricket(List<TeamPreset> presets)
        {
            string path = GetCricketPresetsPath();
            string json = JsonSerializer.Serialize(presets, Options);
            File.WriteAllText(path, json);
        }
    }
}
