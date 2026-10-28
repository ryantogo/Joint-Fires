using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JointFires.Sensors
{
    // Adds the sensor line to the map tooltip, the selected-target box on the map and the
    // cockpit HUD's target info. Vanilla rewrites each of these texts itself, so we append
    // after it and never accumulate.
    internal static class SensorDisplayPatches
    {
        // Hover tooltip on the map (MapToolTip.Refresh shows GetInfoText).
        [HarmonyPatch(typeof(UnitMapIcon), nameof(UnitMapIcon.GetInfoText))]
        private static class TooltipPatch
        {
            private static void Postfix(UnitMapIcon __instance, ref string __result)
            {
                if (!Plugin.ShowOnMap.Value)
                {
                    return;
                }
                string line = SensorClient.Describe(__instance.unit, compact: false);
                if (line != null)
                {
                    __result += "\n" + line;
                }
            }
        }

        // The info block next to a selected unit on the map. infoHeading is the last line and is
        // shown, greyed and hidden together with the rest, so the sensor line rides on it.
        [HarmonyPatch(typeof(TargetMarker), "Update")]
        private static class TargetMarkerPatch
        {
            private static readonly AccessTools.FieldRef<TargetMarker, Text> InfoHeading =
                AccessTools.FieldRefAccess<TargetMarker, Text>("infoHeading");

            private const string Separator = "\n";

            private static void Postfix(TargetMarker __instance)
            {
                if (!Plugin.ShowOnMap.Value)
                {
                    return;
                }
                Text heading = InfoHeading(__instance);
                if (heading == null)
                {
                    return;
                }
                string text = heading.text ?? string.Empty;
                int cut = text.IndexOf(Separator, System.StringComparison.Ordinal);
                string vanilla = cut >= 0 ? text.Substring(0, cut) : text;
                string line = SensorClient.Describe(__instance.GetUnit(), compact: true);
                string wanted = line != null ? vanilla + Separator + line : vanilla;
                if (wanted != text)
                {
                    heading.verticalOverflow = VerticalWrapMode.Overflow;
                    heading.horizontalOverflow = HorizontalWrapMode.Overflow;
                    heading.text = wanted;
                }
            }
        }

        // Cockpit HUD: the text next to the primary target's marker.
        [HarmonyPatch(typeof(CombatHUD), "ShowTargetInfo")]
        private static class HudTargetInfoPatch
        {
            private static readonly AccessTools.FieldRef<CombatHUD, TextMeshProUGUI> TargetInfo =
                AccessTools.FieldRefAccess<CombatHUD, TextMeshProUGUI>("targetInfo");
            private static readonly AccessTools.FieldRef<CombatHUD, List<Unit>> TargetList =
                AccessTools.FieldRefAccess<CombatHUD, List<Unit>>("targetList");

            private static void Postfix(CombatHUD __instance, bool __result)
            {
                List<Unit> targets = __result && Plugin.ShowOnHud.Value ? TargetList(__instance) : null;
                if (targets == null || targets.Count == 0)
                {
                    return;
                }
                string line = SensorClient.Describe(targets[0], compact: true);
                TextMeshProUGUI info = TargetInfo(__instance);
                if (line != null && info != null)
                {
                    info.text += "\n" + line;
                }
            }
        }
    }
}
