using System.Net;
using Galaxies.Protocol;
using Galaxies.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Galaxies.Server.Tests;

public class GameHostTests
{
    private static readonly IPEndPoint Alice = new(IPAddress.Loopback, 40001);
    private static readonly IPEndPoint Bob = new(IPAddress.Loopback, 40002);

    private readonly GameHost _host = new(
        Options.Create(new GameOptions
        {
            Players = [new() { Name = "Ryan", Token = "t1" }, new() { Name = "Kira", Token = "t2" }],
        }),
        NullLogger<GameHost>.Instance);

    private long _now = 10_000;

    [Fact]
    public void ConnectingPlacesTheAvatarAtTheSpawn()
    {
        var replies = Send(Alice, new ConnectRequest(ProtocolConstants.Version, 5, "t1"));

        var accepted = Assert.IsType<ConnectAccepted>(replies[0].Packet);
        Assert.Equal((5UL, "Ryan"), (accepted.Nonce, accepted.PlayerName));
        var correction = Assert.IsType<Correction>(replies[1].Packet);
        Assert.Equal((1u, CorrectionReason.Placed, new Position(0, 0.2f, 236)), (correction.Id, correction.Reason, correction.Position));
    }

    [Fact]
    public void SpreadsPlayersOutAtTheSpawn()
    {
        var first = Connect(Alice, "t1");
        var second = Connect(Bob, "t2");

        Assert.NotEqual(first.Position, second.Position);
    }

    [Theory]
    [InlineData("nope", DenyReason.InvalidToken, ProtocolConstants.Version)]
    [InlineData("t1", DenyReason.UnsupportedVersion, (ushort)99)]
    public void DeniesBadRequests(string token, DenyReason reason, ushort version)
    {
        var reply = Assert.Single(Send(Alice, new ConnectRequest(version, 5, token)));

        Assert.Equal(new ConnectDenied(5, reason), reply.Packet);
    }

    [Fact]
    public void IgnoresMovesUntilThePlacementIsApplied()
    {
        var placed = Connect(Alice, "t1");

        Send(Alice, new Move(placed.Session, 1, CorrectionId: 0, new Position(5, 0, 236), 0));
        Assert.Equal(new Position(0, 0.2f, 236), Avatar(1));

        Send(Alice, new Move(placed.Session, 2, CorrectionId: 1, new Position(0.3f, 0, 236), 0));
        Assert.Equal(new Position(0.3f, 0, 236), Avatar(1));
    }

    [Fact]
    public void RepeatsTheCorrectionEachTickUntilApplied()
    {
        var placed = Connect(Alice, "t1");
        Assert.Contains(Tick(), o => o.Packet is Correction);

        Send(Alice, new Move(placed.Session, 1, 1, placed.Position, 0));
        Assert.DoesNotContain(Tick(), o => o.Packet is Correction);
    }

    [Fact]
    public void CorrectsATeleportAndIgnoresMovesFromBeforeTheCorrection()
    {
        var placed = Connect(Alice, "t1");
        Send(Alice, new Move(placed.Session, 1, 1, placed.Position, 0));

        _now += 50;
        var reply = Assert.Single(Send(Alice, new Move(placed.Session, 2, 1, new Position(0, 0, 0), 0)));
        var correction = Assert.IsType<Correction>(reply.Packet);
        Assert.Equal((2u, CorrectionReason.TooFast, placed.Position), (correction.Id, correction.Reason, correction.Position));

        // Already in flight when the correction went out: started from the refused spot, so it doesn't count.
        _now += 50;
        Send(Alice, new Move(placed.Session, 3, 1, new Position(0, 0, 0.3f), 0));
        Assert.Equal(placed.Position, Avatar(1));

        // The client snapped back and carries on from there.
        Send(Alice, new Move(placed.Session, 4, 2, placed.Position with { Z = 235.7f }, 0));
        Assert.Equal(placed.Position with { Z = 235.7f }, Avatar(1));
    }

    [Fact]
    public void IgnoresOlderMovesThatArriveLate()
    {
        var placed = Connect(Alice, "t1");
        Send(Alice, new Move(placed.Session, 5, 1, placed.Position with { X = 0.4f }, 0));
        Send(Alice, new Move(placed.Session, 4, 1, placed.Position with { X = 0.2f }, 0));

        Assert.Equal(0.4f, Avatar(1).X);
    }

    [Fact]
    public void IgnoresPacketsFromTheWrongAddressOrSession()
    {
        var placed = Connect(Alice, "t1");
        Send(Bob, new Move(placed.Session, 1, 1, placed.Position with { X = 0.4f }, 0));
        Send(Alice, new Move(placed.Session + 1, 1, 1, placed.Position with { X = 0.4f }, 0));

        Assert.Equal(placed.Position, Avatar(1));
    }

    [Fact]
    public void SnapshotsShowOtherPlayersButNotYourself()
    {
        var alice = Connect(Alice, "t1");
        var bob = Connect(Bob, "t2");

        var tick = Tick();
        var toAlice = tick.Select(o => o.Packet).OfType<WorldSnapshot>().Single(s => s.Session == alice.Session);
        var toBob = tick.Select(o => o.Packet).OfType<WorldSnapshot>().Single(s => s.Session == bob.Session);
        Assert.Equal(new[] { 2 }, toAlice.Avatars.Select(a => a.PlayerId));
        Assert.Equal(new[] { 1 }, toBob.Avatars.Select(a => a.PlayerId));
    }

    [Fact]
    public void ANewConnectionSupersedesTheOld()
    {
        var first = Connect(Alice, "t1");

        var replies = Send(Bob, new ConnectRequest(ProtocolConstants.Version, 6, "t1"));

        Assert.Contains(replies, o => o.To.Equals(Alice) && o.Packet == new Disconnected(first.Session, DisconnectReason.Superseded));
        Assert.Contains(replies, o => o.To.Equals(Bob) && o.Packet is ConnectAccepted);
    }

    [Fact]
    public void ResendsTheSameAnswerToARepeatedRequest()
    {
        var first = Send(Alice, new ConnectRequest(ProtocolConstants.Version, 5, "t1"));
        var again = Send(Alice, new ConnectRequest(ProtocolConstants.Version, 5, "t1"));

        Assert.Equal(first.Select(o => o.Packet), again.Select(o => o.Packet));
    }

    [Fact]
    public void TimesOutSilentClientsAndTakesThemOutOfTheWorld()
    {
        var alice = Connect(Alice, "t1");

        _now += 6_000;
        var tick = Tick();

        Assert.Contains(tick, o => o.Packet == new Disconnected(alice.Session, DisconnectReason.TimedOut));
        Assert.False(_host.World.Players[1].Online);
    }

    [Fact]
    public void RemembersWhereAPlayerLeftOff()
    {
        var placed = Connect(Alice, "t1");
        Send(Alice, new Move(placed.Session, 1, 1, placed.Position with { X = 0.5f }, 0));
        Send(Alice, new Disconnect(placed.Session));

        var again = Connect(Alice, "t1");

        Assert.Equal(0.5f, again.Position.X);
    }

    private (ulong Session, Position Position) Connect(IPEndPoint from, string token)
    {
        var replies = Send(from, new ConnectRequest(ProtocolConstants.Version, (ulong)from.Port, token));
        var accepted = replies.Select(o => o.Packet).OfType<ConnectAccepted>().Single();
        var correction = replies.Select(o => o.Packet).OfType<Correction>().Single();
        return (accepted.Session, correction.Position);
    }

    private IReadOnlyList<Outgoing> Send(IPEndPoint from, Packet packet) => _host.Receive(from, PacketCodec.Encode(packet), _now);

    private IReadOnlyList<Outgoing> Tick() => _host.Tick(_now);

    private Position Avatar(int playerId)
    {
        var p = _host.World.Players[playerId].Avatar.Position;
        return new Position(p.X, p.Y, p.Z);
    }
}
