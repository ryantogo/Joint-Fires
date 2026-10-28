using HarmonyLib;

namespace JointFires.Strike
{
    // Keeps launchers in a strike mission from being used by their own AI until it ends, and
    // picks up the missiles a strike round spawns.
    internal static class StrikePatches
    {
        // Turret target choice (radar scan, datalink and fire-support searches all end here).
        [HarmonyPatch(typeof(Turret), "ChooseTarget")]
        private static class TurretChooseTargetPatch
        {
            private static bool Prefix(Turret __instance) => !StrikeServer.IsReserved(__instance);
        }

        // Aiming and firing at whatever target the turret already had.
        [HarmonyPatch(typeof(Turret), "FixedUpdate")]
        private static class TurretFixedUpdatePatch
        {
            private static bool Prefix(Turret __instance) => !StrikeServer.IsReserved(__instance);
        }

        // Batteries and ships plan their own salvos from here.
        [HarmonyPatch(typeof(FireControl), "HQTargetAssessment")]
        private static class FireControlAssessmentPatch
        {
            private static bool Prefix(FireControl __instance) => !StrikeServer.IsReserved(__instance);
        }

        [HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnMissile))]
        private static class SpawnMissilePatch
        {
            private static void Postfix(Missile __result) => StrikeServer.OnMissileSpawned(__result);
        }
    }
}
