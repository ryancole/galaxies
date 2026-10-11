using System.Collections.Generic;
using Godot;

namespace Galaxies;

/// <summary>
/// Shared base for the level builders: the palette, the props every level uses (doors, lamps, screens, crates,
/// vaporators, skiffs...) and the node helpers that place kit meshes with their colliders and lights.
/// Everything random draws from _rng, so a builder that reseeds it gets the same layout every time.
/// </summary>
public abstract partial class LevelBuilder : Node3D
{
	protected static readonly Color Warm = new(1.0f, 0.8f, 0.58f);
	protected static readonly Color Amber = new(1.0f, 0.66f, 0.3f);
	protected static readonly Color Sodium = new(1.0f, 0.74f, 0.45f);
	protected static readonly Color Cool = new(0.8f, 0.9f, 1.0f);
	protected static readonly Color Cyan = new(0.3f, 0.8f, 1.0f);
	protected static readonly Color Blue = new(0.25f, 0.45f, 1.0f);
	protected static readonly Color Green = new(0.35f, 1.0f, 0.45f);
	protected static readonly Color Yellow = new(1.0f, 0.78f, 0.15f);
	protected static readonly Color Red = new(1.0f, 0.15f, 0.06f);
	protected static readonly Color Beacon = new(1.0f, 0.45f, 0.1f);
	protected static readonly Color Orange = new(0.95f, 0.45f, 0.12f);
	protected static readonly Color Rust = new(0.62f, 0.42f, 0.3f);
	protected static readonly Color[] Cloth = [new(0.62f, 0.38f, 0.27f), new(0.42f, 0.5f, 0.48f), new(0.64f, 0.58f, 0.42f)];

	protected static readonly float[] Signs = [-1.0f, 1.0f];

	protected readonly RandomNumberGenerator _rng = new();
	private readonly Dictionary<string, Shape3D> _shapes = new();

	protected const float WallThickness = 0.6f;

	/// <summary>The lines for each screen key used by this level.</summary>
	protected abstract IReadOnlyDictionary<string, string[]> Texts { get; }

	// --- props ---------------------------------------------------------------------

	/// <summary>
	/// The shell of a hollow building with a doorway in its +Z wall: floor, lined walls, ceiling, roof and a heavy
	/// doorframe with a lamp. Furnish the inside, which is (w, d) less a <see cref="WallThickness"/> wall each side.
	/// </summary>
	protected Node3D Enterable(Node3D parent, Vector3 pos, float w, float d, float h, float rotY, float door, float doorH)
	{
		var g = Group(parent, pos, rotY);
		const float t = WallThickness;
		var fz = d * 0.5f - t * 0.5f;
		var pw = (w - door) * 0.5f;
		// Shell.
		Box(g, new Vector3(w, 0.14f, d), Vector3.Zero, "floor", true, 0.03f);
		Box(g, new Vector3(w, h, t), new Vector3(0, 0, -fz), "sandcrete");
		foreach (var s in Signs)
		{
			Box(g, new Vector3(t, h, d - 2 * t), new Vector3(s * (w * 0.5f - t * 0.5f), 0, 0), "sandcrete");
			Box(g, new Vector3(pw, h, t), new Vector3(s * (door * 0.5f + pw * 0.5f), 0, fz), "sandcrete");
		}
		Box(g, new Vector3(door, h - doorH, t), new Vector3(0, doorH, fz), "sandcrete");
		Box(g, new Vector3(w + 0.4f, 0.6f, d + 0.4f), new Vector3(0, h, 0), "dark");
		// Interior lining, skirting and ceiling.
		var iw = w - 2 * t;
		var id = d - 2 * t;
		const float lt = 0.06f;
		var ipw = (iw - door) * 0.5f;
		Box(g, new Vector3(iw, h - 0.14f, lt), new Vector3(0, 0.14f, -id * 0.5f + lt * 0.5f), "olive", false, 0.02f);
		Box(g, new Vector3(iw, 0.4f, 0.16f), new Vector3(0, 0.14f, -id * 0.5f + 0.08f), "dark", false, 0.04f);
		foreach (var s in Signs)
		{
			Box(g, new Vector3(lt, h - 0.14f, id), new Vector3(s * (iw * 0.5f - lt * 0.5f), 0.14f, 0), "olive", false, 0.02f);
			Box(g, new Vector3(0.16f, 0.4f, id), new Vector3(s * (iw * 0.5f - 0.08f), 0.14f, 0), "dark", false, 0.04f);
			Box(g, new Vector3(ipw, h - 0.14f, lt), new Vector3(s * (door * 0.5f + ipw * 0.5f), 0.14f, id * 0.5f - lt * 0.5f), "olive", false, 0.02f);
		}
		Box(g, new Vector3(door, h - doorH, lt), new Vector3(0, doorH, id * 0.5f - lt * 0.5f), "olive", false, 0.02f);
		Box(g, new Vector3(iw, lt, id), new Vector3(0, h - lt, 0), "dark", false, 0.02f);
		// Heavy open doorframe and a lamp over the door.
		Door(g, new Vector3(0, 0, d * 0.5f), true, door, doorH, true);
		return g;
	}

	protected void DomeHut(Node3D parent, Vector3 pos, float r)
	{
		var wall = _rng.RandfRange(2.6f, 3.4f);
		Lathe(parent, [
			new(r + 0.3f, 0), new(r + 0.3f, 0.5f), new(r, 0.6f), new(r, wall),
			new(r * 0.93f, wall + r * 0.36f), new(r * 0.72f, wall + r * 0.68f),
			new(r * 0.4f, wall + r * 0.9f), new(0, wall + r * 0.98f),
		], 10, pos, "sandcrete", r + 0.3f);
		Lathe(parent, [
			new(r - 0.1f, wall - 0.35f), new(r + 0.14f, wall - 0.35f), new(r + 0.14f, wall + 0.05f), new(r - 0.1f, wall + 0.05f),
		], 10, pos, "dark", 0.0f, null, false);
		Lathe(parent, [new(0.5f, 0), new(0.5f, 0.6f), new(0.3f, 0.7f)], 8,
			pos + new Vector3(0, wall + r * 0.98f - 0.1f, 0), "dark", 0.0f);
		// Entrance vestibule facing +Z.
		var v = pos + new Vector3(0, 0, r + 0.4f);
		Box(parent, new Vector3(2.8f, 3.2f, 2.0f), v, "sandcrete");
		Box(parent, new Vector3(3.0f, 0.4f, 2.2f), v + new Vector3(0, 3.2f, 0), "dark", false);
		Door(parent, v + new Vector3(0, 0, 1.0f), _rng.Randf() < 0.3f, 1.6f, 2.5f);
	}

	/// <summary>A doorway (or closed door) set into a facade at pos, facing +Z.</summary>
	protected void Door(Node3D parent, Vector3 pos, bool lamp, float width = 2.0f, float height = 2.8f, bool open = false)
	{
		foreach (var s in Signs)
		{
			var x = s * (width * 0.5f + 0.25f);
			Box(parent, new Vector3(0.5f, height + 0.4f, 0.7f), pos + new Vector3(x, 0, 0), "dark");
			Box(parent, new Vector3(0.16f, height * 0.7f, 0.04f), pos + new Vector3(x, 0.4f, 0.36f), "hazard", false, 0.01f);
		}
		Box(parent, new Vector3(width + 1.0f, 0.6f, 0.7f), pos + new Vector3(0, height, 0), "dark");
		if (!open)
		{
			Box(parent, new Vector3(width, height, 0.12f), pos + new Vector3(0, 0, 0.02f), "metal", false, 0.03f);
			Box(parent, new Vector3(0.3f, 0.45f, 0.06f), pos + new Vector3(width * 0.5f + 0.85f, 1.1f, 0.03f), "dark", false, 0.02f);
			Glow(parent, new Vector3(0.12f, 0.12f, 0.04f), pos + new Vector3(width * 0.5f + 0.85f, 1.38f, 0.07f), Blue, 4.0f);
		}
		if (lamp)
		{
			Glow(parent, new Vector3(width * 0.8f, 0.14f, 0.14f), pos + new Vector3(0, height + 0.65f, 0.3f), Amber, 3.0f);
			Spot(parent, pos + new Vector3(0, height + 0.6f, 0.55f), new Vector3(0, -0.8f, 1), Amber, 1.4f, 8.0f, 60.0f, true, 0.8f, 45.0f);
		}
	}

	protected void DomeRoof(Node3D g, Vector3 pos, float r)
	{
		Lathe(g, [
			new(r, 0), new(r, 0.8f), new(r * 0.92f, r * 0.38f + 0.8f), new(r * 0.7f, r * 0.7f + 0.8f),
			new(r * 0.38f, r * 0.92f + 0.8f), new(0, r + 0.8f),
		], 12, pos, "sandcrete", 0.0f);
	}

	protected void Stall(Node3D parent, Vector3 pos, float rotY, Color cloth)
	{
		var g = Group(parent, pos, rotY);
		foreach (var x in (float[])[-1.4f, 1.4f])
		{
			foreach (var z in (float[])[-1.0f, 1.0f])
				Box(g, new Vector3(0.15f, z < 0 ? 2.6f : 2.3f, 0.15f), new Vector3(x, 0, z), "dark", false, 0.03f);
		}
		Box(g, new Vector3(3.4f, 0.12f, 2.6f), new Vector3(0, 2.38f, 0), "paint", false, 0.04f, cloth, new Vector3(0.12f, 0, 0));
		Box(g, new Vector3(3.0f, 1.0f, 0.8f), new Vector3(0, 0, 0.9f), "metal");
		Color[] goods = [Orange, new(0.5f, 0.55f, 0.42f), new(0.7f, 0.62f, 0.45f), Rust];
		for (var k = 0; k < 4; k++)
		{
			var c = goods[_rng.Randi() % 4];
			Box(g, new Vector3(0.4f, 0.3f, 0.35f), new Vector3(-1.0f + k * 0.65f, 1.0f, 0.9f), "paint", false, 0.04f, c);
		}
		Glow(g, new Vector3(0.3f, 0.2f, 0.3f), new Vector3(0, 2.0f, 0), Amber, 3.0f);
		Omni(g, new Vector3(0, 1.8f, 0.2f), Amber, 0.6f, 5.0f, false, 0.0f, 35.0f);
	}

	protected void StreetLamp(Node3D parent, Vector3 pos, Vector3 arm)
	{
		var g = Group(parent, pos, Mathf.Atan2(arm.X, arm.Z));
		Box(g, new Vector3(0.8f, 0.6f, 0.8f), Vector3.Zero, "metal");
		Box(g, new Vector3(0.35f, 6.0f, 0.35f), Vector3.Zero, "dark", true, 0.06f);
		Box(g, new Vector3(0.25f, 0.25f, 1.9f), new Vector3(0, 5.7f, 0.8f), "dark", false, 0.05f);
		Box(g, new Vector3(0.75f, 0.35f, 0.75f), new Vector3(0, 5.45f, 1.6f), "dark", false, 0.06f);
		Glow(g, new Vector3(0.55f, 0.05f, 0.55f), new Vector3(0, 5.42f, 1.6f), Sodium, 5.0f);
		Spot(g, new Vector3(0, 5.35f, 1.6f), Vector3.Down, Sodium, 5.0f, 9.0f, 28.0f, true, 3.0f, 70.0f);
		Omni(g, new Vector3(0, 4.5f, 1.6f), Sodium, 0.7f, 12.0f, false, 0.0f, 70.0f);
	}

	protected void CeilingLamp(Node3D parent, Vector3 ceiling, float floorY, Color color, bool omniShadow = true)
	{
		Box(parent, new Vector3(0.9f, 0.3f, 0.9f), ceiling - new Vector3(0, 0.36f, 0), "dark", false, 0.05f);
		Glow(parent, new Vector3(0.6f, 0.05f, 0.6f), ceiling - new Vector3(0, 0.39f, 0), color, 5.0f);
		var drop = ceiling.Y - floorY;
		Spot(parent, ceiling - new Vector3(0, 0.45f, 0), Vector3.Down, color, 5.0f, drop + 1.5f, 20.0f, true, 4.0f, 45.0f);
		Omni(parent, ceiling - new Vector3(0, 0.9f, 0), color, 0.8f, 9.0f, omniShadow, 0.0f, 45.0f);
	}

	/// <summary>A wall-mounted yellow sign with blocky dark lettering and a dim light.</summary>
	protected void Sign(Node3D parent, Vector3 center, Vector2 size, Color color, float rotY, int letters)
	{
		var g = Group(parent, center, rotY);
		Box(g, new Vector3(size.X + 0.24f, size.Y + 0.24f, 0.18f), new Vector3(0, -(size.Y + 0.24f) * 0.5f, 0), "dark", false, 0.05f);
		Glow(g, new Vector3(size.X, size.Y, 0.04f), new Vector3(0, -size.Y * 0.5f, 0.09f), color, 2.5f);
		if (letters > 0)
		{
			var lh = size.Y * 0.55f;
			var lw = (size.X - 0.4f) / letters;
			for (var i = 0; i < letters; i++)
			{
				var x = -size.X * 0.5f + 0.2f + i * lw + lw * 0.15f;
				var cw = lw * 0.7f;
				var bits = _rng.RandiRange(3, 31);
				if ((bits & 1) != 0) Box(g, new Vector3(0.07f, lh, 0.02f), new Vector3(x, -lh * 0.5f, 0.14f), "dark", false, 0.0f);
				if ((bits & 2) != 0) Box(g, new Vector3(0.07f, lh, 0.02f), new Vector3(x + cw, -lh * 0.5f, 0.14f), "dark", false, 0.0f);
				if ((bits & 4) != 0) Box(g, new Vector3(cw, 0.07f, 0.02f), new Vector3(x + cw * 0.5f, lh * 0.5f - 0.07f, 0.14f), "dark", false, 0.0f);
				if ((bits & 8) != 0) Box(g, new Vector3(cw, 0.07f, 0.02f), new Vector3(x + cw * 0.5f, -0.035f, 0.14f), "dark", false, 0.0f);
				if ((bits & 16) != 0) Box(g, new Vector3(cw, 0.07f, 0.02f), new Vector3(x + cw * 0.5f, -lh * 0.5f, 0.14f), "dark", false, 0.0f);
			}
		}
		Spot(g, new Vector3(0, 0, 0.35f), new Vector3(0, -0.35f, 1), color, 0.8f, 5.0f, 65.0f, true, 0.4f, 40.0f);
	}

	protected void Screen(Node3D parent, Vector3 center, float rotY, string key, Vector2? size = null)
	{
		parent.AddChild(new TerminalScreen
		{
			Lines = Texts[key],
			ScreenSize = size ?? new Vector2(1.2f, 0.9f),
			Position = center,
			Rotation = new Vector3(0, rotY, 0),
		});
	}

	protected void Console(Node3D parent, Vector3 pos, float rotY, string key)
	{
		var g = Group(parent, pos, rotY);
		Box(g, new Vector3(1.6f, 0.85f, 0.8f), Vector3.Zero, "dark");
		Box(g, new Vector3(1.7f, 0.1f, 0.9f), new Vector3(0, 0.85f, 0), "metal", false, 0.03f);
		for (var k = 0; k < 4; k++)
			Glow(g, new Vector3(0.1f, 0.03f, 0.08f), new Vector3(-0.5f + k * 0.33f, 0.95f, 0.25f), k != 2 ? Blue : Green, 4.0f);
		Box(g, new Vector3(0.12f, 0.6f, 0.12f), new Vector3(0, 0.95f, -0.25f), "dark", false, 0.03f);
		Screen(g, new Vector3(0, 1.85f, -0.25f), 0.0f, key, new Vector2(1.0f, 0.75f));
	}

	protected void Kiosk(Node3D parent, Vector3 pos, float rotY, string key)
	{
		var g = Group(parent, pos, rotY);
		Box(g, new Vector3(1.0f, 1.2f, 0.6f), new Vector3(0, 0.1f, 0), "dark");
		Box(g, new Vector3(1.4f, 0.2f, 0.9f), new Vector3(0, 0.1f, 0), "metal", false, 0.04f);
		Screen(g, new Vector3(0, 1.85f, 0), 0.0f, key, new Vector2(1.0f, 0.75f));
	}

	protected void Crate(Node3D parent, Vector3 pos, float rotY)
	{
		Box(parent, new Vector3(1.2f, 1.2f, 1.2f), pos, "crate", true, 0.08f, null, new Vector3(0, rotY, 0));
	}

	protected void CrateStack(Node3D parent, Vector3 pos, int count)
	{
		for (var i = 0; i < count; i++)
		{
			var jitter = new Vector3(_rng.RandfRange(-0.1f, 0.1f), 0, _rng.RandfRange(-0.1f, 0.1f));
			Crate(parent, pos + jitter + new Vector3(0, i * 1.2f, 0), _rng.RandfRange(-0.15f, 0.15f) + (i % 2 == 1 ? Mathf.Pi * 0.5f : 0.0f));
		}
		if (count < 3 && _rng.Randf() < 0.5f)
			Crate(parent, pos + new Vector3(1.3f, 0, 0), _rng.RandfRange(-0.2f, 0.2f));
	}

	protected void Barrel(Node3D parent, Vector3 pos, Color tint)
	{
		Lathe(parent, [
			new(0.36f, 0), new(0.4f, 0.05f), new(0.4f, 0.46f), new(0.43f, 0.48f), new(0.43f, 0.56f),
			new(0.4f, 0.58f), new(0.4f, 1.0f), new(0.34f, 1.05f), new(0, 1.05f),
		], 10, pos, "paint", 0.43f, tint);
	}

	protected void Vaporator(Node3D parent, Vector3 pos)
	{
		Lathe(parent, [
			new(0.65f, 0), new(0.65f, 0.35f), new(0.38f, 0.45f), new(0.38f, 2.3f), new(0.58f, 2.4f),
			new(0.58f, 2.9f), new(0.32f, 3.0f), new(0.32f, 4.4f), new(0.46f, 4.5f), new(0.46f, 4.75f),
			new(0.14f, 4.85f), new(0.14f, 5.5f), new(0, 5.5f),
		], 10, pos, "metal", 0.6f);
		for (var k = 0; k < 3; k++)
		{
			var fin = Group(parent, pos, Mathf.Tau * k / 3.0f);
			Box(fin, new Vector3(0.08f, 1.4f, 0.5f), new Vector3(0, 3.0f, 0.5f), "dark", false, 0.02f);
		}
		Glow(parent, new Vector3(0.1f, 0.1f, 0.06f), pos + new Vector3(0, 1.6f, 0.39f), Blue, 4.0f);
	}

	/// <summary>A small hover skiff, or a stripped wreck of one.</summary>
	protected void Skiff(Node3D parent, Vector3 pos, float rotY, bool wrecked = false)
	{
		var g = Group(parent, pos, rotY);
		var y = wrecked ? 0.0f : 0.6f;
		Box(g, new Vector3(2.2f, 0.7f, 4.4f), new Vector3(0, y, 0), "paint", true, 0.12f, wrecked ? Rust : Orange);
		Box(g, new Vector3(1.8f, 0.5f, 1.6f), new Vector3(0, y + 0.7f, -0.4f), "dark", false, 0.1f);
		if (!wrecked)
			Glow(g, new Vector3(1.6f, 0.35f, 0.05f), new Vector3(0, y + 0.75f, 0.42f), Cyan, 1.2f);
		foreach (var s in Signs)
		{
			Box(g, new Vector3(0.7f, 0.7f, 1.6f), new Vector3(s * 1.35f, y + 0.1f, -1.8f), "dark", true, 0.1f);
			if (!wrecked)
				Glow(g, new Vector3(0.5f, 0.5f, 0.04f), new Vector3(s * 1.35f, y + 0.2f, -2.62f), Blue, 4.0f);
		}
		if (!wrecked)
		{
			Glow(g, new Vector3(1.6f, 0.04f, 3.4f), new Vector3(0, y - 0.02f, 0), Blue, 1.5f);
			Spot(g, new Vector3(0, y - 0.1f, 0), Vector3.Down, Blue, 1.0f, 3.0f, 70.0f, true, 0.3f, 40.0f);
		}
	}

	/// <summary>A chunky freighter, built nose towards +Z.</summary>
	protected void Ship(Node3D parent, Vector3 pos, float rotY)
	{
		var g = Group(parent, pos, rotY);
		foreach (var x in (float[])[-3.0f, 3.0f])
		{
			foreach (var z in (float[])[-5.0f, 5.0f])
			{
				Box(g, new Vector3(0.6f, 1.3f, 0.6f), new Vector3(x, 0, z), "dark", false, 0.06f);
				Box(g, new Vector3(1.2f, 0.2f, 1.2f), new Vector3(x, 0, z), "dark", false, 0.05f);
			}
		}
		Box(g, new Vector3(8, 2.8f, 16), new Vector3(0, 1.2f, 0), "metal", true, 0.3f);
		Box(g, new Vector3(8.2f, 0.3f, 16.2f), new Vector3(0, 1.2f, 0), "hazard", false, 0.05f);
		Box(g, new Vector3(5, 1.6f, 9), new Vector3(0, 4.0f, -1.5f), "metal", true, 0.25f);
		Box(g, new Vector3(3.2f, 1.8f, 3.6f), new Vector3(0, 1.8f, 9.6f), "dark", true, 0.3f);
		Glow(g, new Vector3(2.4f, 0.32f, 0.05f), new Vector3(0, 2.9f, 11.42f), Cyan, 0.9f);
		foreach (var s in Signs)
		{
			Box(g, new Vector3(5, 0.5f, 6), new Vector3(s * 6.4f, 2.4f, -2), "metal", true, 0.15f);
			Box(g, new Vector3(0.6f, 0.6f, 6.2f), new Vector3(s * 9.1f, 2.35f, -2), "paint", false, 0.1f, Orange);
			Box(g, new Vector3(0.05f, 0.5f, 10), new Vector3(s * 4.03f, 2.6f, 0), "paint", false, 0.01f, Orange);
			Box(g, new Vector3(2.6f, 2.6f, 5), new Vector3(s * 4.2f, 1.6f, -8.5f), "dark", true, 0.25f);
			Glow(g, new Vector3(2.0f, 2.0f, 0.06f), new Vector3(s * 4.2f, 1.9f, -11.03f), Blue, 5.0f);
			Spot(g, new Vector3(s * 4.2f, 2.9f, -11.4f), Vector3.Forward, Blue, 1.5f, 8.0f, 60.0f, true, 1.0f, 60.0f);
		}
	}

	protected void Crawler(Node3D parent, Vector3 pos, float rotY)
	{
		var g = Group(parent, pos, rotY);
		foreach (var x in (float[])[-9.0f, 9.0f])
		{
			foreach (var z in (float[])[-10.0f, 10.0f])
				Box(g, new Vector3(7, 4, 8), new Vector3(x, 0, z), "dark", true, 0.3f);
		}
		Box(g, new Vector3(24, 8, 32), new Vector3(0, 3, 0), "paint", true, 0.4f, Rust);
		Box(g, new Vector3(22, 3, 8), new Vector3(0, 6.5f, 17), "paint", true, 0.4f, Rust, new Vector3(-0.5f, 0, 0));
		Box(g, new Vector3(18, 7, 20), new Vector3(0, 11, -3), "paint", true, 0.4f, Rust);
		Box(g, new Vector3(8, 6, 8), new Vector3(0, 18, -8), "dark", true, 0.3f);
		Glow(g, new Vector3(14, 0.5f, 0.1f), new Vector3(0, 15.5f, 7.02f), Amber, 1.6f);
		Box(g, new Vector3(8, 0.5f, 12), new Vector3(0, 1.5f, 20), "dark", true, 0.1f, null, new Vector3(0.22f, 0, 0));
		foreach (var s in Signs)
		{
			var lamp = new Vector3(s * 8.0f, 9.5f, 16.5f);
			Box(g, new Vector3(1.4f, 0.8f, 1.0f), lamp, "dark", false);
			Glow(g, new Vector3(1.0f, 0.6f, 0.05f), lamp + new Vector3(0, 0.1f, 0.5f), Amber, 5.0f);
			Spot(g, lamp + new Vector3(0, 0.4f, 0.8f), new Vector3(0, -0.6f, 1), Amber, 4.0f, 40.0f, 30.0f, true, 2.0f, 160.0f);
		}
	}

	// --- node helpers --------------------------------------------------------------

	protected static Node3D Group(Node3D parent, Vector3 pos, float rotY = 0.0f)
	{
		var g = new Node3D { Position = pos, Rotation = new Vector3(0, rotY, 0) };
		parent.AddChild(g);
		return g;
	}

	/// <summary>A chamfered box standing on pos (pos is the bottom centre).</summary>
	protected Node3D Box(Node3D parent, Vector3 size, Vector3 pos, string surface, bool collide = true, float bevel = 0.12f,
		Color? tint = null, Vector3? rot = null)
	{
		var mi = new MeshInstance3D { Mesh = Kit.Box(size, bevel), MaterialOverride = Kit.Mat(surface, tint) };
		return Place(parent, mi, pos, rot ?? Vector3.Zero, size.Y * 0.5f, collide ? BoxShape(size) : null,
			Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)));
	}

	protected static void Glow(Node3D parent, Vector3 size, Vector3 pos, Color color, float energy = 3.0f)
	{
		var mi = new MeshInstance3D
		{
			Mesh = Kit.Box(size, Mathf.Min(0.03f, Mathf.Min(size.X, Mathf.Min(size.Y, size.Z)) * 0.3f)),
			MaterialOverride = Kit.Glow(color, energy),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		Place(parent, mi, pos, Vector3.Zero, size.Y * 0.5f, null, Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)));
	}

	protected static void Lathe(Node3D parent, Vector2[] profile, int sides, Vector3 pos, string surface,
		float collideRadius = 0.0f, Color? tint = null, bool capTop = true, bool capBottom = false)
	{
		var mi = new MeshInstance3D
		{
			Mesh = Kit.Lathe(profile, sides, capTop, capBottom),
			MaterialOverride = Kit.Mat(surface, tint),
		};
		var height = profile[^1].Y;
		Place(parent, mi, pos, Vector3.Zero, 0.0f, null, Mathf.Max(collideRadius * 2.0f, height));
		if (collideRadius > 0.0f)
			ColliderShape(parent, new CylinderShape3D { Radius = collideRadius, Height = height }, pos + new Vector3(0, height * 0.5f, 0));
	}

	/// <summary>Adds a mesh centred lift above pos, with an optional static collider.</summary>
	protected static Node3D Place(Node3D parent, MeshInstance3D mi, Vector3 pos, Vector3 rot, float lift, Shape3D? shape, float extent)
	{
		// Small details pop out at a distance to keep the draw count down.
		if (extent < 1.5f)
		{
			mi.VisibilityRangeEnd = 140.0f;
			mi.VisibilityRangeEndMargin = 10.0f;
		}
		Node3D node = mi;
		if (shape is not null)
		{
			var body = new StaticBody3D();
			body.AddChild(new CollisionShape3D { Shape = shape });
			body.AddChild(mi);
			node = body;
		}
		node.Position = pos + Basis.FromEuler(rot) * new Vector3(0, lift, 0);
		node.Rotation = rot;
		parent.AddChild(node);
		return node;
	}

	protected static void ColliderShape(Node3D parent, Shape3D shape, Vector3 center)
	{
		var body = new StaticBody3D { Position = center };
		body.AddChild(new CollisionShape3D { Shape = shape });
		parent.AddChild(body);
	}

	protected BoxShape3D BoxShape(Vector3 size)
	{
		var key = Kit.Key(size);
		if (!_shapes.TryGetValue(key, out var shape))
		{
			shape = new BoxShape3D { Size = size };
			_shapes[key] = shape;
		}
		return (BoxShape3D)shape;
	}

	protected static void Spot(Node3D parent, Vector3 pos, Vector3 dir, Color color, float energy, float range, float angle,
		bool shadow, float fog, float fade)
	{
		var n = dir.Normalized();
		var l = new SpotLight3D
		{
			Position = pos,
			Basis = Basis.LookingAt(n, Mathf.Abs(n.Y) > 0.95f ? Vector3.Forward : Vector3.Up),
			LightColor = color,
			LightEnergy = energy,
			SpotRange = range,
			SpotAngle = angle,
			ShadowEnabled = shadow,
			ShadowBlur = 0.4f,
			LightVolumetricFogEnergy = fog,
		};
		Fade(l, fade);
		parent.AddChild(l);
	}

	protected static void Omni(Node3D parent, Vector3 pos, Color color, float energy, float range, bool shadow, float fog, float fade)
	{
		var l = new OmniLight3D
		{
			Position = pos,
			LightColor = color,
			LightEnergy = energy,
			OmniRange = range,
			ShadowEnabled = shadow,
			ShadowBlur = 0.4f,
			LightVolumetricFogEnergy = fog,
		};
		Fade(l, fade);
		parent.AddChild(l);
	}

	/// <summary>Gives a positional light a distance fade so far lights cost nothing.</summary>
	protected static void Fade(Light3D l, float dist)
	{
		l.DistanceFadeEnabled = true;
		l.DistanceFadeBegin = dist;
		l.DistanceFadeLength = dist * 0.25f;
		l.DistanceFadeShadow = dist * 0.6f;
	}

	protected List<T> Shuffled<T>(List<T> items)
	{
		var result = new List<T>(items);
		for (var i = result.Count - 1; i > 0; i--)
		{
			var j = _rng.RandiRange(0, i);
			(result[i], result[j]) = (result[j], result[i]);
		}
		return result;
	}
}
