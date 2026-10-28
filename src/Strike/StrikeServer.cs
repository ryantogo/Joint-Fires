using System.Collections.Generic;
using JointFires.Net;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace JointFires.Strike
{
    // Server side of strike missions: answers launcher queries, validates orders, and runs each
    // mission. Every round is fired the way the game's own FireControl fires a salvo
    // (WeaponStation.Fire on the server), timed so all rounds arrive at the time on target (TOT).
    // Launchers in a mission are reserved: their own AI won't pick targets or fire until it ends.
    internal static class StrikeServer
    {
        private const float StatusInterval = 1f;
        // How long a round may wait for its station to become ready before it's dropped.
        private const float LaunchGrace = 10f;
        // After TOT, how long to wait for stragglers before calling it complete.
        private const float CompleteAfter = 30f;
        private const float StowAfter = 5f;
        // A strike missile that ends within this distance of the target counts as arrived.
        private const float ArrivalRadius = 300f;

        private class Query
        {
            public uint Id;
            public Unit Target;
            public List<StrikeLauncher> Launchers;
        }

        private class Round
        {
            public StrikeLauncher Launcher;
            public float LaunchAt;
            public bool Done;
        }

        private class Flight
        {
            public Missile Missile;
            public WeaponInfo Info;
            public float LaunchedAt;
            public float Predicted;
        }

        private class Mission
        {
            public uint Id;
            public INetworkPlayer Planner;
            public FactionHQ HQ;
            public Unit Target;
            public uint TargetId;
            public float Tot;
            public readonly List<Round> Rounds = new List<Round>();
            public readonly List<Flight> Flights = new List<Flight>();
            public readonly HashSet<GroundVehicle> Deployed = new HashSet<GroundVehicle>();
            public int Launched;
            public int Dropped;
            public StrikeState State;
            public string Note = string.Empty;
            public float NextStatus;
            public float LastLaunch;
        }

        private static readonly Dictionary<INetworkPlayer, Query> queries = new Dictionary<INetworkPlayer, Query>();
        private static readonly List<Mission> missions = new List<Mission>();
        private static readonly HashSet<Turret> reserved = new HashSet<Turret>();
        private static readonly HashSet<FireControl> reservedFireControls = new HashSet<FireControl>();
        private static uint nextQueryId = 1;
        private static uint nextMissionId = 1;

        // Set while a strike round is being fired, so the Spawner patch can pick up the missile.
        private static Mission firingMission;
        private static StrikeLauncher firingLauncher;

        public static bool IsReserved(Turret turret) => turret != null && reserved.Contains(turret);
        public static bool IsReserved(FireControl fireControl) => fireControl != null && reservedFireControls.Contains(fireControl);

        public static void Reset()
        {
            queries.Clear();
            missions.Clear();
            reserved.Clear();
            reservedFireControls.Clear();
            firingMission = null;
            firingLauncher = null;
            StrikeLaunchers.Reset();
        }

        // ---- permissions ----

        public static void OnPeerReady(INetworkPlayer player) => SendPermission(player);

        public static void OnDisconnected(INetworkPlayer player) => queries.Remove(player);

        public static void PermissionsChanged()
        {
            NetworkServer server = JfNet.Server;
            if (server == null)
            {
                return;
            }
            foreach (INetworkPlayer player in server.AuthenticatedPlayers)
            {
                SendPermission(player);
            }
        }

        private static void SendPermission(INetworkPlayer player)
        {
            JfNet.SendToPlayer(player, new StrikePermissionMsg { Allowed = StrikePlanners.IsAllowed(player) });
        }

        // ---- handlers ----

        public static void OnAssetQuery(INetworkPlayer player, StrikeAssetQueryMsg msg)
        {
            var reply = new StrikeAssetsMsg { TargetId = msg.TargetId, Assets = new StrikeAsset[0] };
            StrikeQueryResult result = CheckTarget(player, msg.TargetId, out Player requester, out Unit target);
            if (result == StrikeQueryResult.Ok)
            {
                List<StrikeLauncher> launchers = StrikeLaunchers.Find(requester.HQ, target);
                launchers.RemoveAll(l => IsReserved(l.Turret));
                if (launchers.Count > JfSerializers.MaxStrikeAssets)
                {
                    launchers.RemoveRange(JfSerializers.MaxStrikeAssets, launchers.Count - JfSerializers.MaxStrikeAssets);
                }
                if (launchers.Count == 0)
                {
                    result = StrikeQueryResult.NoLaunchers;
                }
                else
                {
                    var query = new Query { Id = nextQueryId++, Target = target, Launchers = launchers };
                    queries[player] = query;
                    reply.QueryId = query.Id;
                    reply.Assets = new StrikeAsset[launchers.Count];
                    for (int i = 0; i < launchers.Count; i++)
                    {
                        StrikeLauncher l = launchers[i];
                        reply.Assets[i] = new StrikeAsset
                        {
                            Index = (ushort)i,
                            UnitId = l.Owner.persistentID.Id,
                            Name = StrikeLaunchers.LauncherName(l),
                            Weapon = l.Info.weaponName,
                            Rounds = (byte)Mathf.Min(l.Station.Ammo, 255),
                            FlightTenths = Tenths(l.FlightTime),
                            LeadTenths = Tenths(l.Lead),
                        };
                    }
                }
            }
            reply.Result = (byte)result;
            JfNet.SendToPlayer(player, reply);
            Plugin.Log.LogInfo($"[Strike] {player} asked for launchers on {msg.TargetId}: {result}, {reply.Assets.Length} found");
        }

        public static void OnOrder(INetworkPlayer player, StrikeOrderMsg msg)
        {
            StrikeQueryResult check = CheckTarget(player, msg.TargetId, out Player requester, out Unit target);
            if (check != StrikeQueryResult.Ok)
            {
                Refuse(player, msg.TargetId, StrikeText.Describe(check));
                return;
            }
            if (!queries.TryGetValue(player, out Query query) || query.Id != msg.QueryId || query.Target != target)
            {
                Refuse(player, msg.TargetId, "The launcher list is out of date; search again");
                return;
            }

            var picks = new List<(StrikeLauncher launcher, int rounds)>();
            var seen = new HashSet<WeaponStation>();
            foreach (StrikePick pick in msg.Picks ?? new StrikePick[0])
            {
                if (pick.Index >= query.Launchers.Count || pick.Rounds == 0)
                {
                    continue;
                }
                StrikeLauncher launcher = query.Launchers[pick.Index];
                if (!launcher.Usable || IsReserved(launcher.Turret) || !seen.Add(launcher.Station))
                {
                    continue;
                }
                picks.Add((launcher, Mathf.Min(pick.Rounds, launcher.Station.Ammo)));
            }
            if (picks.Count == 0)
            {
                Refuse(player, msg.TargetId, "None of the chosen launchers can fire any more");
                return;
            }

            float now = Time.timeSinceLevelLoad;
            float earliest = 0f;
            foreach ((StrikeLauncher launcher, int rounds) in picks)
            {
                earliest = Mathf.Max(earliest, launcher.Lead + launcher.FlightTime + (rounds - 1) * launcher.Interval / 2f);
            }
            if (msg.TotSeconds < earliest)
            {
                Refuse(player, msg.TargetId, $"Time on target too soon: needs at least {Mathf.CeilToInt(earliest)} s");
                return;
            }

            var mission = new Mission
            {
                Id = nextMissionId++,
                Planner = player,
                HQ = requester.HQ,
                Target = target,
                TargetId = target.persistentID.Id,
                Tot = now + msg.TotSeconds,
                State = StrikeState.Planned,
            };
            foreach ((StrikeLauncher launcher, int rounds) in picks)
            {
                // Centre the station's rounds on the TOT; one station can't fire them all at once.
                float first = mission.Tot - launcher.FlightTime - (rounds - 1) * launcher.Interval / 2f;
                for (int i = 0; i < rounds; i++)
                {
                    mission.Rounds.Add(new Round { Launcher = launcher, LaunchAt = first + i * launcher.Interval });
                }
                reserved.Add(launcher.Turret);
                if (launcher.FireControl != null)
                {
                    reservedFireControls.Add(launcher.FireControl);
                }
            }
            mission.Rounds.Sort((a, b) => a.LaunchAt.CompareTo(b.LaunchAt));
            missions.Add(mission);
            queries.Remove(player);
            Plugin.Log.LogInfo($"[Strike] Mission {mission.Id} by {player}: {mission.Rounds.Count} rounds from {picks.Count} launchers on {target.unitName}, TOT in {msg.TotSeconds}s");
            SendStatus(mission);
        }

        public static void OnAbort(INetworkPlayer player, StrikeAbortMsg msg)
        {
            Mission mission = missions.Find(m => m.Id == msg.MissionId);
            if (mission == null || (mission.Planner != player && !player.IsHost))
            {
                return;
            }
            Finish(mission, StrikeState.Aborted, "Aborted by " + PlayerName(player));
        }

        // ---- running missions ----

        public static void Tick()
        {
            float now = Time.timeSinceLevelLoad;
            foreach (Mission mission in missions.ToArray())
            {
                try
                {
                    Run(mission, now);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"[Strike] Mission {mission.Id} failed: {e}");
                    Finish(mission, StrikeState.Failed, "Internal error; see the host's log");
                }
            }
        }

        private static void Run(Mission mission, float now)
        {
            if (mission.Target == null || mission.Target.disabled)
            {
                if (mission.Launched == 0)
                {
                    Finish(mission, StrikeState.Aborted, "Target destroyed before launch");
                }
                else
                {
                    Finish(mission, StrikeState.TargetDestroyed, string.Empty);
                }
                return;
            }

            // Raise truck launchers ahead of their first round.
            foreach (Round round in mission.Rounds)
            {
                GroundVehicle deployer = round.Launcher.Deployer;
                if (!round.Done && deployer != null && !mission.Deployed.Contains(deployer) && now >= round.LaunchAt - StrikeLaunchers.DeployLead)
                {
                    mission.Deployed.Add(deployer);
                    deployer.RpcDeployFireControl(deploy: true);
                }
            }

            foreach (Round round in mission.Rounds)
            {
                if (round.Done || now < round.LaunchAt)
                {
                    continue;
                }
                if (!round.Launcher.Usable)
                {
                    round.Done = true;
                    mission.Dropped++;
                    continue;
                }
                if (TryFire(mission, round.Launcher))
                {
                    round.Done = true;
                    mission.Launched++;
                    mission.LastLaunch = now;
                    mission.State = StrikeState.Launching;
                    mission.NextStatus = 0f;
                }
                else if (now > round.LaunchAt + LaunchGrace)
                {
                    round.Done = true;
                    mission.Dropped++;
                    Plugin.Log.LogWarning($"[Strike] Mission {mission.Id}: {StrikeLaunchers.LauncherName(round.Launcher)} wasn't ready to fire; round dropped");
                }
            }

            bool allDone = mission.Rounds.TrueForAll(r => r.Done);
            if (allDone && mission.State < StrikeState.InFlight)
            {
                if (mission.Launched == 0)
                {
                    Finish(mission, StrikeState.Failed, "No round could be fired");
                    return;
                }
                mission.State = StrikeState.InFlight;
                mission.Note = mission.Dropped > 0 ? $"{mission.Dropped} round(s) couldn't fire" : string.Empty;
                ReleaseLaunchers(mission, stow: false);
                mission.NextStatus = 0f;
            }
            if (mission.State == StrikeState.InFlight)
            {
                if (now > mission.LastLaunch + StowAfter)
                {
                    Stow(mission);
                }
                TrackFlights(mission, now);
                bool flightsOver = mission.Flights.Count == 0 && now > mission.Tot;
                if (flightsOver || now > mission.Tot + CompleteAfter)
                {
                    Finish(mission, StrikeState.Complete, mission.Note);
                    return;
                }
            }

            if (now >= mission.NextStatus)
            {
                SendStatus(mission);
            }
        }

        private static bool TryFire(Mission mission, StrikeLauncher launcher)
        {
            WeaponStation station = launcher.Station;
            int before = station.Ammo;
            firingMission = mission;
            firingLauncher = launcher;
            try
            {
                station.Fire(launcher.Owner, mission.Target);
            }
            finally
            {
                firingMission = null;
                firingLauncher = null;
            }
            return station.Ammo < before;
        }

        // Called by the Spawner patch for each missile spawned during TryFire.
        public static void OnMissileSpawned(Missile missile)
        {
            if (firingMission == null || missile == null)
            {
                return;
            }
            Mission mission = firingMission;
            WeaponInfo info = firingLauncher.Info;
            float distance = FastMath.Distance(missile.GlobalPosition(), mission.Target.GlobalPosition());
            mission.Flights.Add(new Flight
            {
                Missile = missile,
                Info = info,
                LaunchedAt = Time.timeSinceLevelLoad,
                Predicted = StrikeLaunchers.EstimateFlightTime(info, distance),
            });
        }

        private static void TrackFlights(Mission mission, float now)
        {
            for (int i = mission.Flights.Count - 1; i >= 0; i--)
            {
                Flight flight = mission.Flights[i];
                if (flight.Missile != null && !flight.Missile.disabled)
                {
                    continue;
                }
                mission.Flights.RemoveAt(i);
                if (flight.Missile != null && mission.Target != null
                    && FastMath.Distance(flight.Missile.GlobalPosition(), mission.Target.GlobalPosition()) < ArrivalRadius)
                {
                    StrikeLaunchers.Calibrate(flight.Info, flight.Predicted, now - flight.LaunchedAt);
                }
            }
        }

        private static void Finish(Mission mission, StrikeState state, string note)
        {
            mission.State = state;
            mission.Note = note ?? string.Empty;
            foreach (Round round in mission.Rounds)
            {
                round.Done = true;
            }
            ReleaseLaunchers(mission, stow: true);
            SendStatus(mission);
            missions.Remove(mission);
            Plugin.Log.LogInfo($"[Strike] Mission {mission.Id} {state}: {mission.Launched} launched, {mission.Dropped} dropped. {mission.Note}");
        }

        private static void ReleaseLaunchers(Mission mission, bool stow)
        {
            foreach (Round round in mission.Rounds)
            {
                reserved.Remove(round.Launcher.Turret);
                if (round.Launcher.FireControl != null)
                {
                    reservedFireControls.Remove(round.Launcher.FireControl);
                }
            }
            if (stow)
            {
                Stow(mission);
            }
        }

        private static void Stow(Mission mission)
        {
            foreach (GroundVehicle deployer in mission.Deployed)
            {
                if (deployer != null && !deployer.disabled)
                {
                    deployer.RpcDeployFireControl(deploy: false);
                }
            }
            mission.Deployed.Clear();
        }

        // ---- messages ----

        private static void SendStatus(Mission mission)
        {
            mission.NextStatus = Time.timeSinceLevelLoad + StatusInterval;
            var msg = new StrikeStatusMsg
            {
                MissionId = mission.Id,
                TargetId = mission.TargetId,
                State = (byte)mission.State,
                TotTenths = Mathf.RoundToInt((mission.Tot - Time.timeSinceLevelLoad) * 10f),
                Launched = (byte)Mathf.Min(mission.Launched, 255),
                Planned = (byte)Mathf.Min(mission.Rounds.Count, 255),
                Note = mission.Note,
            };
            JfNet.SendToPlayer(mission.Planner, msg);
            INetworkPlayer host = HostPlayer();
            if (host != null && host != mission.Planner)
            {
                JfNet.SendToPlayer(host, msg);
            }
        }

        private static void Refuse(INetworkPlayer player, uint targetId, string reason)
        {
            JfNet.SendToPlayer(player, new StrikeStatusMsg { TargetId = targetId, State = (byte)StrikeState.Failed, Note = reason });
            Plugin.Log.LogInfo($"[Strike] Order from {player} refused: {reason}");
        }

        private static StrikeQueryResult CheckTarget(INetworkPlayer player, uint targetId, out Player requester, out Unit target)
        {
            target = null;
            if (!JfNet.IsModded(player) || !player.TryGetPlayer<Player>(out requester) || requester.HQ == null)
            {
                requester = null;
                return StrikeQueryResult.NotAllowed;
            }
            if (!StrikePlanners.IsAllowed(player))
            {
                return StrikeQueryResult.NotAllowed;
            }
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = targetId }, out target) || target == null || target.disabled)
            {
                return StrikeQueryResult.TargetNotFound;
            }
            if (target.NetworkHQ == requester.HQ || !requester.HQ.trackingDatabase.ContainsKey(target.persistentID))
            {
                return StrikeQueryResult.TargetNotTracked;
            }
            return StrikeQueryResult.Ok;
        }

        private static INetworkPlayer HostPlayer()
        {
            NetworkServer server = JfNet.Server;
            if (server == null)
            {
                return null;
            }
            foreach (INetworkPlayer player in server.AuthenticatedPlayers)
            {
                if (player.IsHost)
                {
                    return player;
                }
            }
            return null;
        }

        private static string PlayerName(INetworkPlayer connection)
        {
            return connection.TryGetPlayer<Player>(out Player player) ? player.GetDisplayName(PlayerNameContext.Other) : "a player";
        }

        private static ushort Tenths(float seconds) => (ushort)Mathf.Clamp(Mathf.RoundToInt(seconds * 10f), 0, ushort.MaxValue);
    }
}
