using System.Numerics;

namespace Galaxies.Sim;

/// <summary>Why <see cref="World.TryMove"/> refused a move.</summary>
public enum MoveVerdict
{
    Ok,

    /// <summary>Moved further sideways than running allows.</summary>
    TooFast,

    /// <summary>Rose faster or higher than walking uphill and jumping allow.</summary>
    TooHigh,

    /// <summary>Outside the world.</summary>
    OutOfBounds,

    /// <summary>NaN or infinite numbers.</summary>
    Invalid,
}

/// <summary>
/// The authoritative game state: every player and where their avatar is. Clients move their own avatars and report
/// where they went; <see cref="TryMove"/> accepts a report only if a legitimate client could have made that move.
/// Not thread-safe.
/// </summary>
/// <remarks>
/// The server doesn't know the level's geometry yet, so it can't tell a wall from open ground. It stops speed hacks,
/// teleports, super jumps and leaving the world, but not walking through walls or slow flying.
/// </remarks>
public sealed class World(MovementConfig config)
{
    private readonly Dictionary<int, Player> _players = [];
    private int _nextId = 1;

    public MovementConfig Config { get; } = config;

    public IReadOnlyDictionary<int, Player> Players => _players;

    /// <summary>Adds a player, offline, with their avatar standing at <paramref name="spawn"/>.</summary>
    public Player AddPlayer(string name, Vector3 spawn, float yaw)
    {
        var player = new Player(_nextId++, name, new Avatar(spawn, yaw));
        _players.Add(player.Id, player);
        return player;
    }

    /// <summary>Puts the player in the world, where they left off, with full movement budgets.</summary>
    public void Enter(int playerId, double now)
    {
        var player = _players[playerId];
        player.Online = true;
        ResetBudgets(player.Avatar, now);
    }

    public void Leave(int playerId) => _players[playerId].Online = false;

    /// <summary>Moves the avatar there without any checks, e.g. a respawn. Budgets refill, as after a teleport.</summary>
    public void Place(int playerId, Vector3 position, float yaw, double now)
    {
        var avatar = _players[playerId].Avatar;
        avatar.Position = position;
        avatar.Yaw = yaw;
        ResetBudgets(avatar, now);
    }

    /// <summary>
    /// Moves the avatar to where its client says it went, if a legitimate client could have got there by now.
    /// Otherwise leaves it where it was. <paramref name="now"/> is in seconds on any steady clock.
    /// </summary>
    public MoveVerdict TryMove(int playerId, Vector3 to, float yaw, double now)
    {
        var avatar = _players[playerId].Avatar;

        if (!float.IsFinite(to.X) || !float.IsFinite(to.Y) || !float.IsFinite(to.Z) || !float.IsFinite(yaw))
            return MoveVerdict.Invalid;
        if (MathF.Abs(to.X) > Config.WorldHalfSize || MathF.Abs(to.Z) > Config.WorldHalfSize
            || to.Y < Config.MinY || to.Y > Config.MaxY)
            return MoveVerdict.OutOfBounds;

        // Budgets refill with time, up to a cap, so an avatar can't save up for a long jump across the map.
        var elapsed = (float)Math.Max(0, now - avatar.BudgetTime);
        avatar.BudgetTime = now;
        var speed = Config.MaxSpeed * Config.SpeedTolerance;
        avatar.DistanceBudget = MathF.Min(speed * Config.BurstSeconds, avatar.DistanceBudget + speed * elapsed);
        avatar.ClimbBudget = MathF.Min(Config.ClimbBurst, avatar.ClimbBudget + Config.MaxClimbSpeed * elapsed);

        var from = avatar.Position;
        var sideways = new Vector2(to.X - from.X, to.Z - from.Z).Length();
        var rise = MathF.Max(0, to.Y - from.Y); // Falling is always allowed.
        if (sideways > avatar.DistanceBudget) return MoveVerdict.TooFast;
        if (rise > avatar.ClimbBudget) return MoveVerdict.TooHigh;

        avatar.DistanceBudget -= sideways;
        avatar.ClimbBudget -= rise;
        avatar.Position = to;
        avatar.Yaw = WrapAngle(yaw);
        return MoveVerdict.Ok;
    }

    /// <summary>
    /// The online players nearest <paramref name="playerId"/>'s avatar, within <paramref name="radius"/> metres,
    /// nearest first, at most <paramref name="max"/>. Clients are only told about what's near them.
    /// </summary>
    public IEnumerable<Player> Nearby(int playerId, float radius, int max)
    {
        var centre = _players[playerId].Avatar.Position;
        return _players.Values
            .Where(p => p.Online && p.Id != playerId)
            .Select(p => (Player: p, Distance: Vector3.DistanceSquared(p.Avatar.Position, centre)))
            .Where(x => x.Distance <= radius * radius)
            .OrderBy(x => x.Distance)
            .Take(max)
            .Select(x => x.Player);
    }

    private void ResetBudgets(Avatar avatar, double now)
    {
        avatar.DistanceBudget = Config.MaxSpeed * Config.SpeedTolerance * Config.BurstSeconds;
        avatar.ClimbBudget = Config.ClimbBurst;
        avatar.BudgetTime = now;
    }

    /// <summary>Wraps an angle into [-π, π).</summary>
    private static float WrapAngle(float radians)
    {
        var wrapped = (radians + MathF.PI) % MathF.Tau;
        return (wrapped < 0 ? wrapped + MathF.Tau : wrapped) - MathF.PI;
    }
}
