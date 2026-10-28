namespace JointFires.Strike
{
    // Sent on the wire as bytes; append only.
    public enum StrikeQueryResult : byte
    {
        Ok = 0,
        NotAllowed = 1,
        TargetNotFound = 2,
        TargetNotTracked = 3,
        NoLaunchers = 4,
    }

    public enum StrikeState : byte
    {
        // Waiting for launch times.
        Planned = 0,
        // Some rounds fired, more to come.
        Launching = 1,
        // Everything fired; waiting for impact.
        InFlight = 2,
        Complete = 3,
        Aborted = 4,
        TargetDestroyed = 5,
        // The order was refused or nothing could be fired; Note says why.
        Failed = 6,
    }

    internal static class StrikeText
    {
        public static string Describe(StrikeQueryResult result)
        {
            switch (result)
            {
                case StrikeQueryResult.NotAllowed: return "You're not allowed to plan strikes on this server";
                case StrikeQueryResult.TargetNotFound: return "That target no longer exists";
                case StrikeQueryResult.TargetNotTracked: return "Your faction isn't tracking that target";
                case StrikeQueryResult.NoLaunchers: return "No friendly ship or launcher can reach that target";
                default: return "";
            }
        }

        public static string Describe(StrikeState state)
        {
            switch (state)
            {
                case StrikeState.Planned: return "Planned";
                case StrikeState.Launching: return "Launching";
                case StrikeState.InFlight: return "In flight";
                case StrikeState.Complete: return "Complete";
                case StrikeState.Aborted: return "Aborted";
                case StrikeState.TargetDestroyed: return "Target destroyed";
                default: return "Failed";
            }
        }

        public static bool IsFinal(StrikeState state) => state >= StrikeState.Complete;
    }
}
