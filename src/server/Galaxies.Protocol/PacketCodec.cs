using System.Buffers.Binary;
using System.Text;

namespace Galaxies.Protocol;

/// <summary>Encodes and decodes <see cref="Packet"/>s. All integers and floats are little-endian.</summary>
public static class PacketCodec
{
    /// <summary>Encodes <paramref name="packet"/> into a new array.</summary>
    /// <exception cref="ArgumentException">The packet doesn't fit in <see cref="ProtocolConstants.MaxPacketSize"/>, or a string is too long.</exception>
    public static byte[] Encode(Packet packet)
    {
        Span<byte> buffer = stackalloc byte[ProtocolConstants.MaxPacketSize];
        return buffer[..Encode(packet, buffer)].ToArray();
    }

    /// <summary>Encodes <paramref name="packet"/> into <paramref name="destination"/> and returns the number of bytes written.</summary>
    public static int Encode(Packet packet, Span<byte> destination)
    {
        var w = new Writer(destination);
        switch (packet)
        {
            case ConnectRequest p:
                w.U8((byte)PacketType.ConnectRequest);
                w.U32(ProtocolConstants.Magic);
                w.U16(p.Version);
                w.U64(p.Nonce);
                w.String(p.Token);
                break;
            case KeepAlive p:
                w.U8((byte)PacketType.KeepAlive);
                w.U64(p.Session);
                break;
            case Disconnect p:
                w.U8((byte)PacketType.Disconnect);
                w.U64(p.Session);
                break;
            case Move p:
                w.U8((byte)PacketType.Move);
                w.U64(p.Session);
                w.U32(p.Sequence);
                w.U32(p.CorrectionId);
                w.Pos(p.Position);
                w.F32(p.Yaw);
                break;
            case ConnectAccepted p:
                w.U8((byte)PacketType.ConnectAccepted);
                w.U64(p.Nonce);
                w.U64(p.Session);
                w.I32(p.PlayerId);
                w.String(p.PlayerName);
                w.F32(p.TickRate);
                break;
            case ConnectDenied p:
                w.U8((byte)PacketType.ConnectDenied);
                w.U64(p.Nonce);
                w.U8((byte)p.Reason);
                break;
            case WorldSnapshot p:
                w.U8((byte)PacketType.WorldSnapshot);
                w.U64(p.Session);
                w.I64(p.Tick);
                if (p.Avatars.Count > ProtocolConstants.MaxSnapshotAvatars)
                    throw new ArgumentException("Too many avatars.", nameof(packet));
                w.U8((byte)p.Avatars.Count);
                foreach (var a in p.Avatars)
                {
                    w.I32(a.PlayerId);
                    w.Pos(a.Position);
                    w.F32(a.Yaw);
                }
                break;
            case Disconnected p:
                w.U8((byte)PacketType.Disconnected);
                w.U64(p.Session);
                w.U8((byte)p.Reason);
                break;
            case Correction p:
                w.U8((byte)PacketType.Correction);
                w.U64(p.Session);
                w.U32(p.Id);
                w.U8((byte)p.Reason);
                w.Pos(p.Position);
                w.F32(p.Yaw);
                break;
            default:
                throw new ArgumentException($"Unknown packet {packet.GetType().Name}.", nameof(packet));
        }
        return w.Position;
    }

    /// <summary>
    /// Decodes a packet. Returns false for anything malformed: unknown type, wrong magic, truncated, trailing bytes,
    /// invalid enum values or strings. UDP receivers should silently drop such packets.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, out Packet? packet)
    {
        packet = null;
        if (data.Length is 0 or > ProtocolConstants.MaxPacketSize) return false;

        var r = new Reader(data);
        try
        {
            packet = (PacketType)r.U8() switch
            {
                PacketType.ConnectRequest => r.U32() == ProtocolConstants.Magic
                    ? new ConnectRequest(r.U16(), r.U64(), r.String())
                    : null,
                PacketType.KeepAlive => new KeepAlive(r.U64()),
                PacketType.Disconnect => new Disconnect(r.U64()),
                PacketType.Move => new Move(r.U64(), r.U32(), r.U32(), r.Pos(), r.F32()),
                PacketType.ConnectAccepted => new ConnectAccepted(r.U64(), r.U64(), r.I32(), r.String(), r.F32()),
                PacketType.ConnectDenied => new ConnectDenied(r.U64(), r.Enum<DenyReason>()),
                PacketType.WorldSnapshot => ReadWorldSnapshot(ref r),
                PacketType.Disconnected => new Disconnected(r.U64(), r.Enum<DisconnectReason>()),
                PacketType.Correction => new Correction(r.U64(), r.U32(), r.Enum<CorrectionReason>(), r.Pos(), r.F32()),
                _ => null,
            };
        }
        catch (MalformedPacketException)
        {
            packet = null;
        }

        if (packet is null || !r.AtEnd)
        {
            packet = null;
            return false;
        }
        return true;
    }

    private static WorldSnapshot? ReadWorldSnapshot(ref Reader r)
    {
        var session = r.U64();
        var tick = r.I64();
        var count = r.U8();
        if (count > ProtocolConstants.MaxSnapshotAvatars) return null;
        var avatars = new AvatarData[count];
        for (var i = 0; i < count; i++) avatars[i] = new AvatarData(r.I32(), r.Pos(), r.F32());
        return new WorldSnapshot(session, tick, avatars);
    }

    private sealed class MalformedPacketException : Exception;

    private ref struct Writer(Span<byte> buffer)
    {
        private readonly Span<byte> _buffer = buffer;
        public int Position { get; private set; }

        public void U8(byte v) => Take(1)[0] = v;
        public void U16(ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(Take(2), v);
        public void U32(uint v) => BinaryPrimitives.WriteUInt32LittleEndian(Take(4), v);
        public void U64(ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(Take(8), v);
        public void I32(int v) => BinaryPrimitives.WriteInt32LittleEndian(Take(4), v);
        public void I64(long v) => BinaryPrimitives.WriteInt64LittleEndian(Take(8), v);
        public void F32(float v) => BinaryPrimitives.WriteSingleLittleEndian(Take(4), v);

        public void Pos(Position p)
        {
            F32(p.X);
            F32(p.Y);
            F32(p.Z);
        }

        public void String(string v)
        {
            var length = Encoding.UTF8.GetByteCount(v);
            if (length > ProtocolConstants.MaxStringBytes)
                throw new ArgumentException($"String is {length} bytes; the limit is {ProtocolConstants.MaxStringBytes}.");
            U8((byte)length);
            Encoding.UTF8.GetBytes(v, Take(length));
        }

        private Span<byte> Take(int count)
        {
            if (Position + count > _buffer.Length)
                throw new ArgumentException($"Packet exceeds {_buffer.Length} bytes.");
            var span = _buffer.Slice(Position, count);
            Position += count;
            return span;
        }
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public readonly bool AtEnd => _position == _data.Length;

        public byte U8() => Take(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public long I64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

        public Position Pos() => new(F32(), F32(), F32());

        public T Enum<T>() where T : struct, Enum
        {
            var value = (T)(object)U8();
            return System.Enum.IsDefined(value) ? value : throw new MalformedPacketException();
        }

        public string String()
        {
            var bytes = Take(U8());
            try { return StrictUtf8.GetString(bytes); }
            catch (ArgumentException) { throw new MalformedPacketException(); }
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (_position + count > _data.Length) throw new MalformedPacketException();
            var span = _data.Slice(_position, count);
            _position += count;
            return span;
        }
    }
}
