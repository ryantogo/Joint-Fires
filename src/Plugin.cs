using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using JointFires.Net;
using MedusaMultiCrew;

namespace JointFires
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(MultiCrewGuid, BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.ryantogo.jointfires";
        public const string Name = "Joint Fires";
        public const string Version = "0.0.1";

        private const string MultiCrewGuid = "com.ryantogo.medusamulticrew";
        // MultiCrewApi members this build calls.
        private const int RequiredMultiCrewApi = 1;

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        private Harmony harmony;
        private bool loggedUpdateError;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            if (MultiCrewApi.ApiVersion < RequiredMultiCrewApi)
            {
                Log.LogError($"{Name} needs EW-25 Medusa Multi-Crew API {RequiredMultiCrewApi}, found {MultiCrewApi.ApiVersion}. Update Multi-Crew; {Name} is disabled.");
                enabled = false;
                return;
            }

            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{Name} {Version} loaded (Multi-Crew API {MultiCrewApi.ApiVersion})");
        }

        private void Update()
        {
            try
            {
                JfNet.Tick();
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

        private void OnDestroy()
        {
            JfNet.ResetAll();
            harmony?.UnpatchSelf();
        }
    }
}
