using System.Collections.Generic;
using System.Linq;
using Galaxies.Net;
using Galaxies.Protocol;
using Godot;

namespace Galaxies;

/// <summary>
/// Connects the player to the game server. The client moves its own avatar and reports where it went; the server
/// decides where it really is. The player can't move until the server has placed them, and any move the server
/// refuses snaps the player back to where the server has them (a <see cref="Correction"/>). Other players are drawn
/// from the server's snapshots.
/// </summary>
/// <remarks>
/// Settings can be overridden on the command line after <c>--</c>: <c>--host=</c>, <c>--port=</c>, <c>--token=</c>,
/// and <c>--offline</c> to walk around without a server.
/// </remarks>
public partial class NetworkClient : Node
{
	[Export] public string Host { get; set; } = "localhost";
	[Export] public int Port { get; set; } = 5090;
	[Export] public string Token { get; set; } = "dev-player-1";
	[Export] public bool Offline { get; set; }
	[Export] public NodePath PlayerPath { get; set; } = "../Player";

	private readonly Dictionary<int, RemoteAvatar> _remotes = [];
	private ServerConnection? _connection;
	private Player _player = null!;
	private Node3D _remoteRoot = null!;
	private uint _correctionId;
	private double _moveTimer;
	private string _status = "";
	private string _name = "";

	public override void _Ready()
	{
		_player = GetNode<Player>(PlayerPath);
		_remoteRoot = new Node3D { Name = "RemotePlayers" };
		GetParent().CallDeferred(Node.MethodName.AddChild, _remoteRoot);

		foreach (var arg in OS.GetCmdlineUserArgs())
		{
			var (key, value) = arg.Split('=', 2) is [var k, var v] ? (k, v) : (arg, "");
			switch (key)
			{
				case "--host": Host = value; break;
				case "--port": Port = value.ToInt(); break;
				case "--token": Token = value; break;
				case "--offline": Offline = true; break;
			}
		}

		if (Offline)
		{
			_player.Status = "OFFLINE";
			return;
		}

		// Nothing moves until the server says where we are.
		_player.Frozen = true;
		_connection = new ServerConnection(Host, Port, Token);
		_connection.Log += message =>
		{
			GD.Print("[net] ", message);
			_status = message.ToUpperInvariant();
		};
		_connection.Connected += OnConnected;
		_connection.Disconnected += OnDisconnected;
		_connection.CorrectionReceived += OnCorrection;
		_connection.SnapshotReceived += OnSnapshot;
	}

	public override void _Process(double delta)
	{
		if (_connection is null) return;
		_connection.Update();

		// Report where we are, but only once we're standing where the server put us.
		_moveTimer -= delta;
		if (_connection.State == ConnectionState.Connected && _correctionId > 0 && _moveTimer <= 0)
		{
			_moveTimer = ProtocolConstants.MoveInterval.TotalSeconds;
			var p = _player.GlobalPosition;
			_connection.SendMove(_correctionId, new Position(p.X, p.Y, p.Z), _player.Yaw);
		}

		var now = Time.GetTicksMsec() / 1000.0;
		foreach (var remote in _remotes.Values)
			remote.Advance(now, delta);

		_player.Status = _connection.State == ConnectionState.Connected
			? $"{_name.ToUpperInvariant()}   {_remotes.Count} NEARBY"
			: _status;
	}

	public override void _ExitTree()
	{
		_connection?.Dispose();
	}

	private void OnConnected(ConnectAccepted accepted)
	{
		_name = accepted.PlayerName;
		_correctionId = 0; // Each session's corrections count from 1; wait for the server to place us.
		GD.Print($"[net] Connected as {accepted.PlayerName} (player {accepted.PlayerId})");
	}

	private void OnDisconnected()
	{
		_player.Frozen = true;
		_correctionId = 0;
		foreach (var remote in _remotes.Values) remote.QueueFree();
		_remotes.Clear();
	}

	private void OnCorrection(Correction correction)
	{
		if (correction.Id <= _correctionId) return; // A repeat of one already applied.
		_correctionId = correction.Id;
		var p = correction.Position;
		_player.Teleport(new Vector3(p.X, p.Y, p.Z), correction.Yaw, _player.Pitch);
		_player.Frozen = false;
		if (correction.Reason != CorrectionReason.Placed)
			GD.Print($"[net] Server refused a move ({correction.Reason}); put back at ({p.X:0.0}, {p.Y:0.0}, {p.Z:0.0})");
	}

	private void OnSnapshot(WorldSnapshot snapshot)
	{
		var now = Time.GetTicksMsec() / 1000.0;
		var seen = new HashSet<int>();
		foreach (var a in snapshot.Avatars)
		{
			seen.Add(a.PlayerId);
			if (!_remotes.TryGetValue(a.PlayerId, out var remote))
			{
				remote = RemoteAvatar.Create(a.PlayerId);
				_remoteRoot.AddChild(remote);
				_remotes.Add(a.PlayerId, remote);
			}
			remote.Push(now, new Vector3(a.Position.X, a.Position.Y, a.Position.Z), a.Yaw);
		}

		// Out of range or gone: the server only sends who's near us.
		foreach (var id in _remotes.Keys.Where(id => !seen.Contains(id)).ToList())
		{
			_remotes[id].QueueFree();
			_remotes.Remove(id);
		}
	}
}
