using System.Collections.Generic;
using System.IO;
using BepInEx;
using Mirage;
using NuclearOption.Networking;

namespace JointFires.Strike
{
    // Who may plan strikes: the host always, plus players the host promoted. Promotions are kept
    // in a whitelist file (one SteamID64 per line, optional "// note", the game's ban-list format)
    // so they survive restarts and can be edited by hand on a dedicated server.
    internal static class StrikePlanners
    {
        private static readonly string FilePath = Path.Combine(Paths.ConfigPath, "com.ryantogo.jointfires.strike_planners.txt");
        private static readonly Dictionary<ulong, string> promoted = new Dictionary<ulong, string>();
        private static bool loaded;

        public static bool IsAllowed(INetworkPlayer connection)
        {
            if (connection == null)
            {
                return false;
            }
            if (connection.IsHost)
            {
                return true;
            }
            if (!Plugin.StrikeAllowPromoted.Value || !connection.TryGetPlayer<Player>(out Player player))
            {
                return false;
            }
            EnsureLoaded();
            // SteamID is 0 off the Steam transport; nobody can be matched then.
            return player.SteamID != 0 && promoted.ContainsKey(player.SteamID);
        }

        public static bool IsPromoted(ulong steamId)
        {
            EnsureLoaded();
            return steamId != 0 && promoted.ContainsKey(steamId);
        }

        public static void SetPromoted(ulong steamId, string name, bool value)
        {
            EnsureLoaded();
            if (steamId == 0)
            {
                return;
            }
            if (value)
            {
                promoted[steamId] = name ?? string.Empty;
            }
            else
            {
                promoted.Remove(steamId);
            }
            Save();
            Plugin.Log.LogInfo($"[Strike] {(value ? "Promoted" : "Demoted")} {name} ({steamId}) as strike planner");
        }

        public static void Reload()
        {
            loaded = false;
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }
            loaded = true;
            promoted.Clear();
            if (!File.Exists(FilePath))
            {
                return;
            }
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                string line = raw.Trim();
                string note = string.Empty;
                int comment = line.IndexOf("//", System.StringComparison.Ordinal);
                if (comment >= 0)
                {
                    note = line.Substring(comment + 2).Trim();
                    line = line.Substring(0, comment).Trim();
                }
                if (ulong.TryParse(line, out ulong id) && id != 0)
                {
                    promoted[id] = note;
                }
            }
            Plugin.Log.LogInfo($"[Strike] {promoted.Count} promoted strike planner(s) loaded");
        }

        private static void Save()
        {
            var lines = new List<string> { "// Joint Fires strike planners: one SteamID64 per line. The host can always plan." };
            foreach (KeyValuePair<ulong, string> entry in promoted)
            {
                lines.Add(string.IsNullOrEmpty(entry.Value) ? entry.Key.ToString() : $"{entry.Key} // {entry.Value}");
            }
            try
            {
                File.WriteAllLines(FilePath, lines);
            }
            catch (IOException e)
            {
                Plugin.Log.LogError($"[Strike] Couldn't save {FilePath}: {e.Message}");
            }
        }
    }
}
