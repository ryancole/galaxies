using Galaxies.Protocol;

namespace Galaxies.Protocol.Tests;

public class PacketCodecTests
{
    public static TheoryData<Packet> AllPackets() =>
    [
        new ConnectRequest(ProtocolConstants.Version, 0x0807060504030201, "dev-player-1"),
        new KeepAlive(42),
        new Disconnect(42),
        new Move(42, 7, 2, new Position(1.5f, -2.25f, 236f), 0.5f),
        new ConnectAccepted(1, 42, 3, "Kira", 20f),
        new ConnectDenied(1, DenyReason.InvalidToken),
        new WorldSnapshot(42, 100, [new AvatarData(2, new Position(1, 2, 3), -1f), new AvatarData(4, new Position(-4, 0, 9), 3f)]),
        new Disconnected(42, DisconnectReason.Superseded),
        new Correction(42, 3, CorrectionReason.TooFast, new Position(0, 0.2f, 236), 0f),
    ];

    [Theory]
    [MemberData(nameof(AllPackets))]
    public void RoundTrips(Packet packet)
    {
        Assert.True(PacketCodec.TryDecode(PacketCodec.Encode(packet), out var decoded));

        // Records with list members compare the lists by reference, so compare snapshots field by field.
        if (packet is WorldSnapshot snapshot)
        {
            var back = Assert.IsType<WorldSnapshot>(decoded);
            Assert.Equal((snapshot.Session, snapshot.Tick), (back.Session, back.Tick));
            Assert.Equal(snapshot.Avatars, back.Avatars);
        }
        else
        {
            Assert.Equal(packet, decoded);
        }
    }

    [Fact]
    public void ConnectRequestMatchesTheDocumentedBytes()
    {
        var bytes = PacketCodec.Encode(new ConnectRequest(1, 0x0807060504030201, "ab"));

        Assert.Equal(Convert.FromHexString("01" + "474C5859" + "0100" + "0102030405060708" + "026162"), bytes);
    }

    [Fact]
    public void MoveMatchesTheDocumentedBytes()
    {
        var bytes = PacketCodec.Encode(new Move(0x0807060504030201, 0x0D0C0B0A, 1, new Position(1f, 2f, -1f), 0f));

        Assert.Equal(Convert.FromHexString(
            "04" + "0102030405060708" + "0A0B0C0D" + "01000000" + "0000803F" + "00000040" + "000080BF" + "00000000"), bytes);
        Assert.Equal(33, bytes.Length);
    }

    [Fact]
    public void AFullSnapshotFitsInOnePacket()
    {
        var avatars = Enumerable.Range(1, ProtocolConstants.MaxSnapshotAvatars).Select(i => new AvatarData(i, default, 0)).ToList();

        Assert.True(PacketCodec.Encode(new WorldSnapshot(1, 1, avatars)).Length <= ProtocolConstants.MaxPacketSize);
    }

    [Theory]
    [InlineData("")]                 // empty
    [InlineData("7F")]               // unknown type
    [InlineData("02010203")]         // truncated KeepAlive
    [InlineData("0201020304050607080900")] // trailing bytes
    [InlineData("010000000001000102030405060708026162")] // wrong magic
    [InlineData("84010203040506070809")] // Disconnected with an unknown reason
    public void RejectsMalformedPackets(string hex)
    {
        Assert.False(PacketCodec.TryDecode(Convert.FromHexString(hex), out var packet));
        Assert.Null(packet);
    }

    [Fact]
    public void RejectsSnapshotsClaimingTooManyAvatars()
    {
        var bytes = PacketCodec.Encode(new WorldSnapshot(1, 1, []));
        bytes[^1] = ProtocolConstants.MaxSnapshotAvatars + 1;

        Assert.False(PacketCodec.TryDecode(bytes, out _));
    }
}
