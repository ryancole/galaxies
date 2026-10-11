namespace Galaxies.Sim;

/// <summary>
/// What a legitimate client's avatar can do, so the server can refuse anything more. Matches the client's
/// <c>Player.cs</c> (run speed 8.5 m/s, jump 4.6 m/s) with some slack for network jitter.
/// </summary>
public sealed class MovementConfig
{
    /// <summary>Fastest sideways speed a client can reach (running), in metres per second.</summary>
    public float MaxSpeed { get; set; } = 8.5f;

    /// <summary>Multiplies <see cref="MaxSpeed"/>, so rounding and frame timing never cause a false refusal.</summary>
    public float SpeedTolerance { get; set; } = 1.15f;

    /// <summary>
    /// Seconds of movement a client may bank. Reports bunch up after a lag spike, so an avatar can cover this many
    /// seconds' worth of distance in one report as long as it has averaged a legal speed.
    /// </summary>
    public float BurstSeconds { get; set; } = 1.5f;

    /// <summary>
    /// Fastest sustained climb, in metres per second: running up the steepest walkable slope (45°) is about 6 m/s.
    /// </summary>
    public float MaxClimbSpeed { get; set; } = 6.5f;

    /// <summary>Most height an avatar may gain at once: a jump (about 1.1 m) plus slack.</summary>
    public float ClimbBurst { get; set; } = 1.6f;

    /// <summary>The world is a square this far from the origin on x and z, in metres (the client's terrain is 3600 m across).</summary>
    public float WorldHalfSize { get; set; } = 1800f;

    /// <summary>Lowest an avatar can be: anything below has fallen out of the world.</summary>
    public float MinY { get; set; } = -20f;

    /// <summary>Highest an avatar can be: above the tallest mesa.</summary>
    public float MaxY { get; set; } = 400f;
}
