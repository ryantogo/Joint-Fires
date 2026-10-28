using Mirage.Serialization;

namespace JointFires.Net
{
    // Hand-written Mirage serializers (no weaver at mod build time), as in Multi-Crew.
    // Readers must clamp any array lengths so a malformed packet can't allocate unbounded memory.
    internal static class JfSerializers
    {
        public const int MaxSensorSources = 8;

        private static bool registered;

        public static void Register()
        {
            if (registered)
            {
                return;
            }
            registered = true;

            Writer<JfHelloMsg>.Write = (w, m) => { w.WriteByte(m.Protocol); w.WriteUInt16(m.Version); };
            Reader<JfHelloMsg>.Read = r => new JfHelloMsg { Protocol = r.ReadByte(), Version = r.ReadUInt16() };

            Writer<JfHelloAckMsg>.Write = (w, m) => { w.WriteByte(m.Protocol); w.WriteUInt16(m.Version); };
            Reader<JfHelloAckMsg>.Read = r => new JfHelloAckMsg { Protocol = r.ReadByte(), Version = r.ReadUInt16() };

            Writer<SensorQueryMsg>.Write = (w, m) => w.WriteUInt32(m.TargetId);
            Reader<SensorQueryMsg>.Read = r => new SensorQueryMsg { TargetId = r.ReadUInt32() };

            Writer<SensorReportMsg>.Write = (w, m) =>
            {
                w.WriteUInt32(m.TargetId);
                int count = m.Sources == null ? 0 : System.Math.Min(m.Sources.Length, MaxSensorSources);
                w.WriteByte((byte)count);
                for (int i = 0; i < count; i++)
                {
                    w.WriteUInt32(m.Sources[i].DetectorId);
                    w.WriteByte(m.Sources[i].Kind);
                    w.WriteUInt16(m.Sources[i].AgeTenths);
                }
            };
            Reader<SensorReportMsg>.Read = r =>
            {
                var msg = new SensorReportMsg { TargetId = r.ReadUInt32() };
                int count = r.ReadByte();
                var sources = new SensorSource[System.Math.Min(count, MaxSensorSources)];
                for (int i = 0; i < count; i++)
                {
                    var source = new SensorSource { DetectorId = r.ReadUInt32(), Kind = r.ReadByte(), AgeTenths = r.ReadUInt16() };
                    if (i < sources.Length)
                    {
                        sources[i] = source;
                    }
                }
                msg.Sources = sources;
                return msg;
            };
        }
    }
}
