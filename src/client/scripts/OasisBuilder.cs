using System.Collections;
using System.Collections.Generic;
using Godot;

namespace Galaxies;

/// <summary>
/// Builds Halun Springs, a little oasis in a basin of dunes about 550 m south of Kessa Outpost's south gate, between
/// two mesas. A pool with an island, groves of palms, a pump station and water tower on the north shore, a traders'
/// camp to the west, dome huts to the east, and sandstone outcrops round the south. A line of beacons leads there
/// from the gate.
///
/// It streams in (see <see cref="StreamedLevel"/>). Always there: the coarse ground, the water, palms, rocks,
/// tower, pump house, huts and the beacons, so the oasis shows on the skyline from the city. Streamed: the fine
/// ground, the jetty, pipes, camp, reeds, shrubs, small props and most of the lights.
/// </summary>
[Tool]
[GlobalClass]
public partial class OasisBuilder : StreamedLevel
{
	[Export] public int LayoutSeed { get; set; } = 11;

	private static readonly Dictionary<string, string[]> ScreenText = new()
	{
		["pump"] = ["#HALUN SPRINGS", "PUMP STATION", "", "FLOW.....410L/M", "TOWER.......88%", "LINE TO KESSA OK", "", "!FILTER CLOGGED"],
		["tank"] = ["#WATER TOWER", "LEVEL [#######-]", "PRESSURE.....OK", "", "#POOL", "DEPTH......3.1M", "TEMP........24C", "!ALGAE BLOOM"],
		["traders"] = ["#TRADERS CAMP", "DATES....2CR/KG", "WATER....1.5CR/L", "SALT.........0.9", "", "#CARAVAN", "LEAVES 3RD DAWN", "!NO CREDIT"],
	};

	protected override IReadOnlyDictionary<string, string[]> Texts => ScreenText;

	protected override Vector2 Centre => DesertTerrain.Oasis;

	private static readonly Color Bark = new(0.34f, 0.28f, 0.21f);
	private static readonly Color BarkDark = new(0.2f, 0.16f, 0.12f);
	private static readonly Color Leaf = new(0.16f, 0.27f, 0.09f);
	private static readonly Color LeafTip = new(0.32f, 0.4f, 0.14f);
	private static readonly Color DeadLeaf = new(0.42f, 0.32f, 0.19f);

	private readonly DesertTerrain _terrain = new();
	private readonly float[] _shore = new float[180];
	private Node3D? _farGround;

	// The plan: where everything goes, worked out once so the far and near halves agree.
	private readonly record struct Site(Vector2 Pos, float Yaw, float Floor);
	private readonly record struct Palm(Vector2 Pos, int Variant, float Yaw, float Scale);
	private readonly record struct Rock(Vector2 Pos, float Radius, float Height, float Yaw);
	private readonly List<(Vector2 Pos, float Radius)> _clear = [];
	private readonly List<Palm> _palms = [];
	private readonly List<Rock> _rocks = [];
	private Site _pump, _tower, _camp, _jetty;
	private readonly List<(Site Site, float Radius)> _huts = [];
	private float _jettyLength;

	private float Water => _terrain.WaterLevel;

	// --- plan ----------------------------------------------------------------------

	/// <summary>Ground height at a position relative to the oasis.</summary>
	private float H(Vector2 p) => _terrain.HeightAt(Centre.X + p.X, Centre.Y + p.Y);

	private Vector3 At(Vector2 p, float lift = 0.0f) => new(p.X, H(p) + lift, p.Y);

	/// <summary>A unit vector on the ground: 0 points north (−z), π/2 east.</summary>
	private static Vector2 Dir(float a) => new(Mathf.Sin(a), -Mathf.Cos(a));

	/// <summary>Distance from the middle of the pool to its shore, heading a.</summary>
	private float Shore(float a)
	{
		var f = Mathf.PosMod(a, Mathf.Tau) / Mathf.Tau * _shore.Length;
		var i = (int)f;
		return Mathf.Lerp(_shore[i % _shore.Length], _shore[(i + 1) % _shore.Length], f - i);
	}

	/// <summary>A building site facing the pool, its floor level with the highest ground under it.</summary>
	private Site Facing(float a, float fromShore, float radius)
	{
		var p = Dir(a) * (Shore(a) + fromShore);
		var floor = H(p);
		for (var k = 0; k < 8; k++)
			floor = Mathf.Max(floor, H(p + Dir(Mathf.Tau * k / 8.0f) * radius));
		_clear.Add((p, radius + 2.0f));
		// Turned so its +Z side faces the middle of the pool.
		return new Site(p, -a, floor + 0.1f);
	}

	private bool Clear(Vector2 p, float margin) =>
		_clear.TrueForAll(c => p.DistanceTo(c.Pos) > c.Radius + margin);

	private void Plan()
	{
		_clear.Clear();
		_palms.Clear();
		_rocks.Clear();
		_huts.Clear();
		for (var k = 0; k < _shore.Length; k++)
		{
			var d = Dir(Mathf.Tau * k / _shore.Length);
			// Start past the island.
			var r = 30.0f;
			while (r < 150.0f && H(d * r) < Water + 0.05f)
				r += 0.5f;
			_shore[k] = r;
		}
		var rng = new RandomNumberGenerator { Seed = (ulong)LayoutSeed };

		// The jetty runs out from the north shore, with the pump house behind it.
		const float jettyA = 0.12f;
		var start = Dir(jettyA) * (Shore(jettyA) + 3.0f);
		_jettyLength = 19.0f;
		_jetty = new Site(start, -jettyA, Water + 0.55f);
		for (var t = 0.0f; t <= _jettyLength; t += 4.0f)
			_clear.Add((start - Dir(jettyA) * t, 3.0f));
		_pump = Facing(jettyA, 12.0f, 7.0f);
		_tower = Facing(0.62f, 15.0f, 5.0f);
		_camp = Facing(-1.7f, 15.0f, 11.0f);
		foreach (var (a, r) in ((float A, float R)[])[(1.55f, 4.0f), (1.95f, 3.4f), (2.4f, 4.6f)])
			_huts.Add((Facing(a, 11.0f + r, r + 1.5f), r));
		// The path in from the north stays open.
		for (var t = 0.0f; t < 60.0f; t += 6.0f)
			_clear.Add((Dir(jettyA) * (Shore(jettyA) + 22.0f + t), 3.0f));

		// Sandstone outcrops round the south and a small one to the north-west.
		foreach (var a in (float[])[2.75f, 3.15f, 3.6f, 4.0f, 4.55f, -0.8f])
		{
			var small = a < 0.0f;
			var p = Dir(a + rng.RandfRange(-0.1f, 0.1f)) * rng.RandfRange(100.0f, 125.0f);
			var rad = small ? 7.0f : rng.RandfRange(9.0f, 17.0f);
			_rocks.Add(new Rock(p, rad, small ? 9.0f : rng.RandfRange(11.0f, 26.0f), rng.Randf() * Mathf.Tau));
			_clear.Add((p, rad));
		}

		// Palms in groves round the shore, and a few on the island.
		var groves = new float[8];
		for (var k = 0; k < groves.Length; k++)
			groves[k] = Mathf.Tau * k / groves.Length + rng.RandfRange(-0.25f, 0.25f);
		for (var attempt = 0; attempt < 500 && _palms.Count < 64; attempt++)
		{
			var a = groves[rng.Randi() % groves.Length] + rng.RandfRange(-0.22f, 0.22f);
			var u = rng.Randf();
			var p = Dir(a) * (Shore(a) + 1.0f + 32.0f * u * u);
			if (H(p) < Water + 0.3f || !Clear(p, 1.5f) || _palms.Exists(o => o.Pos.DistanceTo(p) < 3.4f))
				continue;
			_palms.Add(new Palm(p, rng.RandiRange(0, 3), rng.Randf() * Mathf.Tau, rng.RandfRange(0.85f, 1.15f)));
		}
		foreach (var off in (Vector2[])[new(-1.5f, 0.5f), new(2.0f, -1.0f), new(0.5f, 2.5f)])
		{
			var p = DesertTerrain.Island + off;
			if (H(p) > Water + 0.3f)
				_palms.Add(new Palm(p, rng.RandiRange(0, 3), rng.Randf() * Mathf.Tau, rng.RandfRange(0.8f, 1.0f)));
		}
	}

	// --- far: always there -----------------------------------------------------------

	protected override void BuildFar(Node3D far)
	{
		Plan();
		_rng.Seed = (ulong)LayoutSeed + 1;

		// The coarse ground, until the fine ground streams in.
		var mesh = _terrain.Build(DesertTerrain.OasisCells);
		_farGround = new StaticBody3D { Name = "Ground" };
		_farGround.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
		_farGround.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = Kit.Mat("terrain") });
		far.AddChild(_farGround);

		var g = Group(far, new Vector3(Centre.X, 0, Centre.Y));
		g.Name = "Oasis";
		far.AddChild(new MeshInstance3D
		{
			Name = "Water",
			Mesh = new PlaneMesh { Size = new Vector2(240, 240) },
			Position = new Vector3(Centre.X, Water, Centre.Y),
			MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://env/oasis_water.gdshader") },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});

		BuildPalms(g);
		foreach (var rock in _rocks)
			Outcrop(g, rock);
		WaterTower(g);
		PumpHouse(g);
		foreach (var (site, r) in _huts)
		{
			var hut = Group(g, new Vector3(site.Pos.X, site.Floor, site.Pos.Y), site.Yaw);
			Box(hut, new Vector3(2 * r + 2.0f, 3.0f, 2 * r + 4.0f), new Vector3(0, -3.0f, 0.8f), "sandcrete");
			DomeHut(hut, Vector3.Zero, r);
		}
		Beacons(far);
	}

	private void BuildPalms(Node3D g)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)LayoutSeed + 7 };
		(float Height, float Lean)[] variants = [(9.0f, 1.2f), (11.5f, 2.2f), (13.5f, 1.4f), (15.5f, 2.8f)];
		var trunk = new CylinderShape3D { Radius = 0.45f, Height = 4.0f };
		for (var v = 0; v < variants.Length; v++)
		{
			var mine = _palms.FindAll(p => p.Variant == v);
			var mm = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				Mesh = PalmMesh(rng, variants[v].Height, variants[v].Lean),
				InstanceCount = mine.Count,
			};
			for (var i = 0; i < mine.Count; i++)
			{
				var p = mine[i];
				var basis = new Basis(Vector3.Up, p.Yaw).Scaled(Vector3.One * p.Scale);
				mm.SetInstanceTransform(i, new Transform3D(basis, At(p.Pos, -0.3f)));
				ColliderShape(g, trunk, At(p.Pos, 1.7f));
			}
			g.AddChild(new MultiMeshInstance3D { Name = $"Palms{v}", Multimesh = mm, MaterialOverride = Kit.Foliage() });
		}
	}

	/// <summary>A palm leaning towards +X: a stepped six-sided trunk and a crown of creased, drooping fronds.</summary>
	private static ArrayMesh PalmMesh(RandomNumberGenerator rng, float height, float lean)
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		const int segments = 7;
		const int sides = 6;
		Vector3 Spine(float t) => new(lean * t * t, height * t, 0);
		float Radius(float t) => 0.38f - 0.14f * t;
		for (var s = 0; s < segments; s++)
		{
			var t0 = (float)s / segments;
			var t1 = (float)(s + 1) / segments;
			var b = Spine(t0);
			var e = Spine(t1);
			var rb = Radius(t0) * 1.2f;
			var rt = Radius(t1) * 0.95f;
			for (var k = 0; k < sides; k++)
			{
				var d0 = Dir(Mathf.Tau * k / sides);
				var d1 = Dir(Mathf.Tau * (k + 1) / sides);
				var n0 = new Vector3(d0.X, 0, d0.Y);
				var n1 = new Vector3(d1.X, 0, d1.Y);
				Kit.AddPoly(st, [b + n0 * rb, b + n1 * rb, e + n1 * rt, e + n0 * rt], (n0 + n1) * 0.5f,
					null, [BarkDark, BarkDark, Bark, Bark]);
			}
		}
		// A knob where the fronds meet.
		var top = Spine(1.0f);
		var r1 = Radius(1.0f) * 0.95f;
		var knob = top + Vector3.Up * 0.55f;
		for (var k = 0; k < sides; k++)
		{
			var n0 = new Vector3(Dir(Mathf.Tau * k / sides).X, 0, Dir(Mathf.Tau * k / sides).Y);
			var n1 = new Vector3(Dir(Mathf.Tau * (k + 1) / sides).X, 0, Dir(Mathf.Tau * (k + 1) / sides).Y);
			Kit.AddPoly(st, [top + n0 * r1, top + n1 * r1, knob], (n0 + n1) * 0.5f + Vector3.Up * 0.3f,
				null, [BarkDark, BarkDark, Leaf]);
		}

		var fronds = rng.RandiRange(9, 11);
		for (var f = 0; f < fronds + 2; f++)
		{
			// Two dead fronds hang under the crown.
			var dead = f >= fronds;
			var a = dead ? rng.Randf() * Mathf.Tau : Mathf.Tau * f / fronds + rng.RandfRange(-0.2f, 0.2f);
			var length = rng.RandfRange(3.8f, 5.2f) * Mathf.Sqrt(height / 12.0f) * (dead ? 0.7f : 1.0f);
			var (rise, droop) = dead ? (-0.3f, 0.9f) : f % 3 == 0 ? (0.95f, 0.75f) : (0.55f, 1.15f);
			var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			var side = new Vector3(-d.Z, 0, d.X);
			const int stations = 6;
			var left = new Vector3[stations + 1];
			var mid = new Vector3[stations + 1];
			var right = new Vector3[stations + 1];
			var colors = new Color[stations + 1];
			for (var k = 0; k <= stations; k++)
			{
				var u = (float)k / stations;
				var c = knob - Vector3.Up * 0.2f + d * (length * u) + Vector3.Up * (length * (rise * u - droop * u * u));
				var w = length * 0.15f * Mathf.Pow(Mathf.Sin(Mathf.Pi * u), 0.6f);
				mid[k] = c;
				left[k] = c - side * w - Vector3.Up * (0.4f * w);
				right[k] = c + side * w - Vector3.Up * (0.4f * w);
				colors[k] = dead ? DeadLeaf : Leaf.Lerp(LeafTip, u);
			}
			for (var k = 0; k < stations; k++)
			{
				Color[] cs = [colors[k], colors[k], colors[k + 1], colors[k + 1]];
				Kit.AddPoly(st, [left[k], mid[k], mid[k + 1], left[k + 1]], Vector3.Up, null, cs);
				Kit.AddPoly(st, [mid[k], right[k], right[k + 1], mid[k + 1]], Vector3.Up, null, cs);
			}
		}
		return st.Commit();
	}

	/// <summary>A craggy sandstone outcrop: a big faceted lathe with a smaller one shouldering out of it.</summary>
	private void Outcrop(Node3D g, Rock rock)
	{
		var low = Mathf.Min(H(rock.Pos), H(rock.Pos + Dir(rock.Yaw) * rock.Radius));
		var o = Group(g, new Vector3(rock.Pos.X, low - 1.0f, rock.Pos.Y), rock.Yaw);
		var r = rock.Radius;
		var h = rock.Height;
		Lathe(o, [
			new(r, -3), new(r * 0.97f, h * 0.3f), new(r * 0.86f, h * 0.42f), new(r * 0.8f, h * 0.82f),
			new(r * 0.62f, h), new(0, h),
		], 7, Vector3.Zero, "rock", r);
		var r2 = r * 0.6f;
		Lathe(o, [
			new(r2, -3), new(r2 * 0.95f, h * 0.45f), new(r2 * 0.75f, h * 0.62f), new(0, h * 0.66f),
		], 6, new Vector3(r * 0.75f, 0, r * 0.3f), "rock", r2);
	}

	/// <summary>The landmark: a tank on four legs with a cyan band, a red beacon and a floodlight over the pool.</summary>
	private void WaterTower(Node3D g)
	{
		var t = Group(g, new Vector3(_tower.Pos.X, _tower.Floor, _tower.Pos.Y), _tower.Yaw);
		t.Name = "WaterTower";
		const float deck = 18.0f;
		Box(t, new Vector3(8, 4.2f, 8), new Vector3(0, -4.0f, 0), "sandcrete");
		foreach (var x in Signs)
		{
			foreach (var z in Signs)
			{
				Box(t, new Vector3(1.2f, 0.6f, 1.2f), new Vector3(x * 2.8f, 0.2f, z * 2.8f), "dark");
				Box(t, new Vector3(0.6f, deck, 0.6f), new Vector3(x * 2.8f, 0.2f, z * 2.8f), "dark", true, 0.06f);
			}
			foreach (var y in (float[])[6.0f, 12.0f])
			{
				Box(t, new Vector3(5.6f, 0.3f, 0.3f), new Vector3(0, y, x * 2.8f), "dark", false, 0.04f);
				Box(t, new Vector3(0.3f, 0.3f, 5.6f), new Vector3(x * 2.8f, y, 0), "dark", false, 0.04f);
			}
		}
		// Ladder up the front leg.
		Box(t, new Vector3(0.6f, deck, 0.08f), new Vector3(2.8f, 0.2f, 3.15f), "metal", false, 0.02f);
		Box(t, new Vector3(7.6f, 0.4f, 7.6f), new Vector3(0, deck, 0), "metal");
		Box(t, new Vector3(7.8f, 0.12f, 7.8f), new Vector3(0, deck, 0), "hazard", false, 0.02f);
		Lathe(t, [
			new(3.4f, 0), new(3.6f, 0.3f), new(3.6f, 5.4f), new(3.4f, 5.7f), new(1.2f, 7.0f), new(0, 7.2f),
		], 12, new Vector3(0, deck + 0.4f, 0), "olive", 3.6f);
		var band = new MeshInstance3D
		{
			Mesh = Kit.Lathe([new(3.64f, 0), new(3.64f, 0.3f)], 12, false),
			MaterialOverride = Kit.Glow(Cyan, 1.4f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Position = new Vector3(0, deck + 3.9f, 0),
		};
		t.AddChild(band);
		for (var k = 0; k < 4; k++)
		{
			var a = Mathf.Tau * k / 4.0f + 0.4f;
			var n = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			Spot(t, n * 4.0f + new Vector3(0, deck + 4.0f, 0), n - Vector3.Up * 0.2f, Cyan, 0.5f, 7.0f, 70.0f, true, 0.3f, 60.0f);
		}
		Box(t, new Vector3(0.24f, 3.0f, 0.24f), new Vector3(0, deck + 7.4f, 0), "dark", false, 0.04f);
		Glow(t, new Vector3(0.45f, 0.45f, 0.45f), new Vector3(0, deck + 10.4f, 0), Red, 6.0f);
		// Floodlight on the deck over the water.
		var lamp = new Vector3(0, deck + 0.6f, 3.9f);
		Box(t, new Vector3(1.4f, 0.8f, 1.2f), lamp, "dark", false);
		Glow(t, new Vector3(1.0f, 0.6f, 0.05f), lamp + new Vector3(0, 0.1f, 0.6f), Cool, 5.0f);
		Spot(t, lamp + new Vector3(0, 0.4f, 0.9f), new Vector3(0, -0.55f, 1), Cool, 5.0f, 70.0f, 24.0f, true, 1.5f, 180.0f);
	}

	private void PumpHouse(Node3D g)
	{
		var p = Group(g, new Vector3(_pump.Pos.X, _pump.Floor, _pump.Pos.Y), _pump.Yaw);
		p.Name = "PumpHouse";
		Box(p, new Vector3(11, 3.0f, 9), new Vector3(0, -3.0f, 0), "sandcrete");
		Enterable(p, Vector3.Zero, 10, 8, 5, 0.0f, 2.4f, 2.8f);
		Sign(p, new Vector3(0, 4.6f, 4.15f), new Vector2(4.0f, 0.8f), Yellow, 0.0f, 6);
		// A vent stack and a pressure tank on the roof.
		Box(p, new Vector3(1.4f, 3.0f, 1.4f), new Vector3(-3.0f, 5.6f, -2.0f), "metal");
		Box(p, new Vector3(1.8f, 0.3f, 1.8f), new Vector3(-3.0f, 8.6f, -2.0f), "dark", false);
		Lathe(p, [new(1.2f, 0), new(1.2f, 2.2f), new(0.7f, 2.6f), new(0, 2.7f)], 10, new Vector3(2.4f, 5.6f, -1.0f), "metal");
	}

	/// <summary>A line of beacon posts across the dunes from Kessa's south gate to the oasis.</summary>
	private void Beacons(Node3D far)
	{
		var g = Group(far, Vector3.Zero);
		g.Name = "Beacons";
		var from = new Vector2(0, 285);
		var to = Centre + Dir(0.12f) * (Shore(0.12f) + 34.0f);
		var count = Mathf.FloorToInt(from.DistanceTo(to) / 30.0f);
		for (var k = 0; k <= count; k++)
		{
			var p = from.Lerp(to, (float)k / count);
			var y = _terrain.HeightAt(p.X, p.Y) - 0.8f;
			Box(g, new Vector3(0.35f, 3.0f, 0.35f), new Vector3(p.X, y, p.Y), "dark", true, 0.06f);
			Box(g, new Vector3(0.4f, 0.4f, 0.4f), new Vector3(p.X, y + 1.6f, p.Y), "hazard", false, 0.04f);
			Glow(g, new Vector3(0.3f, 0.3f, 0.3f), new Vector3(p.X, y + 3.0f, p.Y), Beacon, 5.0f);
		}
	}

	// --- near: streamed in --------------------------------------------------------------

	protected override IEnumerable BuildNear(Node3D near)
	{
		_rng.Seed = (ulong)LayoutSeed + 2;

		// The fine ground, a coarse row at a time.
		var cells = DesertTerrain.OasisCells;
		for (var row = 0; row < cells.Size.Y; row++)
		{
			var strip = new Rect2I(cells.Position.X, cells.Position.Y + row, cells.Size.X, 1);
			var mesh = _terrain.Build(strip, 8, null, cells);
			var body = new StaticBody3D { Name = $"Ground{row}" };
			body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
			body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = Kit.Mat("terrain") });
			near.AddChild(body);
			yield return null;
		}

		var g = Group(near, new Vector3(Centre.X, 0, Centre.Y));
		g.Name = "Oasis";
		Jetty(g);
		yield return null;
		PumpRoom(g);
		yield return null;
		Camp(g);
		yield return null;
		foreach (var (site, r) in _huts)
		{
			var yard = Group(g, new Vector3(site.Pos.X, site.Floor, site.Pos.Y), site.Yaw);
			Vaporator(yard, new Vector3(-r - 2.0f, -0.2f, -1.0f));
			if (_rng.Randf() < 0.6f)
				Barrel(yard, new Vector3(r + 1.2f, 0, r * 0.4f), _rng.Randf() < 0.5f ? Rust : new Color(0.5f, 0.56f, 0.42f));
			CrateStack(yard, new Vector3(r + 0.8f, 0, -r * 0.6f), _rng.RandiRange(1, 2));
		}
		yield return null;
		Reeds(g);
		yield return null;
		Shrubs(g);
		yield return null;
		Boulders(g);
	}

	protected override void OnNearChanged(bool loaded)
	{
		if (_farGround is null)
			return;
		_farGround.Visible = !loaded;
		_farGround.ProcessMode = loaded ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
	}

	/// <summary>A plated walkway out over the water, with the pump's intake pipe along it and a lamp at the end.</summary>
	private void Jetty(Node3D g)
	{
		var j = Group(g, new Vector3(_jetty.Pos.X, 0, _jetty.Pos.Y), _jetty.Yaw);
		j.Name = "Jetty";
		var deck = _jetty.Floor;
		var along = -Dir(-_jetty.Yaw);
		for (var z = 0.0f; z < _jettyLength; z += 2.0f)
			Box(j, new Vector3(3.0f, 0.3f, 2.0f), new Vector3(0, deck - 0.3f, z + 1.0f), "floor", true, 0.04f);
		foreach (var s in Signs)
		{
			Box(j, new Vector3(0.12f, 0.04f, _jettyLength), new Vector3(s * 1.38f, deck, _jettyLength * 0.5f), "hazard", false, 0.01f);
			for (var z = 0.5f; z < _jettyLength; z += 4.5f)
			{
				var floor = H(_jetty.Pos + along * z) - 0.4f;
				Box(j, new Vector3(0.35f, deck - 0.3f - floor, 0.35f), new Vector3(s * 1.3f, floor, z), "dark", true, 0.05f);
				Box(j, new Vector3(0.1f, 1.0f, 0.1f), new Vector3(s * 1.4f, deck, z), "dark", false, 0.02f);
			}
			Box(j, new Vector3(0.1f, 0.1f, _jettyLength - 1.0f), new Vector3(s * 1.4f, deck + 1.0f, _jettyLength * 0.5f), "dark", false, 0.02f);
		}
		// Intake: along the right rail and down into the water at the end.
		var pipeY = deck + 0.25f;
		Pipe(j, new Vector3(1.9f, pipeY, -5.0f), new Vector3(1.9f, pipeY, _jettyLength - 1.0f), 0.28f);
		Pipe(j, new Vector3(1.9f, pipeY, _jettyLength - 1.0f), new Vector3(1.9f, deck - 2.5f, _jettyLength - 1.0f), 0.28f);
		Box(j, new Vector3(0.5f, 0.5f, 0.3f), new Vector3(1.9f, pipeY - 0.25f, 4.0f), "paint", false, 0.05f, Orange);
		StreetLamp(j, new Vector3(-1.1f, deck, _jettyLength - 0.6f), new Vector3(1, 0, 0));
	}

	/// <summary>A pipe of the given radius from a to b, with flanges at the ends.</summary>
	private static void Pipe(Node3D parent, Vector3 a, Vector3 b, float r)
	{
		var d = b - a;
		var len = d.Length();
		var g = new Node3D { Position = a, Basis = new Basis(new Quaternion(Vector3.Up, d / len)) };
		parent.AddChild(g);
		Lathe(g, [new(r, 0), new(r, len)], 8, Vector3.Zero, "metal", 0.0f, null, false);
		foreach (var y in (float[])[0.0f, len - 0.25f])
			Lathe(g, [new(r * 1.4f, 0), new(r * 1.4f, 0.25f)], 8, new Vector3(0, y, 0), "dark", 0.0f, null, true, true);
	}

	/// <summary>The pump inside the pump house, and the pipe from it up to the tower.</summary>
	private void PumpRoom(Node3D g)
	{
		var p = Group(g, new Vector3(_pump.Pos.X, _pump.Floor, _pump.Pos.Y), _pump.Yaw);
		const float f = 0.14f;
		// Two pumps on a plinth, with orange valve wheels.
		Box(p, new Vector3(5.0f, 0.3f, 2.4f), new Vector3(0, f, -1.6f), "dark");
		foreach (var s in Signs)
		{
			var c = new Vector3(s * 1.3f, f + 0.3f, -1.6f);
			Lathe(p, [new(0.8f, 0), new(0.8f, 1.6f), new(0.6f, 1.8f), new(0.3f, 1.9f), new(0, 1.9f)], 10, c, "metal", 0.8f);
			Box(p, new Vector3(0.6f, 0.08f, 0.6f), c + new Vector3(0, 2.3f, 0), "paint", false, 0.03f, Orange);
			Box(p, new Vector3(0.1f, 0.4f, 0.1f), c + new Vector3(0, 1.9f, 0), "dark", false, 0.02f);
			Glow(p, new Vector3(0.12f, 0.12f, 0.04f), c + new Vector3(0, 1.2f, 0.82f), s < 0 ? Green : Amber, 4.0f);
		}
		Pipe(p, new Vector3(-1.3f, f + 2.5f, -1.6f), new Vector3(1.3f, f + 2.5f, -1.6f), 0.2f);
		Console(p, new Vector3(-3.6f, f, 1.4f), Mathf.Pi * 0.5f, "pump");
		Screen(p, new Vector3(4.33f, 2.2f, 0.4f), -Mathf.Pi * 0.5f, "tank");
		CeilingLamp(p, new Vector3(0, 5.0f, 0), f, Cool);
		// Out through the back wall and up the hill to the tower.
		var foot = new Vector3(_tower.Pos.X, _tower.Floor + 1.4f, _tower.Pos.Y);
		var back = p.Transform * new Vector3(0, 1.4f, -6.0f);
		Pipe(g, p.Transform * new Vector3(0, 1.4f, -3.9f), back, 0.3f);
		Pipe(g, back, foot, 0.3f);
		var d = foot - back;
		for (var t = 5.0f; t < d.Length() - 3.0f; t += 6.0f)
		{
			var q = back + d.Normalized() * t;
			var ground = H(new Vector2(q.X, q.Z)) - 0.3f;
			if (q.Y - ground > 0.6f)
				Box(g, new Vector3(0.4f, q.Y - ground - 0.3f, 0.6f), new Vector3(q.X, ground, q.Z), "dark", false, 0.05f);
		}
		Pipe(g, foot, foot + new Vector3(0, 18.0f, 0), 0.3f);
		// A floodlit apron with crates by the door.
		var apron = Group(g, new Vector3(_pump.Pos.X, _pump.Floor, _pump.Pos.Y), _pump.Yaw);
		CrateStack(apron, new Vector3(3.6f, 0, 5.4f), 2);
		Barrel(apron, new Vector3(-3.4f, 0, 5.0f), Rust);
		Barrel(apron, new Vector3(-4.2f, 0, 5.6f), Rust);
	}

	/// <summary>Three tents round a fire, two stalls, a kiosk and the traders' skiff.</summary>
	private void Camp(Node3D g)
	{
		var c = Group(g, new Vector3(_camp.Pos.X, 0, _camp.Pos.Y), _camp.Yaw);
		c.Name = "Camp";
		Vector3 On(float x, float z)
		{
			var w = c.Transform * new Vector3(x, 0, z);
			return new Vector3(x, H(new Vector2(w.X, w.Z)), z);
		}
		// The fire pit.
		var fire = On(0, 0);
		for (var k = 0; k < 8; k++)
		{
			var a = Mathf.Tau * k / 8.0f;
			Box(c, new Vector3(0.5f, 0.35f, 0.4f), fire + new Vector3(Mathf.Cos(a) * 1.0f, -0.1f, Mathf.Sin(a) * 1.0f), "rock", false, 0.08f,
				null, new Vector3(0, -a, 0));
		}
		Glow(c, new Vector3(1.0f, 0.12f, 1.0f), fire + new Vector3(0, -0.02f, 0), Beacon, 4.0f);
		Box(c, new Vector3(1.4f, 0.18f, 0.18f), fire + new Vector3(0, 0.08f, 0), "dark", false, 0.04f, null, new Vector3(0, 0.6f, 0));
		Box(c, new Vector3(1.4f, 0.18f, 0.18f), fire + new Vector3(0, 0.08f, 0), "dark", false, 0.04f, null, new Vector3(0, -0.7f, 0));
		Omni(c, fire + new Vector3(0, 1.2f, 0), Beacon, 2.2f, 14.0f, true, 0.6f, 110.0f);
		for (var k = 0; k < 3; k++)
		{
			var a = Mathf.Pi * 0.75f + k * Mathf.Pi * 0.5f + 0.1f;
			var pos = On(Mathf.Cos(a) * 6.5f, Mathf.Sin(a) * 6.5f);
			// Each tent opens towards the fire.
			Tent(c, pos, Mathf.Atan2(-pos.X, -pos.Z), Cloth[k % Cloth.Length]);
			Box(c, new Vector3(1.6f, 0.45f, 0.5f), On(Mathf.Cos(a) * 3.0f, Mathf.Sin(a) * 3.0f) - new Vector3(0, 0.1f, 0), "olive",
				true, 0.08f, null, new Vector3(0, Mathf.Pi * 0.5f - a, 0));
		}
		// Stalls and the kiosk face the path round the shore.
		Stall(c, On(-3.0f, 9.0f), Mathf.Pi, Cloth[0]);
		Stall(c, On(2.0f, 9.5f), Mathf.Pi, Cloth[1]);
		Kiosk(c, On(6.0f, 7.0f), 0.0f, "traders");
		CrateStack(c, On(8.5f, 1.0f), 3);
		CrateStack(c, On(8.0f, -1.6f), 1);
		for (var k = 0; k < 4; k++)
			Barrel(c, On(-8.5f + (k % 2) * 0.9f, 2.0f + k / 2 * 0.9f), k == 3 ? Orange : Rust);
		Skiff(c, On(4.0f, -8.0f), 1.2f);
	}

	private void Tent(Node3D parent, Vector3 pos, float yaw, Color cloth)
	{
		var t = Group(parent, pos, yaw);
		foreach (var z in Signs)
			Box(t, new Vector3(0.14f, 2.6f, 0.14f), new Vector3(0, -0.2f, z * 1.9f), "dark", false, 0.03f);
		Box(t, new Vector3(0.18f, 0.18f, 4.2f), new Vector3(0, 2.4f, 0), "dark", false, 0.04f);
		const float slope = 0.82f;
		foreach (var s in Signs)
			Box(t, new Vector3(2.45f, 0.07f, 4.4f), new Vector3(s * 0.8f, 1.62f, 0), "paint", true, 0.03f, cloth,
				new Vector3(0, 0, -s * slope));
		Box(t, new Vector3(1.0f, 0.2f, 2.0f), new Vector3(-0.5f, 0, -0.6f), "paint", false, 0.06f, Cloth[(int)(yaw * 7) & 1]);
		Glow(t, new Vector3(0.2f, 0.25f, 0.2f), new Vector3(0, 1.8f, 1.2f), Amber, 3.0f);
		Omni(t, new Vector3(0, 1.6f, 1.2f), Amber, 0.5f, 4.5f, false, 0.0f, 35.0f);
	}

	/// <summary>Clumps of reeds along the waterline, as one multimesh.</summary>
	private void Reeds(Node3D g)
	{
		var xforms = new List<Transform3D>();
		var colors = new List<Color>();
		for (var k = 0; k < 160; k++)
		{
			var a = Mathf.Tau * k / 160.0f + _rng.RandfRange(-0.02f, 0.02f);
			if (_rng.Randf() < 0.35f)
				continue;
			var shore = Dir(a) * (Shore(a) + _rng.RandfRange(-1.5f, 1.0f));
			if (!Clear(shore, 0.5f))
				continue;
			var count = _rng.RandiRange(3, 8);
			for (var i = 0; i < count; i++)
			{
				var p = shore + new Vector2(_rng.RandfRange(-1.2f, 1.2f), _rng.RandfRange(-1.2f, 1.2f));
				var ground = H(p);
				if (ground < Water - 0.6f || ground > Water + 0.5f)
					continue;
				var h = _rng.RandfRange(1.0f, 2.3f);
				var basis = Basis.FromEuler(new Vector3(_rng.RandfRange(-0.2f, 0.2f), _rng.Randf() * Mathf.Tau, _rng.RandfRange(-0.2f, 0.2f)))
					.Scaled(new Vector3(1, h, 1));
				xforms.Add(new Transform3D(basis, new Vector3(p.X, ground, p.Y) + basis * new Vector3(0, 0.5f, 0)));
				colors.Add(Leaf.Lerp(DeadLeaf, _rng.Randf() * 0.6f));
			}
		}
		Scatter(g, "Reeds", Kit.Box(new Vector3(0.08f, 1.0f, 0.08f), 0.02f), xforms, colors);
	}

	/// <summary>Low faceted shrubs in the green band above the shore, as one multimesh.</summary>
	private void Shrubs(Node3D g)
	{
		var mesh = Kit.Lathe([new(0.3f, 0), new(1.0f, 0.35f), new(0.85f, 0.8f), new(0.4f, 1.1f), new(0, 1.15f)], 6, true);
		var xforms = new List<Transform3D>();
		var colors = new List<Color>();
		for (var attempt = 0; attempt < 600 && xforms.Count < 110; attempt++)
		{
			var a = _rng.Randf() * Mathf.Tau;
			var p = Dir(a) * (Shore(a) + _rng.RandfRange(1.0f, 40.0f));
			var ground = H(p);
			if (ground < Water + 0.5f || ground > Water + 4.0f || !Clear(p, 0.5f))
				continue;
			var s = _rng.RandfRange(0.4f, 1.1f);
			var basis = new Basis(Vector3.Up, _rng.Randf() * Mathf.Tau).Scaled(new Vector3(s, s * _rng.RandfRange(0.6f, 1.0f), s));
			xforms.Add(new Transform3D(basis, new Vector3(p.X, ground - 0.15f, p.Y)));
			colors.Add(new Color(0.1f, 0.15f, 0.05f).Lerp(new Color(0.22f, 0.22f, 0.1f), _rng.Randf()));
		}
		Scatter(g, "Shrubs", mesh, xforms, colors);
	}

	private static void Scatter(Node3D g, string name, Mesh mesh, List<Transform3D> xforms, List<Color> colors)
	{
		var mm = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = mesh,
			InstanceCount = xforms.Count,
		};
		for (var i = 0; i < xforms.Count; i++)
		{
			mm.SetInstanceTransform(i, xforms[i]);
			mm.SetInstanceColor(i, colors[i]);
		}
		g.AddChild(new MultiMeshInstance3D { Name = name, Multimesh = mm, MaterialOverride = Kit.Foliage() });
	}

	/// <summary>Loose sandstone boulders round the basin.</summary>
	private void Boulders(Node3D g)
	{
		for (var attempt = 0; attempt < 50; attempt++)
		{
			var a = _rng.Randf() * Mathf.Tau;
			var p = Dir(a) * (Shore(a) + _rng.RandfRange(4.0f, 45.0f));
			var r = _rng.RandfRange(0.6f, 2.2f);
			if (H(p) < Water + 0.3f || !Clear(p, r))
				continue;
			var h = r * _rng.RandfRange(0.8f, 1.5f);
			var b = Group(g, At(p, -0.4f), _rng.Randf() * Mathf.Tau);
			Lathe(b, [new(r, 0), new(r * 0.95f, h * 0.5f), new(r * 0.6f, h), new(0, h * 1.05f)], 5, Vector3.Zero, "rock", r * 0.9f);
		}
	}
}
