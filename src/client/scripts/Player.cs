using Godot;

namespace Galaxies;

/// <summary>
/// The local player's character, seen from behind (third person) by default or through its eyes (V toggles; the
/// mouse wheel zooms). The body moves on physics ticks with physics interpolation on; the camera and the model are
/// top-level and follow the interpolated body every frame, so mouse look stays responsive at any frame rate.
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
	[Export] public bool FirstPerson { get; set; }
	[Export] public float CameraDistance { get; set; } = 3.5f;
	[Export] public float MinCameraDistance { get; set; } = 1.5f;
	[Export] public float MaxCameraDistance { get; set; } = 8.0f;
	[Export(PropertyHint.File, "*.glb")] public string Model { get; set; } = RemoteAvatar.MaleModel;

	/// <summary>How far the third-person camera keeps from walls it would otherwise poke through.</summary>
	private const float CameraMargin = 0.25f;

	/// <summary>Holds the player still, e.g. until the server has said where they are.</summary>
	public bool Frozen { get; set; }

	/// <summary>A line of connection status shown under the controls.</summary>
	public string Status { get; set; } = "";

	/// <summary>Facing, in radians: 0 looks north (-z), positive turns left.</summary>
	public float Yaw => _yaw;

	public float Pitch => _pitch;

	private float _yaw;
	private float _pitch;
	private readonly float _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
	private double _fpsTimer;

	private float _cameraDistance;
	private Vector3 _lastPosition;

	private Camera3D _camera = null!;
	private Label _info = null!;
	private Label _crosshair = null!;
	private Node3D _body = null!;
	private AnimationPlayer? _animation;

	public override void _Ready()
	{
		_camera = GetNode<Camera3D>("Camera3D");
		_info = GetNode<Label>("HUD/Info");
		_crosshair = GetNode<Label>("HUD/Crosshair");
		AddToGroup("player");
		_yaw = Rotation.Y;
		Rotation = Vector3.Zero;
		_camera.TopLevel = true;
		_camera.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		_cameraDistance = CameraDistance;
		_body = new Node3D { Name = "Body", TopLevel = true, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
		_body.AddChild(RemoteAvatar.LoadBody(Model, out _animation));
		AddChild(_body);
		_lastPosition = GlobalPosition;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			_yaw -= motion.ScreenRelative.X * MouseSensitivity;
			_pitch = Mathf.Clamp(_pitch - motion.ScreenRelative.Y * MouseSensitivity, -1.5f, 1.5f);
		}
		else if (@event.IsActionPressed("toggle_view"))
		{
			FirstPerson = !FirstPerson;
		}
		else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel
			&& Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			var zoom = wheel.ButtonIndex == MouseButton.WheelUp ? 0.85f : 1.0f / 0.85f;
			CameraDistance = Mathf.Clamp(CameraDistance * zoom, MinCameraDistance, MaxCameraDistance);
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
		if (Frozen)
		{
			Velocity = Vector3.Zero;
			return;
		}
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
		var feet = GetGlobalTransformInterpolated().Origin;
		var eye = feet + Vector3.Up * EyeHeight;
		var look = Basis.FromEuler(new Vector3(_pitch, _yaw, 0.0f));
		_camera.GlobalTransform = new Transform3D(look, FirstPerson ? eye : eye + look.Z * ThirdPersonDistance(eye, look.Z, (float)delta));

		_body.Visible = !FirstPerson;
		_crosshair.Visible = FirstPerson;
		_body.GlobalTransform = new Transform3D(new Basis(Vector3.Up, _yaw), feet);
		var moved = new Vector2(feet.X - _lastPosition.X, feet.Z - _lastPosition.Z).Length();
		_lastPosition = feet;
		RemoteAvatar.Animate(_animation, moved / Mathf.Max((float)delta, 1e-4f));

		_fpsTimer -= delta;
		if (_fpsTimer <= 0.0)
		{
			_fpsTimer = 0.5;
			_info.Text = $"KESSA OUTPOST   {Engine.GetFramesPerSecond():F0} FPS\nWASD move  SHIFT run  SPACE jump  V view  WHEEL zoom  ESC free mouse\n{Status}";
		}
	}

	/// <summary>
	/// How far behind the eye the third-person camera sits: <see cref="CameraDistance"/>, pulled in to stay in front
	/// of any wall between it and the player. It snaps in at once and eases back out.
	/// </summary>
	private float ThirdPersonDistance(Vector3 eye, Vector3 back, float delta)
	{
		var want = CameraDistance;
		var query = PhysicsRayQueryParameters3D.Create(eye, eye + back * (want + CameraMargin), CollisionMask, [GetRid()]);
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (hit.Count > 0)
			want = Mathf.Max(eye.DistanceTo(hit["position"].AsVector3()) - CameraMargin, 0.0f);
		_cameraDistance = want < _cameraDistance ? want : Mathf.MoveToward(_cameraDistance, want, 6.0f * delta);
		return _cameraDistance;
	}

	/// <summary>Moves the player and points the view, without interpolating from the old spot.</summary>
	public void Teleport(Vector3 pos, float yaw, float pitch)
	{
		GlobalPosition = pos;
		Velocity = Vector3.Zero;
		_yaw = yaw;
		_pitch = pitch;
		_lastPosition = pos;
		ResetPhysicsInterpolation();
	}
}
