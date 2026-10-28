using System.Collections.Generic;
using JointFires.Net;
using Mirage;
using UnityEngine;

namespace JointFires.Strike
{
    // Client side of strike missions: permission, the latest launcher list and mission statuses,
    // for the planner window.
    internal static class StrikeClient
    {
        public class MissionView
        {
            public StrikeStatusMsg Status;
            public float Received;

            public StrikeState State => (StrikeState)Status.State;
            // Live countdown between status messages.
            public float SecondsToTot => Status.TotTenths / 10f - (Time.unscaledTime - Received);
        }

        public static bool Allowed { get; private set; }
        public static StrikeAssetsMsg? Assets { get; private set; }
        public static bool WaitingForAssets { get; private set; }
        // Last refusal or problem, shown in the window.
        public static string LastMessage { get; private set; }

        public static readonly Dictionary<uint, MissionView> Missions = new Dictionary<uint, MissionView>();

        // Finished missions stay listed for a while.
        private const float KeepFinished = 20f;

        public static void Reset()
        {
            Allowed = false;
            Assets = null;
            WaitingForAssets = false;
            LastMessage = null;
            Missions.Clear();
        }

        public static void OnPermission(INetworkPlayer _, StrikePermissionMsg msg)
        {
            if (Allowed != msg.Allowed)
            {
                Plugin.Log.LogInfo($"[Strike] Strike planning {(msg.Allowed ? "allowed" : "not allowed")} for you on this server");
            }
            Allowed = msg.Allowed;
        }

        public static void OnAssets(INetworkPlayer _, StrikeAssetsMsg msg)
        {
            WaitingForAssets = false;
            Assets = msg;
            var result = (StrikeQueryResult)msg.Result;
            LastMessage = result == StrikeQueryResult.Ok ? null : StrikeText.Describe(result);
        }

        public static void OnStatus(INetworkPlayer _, StrikeStatusMsg msg)
        {
            if (msg.MissionId == 0)
            {
                // A refused order.
                LastMessage = msg.Note;
                return;
            }
            bool isNew = !Missions.TryGetValue(msg.MissionId, out MissionView view);
            if (isNew)
            {
                view = new MissionView();
                Missions[msg.MissionId] = view;
                // The order went through, so the old launcher list is spent.
                Assets = null;
                LastMessage = null;
            }
            StrikeState previous = isNew ? (StrikeState)255 : view.State;
            view.Status = msg;
            view.Received = Time.unscaledTime;
            if (view.State != previous)
            {
                Plugin.Log.LogInfo($"[Strike] Mission {msg.MissionId}: {StrikeText.Describe(view.State)} {msg.Launched}/{msg.Planned} {msg.Note}");
                Notify(view);
            }
        }

        public static void Update()
        {
            var expired = new List<uint>();
            foreach (KeyValuePair<uint, MissionView> entry in Missions)
            {
                if (StrikeText.IsFinal(entry.Value.State) && Time.unscaledTime - entry.Value.Received > KeepFinished)
                {
                    expired.Add(entry.Key);
                }
            }
            foreach (uint id in expired)
            {
                Missions.Remove(id);
            }
        }

        public static void RequestAssets(Unit target)
        {
            if (target == null)
            {
                return;
            }
            Assets = null;
            LastMessage = null;
            WaitingForAssets = true;
            JfNet.SendToServer(new StrikeAssetQueryMsg { TargetId = target.persistentID.Id });
        }

        public static void Order(StrikeAssetsMsg assets, int totSeconds, List<StrikePick> picks)
        {
            LastMessage = null;
            JfNet.SendToServer(new StrikeOrderMsg
            {
                TargetId = assets.TargetId,
                QueryId = assets.QueryId,
                TotSeconds = (ushort)Mathf.Clamp(totSeconds, 0, ushort.MaxValue),
                Picks = picks.ToArray(),
            });
        }

        public static void Abort(uint missionId) => JfNet.SendToServer(new StrikeAbortMsg { MissionId = missionId });

        private static void Notify(MissionView view)
        {
            string text;
            switch (view.State)
            {
                case StrikeState.Planned: text = $"Strike planned: {view.Status.Planned} rounds, impact in {Mathf.CeilToInt(view.SecondsToTot)} s"; break;
                case StrikeState.Launching: text = "Strike launching"; break;
                case StrikeState.InFlight: text = $"Strike in flight: {view.Status.Launched} rounds, impact in {Mathf.Max(0, Mathf.CeilToInt(view.SecondsToTot))} s"; break;
                default: text = $"Strike {StrikeText.Describe(view.State).ToLowerInvariant()}"; break;
            }
            if (!string.IsNullOrEmpty(view.Status.Note))
            {
                text += $" ({view.Status.Note})";
            }
            SceneSingleton<AircraftActionsReport>.i?.ReportText(text, 5f);
        }
    }
}
