using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JointFires.Net;
using JointFires.Strike;
using UnityEngine;

namespace JointFires
{
    [BepInPlugin(Guid, Name, Version)]
    // Optional: when EW-25 Medusa Multi-Crew is installed it owns the jamming pod boost.
    [BepInDependency(MultiCrewGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.ryantogo.jointfires";
        public const string Name = "Joint Fires";
        public const string Version = "0.2.0";

        private const string MultiCrewGuid = "com.ryantogo.medusamulticrew";
        // First Multi-Crew version that boosts jamming pod range itself.
        private static readonly System.Version MultiCrewJammingVersion = new System.Version(0, 5, 1);

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        internal static ConfigEntry<bool> ShowOnMap;
        internal static ConfigEntry<bool> ShowOnHud;
        internal static ConfigEntry<float> JammingPodRangeMultiplier;
        internal static ConfigEntry<KeyCode> StrikePlannerKey;
        internal static ConfigEntry<bool> StrikeAllowPromoted;
        internal static ConfigEntry<bool> StrikeAllowNuclear;
        internal static ConfigEntry<bool> StrikeIgnoreEscalation;

        internal static bool MultiCrewHandlesJamming { get; private set; }

        private Harmony harmony;
        private bool loggedUpdateError;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            MultiCrewHandlesJamming = Chainloader.PluginInfos.TryGetValue(MultiCrewGuid, out PluginInfo multiCrew)
                && multiCrew.Metadata.Version >= MultiCrewJammingVersion;

            ShowOnMap = Config.Bind("Sensors", "ShowOnMap", true,
                "Show which friendly sensors detect a target in the map tooltip and the selected-target box.");
            ShowOnHud = Config.Bind("Sensors", "ShowOnHud", true,
                "Show the detecting sensors under the primary target's info on the cockpit HUD.");
            JammingPodRangeMultiplier = Config.Bind("Jamming", "PodRangeMultiplier", 1.25f,
                new ConfigDescription("Range multiplier for every Radar Jamming Pod (1 = vanilla). Jamming is worked out on the host, so the host's value counts. Applied when a mission loads. Ignored when EW-25 Medusa Multi-Crew 0.5.1+ is installed; set it there instead.",
                    new AcceptableValueRange<float>(0.5f, 3f)));
            StrikePlannerKey = Config.Bind("Strike", "PlannerKey", KeyCode.F9,
                "Open or close the strike planner.");
            StrikeAllowPromoted = Config.Bind("Strike", "AllowPromotedPlanners", true,
                "Host: let players you promoted (saved in com.ryantogo.jointfires.strike_planners.txt) plan strikes. The host can always plan.");
            StrikeAllowNuclear = Config.Bind("Strike", "AllowNuclearWeapons", false,
                "Host: let strikes use nuclear-armed missiles.");
            StrikeIgnoreEscalation = Config.Bind("Strike", "IgnoreEscalation", false,
                "Host: let strikes use strategic launchers before the mission's strategic escalation threshold is reached (the game's own AI waits for it).");

            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            try
            {
                JfNet.Tick();
                StrikeClient.Update();
                StrikePlannerWindow.Update();
            }
            catch (System.Exception e)
            {
                if (!loggedUpdateError)
                {
                    loggedUpdateError = true;
                    Log.LogError($"Update failed (logged once): {e}");
                }
            }
        }

        private void OnGUI()
        {
            try
            {
                StrikePlannerWindow.OnGUI();
            }
            catch (System.Exception e)
            {
                if (!loggedUpdateError)
                {
                    loggedUpdateError = true;
                    Log.LogError($"OnGUI failed (logged once): {e}");
                }
            }
        }

        private void OnDestroy()
        {
            JfNet.ResetAll();
            harmony?.UnpatchSelf();
        }
    }
}
