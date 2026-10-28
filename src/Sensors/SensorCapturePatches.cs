using System.Linq;
using System.Reflection;
using HarmonyLib;
using JointFires.Net;
using Mirage;
using NuclearOption.Networking;

namespace JointFires.Sensors
{
    // Server-side hooks that see a detection while the detecting unit is still known. The game
    // drops it right after: FactionHQ.RpcUpdateTrackingInfo carries only the target's ID.
    internal static class SensorCapturePatches
    {
        // Set while a local reporter (laser, RWR, missile warning) may call CmdUpdateTrackingInfo.
        private static Unit reporter;
        private static SensorKind reporterKind;
        // Set while the server handles a CmdUpdateTrackingInfo from a remote client.
        private static INetworkPlayer remoteSender;

        private static bool ServerActive => JfNet.Server != null;

        // Every radar, visual and IRST detection funnels through here (DetectorManager job results).
        [HarmonyPatch(typeof(TargetDetector), nameof(TargetDetector.DetectTarget))]
        private static class DetectTargetPatch
        {
            private static void Postfix(TargetDetector __instance, Unit target)
            {
                if (!ServerActive)
                {
                    return;
                }
                Unit detector = __instance.GetAttachedUnit();
                if (detector == null)
                {
                    return;
                }
                SensorKind kind = detector is Missile ? SensorKind.MissileSeeker
                    : __instance is Radar ? SensorKind.Radar
                    : SensorKind.Visual;
                SensorServer.Record(detector.NetworkHQ, target, detector, kind);
            }
        }

        // On the server (host or dedicated) these reporters call CmdUpdateTrackingInfo, which runs
        // locally there, so we know the aircraft. Remote players' reports arrive through the
        // skeleton below instead.
        [HarmonyPatch(typeof(MissileWarning), "Update")]
        private static class MissileWarningPatch
        {
            private static void Prefix(MissileWarning __instance) => Begin(Traverse.Create(__instance).Field<Aircraft>("aircraft").Value, SensorKind.MissileWarning);
            private static void Finalizer() => End();
        }

        [HarmonyPatch(typeof(RadarLocator), "RadarLocator_OnRadarWarning")]
        private static class RadarLocatorPatch
        {
            private static void Prefix(RadarLocator __instance) => Begin(Traverse.Create(__instance).Field<Aircraft>("aircraft").Value, SensorKind.RadarWarning);
            private static void Finalizer() => End();
        }

        // LaseTargets is async, so patch its state machine's MoveNext.
        [HarmonyPatch]
        private static class LaserDesignatorPatch
        {
            private static MethodBase target;

            private static bool Prepare()
            {
                MethodInfo method = AccessTools.Method(typeof(LaserDesignator), "LaseTargets");
                var stateMachine = method?.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>();
                target = stateMachine != null ? AccessTools.Method(stateMachine.StateMachineType, "MoveNext") : null;
                if (target == null)
                {
                    Plugin.Log.LogWarning("[Sensors] LaserDesignator.LaseTargets not found; laser reports won't be attributed");
                }
                return target != null;
            }

            private static MethodBase TargetMethod() => target;

            private static void Prefix(object __instance)
            {
                var designator = Traverse.Create(__instance).Field<LaserDesignator>("<>4__this").Value;
                Begin(designator != null ? Traverse.Create(designator).Field<Aircraft>("aircraft").Value : null, SensorKind.Laser);
            }

            private static void Finalizer() => End();
        }

        // Mirage's generated receive stub knows which connection sent the ServerRpc. Its name has a
        // hash suffix that may change between game builds, so find it by prefix.
        [HarmonyPatch]
        private static class CmdSkeletonPatch
        {
            private static MethodBase target;

            private static bool Prepare()
            {
                target = AccessTools.GetDeclaredMethods(typeof(FactionHQ))
                    .FirstOrDefault(m => m.Name.StartsWith("Skeleton_CmdUpdateTrackingInfo"));
                if (target == null)
                {
                    Plugin.Log.LogWarning("[Sensors] CmdUpdateTrackingInfo receiver not found; remote players' reports won't be attributed");
                }
                return target != null;
            }

            private static MethodBase TargetMethod() => target;

            private static void Prefix(INetworkPlayer senderConnection) => remoteSender = senderConnection;
            private static void Finalizer() => remoteSender = null;
        }

        [HarmonyPatch]
        private static class CmdUserCodePatch
        {
            private static MethodBase target;

            private static bool Prepare()
            {
                target = AccessTools.GetDeclaredMethods(typeof(FactionHQ))
                    .FirstOrDefault(m => m.Name.StartsWith("UserCode_CmdUpdateTrackingInfo"));
                return target != null;
            }

            private static MethodBase TargetMethod() => target;

            private static void Prefix(FactionHQ __instance, PersistentID id)
            {
                if (!ServerActive || !UnitRegistry.TryGetUnit(id, out Unit unit))
                {
                    return;
                }
                if (remoteSender != null)
                {
                    if (remoteSender.TryGetPlayer<Player>(out Player player) && player.Aircraft != null)
                    {
                        SensorServer.Record(__instance, unit, player.Aircraft, SensorKind.Reported);
                    }
                }
                else if (reporter != null)
                {
                    SensorServer.Record(__instance, unit, reporter, reporterKind);
                }
            }
        }

        private static void Begin(Unit unit, SensorKind kind)
        {
            reporter = ServerActive ? unit : null;
            reporterKind = kind;
        }

        private static void End() => reporter = null;
    }
}
