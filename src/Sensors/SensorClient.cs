using System.Collections.Generic;
using System.Text;
using JointFires.Net;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace JointFires.Sensors
{
    // Client side of sensor attribution: asks the server about targets the player is looking at
    // (map tooltip, map selection, HUD target) and formats the answer. Nothing is broadcast; a
    // target is only queried while something on screen shows it.
    internal static class SensorClient
    {
        // Within this age a source counts as detecting now (the game's tracking uses 4 s).
        public const float LiveAge = 4f;
        private const float RefreshAfter = 1f;
        private const float RequestTimeout = 2f;

        private class Entry
        {
            public SensorSource[] Sources = new SensorSource[0];
            public float Received = -100f;
            public float Requested = -100f;
        }

        private static readonly Dictionary<uint, Entry> cache = new Dictionary<uint, Entry>();
        private static readonly StringBuilder text = new StringBuilder();

        public static void Reset() => cache.Clear();

        public static void OnReport(INetworkPlayer _, SensorReportMsg msg)
        {
            if (!cache.TryGetValue(msg.TargetId, out Entry entry))
            {
                entry = new Entry();
                cache[msg.TargetId] = entry;
            }
            entry.Sources = msg.Sources ?? new SensorSource[0];
            entry.Received = Time.unscaledTime;
        }

        // One line per detection kind group, e.g. "Radar: EW-25 Medusa (Ryan), SAM-3" or
        // "Last seen 42s ago: Visual: AFV". Null when there's nothing to show (yet).
        public static string Describe(Unit target, bool compact)
        {
            if (target == null || !JfNet.ServerHasMod || !IsEnemyOfLocal(target))
            {
                return null;
            }
            Entry entry = Request(target.persistentID.Id);
            if (entry == null || entry.Sources.Length == 0)
            {
                return null;
            }
            // Ages are as of when the report arrived.
            float extra = Time.unscaledTime - entry.Received;

            text.Clear();
            bool anyLive = false;
            for (int i = 0; i < entry.Sources.Length; i++)
            {
                SensorSource source = entry.Sources[i];
                if (source.AgeTenths / 10f + extra > LiveAge)
                {
                    continue;
                }
                if (anyLive)
                {
                    text.Append(compact ? " " : ", ");
                }
                anyLive = true;
                AppendSource(source, compact);
            }
            if (anyLive)
            {
                return text.ToString();
            }

            // Nobody sees it now: show who saw it last.
            SensorSource newest = entry.Sources[0];
            int age = Mathf.RoundToInt(newest.AgeTenths / 10f + extra);
            text.Append(compact ? $"{age}s " : $"Last seen {age}s ago by ");
            AppendSource(newest, compact);
            return text.ToString();
        }

        private static Entry Request(uint targetId)
        {
            float now = Time.unscaledTime;
            if (!cache.TryGetValue(targetId, out Entry entry))
            {
                entry = new Entry();
                cache[targetId] = entry;
            }
            bool stale = now - entry.Received > RefreshAfter;
            bool waiting = entry.Requested > entry.Received && now - entry.Requested < RequestTimeout;
            if (stale && !waiting)
            {
                entry.Requested = now;
                JfNet.SendToServer(new SensorQueryMsg { TargetId = targetId });
            }
            return entry;
        }

        private static void AppendSource(SensorSource source, bool compact)
        {
            var kind = (SensorKind)source.Kind;
            text.Append(compact ? ShortKind(kind) : LongKind(kind));
            text.Append(compact ? " " : ": ");
            text.Append(DetectorName(source.DetectorId, compact));
        }

        private static string DetectorName(uint detectorId, bool compact)
        {
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = detectorId }, out Unit unit) || unit == null)
            {
                return compact ? "?" : "unknown unit";
            }
            if (unit is Missile missile && missile.owner != null)
            {
                // A missile's seeker: name the shooter, it's who the player knows.
                return compact ? missile.owner.definition.code : $"{missile.definition.unitName} from {UnitName(missile.owner)}";
            }
            return compact ? unit.definition.code : UnitName(unit);
        }

        private static string UnitName(Unit unit)
        {
            string name = unit.definition != null ? unit.definition.unitName : unit.unitName;
            if (unit is Aircraft aircraft && aircraft.Player != null)
            {
                name += $" ({aircraft.Player.GetDisplayName(PlayerNameContext.Other)})";
            }
            return name;
        }

        private static string LongKind(SensorKind kind)
        {
            switch (kind)
            {
                case SensorKind.Radar: return "Radar";
                case SensorKind.Visual: return "Visual/IR";
                case SensorKind.MissileSeeker: return "Seeker";
                case SensorKind.Laser: return "Laser";
                case SensorKind.RadarWarning: return "RWR";
                case SensorKind.MissileWarning: return "MAW";
                default: return "Reported";
            }
        }

        private static string ShortKind(SensorKind kind)
        {
            switch (kind)
            {
                case SensorKind.Radar: return "RDR";
                case SensorKind.Visual: return "VIS";
                case SensorKind.MissileSeeker: return "SKR";
                case SensorKind.Laser: return "LSR";
                case SensorKind.RadarWarning: return "RWR";
                case SensorKind.MissileWarning: return "MAW";
                default: return "RPT";
            }
        }

        private static bool IsEnemyOfLocal(Unit target)
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player.HQ == null)
            {
                return false;
            }
            return target.NetworkHQ != null && target.NetworkHQ != player.HQ;
        }
    }
}
