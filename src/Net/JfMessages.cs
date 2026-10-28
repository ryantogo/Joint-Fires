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
}
