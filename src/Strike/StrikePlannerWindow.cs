using System.Collections.Generic;
using HarmonyLib;
using JointFires.Core;
using JointFires.Net;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace JointFires.Strike
{
    // The strike planner: an IMGUI window toggled with Strike.PlannerKey. Pick an enemy on the map
    // (or your HUD target), find launchers, choose rounds and a time on target, launch. Also lists
    // running missions, and lets the host promote players to planners. Best used with the map
    // open, where the cursor is free.
    internal static class StrikePlannerWindow
    {
        private const float ReferenceHeight = 1080f;
        private const int WindowId = 0x4A465354; // "JFST"
        private const int TotStep = 10;

        private static readonly AccessTools.FieldRef<CombatHUD, List<Unit>> HudTargets =
            AccessTools.FieldRefAccess<CombatHUD, List<Unit>>("targetList");

        private static bool open;
        private static Rect window = new Rect(40f, 120f, 560f, 620f);
        private static Vector2 scroll;
        private static uint shownQueryId;
        private static readonly Dictionary<ushort, int> chosenRounds = new Dictionary<ushort, int>();
        private static int totSeconds;
        private static bool showPlanners;

        public static void Update()
        {
            if (RewiredInput.GetKeyDown(Plugin.StrikePlannerKey.Value))
            {
                open = !open;
            }
        }

        public static void OnGUI()
        {
            if (!open)
            {
                return;
            }
            float scale = Screen.height / ReferenceHeight;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            window = GUI.Window(WindowId, window, Draw, "STRIKE PLANNER");
            GUI.matrix = previous;
        }

        private static void Draw(int id)
        {
            GUILayout.BeginVertical();
            if (!JfNet.ServerHasMod)
            {
                GUILayout.Label("The host doesn't run Joint Fires, so strikes aren't available.");
                Footer();
                return;
            }
            if (!StrikeClient.Allowed)
            {
                GUILayout.Label("Only the host and players the host promoted can plan strikes.");
                DrawMissions();
                Footer();
                return;
            }

            DrawTarget();
            DrawAssets();
            DrawMissions();
            if (JfNet.Server != null)
            {
                DrawPlanners();
            }
            Footer();
        }

        private static void Footer()
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{Plugin.StrikePlannerKey.Value} closes this window. Use it with the map open to have a cursor.");
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private static void DrawTarget()
        {
            Unit target = SelectedEnemy();
            GUILayout.Label(target != null ? $"Target: {UnitLabel(target)}" : "Target: select an enemy on the map (or target one in the cockpit)");
            GUI.enabled = target != null && !StrikeClient.WaitingForAssets;
            if (GUILayout.Button(StrikeClient.WaitingForAssets ? "Searching..." : "Find launchers for this target"))
            {
                StrikeClient.RequestAssets(target);
            }
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(StrikeClient.LastMessage))
            {
                GUILayout.Label(StrikeClient.LastMessage);
            }
        }

        private static void DrawAssets()
        {
            if (!StrikeClient.Assets.HasValue || StrikeClient.Assets.Value.Assets.Length == 0)
            {
                return;
            }
            StrikeAssetsMsg assets = StrikeClient.Assets.Value;
            if (assets.QueryId != shownQueryId)
            {
                shownQueryId = assets.QueryId;
                chosenRounds.Clear();
                totSeconds = 0;
            }

            string targetName = UnitRegistry.TryGetUnit(new PersistentID { Id = assets.TargetId }, out Unit target) ? UnitLabel(target) : "target";
            GUILayout.Label($"Launchers that can reach {targetName}:");
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(220f));
            foreach (StrikeAsset asset in assets.Assets)
            {
                chosenRounds.TryGetValue(asset.Index, out int rounds);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{asset.Name}\n{asset.Weapon}, {asset.Rounds} ready, flight ~{asset.FlightTenths / 10f:0}s", GUILayout.Width(330f));
                if (GUILayout.Button("-", GUILayout.Width(28f)))
                {
                    rounds = Mathf.Max(0, rounds - 1);
                }
                GUILayout.Label(rounds.ToString(), GUILayout.Width(28f));
                if (GUILayout.Button("+", GUILayout.Width(28f)))
                {
                    rounds = Mathf.Min(asset.Rounds, rounds + 1);
                }
                if (GUILayout.Button("All", GUILayout.Width(40f)))
                {
                    rounds = asset.Rounds;
                }
                chosenRounds[asset.Index] = rounds;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            int minimum = MinimumTot(assets);
            totSeconds = Mathf.Max(totSeconds, minimum);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Impact in {totSeconds} s (earliest {minimum} s)", GUILayout.Width(330f));
            if (GUILayout.Button($"-{TotStep}", GUILayout.Width(50f)))
            {
                totSeconds = Mathf.Max(minimum, totSeconds - TotStep);
            }
            if (GUILayout.Button($"+{TotStep}", GUILayout.Width(50f)))
            {
                totSeconds += TotStep;
            }
            if (GUILayout.Button("+60", GUILayout.Width(50f)))
            {
                totSeconds += 60;
            }
            GUILayout.EndHorizontal();

            var picks = new List<StrikePick>();
            int total = 0;
            foreach (KeyValuePair<ushort, int> entry in chosenRounds)
            {
                if (entry.Value > 0)
                {
                    picks.Add(new StrikePick { Index = entry.Key, Rounds = (byte)entry.Value });
                    total += entry.Value;
                }
            }
            GUI.enabled = total > 0;
            if (GUILayout.Button(total > 0 ? $"LAUNCH STRIKE: {total} rounds, impact in {totSeconds} s" : "Choose rounds to launch", GUILayout.Height(36f)))
            {
                StrikeClient.Order(assets, totSeconds, picks);
            }
            GUI.enabled = true;
        }

        private static void DrawMissions()
        {
            if (StrikeClient.Missions.Count == 0)
            {
                return;
            }
            GUILayout.Space(8f);
            GUILayout.Label("Missions:");
            foreach (KeyValuePair<uint, StrikeClient.MissionView> entry in StrikeClient.Missions)
            {
                StrikeClient.MissionView view = entry.Value;
                string targetName = UnitRegistry.TryGetUnit(new PersistentID { Id = view.Status.TargetId }, out Unit target) ? UnitLabel(target) : "target";
                string timing = StrikeText.IsFinal(view.State) ? "" : view.SecondsToTot >= 0f ? $", impact in {Mathf.CeilToInt(view.SecondsToTot)} s" : ", impact due";
                GUILayout.BeginHorizontal();
                GUILayout.Label($"#{entry.Key} {targetName}: {StrikeText.Describe(view.State)}, {view.Status.Launched}/{view.Status.Planned} launched{timing}"
                    + (string.IsNullOrEmpty(view.Status.Note) ? "" : $"\n{view.Status.Note}"), GUILayout.Width(440f));
                if (!StrikeText.IsFinal(view.State) && GUILayout.Button("Abort", GUILayout.Width(70f)))
                {
                    StrikeClient.Abort(entry.Key);
                }
                GUILayout.EndHorizontal();
            }
        }

        // Host only: promote connected players to strike planners (saved to the whitelist file).
        private static void DrawPlanners()
        {
            GUILayout.Space(8f);
            showPlanners = GUILayout.Toggle(showPlanners, "Strike planners (host)");
            if (!showPlanners)
            {
                return;
            }
            if (!Plugin.StrikeAllowPromoted.Value)
            {
                GUILayout.Label("Promoting is off (Strike.AllowPromotedPlanners).");
                return;
            }
            bool any = false;
            foreach (INetworkPlayer connection in JfNet.Server.AuthenticatedPlayers)
            {
                if (connection.IsHost || !connection.TryGetPlayer<Player>(out Player player))
                {
                    continue;
                }
                any = true;
                bool promoted = StrikePlanners.IsPromoted(player.SteamID);
                GUILayout.BeginHorizontal();
                string modNote = JfNet.IsModded(connection) ? "" : " (no Joint Fires)";
                GUILayout.Label($"{player.GetDisplayName(PlayerNameContext.Other)}{modNote}", GUILayout.Width(330f));
                GUI.enabled = player.SteamID != 0;
                if (GUILayout.Button(promoted ? "Demote" : "Promote", GUILayout.Width(100f)))
                {
                    StrikePlanners.SetPromoted(player.SteamID, player.GetDisplayName(PlayerNameContext.Other), !promoted);
                    StrikeServer.PermissionsChanged();
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (!any)
            {
                GUILayout.Label("No other players connected.");
            }
        }

        private static int MinimumTot(StrikeAssetsMsg assets)
        {
            float minimum = 0f;
            foreach (StrikeAsset asset in assets.Assets)
            {
                if (chosenRounds.TryGetValue(asset.Index, out int rounds) && rounds > 0)
                {
                    // Matches the server's check, with the station's rounds spread around the TOT.
                    minimum = Mathf.Max(minimum, (asset.LeadTenths + asset.FlightTenths) / 10f + (rounds - 1) * StrikeLaunchers.MinInterval);
                }
            }
            return Mathf.CeilToInt(minimum) + 5;
        }

        private static Unit SelectedEnemy()
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player.HQ == null)
            {
                return null;
            }
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map != null)
            {
                for (int i = map.selectedIcons.Count - 1; i >= 0; i--)
                {
                    if (map.selectedIcons[i] is UnitMapIcon icon && IsEnemy(icon.unit, player))
                    {
                        return icon.unit;
                    }
                }
            }
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            List<Unit> targets = hud != null ? HudTargets(hud) : null;
            if (targets != null && targets.Count > 0 && IsEnemy(targets[0], player))
            {
                return targets[0];
            }
            return null;
        }

        private static bool IsEnemy(Unit unit, Player player)
        {
            return unit != null && !unit.disabled && unit.NetworkHQ != null && unit.NetworkHQ != player.HQ;
        }

        private static string UnitLabel(Unit unit)
        {
            return unit.definition != null ? unit.definition.unitName : unit.unitName;
        }
    }
}
