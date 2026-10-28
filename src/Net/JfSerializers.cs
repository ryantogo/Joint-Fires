using Mirage.Serialization;

namespace JointFires.Net
{
    // Hand-written Mirage serializers (no weaver at mod build time), as in Multi-Crew.
    // Readers must clamp any array lengths so a malformed packet can't allocate unbounded memory.
    internal static class JfSerializers
    {
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
        }
    }
}
