using System.Collections.Generic;
using Godot;

namespace Galaxies;

/// <summary>
/// Another player, as the server last reported them. Drawn a little in the past (<see cref="Delay"/>) so it can
/// glide between snapshots instead of jumping at each one, and walks or runs to match its speed.
/// </summary>
public partial class RemoteAvatar : Node3D
{
	/// <summary>How far behind the newest snapshot the avatar is drawn: two snapshots at 20 Hz, so one can be lost.</summary>
	private const double Delay = 0.1;

	private static readonly string[] Models = ["res://models/meshy_human_10k.glb", "res://models/meshy_female_10k.glb"];

	private readonly List<(double Time, Vector3 Position, float Yaw)> _samples = [];
	private AnimationPlayer? _animation;
	private Vector3 _lastPosition;

	public int PlayerId { get; private set; }

	public static RemoteAvatar Create(int playerId)
	{
		var avatar = new RemoteAvatar { PlayerId = playerId, Name = $"Player{playerId}" };
		var body = GD.Load<PackedScene>(Models[playerId % Models.Length]).Instantiate<Node3D>();
		// The models face +z; a yaw of 0 looks north (-z).
		body.RotationDegrees = new Vector3(0, 180, 0);
		avatar.AddChild(body);
		avatar._animation = body.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
		return avatar;
	}

	/// <summary>Adds a position from a snapshot, received at <paramref name="time"/> seconds.</summary>
	public void Push(double time, Vector3 position, float yaw)
	{
		if (_samples.Count == 0)
		{
			Position = _lastPosition = position;
			Rotation = new Vector3(0, yaw, 0);
		}
		_samples.Add((time, position, yaw));
		if (_samples.Count > 20) _samples.RemoveAt(0);
	}

	/// <summary>Moves the avatar to where it was <see cref="Delay"/> seconds before <paramref name="now"/>.</summary>
	public void Advance(double now, double delta)
	{
		var at = now - Delay;
		while (_samples.Count > 2 && _samples[1].Time <= at) _samples.RemoveAt(0);
		if (_samples.Count == 0) return;

		var (t0, p0, y0) = _samples[0];
		if (_samples.Count == 1 || at <= t0)
		{
			Position = p0;
			Rotation = new Vector3(0, y0, 0);
		}
		else
		{
			var (t1, p1, y1) = _samples[1];
			var w = (float)Mathf.Clamp((at - t0) / (t1 - t0), 0.0, 1.0);
			Position = p0.Lerp(p1, w);
			Rotation = new Vector3(0, Mathf.LerpAngle(y0, y1, w), 0);
		}

		var speed = new Vector2(Position.X - _lastPosition.X, Position.Z - _lastPosition.Z).Length() / Mathf.Max((float)delta, 1e-4f);
		_lastPosition = Position;
		Animate(speed);
	}

	private void Animate(float speed)
	{
		if (_animation is null) return;
		var name = speed switch
		{
			> 6.0f when _animation.HasAnimation("Run") => "Run",
			> 0.5f when _animation.HasAnimation("Walk") => "Walk",
			_ => "Idle",
		};
		if (_animation.CurrentAnimation == name || !_animation.HasAnimation(name)) return;
		_animation.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
		_animation.Play(name, 0.2);
	}
}
