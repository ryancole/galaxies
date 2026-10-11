using System.Collections;
using System.Diagnostics;
using Godot;

namespace Galaxies;

/// <summary>
/// A level that streams in as the camera nears it, like a World of Warcraft zone.
///
/// It has two halves. The far half (<see cref="BuildFar"/>) is built once and always there: the big shapes you
/// can see from across the desert, with their colliders. The near half (<see cref="BuildNear"/>) is all the
/// detail, built when the camera comes within <see cref="LoadRadius"/> and freed again beyond
/// <see cref="UnloadRadius"/>. It's built hidden, a slice at a time, a few milliseconds a frame (adding nodes to
/// the tree is part of the slice), and only shown once it's whole, so walking up to a level never hitches. Arriving right inside it (a teleport, or the
/// server placing you there) builds it at once instead.
///
/// In the editor both halves are built straight away; press Rebuild after changing anything.
/// </summary>
[Tool]
public abstract partial class StreamedLevel : LevelBuilder
{
	[Export] public float LoadRadius { get; set; } = 400.0f;
	[Export] public float UnloadRadius { get; set; } = 500.0f;

	/// <summary>Time the near half may take to build each frame.</summary>
	[Export] public float FrameBudgetMs { get; set; } = 3.0f;

	[ExportToolButton("Rebuild", Icon = "Reload")]
	public Callable RebuildButton => Callable.From(Rebuild);

	/// <summary>Whether the near half is in the tree.</summary>
	public bool NearLoaded => _near is not null;

	/// <summary>Where the level is, in world x/z; distances are measured from here.</summary>
	protected abstract Vector2 Centre { get; }

	/// <summary>Builds the half that's always there into far.</summary>
	protected abstract void BuildFar(Node3D far);

	/// <summary>
	/// Builds the detail into near, yielding whenever it's a good moment to pause. near is in the tree but hidden
	/// until it's done; the far half is still showing. Reseed _rng first so the level comes back the same every time it streams in.
	/// </summary>
	protected abstract IEnumerable BuildNear(Node3D near);

	/// <summary>Called when the near half has been added (true) or freed (false).</summary>
	protected virtual void OnNearChanged(bool loaded)
	{
	}

	private Node3D? _near;
	private Node3D? _pending;
	private IEnumerator? _build;

	public override void _Ready()
	{
		Rebuild();
	}

	public void Rebuild()
	{
		CancelBuild();
		_near = null;
		foreach (var child in GetChildren())
			child.Free();
		var far = new Node3D { Name = "Far" };
		AddChild(far);
		BuildFar(far);
		if (Engine.IsEditorHint())
			LoadNow();
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint() || GetViewport().GetCamera3D() is not { } camera)
			return;
		var pos = camera.GlobalPosition;
		var dist = new Vector2(pos.X, pos.Z).DistanceTo(Centre);
		if (dist > UnloadRadius)
		{
			CancelBuild();
			Unload();
		}
		else if (_near is null && (_build is not null || dist < LoadRadius))
		{
			if (dist < LoadRadius * 0.5f)
				LoadNow();
			else
				StepBuild();
		}
	}

	/// <summary>Builds the near half for a frame's budget, and adds it once it's done.</summary>
	private void StepBuild()
	{
		StartBuild();
		var clock = Stopwatch.StartNew();
		while (clock.Elapsed.TotalMilliseconds < FrameBudgetMs)
		{
			if (!_build!.MoveNext())
			{
				Attach();
				return;
			}
		}
	}

	/// <summary>Builds (or finishes building) the near half right now.</summary>
	private void LoadNow()
	{
		if (_near is not null)
			return;
		StartBuild();
		while (_build!.MoveNext())
		{
		}
		Attach();
	}

	private void StartBuild()
	{
		if (_build is not null)
			return;
		_pending = new Node3D { Name = "Near", Visible = false };
		AddChild(_pending);
		_build = BuildNear(_pending).GetEnumerator();
	}

	private void Attach()
	{
		_near = _pending!;
		_pending = null;
		_build = null;
		_near.Visible = true;
		OnNearChanged(true);
	}

	private void Unload()
	{
		if (_near is null)
			return;
		_near.QueueFree();
		_near = null;
		OnNearChanged(false);
	}

	private void CancelBuild()
	{
		_pending?.QueueFree();
		_pending = null;
		_build = null;
	}

	public override void _ExitTree()
	{
		CancelBuild();
	}
}
