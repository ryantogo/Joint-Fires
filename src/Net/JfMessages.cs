namespace JointFires.Net
{
    // Wire messages. Flat structs of primitives; serializers are registered by hand in
    // JfSerializers because the mod is built without the Mirage weaver. Mirage keys messages
    // by type name, so every name here must stay distinct from Multi-Crew's Crew*Msg types.

    // client -> server, once per session. Nothing else is sent until the server acks.
    public struct JfHelloMsg
    {
        public byte Protocol;
        public ushort Version;
    }

    // server -> client, only to clients that sent JfHelloMsg.
    public struct JfHelloAckMsg
    {
        public byte Protocol;
        public ushort Version;
    }

    // client -> server: who in my faction detects this target? TargetId is a Unit persistent ID.
    public struct SensorQueryMsg
    {
        public uint TargetId;
    }

    // server -> the asking client. Newest first; empty when nobody in their faction has seen it.
    public struct SensorReportMsg
    {
        public uint TargetId;
        public SensorSource[] Sources;
    }

    public struct SensorSource
    {
        // Persistent ID of the detecting unit (for MissileSeeker, the missile).
        public uint DetectorId;
        // SensorKind.
        public byte Kind;
        // Seconds since this source last detected the target, in tenths (capped).
        public ushort AgeTenths;
    }

    // ---- strike missions ----

    // server -> client: whether this player may plan strikes (host, or on the planner whitelist).
    // Sent after the hello and whenever it changes.
    public struct StrikePermissionMsg
    {
        public bool Allowed;
    }

    // client -> server: which of my faction's launchers can hit this target?
    public struct StrikeAssetQueryMsg
    {
        public uint TargetId;
    }

    // server -> the asking client. Asset indices are only valid for orders quoting QueryId.
    public struct StrikeAssetsMsg
    {
        public uint TargetId;
        public uint QueryId;
        // StrikeQueryResult.
        public byte Result;
        public StrikeAsset[] Assets;
    }

    public struct StrikeAsset
    {
        public ushort Index;
        // Persistent ID of the launching unit (ship, launcher vehicle or battery).
        public uint UnitId;
        public string Name;
        public string Weapon;
        public byte Rounds;
        // Estimated launch-to-impact time and the setup time needed before the first launch.
        public ushort FlightTenths;
        public ushort LeadTenths;
    }

    // client -> server: launch these so they all arrive TotSeconds from now.
    public struct StrikeOrderMsg
    {
        public uint TargetId;
        public uint QueryId;
        public ushort TotSeconds;
        public StrikePick[] Picks;
    }

    public struct StrikePick
    {
        public ushort Index;
        public byte Rounds;
    }

    // client -> server.
    public struct StrikeAbortMsg
    {
        public uint MissionId;
    }

    // server -> the planner and the host, on every change and once a second while active.
    public struct StrikeStatusMsg
    {
        public uint MissionId;
        public uint TargetId;
        // StrikeState.
        public byte State;
        // Seconds until time on target, in tenths; negative after it.
        public int TotTenths;
        public byte Launched;
        public byte Planned;
        public string Note;
    }
}
