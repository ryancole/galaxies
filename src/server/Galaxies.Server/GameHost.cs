using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using Galaxies.Protocol;
using Galaxies.Sim;
using Microsoft.Extensions.Options;

namespace Galaxies.Server;

/// <summary>A packet the server wants sent.</summary>
public readonly record struct Outgoing(IPEndPoint To, Packet Packet);

/// <summary>
/// Owns the world and the client sessions. Pure game and connection logic: packets come in, packets to send come
/// out, and the caller supplies the clock, so it can be tested without sockets. Thread-safe: every entry point
/// takes <see cref="_gate"/>.
/// </summary>
/// <remarks>
/// Clients move their own avatars and report where they went (<see cref="Move"/>). The server checks each report
/// against <see cref="World.TryMove"/>. A refused move gets a <see cref="Correction"/>, putting the avatar back where
/// the server has it, and the server ignores the client's moves until one shows it applied that correction: moves
/// sent before then started from the refused position. Connecting works the same way: the server places the avatar
/// with a correction, and nothing the client reports counts until it's standing there.
/// </remarks>
public sealed class GameHost
{
    private readonly Lock _gate = new();
    private readonly World _world;
    private readonly GameOptions _options;
    private readonly Dictionary<string, int> _playerIdsByToken = new(StringComparer.Ordinal);
    private readonly Dictionary<IPEndPoint, Session> _sessionsByAddress = [];
    private readonly Dictionary<int, Session> _sessionsByPlayer = [];
    private readonly ILogger<GameHost> _logger;
    private long _tick;

    public GameHost(IOptions<GameOptions> options, ILogger<GameHost> logger)
    {
        _logger = logger;
        _options = options.Value;
        _world = new World(_options.Movement);

        var spawn = _options.Spawn;
        foreach (var (slot, index) in _options.Players.Select((s, i) => (s, i)))
        {
            if (string.IsNullOrWhiteSpace(slot.Token))
                throw new InvalidOperationException($"Player '{slot.Name}' has no token configured.");
            if (_playerIdsByToken.ContainsKey(slot.Token))
                throw new InvalidOperationException($"Player '{slot.Name}' reuses a token that's already configured.");

            // Spread players out sideways from the spawn point: 0, +1, -1, +2, -2... spacings.
            var offset = (index + 1) / 2 * (index % 2 == 1 ? 1 : -1) * spawn.Spacing;
            var player = _world.AddPlayer(slot.Name, new Vector3(spawn.X + offset, spawn.Y, spawn.Z), spawn.Yaw);
            _playerIdsByToken.Add(slot.Token, player.Id);
        }
        _logger.LogInformation("{Count} player(s) can log in", _world.Players.Count);
    }

    /// <summary>The world, for tests. Don't touch it from outside the gate while the server is running.</summary>
    internal World World => _world;

    /// <summary>Handles one datagram. Malformed packets and packets from unknown sessions are silently dropped.</summary>
    public IReadOnlyList<Outgoing> Receive(IPEndPoint from, ReadOnlySpan<byte> data, long nowMs)
    {
        if (!PacketCodec.TryDecode(data, out var packet)) return [];

        lock (_gate)
        {
            switch (packet)
            {
                case ConnectRequest request:
                    return Connect(from, request, nowMs);

                case KeepAlive { Session: var id } when FindSession(from, id) is { } session:
                    session.LastHeardMs = nowMs;
                    return [];

                case Disconnect { Session: var id } when FindSession(from, id) is { } session:
                    Remove(session);
                    _logger.LogInformation("Disconnected: {Session} (client left)", session);
                    return [];

                case Move move when FindSession(from, move.Session) is { } session:
                    session.LastHeardMs = nowMs;
                    return HandleMove(session, move, nowMs);

                default:
                    return []; // Server-to-client packet types, or a stale or spoofed session.
            }
        }
    }

    /// <summary>Drops timed-out sessions and returns every client's snapshot, plus any corrections not yet applied.</summary>
    public IReadOnlyList<Outgoing> Tick(long nowMs)
    {
        var outgoing = new List<Outgoing>();
        lock (_gate)
        {
            foreach (var session in _sessionsByAddress.Values.Where(s => nowMs - s.LastHeardMs > (long)ProtocolConstants.Timeout.TotalMilliseconds).ToList())
            {
                Remove(session);
                outgoing.Add(new(session.Address, new Disconnected(session.Id, DisconnectReason.TimedOut)));
                _logger.LogInformation("Disconnected: {Session} (timed out)", session);
            }

            _tick++;
            foreach (var session in _sessionsByAddress.Values)
            {
                var avatars = _world.Nearby(session.PlayerId, _options.InterestRadius, ProtocolConstants.MaxSnapshotAvatars)
                    .Select(p => new AvatarData(p.Id, ToPosition(p.Avatar.Position), p.Avatar.Yaw))
                    .ToList();
                outgoing.Add(new(session.Address, new WorldSnapshot(session.Id, _tick, avatars)));

                // Corrections must arrive, so they repeat until the client's moves show it applied the latest one.
                if (!session.CorrectionApplied)
                    outgoing.Add(new(session.Address, CorrectionFor(session)));
            }
        }
        return outgoing;
    }

    /// <summary>Ends every session, e.g. on shutdown, and returns the notices to send.</summary>
    public IReadOnlyList<Outgoing> DisconnectAll(DisconnectReason reason)
    {
        lock (_gate)
        {
            var outgoing = _sessionsByAddress.Values.Select(s => new Outgoing(s.Address, new Disconnected(s.Id, reason))).ToList();
            foreach (var session in _sessionsByAddress.Values.ToList()) Remove(session);
            return outgoing;
        }
    }

    private List<Outgoing> Connect(IPEndPoint from, ConnectRequest request, long nowMs)
    {
        if (request.Version != ProtocolConstants.Version)
            return [new(from, new ConnectDenied(request.Nonce, DenyReason.UnsupportedVersion))];

        if (!_playerIdsByToken.TryGetValue(request.Token, out var playerId))
        {
            _logger.LogInformation("Denied connection from {Address}: invalid token", from);
            return [new(from, new ConnectDenied(request.Nonce, DenyReason.InvalidToken))];
        }
        var player = _world.Players[playerId];

        var outgoing = new List<Outgoing>();
        if (_sessionsByAddress.TryGetValue(from, out var existing))
        {
            // A resend of a request we already accepted (the client hasn't seen our answer yet): answer again.
            if (existing.Nonce == request.Nonce && existing.PlayerId == playerId)
            {
                existing.LastHeardMs = nowMs;
                return [new(from, Accepted(existing, player)), new(from, CorrectionFor(existing))];
            }
            // Otherwise the client at this address started over; its old session is gone.
            Remove(existing);
            _logger.LogInformation("Disconnected: {Session} (reconnected)", existing);
        }

        if (_sessionsByPlayer.TryGetValue(playerId, out var previous))
        {
            Remove(previous);
            outgoing.Add(new(previous.Address, new Disconnected(previous.Id, DisconnectReason.Superseded)));
            _logger.LogInformation("Disconnected: {Session} (superseded by a new connection)", previous);
        }

        var session = new Session(from, NewSessionId(), playerId, request.Nonce) { LastHeardMs = nowMs };
        _sessionsByAddress.Add(from, session);
        _sessionsByPlayer.Add(playerId, session);
        _world.Enter(playerId, Seconds(nowMs));
        _logger.LogInformation("Connected: {Session} '{Name}' at {Position}", session, player.Name, Format(player.Avatar.Position));

        // The client's avatar goes wherever the server says, so place it with the first correction.
        Correct(session, CorrectionReason.Placed);
        outgoing.Add(new(from, Accepted(session, player)));
        outgoing.Add(new(from, CorrectionFor(session)));
        return outgoing;
    }

    private List<Outgoing> HandleMove(Session session, Move move, long nowMs)
    {
        // UDP can reorder: only the newest move counts.
        if (move.Sequence <= session.LastMoveSequence) return [];
        session.LastMoveSequence = move.Sequence;

        // A move made before the client applied our latest correction started from somewhere we refused.
        if (move.CorrectionId != session.CorrectionId) return [];
        session.CorrectionApplied = true;

        var to = new Vector3(move.Position.X, move.Position.Y, move.Position.Z);
        var verdict = _world.TryMove(session.PlayerId, to, move.Yaw, Seconds(nowMs));
        if (verdict == MoveVerdict.Ok) return [];

        Correct(session, ToReason(verdict));
        _logger.LogInformation("Refused move for {Session} to {To}: {Verdict}; correcting to {Position}",
            session, Format(to), verdict, Format(_world.Players[session.PlayerId].Avatar.Position));
        return [new(session.Address, CorrectionFor(session))];
    }

    /// <summary>Starts a new correction: the client must apply it before any of its moves count again.</summary>
    private static void Correct(Session session, CorrectionReason reason)
    {
        session.CorrectionId++;
        session.CorrectionReason = reason;
        session.CorrectionApplied = false;
    }

    private Correction CorrectionFor(Session session)
    {
        var avatar = _world.Players[session.PlayerId].Avatar;
        return new Correction(session.Id, session.CorrectionId, session.CorrectionReason, ToPosition(avatar.Position), avatar.Yaw);
    }

    private ConnectAccepted Accepted(Session session, Player player) =>
        new(session.Nonce, session.Id, player.Id, player.Name, (float)_options.TicksPerSecond);

    private Session? FindSession(IPEndPoint from, ulong id) =>
        _sessionsByAddress.TryGetValue(from, out var session) && session.Id == id ? session : null;

    private void Remove(Session session)
    {
        _sessionsByAddress.Remove(session.Address);
        _sessionsByPlayer.Remove(session.PlayerId);
        _world.Leave(session.PlayerId);
    }

    private static CorrectionReason ToReason(MoveVerdict verdict) => verdict switch
    {
        MoveVerdict.TooFast => CorrectionReason.TooFast,
        MoveVerdict.TooHigh => CorrectionReason.TooHigh,
        MoveVerdict.OutOfBounds => CorrectionReason.OutOfBounds,
        MoveVerdict.Invalid => CorrectionReason.Invalid,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Not a refusal."),
    };

    private static Position ToPosition(Vector3 v) => new(v.X, v.Y, v.Z);

    private static double Seconds(long ms) => ms / 1000.0;

    private static string Format(Vector3 v) => $"({v.X:0.0}, {v.Y:0.0}, {v.Z:0.0})";

    /// <summary>A random, non-zero session id. Unguessable, so another host can't inject packets into a session.</summary>
    private static ulong NewSessionId()
    {
        Span<byte> bytes = stackalloc byte[8];
        ulong id;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            id = BitConverter.ToUInt64(bytes);
        } while (id == 0);
        return id;
    }

    private sealed record Session(IPEndPoint Address, ulong Id, int PlayerId, ulong Nonce)
    {
        public long LastHeardMs { get; set; }
        public uint LastMoveSequence { get; set; }

        /// <summary>The latest correction sent; the client's moves must carry this id to count.</summary>
        public uint CorrectionId { get; set; }

        public CorrectionReason CorrectionReason { get; set; }

        /// <summary>Whether a move carrying <see cref="CorrectionId"/> has arrived; until then the correction repeats each tick.</summary>
        public bool CorrectionApplied { get; set; }

        public override string ToString() => $"player {PlayerId} from {Address}";
    }
}
