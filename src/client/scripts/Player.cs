using Godot;

namespace Galaxies;

/// <summary>
/// First-person walker. The body moves on physics ticks with physics
/// interpolation on; the camera is top-level and follows the interpolated body
/// every frame, so mouse look stays responsive at any frame rate.
/// </summary>
[GlobalClass]
public partial class Player : CharacterBody3D
{
	[Export] public float WalkSpeed { get; set; } = 4.5f;
	[Export] public float RunSpeed { get; set; } = 8.5f;
	[Export] public float JumpVelocity { get; set; } = 4.6f;
	[Export] public float Acceleration { get; set; } = 12.0f;
	[Export] public float AirAcceleration { get; set; } = 2.5f;
	[Export] public float MouseSensitivity { get; set; } = 0.0022f;
	[Export] public float EyeHeight { get; set; } = 1.62f;

	private float _yaw;
	private float _pitch;
	private readonly float _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
	private double _fpsTimer;

	private Camera3D _camera = null!;
	private Label _info = null!;

	public override void _Ready()
	{
		_camera = GetNode<Camera3D>("Camera3D");
		_info = GetNode<Label>("HUD/Info");
		AddToGroup("player");
		_yaw = Rotation.Y;
		Rotation = Vector3.Zero;
		_camera.TopLevel = true;
		_camera.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			_yaw -= motion.ScreenRelative.X * MouseSensitivity;
			_pitch = Mathf.Clamp(_pitch - motion.ScreenRelative.Y * MouseSensitivity, -1.5f, 1.5f);
		}
		else if (@event.IsActionPressed("ui_cancel"))
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
		}
		else if (@event is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		var dt = (float)delta;
		var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		var wish = new Basis(Vector3.Up, _yaw) * new Vector3(input.X, 0.0f, input.Y);
		var speed = Input.IsActionPressed("run") ? RunSpeed : WalkSpeed;
		var accel = IsOnFloor() ? Acceleration : AirAcceleration;
		var velocity = Velocity;
		var horizontal = new Vector3(velocity.X, 0.0f, velocity.Z).MoveToward(wish * speed, accel * speed * dt);
		velocity.X = horizontal.X;
		velocity.Z = horizontal.Z;
		if (IsOnFloor())
		{
			if (Input.IsActionJustPressed("jump"))
				velocity.Y = JumpVelocity;
		}
		else
		{
			velocity.Y -= _gravity * dt;
		}
		Velocity = velocity;
		MoveAndSlide();
	}

	public override void _Process(double delta)
	{
		var eye = GetGlobalTransformInterpolated().Origin + Vector3.Up * EyeHeight;
		_camera.GlobalTransform = new Transform3D(Basis.FromEuler(new Vector3(_pitch, _yaw, 0.0f)), eye);
		_fpsTimer -= delta;
		if (_fpsTimer <= 0.0)
		{
			_fpsTimer = 0.5;
			_info.Text = $"KESSA OUTPOST   {Engine.GetFramesPerSecond():F0} FPS\nWASD move  SHIFT run  SPACE jump  ESC free mouse";
		}
	}

	/// <summary>Moves the player and points the view, without interpolating from the old spot.</summary>
	public void Teleport(Vector3 pos, float yaw, float pitch)
	{
		GlobalPosition = pos;
		Velocity = Vector3.Zero;
		_yaw = yaw;
		_pitch = pitch;
		ResetPhysicsInterpolation();
	}
}
