using System.Numerics;
using Galaxies.Sim;

namespace Galaxies.Sim.Tests;

public class WorldTests
{
    private static readonly Vector3 Start = new(0, 0, 236);

    private readonly World _world = new(new MovementConfig());
    private readonly Player _player;

    public WorldTests()
    {
        _player = _world.AddPlayer("Ryan", Start, 0);
        _world.Enter(_player.Id, now: 0);
    }

    [Fact]
    public void AcceptsRunningAtFullSpeedForAWhile()
    {
        // 20 reports a second at 8.5 m/s, for 10 seconds: about the client's run speed.
        var position = Start;
        for (var i = 1; i <= 200; i++)
        {
            position += new Vector3(0, 0, -8.5f / 20);
            Assert.Equal(MoveVerdict.Ok, _world.TryMove(_player.Id, position, 0, i / 20.0));
        }
        Assert.Equal(position, _player.Avatar.Position);
    }

    [Fact]
    public void RefusesRunningTooFastOnceTheBurstIsSpent()
    {
        // Double speed: the first second and a half or so eats the banked burst, then reports start failing.
        var position = Start;
        MoveVerdict verdict = MoveVerdict.Ok;
        for (var i = 1; i <= 100 && verdict == MoveVerdict.Ok; i++)
        {
            position += new Vector3(17f / 20, 0, 0);
            verdict = _world.TryMove(_player.Id, position, 0, i / 20.0);
        }
        Assert.Equal(MoveVerdict.TooFast, verdict);
    }

    [Fact]
    public void RefusesATeleportAndLeavesTheAvatarWhereItWas()
    {
        Assert.Equal(MoveVerdict.TooFast, _world.TryMove(_player.Id, new Vector3(0, 0, 0), 0, 0.05));
        Assert.Equal(Start, _player.Avatar.Position);
    }

    [Fact]
    public void AcceptsABunchOfReportsAfterALagSpike()
    {
        // A second of silence, then the client's backlog arrives at once: a second of running in one report.
        Assert.Equal(MoveVerdict.Ok, _world.TryMove(_player.Id, Start + new Vector3(8.5f, 0, 0), 0, 1.0));
    }

    [Fact]
    public void AcceptsAJumpButRefusesASuperJump()
    {
        Assert.Equal(MoveVerdict.Ok, _world.TryMove(_player.Id, Start + new Vector3(0, 1.1f, 0), 0, 0.5));
        Assert.Equal(MoveVerdict.Ok, _world.TryMove(_player.Id, Start, 0, 1.0)); // Landed.
        Assert.Equal(MoveVerdict.TooHigh, _world.TryMove(_player.Id, Start + new Vector3(0, 5f, 0), 0, 1.5));
    }

    [Fact]
    public void AlwaysAllowsFalling()
    {
        _world.Place(_player.Id, Start + new Vector3(0, 300, 0), 0, now: 0);

        Assert.Equal(MoveVerdict.Ok, _world.TryMove(_player.Id, Start, 0, 0.05));
    }

    [Fact]
    public void AcceptsRunningUpASteepSlope()
    {
        // 45°: 6 m/s up and 6 m/s along.
        var position = Start;
        for (var i = 1; i <= 100; i++)
        {
            position += new Vector3(0, 6f / 20, -6f / 20);
            Assert.Equal(MoveVerdict.Ok, _world.TryMove(_player.Id, position, 0, i / 20.0));
        }
    }

    [Theory]
    [InlineData(1801, 0, 0)]
    [InlineData(0, 0, -1801)]
    [InlineData(0, -21, 0)]
    [InlineData(0, 401, 0)]
    public void RefusesLeavingTheWorld(float x, float y, float z)
    {
        _world.Place(_player.Id, new Vector3(Math.Clamp(x, -1799, 1799), Math.Clamp(y, -19, 399), Math.Clamp(z, -1799, 1799)), 0, now: 0);

        Assert.Equal(MoveVerdict.OutOfBounds, _world.TryMove(_player.Id, new Vector3(x, y, z), 0, 0.05));
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    [InlineData(0f, float.NaN)]
    public void RefusesNonsense(float x, float yaw)
    {
        Assert.Equal(MoveVerdict.Invalid, _world.TryMove(_player.Id, Start with { X = x }, yaw, 0.05));
    }

    [Fact]
    public void WrapsTheYaw()
    {
        _world.TryMove(_player.Id, Start, 4 * MathF.PI + 0.5f, 0.05);

        Assert.Equal(0.5f, _player.Avatar.Yaw, 4);
    }

    [Fact]
    public void NearbyListsOnlineOthersNearestFirstWithinTheRadius()
    {
        var near = _world.AddPlayer("Near", Start + new Vector3(5, 0, 0), 0);
        var nearer = _world.AddPlayer("Nearer", Start + new Vector3(2, 0, 0), 0);
        var far = _world.AddPlayer("Far", Start + new Vector3(500, 0, 0), 0);
        var offline = _world.AddPlayer("Offline", Start + new Vector3(1, 0, 0), 0);
        foreach (var p in new[] { near, nearer, far }) _world.Enter(p.Id, 0);

        Assert.Equal(new[] { nearer.Id, near.Id }, _world.Nearby(_player.Id, 250, 50).Select(p => p.Id));
        Assert.Equal(new[] { nearer.Id }, _world.Nearby(_player.Id, 250, 1).Select(p => p.Id));
        Assert.DoesNotContain(offline.Id, _world.Nearby(_player.Id, 250, 50).Select(p => p.Id));
    }
}
