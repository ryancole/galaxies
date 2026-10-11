using Galaxies.Sim;

namespace Galaxies.Server;

public sealed class GameOptions
{
    /// <summary>UDP port the server listens on (IPv4 and IPv6).</summary>
    public int Port { get; set; } = 5090;

    /// <summary>Ticks per second. Each tick sends every client a snapshot of the avatars near it.</summary>
    public double TicksPerSecond { get; set; } = 20;

    /// <summary>Clients are told about other avatars within this many metres of their own.</summary>
    public float InterestRadius { get; set; } = 250;

    /// <summary>Where new players' avatars start: just inside the south gate of Kessa Outpost, facing north.</summary>
    public SpawnPoint Spawn { get; set; } = new();

    public MovementConfig Movement { get; set; } = new();

    /// <summary>Players who can log in. Dev-only until accounts exist.</summary>
    public List<PlayerSlot> Players { get; set; } = [];
}

public sealed class SpawnPoint
{
    public float X { get; set; }
    public float Y { get; set; } = 0.2f;
    public float Z { get; set; } = 236;

    /// <summary>Radians: 0 looks north (-z).</summary>
    public float Yaw { get; set; }

    /// <summary>Metres between players' spawn spots, so they don't start inside each other.</summary>
    public float Spacing { get; set; } = 1.5f;
}

public sealed class PlayerSlot
{
    public string Name { get; set; } = "";

    /// <summary>Lets a client connect as this player.</summary>
    public string Token { get; set; } = "";
}
