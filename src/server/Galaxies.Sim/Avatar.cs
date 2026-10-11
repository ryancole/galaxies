using System.Numerics;

namespace Galaxies.Sim;

/// <summary>A player's body in the world. The server's copy is the truth; clients only propose moves.</summary>
public sealed class Avatar(Vector3 position, float yaw)
{
    /// <summary>Feet position, in metres. Godot axes: y is up, -z is north.</summary>
    public Vector3 Position { get; internal set; } = position;

    /// <summary>Facing, in radians: 0 looks north (-z), positive turns left.</summary>
    public float Yaw { get; internal set; } = yaw;

    /// <summary>Sideways metres the avatar may still cover right now; refills at the max speed.</summary>
    internal float DistanceBudget { get; set; }

    /// <summary>Metres the avatar may still rise right now; refills at the max climb speed.</summary>
    internal float ClimbBudget { get; set; }

    /// <summary>When the budgets were last refilled, in seconds on the caller's clock.</summary>
    internal double BudgetTime { get; set; }
}
