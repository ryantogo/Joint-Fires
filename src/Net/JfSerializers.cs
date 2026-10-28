using Mirage.Serialization;

namespace JointFires.Net
{
    // Hand-written Mirage serializers (no weaver at mod build time), as in Multi-Crew.
    // Readers must clamp any array lengths so a malformed packet can't allocate unbounded memory.
    internal static class JfSerializers
    {
        public const int MaxSensorSources = 8;
        public const int MaxStrikeAssets = 64;
        // Characters, for unit/weapon names and status notes.
        private const int MaxText = 64;

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

            Writer<StrikePermissionMsg>.Write = (w, m) => w.WriteBoolean(m.Allowed);
            Reader<StrikePermissionMsg>.Read = r => new StrikePermissionMsg { Allowed = r.ReadBoolean() };

            Writer<StrikeAssetQueryMsg>.Write = (w, m) => w.WriteUInt32(m.TargetId);
            Reader<StrikeAssetQueryMsg>.Read = r => new StrikeAssetQueryMsg { TargetId = r.ReadUInt32() };

            Writer<StrikeAssetsMsg>.Write = (w, m) =>
            {
                w.WriteUInt32(m.TargetId);
                w.WriteUInt32(m.QueryId);
                w.WriteByte(m.Result);
                int count = m.Assets == null ? 0 : System.Math.Min(m.Assets.Length, MaxStrikeAssets);
                w.WriteByte((byte)count);
                for (int i = 0; i < count; i++)
                {
                    StrikeAsset a = m.Assets[i];
                    w.WriteUInt16(a.Index);
                    w.WriteUInt32(a.UnitId);
                    w.WriteString(Clip(a.Name), MaxText);
                    w.WriteString(Clip(a.Weapon), MaxText);
                    w.WriteByte(a.Rounds);
                    w.WriteUInt16(a.FlightTenths);
                    w.WriteUInt16(a.LeadTenths);
                }
            };
            Reader<StrikeAssetsMsg>.Read = r =>
            {
                var msg = new StrikeAssetsMsg { TargetId = r.ReadUInt32(), QueryId = r.ReadUInt32(), Result = r.ReadByte() };
                int count = r.ReadByte();
                var assets = new StrikeAsset[System.Math.Min(count, MaxStrikeAssets)];
                for (int i = 0; i < count; i++)
                {
                    var a = new StrikeAsset
                    {
                        Index = r.ReadUInt16(),
                        UnitId = r.ReadUInt32(),
                        Name = r.ReadString(MaxText),
                        Weapon = r.ReadString(MaxText),
                        Rounds = r.ReadByte(),
                        FlightTenths = r.ReadUInt16(),
                        LeadTenths = r.ReadUInt16(),
                    };
                    if (i < assets.Length)
                    {
                        assets[i] = a;
                    }
                }
                msg.Assets = assets;
                return msg;
            };

            Writer<StrikeOrderMsg>.Write = (w, m) =>
            {
                w.WriteUInt32(m.TargetId);
                w.WriteUInt32(m.QueryId);
                w.WriteUInt16(m.TotSeconds);
                int count = m.Picks == null ? 0 : System.Math.Min(m.Picks.Length, MaxStrikeAssets);
                w.WriteByte((byte)count);
                for (int i = 0; i < count; i++)
                {
                    w.WriteUInt16(m.Picks[i].Index);
                    w.WriteByte(m.Picks[i].Rounds);
                }
            };
            Reader<StrikeOrderMsg>.Read = r =>
            {
                var msg = new StrikeOrderMsg { TargetId = r.ReadUInt32(), QueryId = r.ReadUInt32(), TotSeconds = r.ReadUInt16() };
                int count = r.ReadByte();
                var picks = new StrikePick[System.Math.Min(count, MaxStrikeAssets)];
                for (int i = 0; i < count; i++)
                {
                    var pick = new StrikePick { Index = r.ReadUInt16(), Rounds = r.ReadByte() };
                    if (i < picks.Length)
                    {
                        picks[i] = pick;
                    }
                }
                msg.Picks = picks;
                return msg;
            };

            Writer<StrikeAbortMsg>.Write = (w, m) => w.WriteUInt32(m.MissionId);
            Reader<StrikeAbortMsg>.Read = r => new StrikeAbortMsg { MissionId = r.ReadUInt32() };

            Writer<StrikeStatusMsg>.Write = (w, m) =>
            {
                w.WriteUInt32(m.MissionId);
                w.WriteUInt32(m.TargetId);
                w.WriteByte(m.State);
                w.WriteInt32(m.TotTenths);
                w.WriteByte(m.Launched);
                w.WriteByte(m.Planned);
                w.WriteString(Clip(m.Note), MaxText);
            };
            Reader<StrikeStatusMsg>.Read = r => new StrikeStatusMsg
            {
                MissionId = r.ReadUInt32(),
                TargetId = r.ReadUInt32(),
                State = r.ReadByte(),
                TotTenths = r.ReadInt32(),
                Launched = r.ReadByte(),
                Planned = r.ReadByte(),
                Note = r.ReadString(MaxText),
            };
        }

        private static string Clip(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            return text.Length > MaxText ? text.Substring(0, MaxText) : text;
        }
    }
}
