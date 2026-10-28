using System.Collections.Generic;
using JointFires.Net;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace JointFires.Sensors
{
    // Server side of sensor attribution. The game keeps only "where and when" per tracked target
    // (TrackingInfo); this records "who and how", per faction, from the capture patches, and
    // answers clients' queries for their own faction only.
    internal static class SensorServer
    {
        // A source keeps reporting for this long after its last detection (the game's tracking
        // uses 4 s); older sources stay as "last seen by" until ForgetAfter.
        private const float ForgetAfter = 120f;
        private const float PruneInterval = 5f;
        private const float MinQueryInterval = 0.05f;

        private class Source
        {
            public PersistentID Detector;
            public SensorKind Kind;
            public float Time;
        }

        private static readonly Dictionary<FactionHQ, Dictionary<PersistentID, List<Source>>> byFaction =
            new Dictionary<FactionHQ, Dictionary<PersistentID, List<Source>>>();
        private static readonly Dictionary<INetworkPlayer, float> lastQuery = new Dictionary<INetworkPlayer, float>();
        private static float nextPrune;

        public static void Reset()
        {
            byFaction.Clear();
            lastQuery.Clear();
        }

        public static void Forget(INetworkPlayer player) => lastQuery.Remove(player);

        // Called from the capture patches on the server. Detections of friendlies are ignored.
        public static void Record(FactionHQ hq, Unit target, Unit detector, SensorKind kind)
        {
            if (hq == null || target == null || detector == null || target.NetworkHQ == hq || !target.persistentID.IsValid)
            {
                return;
            }
            if (!byFaction.TryGetValue(hq, out Dictionary<PersistentID, List<Source>> targets))
            {
                targets = new Dictionary<PersistentID, List<Source>>();
                byFaction[hq] = targets;
            }
            if (!targets.TryGetValue(target.persistentID, out List<Source> sources))
            {
                sources = new List<Source>(2);
                targets[target.persistentID] = sources;
            }

            float now = Time.timeSinceLevelLoad;
            foreach (Source source in sources)
            {
                if (source.Detector.Equals(detector.persistentID) && source.Kind == kind)
                {
                    source.Time = now;
                    return;
                }
            }
            if (sources.Count >= JfSerializers.MaxSensorSources)
            {
                // Replace the stalest.
                int oldest = 0;
                for (int i = 1; i < sources.Count; i++)
                {
                    if (sources[i].Time < sources[oldest].Time)
                    {
                        oldest = i;
                    }
                }
                sources.RemoveAt(oldest);
            }
            sources.Add(new Source { Detector = detector.persistentID, Kind = kind, Time = now });
        }

        public static void Tick()
        {
            float now = Time.timeSinceLevelLoad;
            if (now < nextPrune && now >= nextPrune - PruneInterval)
            {
                return;
            }
            nextPrune = now + PruneInterval;
            var emptyTargets = new List<PersistentID>();
            foreach (Dictionary<PersistentID, List<Source>> targets in byFaction.Values)
            {
                emptyTargets.Clear();
                foreach (KeyValuePair<PersistentID, List<Source>> entry in targets)
                {
                    entry.Value.RemoveAll(s => now - s.Time > ForgetAfter || now < s.Time);
                    if (entry.Value.Count == 0 || !UnitRegistry.TryGetUnit(entry.Key, out Unit unit) || unit.disabled)
                    {
                        emptyTargets.Add(entry.Key);
                    }
                }
                foreach (PersistentID id in emptyTargets)
                {
                    targets.Remove(id);
                }
            }
        }

        public static void OnQuery(INetworkPlayer player, SensorQueryMsg msg)
        {
            if (!JfNet.IsModded(player) || !player.TryGetPlayer<Player>(out Player requester) || requester.HQ == null)
            {
                return;
            }
            float now = Time.unscaledTime;
            if (lastQuery.TryGetValue(player, out float last) && now - last < MinQueryInterval)
            {
                return;
            }
            lastQuery[player] = now;

            var report = new SensorReportMsg { TargetId = msg.TargetId, Sources = new SensorSource[0] };
            var id = new PersistentID { Id = msg.TargetId };
            // Only for targets the requester's faction tracks, so this can't reveal unknown units.
            if (requester.HQ.trackingDatabase.ContainsKey(id)
                && byFaction.TryGetValue(requester.HQ, out Dictionary<PersistentID, List<Source>> targets)
                && targets.TryGetValue(id, out List<Source> sources))
            {
                float levelTime = Time.timeSinceLevelLoad;
                var sorted = new List<Source>(sources);
                sorted.Sort((a, b) => b.Time.CompareTo(a.Time));
                report.Sources = new SensorSource[sorted.Count];
                for (int i = 0; i < sorted.Count; i++)
                {
                    float age = Mathf.Clamp(levelTime - sorted[i].Time, 0f, 6500f);
                    report.Sources[i] = new SensorSource
                    {
                        DetectorId = sorted[i].Detector.Id,
                        Kind = (byte)sorted[i].Kind,
                        AgeTenths = (ushort)Mathf.RoundToInt(age * 10f),
                    };
                }
            }
            JfNet.SendToPlayer(player, report);
        }
    }
}
