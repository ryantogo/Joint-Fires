using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace JointFires.Strike
{
    // One missile weapon station that a strike can use: a ship's launcher, a missile truck, or a
    // battery launcher driven by a FireControl unit.
    internal sealed class StrikeLauncher
    {
        public Turret Turret;
        public WeaponStation Station;
        // The unit the game fires this station as: the FireControl's unit for batteries and
        // ships, the turret's own unit otherwise.
        public Unit Owner;
        public FireControl FireControl;
        // Launch trucks that raise their launchers first (FireControl deployables).
        public GroundVehicle Deployer;
        public bool Strategic;
        public float Distance;
        public float FlightTime;

        public WeaponInfo Info => Station.WeaponInfo;
        public float Lead => Deployer != null ? StrikeLaunchers.DeployLead : StrikeLaunchers.MinLead;

        // Seconds between two rounds from this station.
        public float Interval
        {
            get
            {
                float salvo = FireControl != null ? StrikeLaunchers.SalvoInterval(FireControl) : 0f;
                return Mathf.Max(StrikeLaunchers.MinInterval, Info.fireInterval, salvo);
            }
        }

        public bool Usable => Turret != null && Station != null && Owner != null && !Owner.disabled
            && !Turret.GetAttachedUnit().disabled && Station.Ammo > 0;
    }

    // Finds the launchers that can hit a target, and estimates missile flight times. Server only.
    // Flight times start from the game's own estimate (AimSolver: range / 0.7 top speed) and are
    // calibrated per weapon from strike missiles that actually reach their target.
    internal static class StrikeLaunchers
    {
        public const float MinLead = 3f;
        // Launch trucks need time to raise their launchers.
        public const float DeployLead = 20f;
        public const float MinInterval = 1f;
        private const float AssumedSpeed = 250f;

        private static readonly AccessTools.FieldRef<Turret, WeaponStation[]> TurretStations =
            AccessTools.FieldRefAccess<Turret, WeaponStation[]>("weaponStations");
        private static readonly AccessTools.FieldRef<Turret, FireControl> TurretFireControl =
            AccessTools.FieldRefAccess<Turret, FireControl>("fireControl");
        private static readonly AccessTools.FieldRef<FireControl, Unit> FireControlUnit =
            AccessTools.FieldRefAccess<FireControl, Unit>("attachedUnit");
        private static readonly AccessTools.FieldRef<FireControl, float> FireControlSalvoInterval =
            AccessTools.FieldRefAccess<FireControl, float>("salvoInterval");

        // Measured / estimated flight time, per weapon.
        private static readonly Dictionary<WeaponInfo, float> calibration = new Dictionary<WeaponInfo, float>();

        public static float SalvoInterval(FireControl fireControl) => FireControlSalvoInterval(fireControl);

        public static List<StrikeLauncher> Find(FactionHQ hq, Unit target)
        {
            var found = new List<StrikeLauncher>();
            if (hq == null || target == null)
            {
                return found;
            }
            GlobalPosition targetPosition = hq.TryGetKnownPosition(target, out GlobalPosition known) ? known : target.GlobalPosition();

            foreach (Turret turret in Object.FindObjectsOfType<Turret>())
            {
                Unit turretUnit = turret.GetAttachedUnit();
                if (turretUnit == null || turretUnit.disabled)
                {
                    continue;
                }
                FireControl fireControl = TurretFireControl(turret);
                Unit owner = fireControl != null ? FireControlUnit(fireControl) : turretUnit;
                if (owner == null || owner.disabled || owner.NetworkHQ != hq)
                {
                    continue;
                }
                WeaponStation[] stations = TurretStations(turret);
                if (stations == null)
                {
                    continue;
                }
                foreach (WeaponStation station in stations)
                {
                    if (!IsStrikeWeapon(station))
                    {
                        continue;
                    }
                    bool strategic = station.WeaponInfo.strategic || IsStrategicFireControl(fireControl);
                    if (strategic && !StrategicAllowed())
                    {
                        continue;
                    }
                    float distance = FastMath.Distance(owner.GlobalPosition(), targetPosition);
                    if (!CanEngage(station, owner, target, distance))
                    {
                        continue;
                    }
                    var launcher = new StrikeLauncher
                    {
                        Turret = turret,
                        Station = station,
                        Owner = owner,
                        FireControl = fireControl,
                        Deployer = HasDeployables(fireControl) ? owner as GroundVehicle : null,
                        Strategic = strategic,
                        Distance = distance,
                    };
                    launcher.FlightTime = EstimateFlightTime(station.WeaponInfo, distance);
                    found.Add(launcher);
                }
            }
            found.Sort((a, b) => a.FlightTime.CompareTo(b.FlightTime));
            return found;
        }

        public static string LauncherName(StrikeLauncher launcher)
        {
            Unit unit = launcher.Owner;
            string name = unit.definition != null ? unit.definition.unitName : unit.unitName;
            return $"{name} ({launcher.Distance / 1000f:0} km)";
        }

        public static float EstimateFlightTime(WeaponInfo info, float distance)
        {
            float speed = info.GetMaxSpeed();
            if (speed <= 1f)
            {
                speed = AssumedSpeed;
            }
            float estimate = distance / (0.7f * speed);
            if (calibration.TryGetValue(info, out float factor))
            {
                estimate *= factor;
            }
            return Mathf.Clamp(estimate, 1f, 3600f);
        }

        // A strike missile reached its target: nudge this weapon's estimate toward reality.
        public static void Calibrate(WeaponInfo info, float predicted, float measured)
        {
            if (info == null || predicted <= 0.5f || measured <= 0.5f)
            {
                return;
            }
            float current = calibration.TryGetValue(info, out float factor) ? factor : 1f;
            float corrected = Mathf.Clamp(current * measured / predicted, 0.3f, 3f);
            calibration[info] = Mathf.Lerp(current, corrected, 0.5f);
            Plugin.Log.LogInfo($"[Strike] {info.weaponName}: flight {measured:0.0}s vs estimate {predicted:0.0}s, factor now {calibration[info]:0.00}");
        }

        public static void Reset() => calibration.Clear();

        private static bool IsStrikeWeapon(WeaponStation station)
        {
            if (station == null || station.Ammo <= 0 || station.Weapons == null || station.Weapons.Count == 0)
            {
                return false;
            }
            WeaponInfo info = station.WeaponInfo;
            if (info == null || !info.missile || info.troops || info.cargo || info.jammer || info.energy)
            {
                return false;
            }
            if (info.nuclear && !Plugin.StrikeAllowNuclear.Value)
            {
                return false;
            }
            return station.Weapons[0] is MissileLauncher;
        }

        private static bool CanEngage(WeaponStation station, Unit owner, Unit target, float distance)
        {
            TargetRequirements requirements = station.WeaponInfo.targetRequirements;
            if (distance > requirements.maxRange || distance < requirements.minRange)
            {
                return false;
            }
            if (requirements.maxSpeed > 0f && target.speed > requirements.maxSpeed)
            {
                return false;
            }
            if (requirements.maxAltitude > 0f && target.radarAlt > requirements.maxAltitude)
            {
                return false;
            }
            // The game's own suitability score: anti-ship missiles at ships, land attack at ground, etc.
            return station.CalcOpportunityThreat(target.definition, owner).opportunity > 0f;
        }

        private static bool IsStrategicFireControl(FireControl fireControl)
        {
            return fireControl != null && Traverse.Create(fireControl).Field("targetAcquisitionMode").GetValue()?.ToString() == "strategicStrike";
        }

        private static bool HasDeployables(FireControl fireControl)
        {
            if (fireControl == null)
            {
                return false;
            }
            System.Array deployables = Traverse.Create(fireControl).Field("deployables").GetValue() as System.Array;
            return deployables != null && deployables.Length > 0;
        }

        private static bool StrategicAllowed()
        {
            if (Plugin.StrikeIgnoreEscalation.Value)
            {
                return true;
            }
            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            return mission != null && mission.currentEscalation > mission.strategicThreshold;
        }
    }
}
