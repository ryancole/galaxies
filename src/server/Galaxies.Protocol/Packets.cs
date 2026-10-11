namespace Galaxies.Protocol;

// The Galaxies wire protocol: small binary packets over UDP. See docs/protocol.md for the byte layout.
// Types 0x01–0x7F travel client -> server; 0x80–0xFF travel server -> client.

public enum PacketType : byte
{
    // Client -> server
    ConnectRequest = 0x01,
    KeepAlive = 0x02,
    Disconnect = 0x03,
    Move = 0x04,

    // Server -> client
    ConnectAccepted = 0x81,
    ConnectDenied = 0x82,
    WorldSnapshot = 0x83,
    Disconnected = 0x84,
    Correction = 0x85,
}

public enum DenyReason : byte
{
    UnsupportedVersion = 1,
    InvalidToken = 2,
}

public enum DisconnectReason : byte
{
    ServerShutdown = 1,
    Superseded = 2,
    TimedOut = 3,
}

/// <summary>Why the server refused a <see cref="Move"/> and put the avatar back.</summary>
public enum CorrectionReason : byte
{
    /// <summary>The server placed the avatar (on connect, or a respawn). Not a refusal.</summary>
    Placed = 1,

    /// <summary>Moved further sideways than running allows.</summary>
    TooFast = 2,

    /// <summary>Rose faster or higher than walking uphill and jumping allow.</summary>
    TooHigh = 3,

    /// <summary>Outside the world.</summary>
    OutOfBounds = 4,

    /// <summary>NaN or infinite numbers.</summary>
    Invalid = 5,
}

/// <summary>A position in the world, in metres. Godot axes: y is up, -z is north.</summary>
public readonly record struct Position(float X, float Y, float Z);

/// <summary>Where one avatar is and which way it faces (yaw in radians, 0 looks north, positive turns left).</summary>
public readonly record struct AvatarData(int PlayerId, Position Position, float Yaw);

public abstract record Packet;

// Client -> server

/// <param name="Nonce">Random, chosen by the client; echoed in the answer so the client can match it.</param>
public sealed record ConnectRequest(ushort Version, ulong Nonce, string Token) : Packet;

public sealed record KeepAlive(ulong Session) : Packet;

public sealed record Disconnect(ulong Session) : Packet;

/// <summary>Where the client has moved its avatar. The server checks it and answers a refusal with a <see cref="Correction"/>.</summary>
/// <param name="Sequence">Counts up from 1 within a session. The server ignores moves older than the newest it has seen.</param>
/// <param name="CorrectionId">The id of the last <see cref="Correction"/> the client applied, 0 if none.</param>
public sealed record Move(ulong Session, uint Sequence, uint CorrectionId, Position Position, float Yaw) : Packet;

// Server -> client

public sealed record ConnectAccepted(ulong Nonce, ulong Session, int PlayerId, string PlayerName, float TickRate) : Packet;

public sealed record ConnectDenied(ulong Nonce, DenyReason Reason) : Packet;

/// <summary>The other avatars near this client's, every tick. Never includes the client's own avatar.</summary>
public sealed record WorldSnapshot(ulong Session, long Tick, IReadOnlyList<AvatarData> Avatars) : Packet;

public sealed record Disconnected(ulong Session, DisconnectReason Reason) : Packet;

/// <summary>
/// Where the server says this client's avatar is. Sent when the client connects and whenever it refuses a move,
/// then resent every tick until the client's moves carry this <paramref name="Id"/>.
/// </summary>
public sealed record Correction(ulong Session, uint Id, CorrectionReason Reason, Position Position, float Yaw) : Packet;
