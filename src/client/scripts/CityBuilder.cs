using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Galaxies;

/// <summary>
/// Builds Kessa Outpost, a walled spaceport town on a desert planet, from the
/// modular kit in Kit. Everything is generated from CitySeed when the scene
/// loads, in the editor too; press Rebuild after changing the seed.
///
/// Layout: a square wall (Half from the centre) with a gate in each side,
/// avenues along both axes, a paved plaza with the cantina at the centre, three
/// docking bays, and 20 m lots everywhere else. Outside: dunes, moisture farms,
/// a crawler, a wreck and a ring of mesas.
/// </summary>
[Tool]
[GlobalClass]
public partial class CityBuilder : Node3D
{
	[Export] public int CitySeed { get; set; } = 7;

	[ExportToolButton("Rebuild", Icon = "Reload")]
	public Callable RebuildButton => Callable.From(Rebuild);

	private const float Half = 250.0f;
	private const float LotSize = 20.0f;
	private const float FlatRadius = 370.0f;
	private const float TerrainSize = 3600.0f;
	private const int TerrainCells = 150;
	private static readonly Vector2[] Bays = [new(-120, -120), new(120, -120), new(-120, 130)];

	private static readonly Color Warm = new(1.0f, 0.8f, 0.58f);
	private static readonly Color Amber = new(1.0f, 0.66f, 0.3f);
	private static readonly Color Sodium = new(1.0f, 0.74f, 0.45f);
	private static readonly Color Cool = new(0.8f, 0.9f, 1.0f);
	private static readonly Color Cyan = new(0.3f, 0.8f, 1.0f);
	private static readonly Color Blue = new(0.25f, 0.45f, 1.0f);
	private static readonly Color Green = new(0.35f, 1.0f, 0.45f);
	private static readonly Color Yellow = new(1.0f, 0.78f, 0.15f);
	private static readonly Color Red = new(1.0f, 0.15f, 0.06f);
	private static readonly Color Beacon = new(1.0f, 0.45f, 0.1f);
	private static readonly Color Orange = new(0.95f, 0.45f, 0.12f);
	private static readonly Color Rust = new(0.62f, 0.42f, 0.3f);
	private static readonly Color[] Cloth = [new(0.62f, 0.38f, 0.27f), new(0.42f, 0.5f, 0.48f), new(0.64f, 0.58f, 0.42f)];

	private static readonly Dictionary<string, string[]> Text = new()
	{
		["gate"] = ["#KESSA OUTPOST", "GATE CONTROL", "", "GATE.......OPEN", "PATROL....2 CREW", "WATER...RATION B", "", "!DUST FRONT 40KM"],
		["bay"] = ["#DOCKING BAY", "BERTH...OCCUPIED", "CLAMPS....LOCKED", "FUEL [######--]", "COOLANT......74%", "", "!REPULSOR HOT", "!KEEP CLEAR"],
		["plaza"] = ["#PUBLIC NOTICE", "CURFEW 2ND DUSK", "WATER 3 CR/LITRE", "", "#ARRIVALS", "TALLAH.....BAY 1", "SKIFF 09...BAY 3", "!BAY 2 SERVICING"],
		["market"] = ["#EXCHANGE", "WATER........3.0", "POWER CELL..12.5", "MOTOR OIL....4.2", "SCRAP/KG.....0.8", "", "!NO CREDIT", ""],
		["cantina"] = ["#TONIGHT", "THE RUSTBUCKETS", "LIVE 2ND DUSK", "", "#HOUSE RULES", "NO BLASTERS", "NO BRAWLING", "!PAY IN ADVANCE"],
		["tab"] = ["#BAR TAB 0417", "CACTUS GIN...x2", "SAND ALE.....x5", "", "TOTAL.....41 CR", "", "!OVERDUE", ""],
		["traffic"] = ["#TRAFFIC CONTROL", "APPROACH...CLEAR", "WIND...12KM/H W", "VIS.........8KM", "", "#BEACON", "STATUS....ACTIVE", "!SENSOR 4 DOWN"],
		["power"] = ["#POWER GRID", "SECTOR A.....OK", "SECTOR B.....OK", "SECTOR C....LOW", "SECTOR D.....OK", "", "LOAD.........83%", "!CELL BANK 2 HOT"],
		["water"] = ["#WATER", "VAPORATORS...112", "ONLINE.......104", "YIELD...9.4KL/D", "RESERVE......61%", "", "!FARM 3 SILTED", ""],
		["workshop"] = ["#DIAGNOSTIC", "SKIFF 14", "MOTIVATOR...FAIL", "REPULSORS....OK", "STEERING.....OK", "", "!REPLACE PART", ""],
		["hab"] = ["#MESSAGES (2)", "FROM: UNCLE", "BRING THE NEW", "CONVERTER BEFORE", "THE HARVEST.", "", "#WEATHER", "!STORM WARNING"],
		["storage"] = ["#INVENTORY", "CRATES.......148", "POWER CELLS...36", "RATIONS.....2.1T", "WATER.......9KL", "", "!AISLE 3 BLOCKED", ""],
	};

	private static readonly float[] Signs = [-1.0f, 1.0f];

	private readonly RandomNumberGenerator _rng = new();
	private readonly FastNoiseLite _noise = new();
	private readonly Dictionary<string, Shape3D> _shapes = new();

	public override void _Ready()
	{
		Rebuild();
	}

	public void Rebuild()
	{
		foreach (var child in GetChildren())
			child.Free();
		_rng.Seed = (ulong)CitySeed;
		_noise.Seed = CitySeed;
		_noise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
		_noise.Frequency = 0.004f;
		_noise.FractalOctaves = 3;
		BuildTerrain();
		BuildWall();
		BuildPlaza();
		for (var i = 0; i < Bays.Length; i++)
			BuildBay(Bays[i], i + 1);
		BuildAvenues();
		BuildLots();
		BuildOutskirts();
	}

	/// <summary>Ground height: flat under the city, dunes beyond, a high rim at the edge.</summary>
	public float HeightAt(float x, float z)
	{
		var r = new Vector2(x, z).Length();
		var t = Mathf.SmoothStep(FlatRadius, FlatRadius + 320.0f, r);
		if (t <= 0.0f)
			return 0.0f;
		var dunes = (_noise.GetNoise2D(x, z) * 0.5f + 0.5f) * 30.0f;
		var ridges = (1.0f - Mathf.Abs(_noise.GetNoise2D(x * 0.5f + 900.0f, z * 0.5f))) * 16.0f;
		return t * (dunes + ridges) + Mathf.SmoothStep(1350.0f, 1750.0f, r) * 170.0f;
	}

	// --- terrain, wall and districts ---------------------------------------------

	private void BuildTerrain()
	{
		const int n = TerrainCells;
		const float cell = TerrainSize / n;
		const float origin = -TerrainSize * 0.5f;
		var pts = new Vector3[(n + 1) * (n + 1)];
		var normals = new Vector3[pts.Length];
		var colors = new Color[pts.Length];
		for (var j = 0; j <= n; j++)
		{
			for (var i = 0; i <= n; i++)
			{
				var k = j * (n + 1) + i;
				var x = origin + i * cell;
				var z = origin + j * cell;
				pts[k] = new Vector3(x, HeightAt(x, z), z);
				var nrm = new Vector3(HeightAt(x - cell, z) - HeightAt(x + cell, z), 2.0f * cell,
					HeightAt(x, z - cell) - HeightAt(x, z + cell)).Normalized();
				normals[k] = nrm;
				var shade = 0.86f + 0.24f * (_noise.GetNoise2D(x * 3.0f + 300.0f, z * 3.0f) * 0.5f + 0.5f);
				var tint = new Color(shade, shade * 0.97f, shade * 0.93f);
				colors[k] = tint.Lerp(new Color(0.82f, 0.62f, 0.5f), Mathf.Clamp((0.95f - nrm.Y) * 4.0f, 0.0f, 1.0f));
			}
		}
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		for (var j = 0; j < n; j++)
		{
			for (var i = 0; i < n; i++)
			{
				var a = j * (n + 1) + i;
				int[][] tris = [[a, a + 1, a + n + 2], [a, a + n + 2, a + n + 1]];
				foreach (var tri in tris)
				{
					Kit.AddPoly(st, tri.Select(k => pts[k]).ToArray(), Vector3.Up,
						tri.Select(k => normals[k]).ToArray(), tri.Select(k => colors[k]).ToArray());
				}
			}
		}
		var mesh = st.Commit();
		var body = new StaticBody3D { Name = "Terrain" };
		body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
		body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = Kit.Mat("terrain") });
		AddChild(body);
	}

	private void BuildWall()
	{
		var root = Group(this, Vector3.Zero);
		root.Name = "Wall";
		const float h = 9.0f;
		const float gate = 14.0f;
		const float run = Half - gate * 0.5f;
		for (var side = 0; side < 4; side++)
		{
			// Built as the south wall (z = +Half), then turned to each side.
			var g = Group(root, Vector3.Zero, side * Mathf.Pi * 0.5f);
			foreach (var s in Signs)
			{
				var mid = s * (gate * 0.5f + run * 0.5f);
				Box(g, new Vector3(run, h, 3.0f), new Vector3(mid, 0, Half), "sandcrete");
				Box(g, new Vector3(run, 0.6f, 3.6f), new Vector3(mid, h, Half), "dark", false);
				Box(g, new Vector3(run, 0.8f, 3.4f), new Vector3(mid, 0, Half), "dark", false);
				// Buttresses on the inner face every 24 m.
				for (var x = gate * 0.5f + 18.0f; x < Half - 10.0f; x += 24.0f)
					Box(g, new Vector3(2.0f, h - 1.0f, 1.4f), new Vector3(s * x, 0, Half - 2.2f), "sandcrete");
				// Gate towers with floodlights over the road inside and out.
				var tx = s * (gate * 0.5f + 3.0f);
				Box(g, new Vector3(6, 13, 8), new Vector3(tx, 0, Half), "sandcrete");
				Box(g, new Vector3(6.6f, 0.8f, 8.6f), new Vector3(tx, 13, Half), "dark", false);
				Box(g, new Vector3(0.3f, 7.0f, 0.05f), new Vector3(s * (gate * 0.5f + 0.4f), 1.0f, Half - 4.02f), "hazard", false, 0.01f);
				foreach (var face in Signs)
				{
					var lamp = new Vector3(s * (gate * 0.5f + 1.2f), 11.0f, Half + face * 4.4f);
					Box(g, new Vector3(1.2f, 0.6f, 0.8f), lamp + new Vector3(0, 0.1f, 0), "dark", false);
					Glow(g, new Vector3(0.9f, 0.05f, 0.5f), lamp + new Vector3(0, 0.05f, 0), Sodium, 5.0f);
					Spot(g, lamp, new Vector3(-s * 0.35f, -1.0f, face * 0.7f), Sodium, 5.0f, 26.0f, 32.0f, true, 2.0f, 90.0f);
				}
				// Corner tower.
				if (s > 0.0f)
				{
					Box(g, new Vector3(12, 15, 12), new Vector3(Half, 0, Half), "sandcrete");
					Box(g, new Vector3(12.8f, 0.8f, 12.8f), new Vector3(Half, 15, Half), "dark", false);
					Box(g, new Vector3(0.2f, 6, 0.2f), new Vector3(Half, 15.8f, Half), "dark", false);
					Glow(g, new Vector3(0.4f, 0.4f, 0.4f), new Vector3(Half, 21.8f, Half), Red, 6.0f);
				}
			}
			// Heavy lintel over the gate, a sign and a gate screen inside.
			Box(g, new Vector3(gate + 2.0f, 2.4f, 5.0f), new Vector3(0, 10.0f, Half), "dark");
			Box(g, new Vector3(gate, 0.5f, 0.06f), new Vector3(0, 10.4f, Half - 2.53f), "hazard", false, 0.01f);
			Box(g, new Vector3(gate, 0.5f, 0.06f), new Vector3(0, 10.4f, Half + 2.53f), "hazard", false, 0.01f);
			Sign(g, new Vector3(0, 9.6f, Half - 2.6f), new Vector2(6.0f, 1.0f), Yellow, Mathf.Pi, 9);
			Screen(g, new Vector3(-(gate * 0.5f + 3.0f), 2.0f, Half - 4.1f), Mathf.Pi, "gate");
			Console(g, new Vector3(gate * 0.5f + 2.4f, 0, Half - 6.0f), Mathf.Pi, "gate");
		}
	}

	private void BuildPlaza()
	{
		var g = Group(this, Vector3.Zero);
		g.Name = "Plaza";
		Box(g, new Vector3(88, 0.1f, 88), Vector3.Zero, "floor", true, 0.04f);
		foreach (var s in Signs)
		{
			Box(g, new Vector3(88, 0.03f, 0.5f), new Vector3(0, 0.1f, s * 43.5f), "hazard", false, 0.01f);
			Box(g, new Vector3(0.5f, 0.03f, 88), new Vector3(s * 43.5f, 0.1f, 0), "hazard", false, 0.01f);
		}

		// Beacon mast in the middle, with a ring of outward lights round its glow.
		Box(g, new Vector3(6, 0.8f, 6), new Vector3(0, 0.1f, 0), "dark");
		Box(g, new Vector3(6.2f, 0.2f, 6.2f), new Vector3(0, 0.1f, 0), "hazard", false, 0.02f);
		Box(g, new Vector3(1.6f, 20, 1.6f), new Vector3(0, 0.9f, 0), "metal");
		Box(g, new Vector3(3.0f, 1.4f, 3.0f), new Vector3(0, 20.5f, 0), "dark", false);
		Glow(g, new Vector3(1.4f, 1.2f, 1.4f), new Vector3(0, 21.9f, 0), Beacon, 6.0f);
		Box(g, new Vector3(2.0f, 0.4f, 2.0f), new Vector3(0, 23.1f, 0), "dark", false);
		for (var k = 0; k < 4; k++)
		{
			var a = Mathf.Tau * k / 4.0f + Mathf.Pi * 0.25f;
			Spot(g, new Vector3(Mathf.Cos(a), 22.5f, Mathf.Sin(a)), new Vector3(Mathf.Cos(a), -0.35f, Mathf.Sin(a)), Beacon, 1.2f, 30.0f, 70.0f, true, 0.6f, 140.0f);
		}
		for (var k = 0; k < 3; k++)
		{
			var a = Mathf.Tau * k / 3.0f + 0.5f;
			var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			Kiosk(g, dir * 8.0f, Mathf.Atan2(dir.X, dir.Z), k != 1 ? "plaza" : "market");
		}

		// Cantina in the north-west corner, its door facing the plaza.
		BuildEnterable(g, new Vector3(-29, 0, -30), 26, 20, 6, 0.0f, "cantina");

		// Market in the north-east corner.
		for (var k = 0; k < 4; k++)
			Stall(g, new Vector3(18 + k * 6.0f, 0.1f, -30), 0.0f, Cloth[k % Cloth.Length]);
		SkipDraws(1); // A townsperson stood here.
		StreetLamp(g, new Vector3(27, 0.1f, -20), new Vector3(0, 0, -1));

		// Cargo yard in the south-east corner.
		for (var k = 0; k < 6; k++)
		{
			var p = new Vector3(20 + (k % 3) * 6.0f, 0.1f, 22 + (k / 3) * 8.0f);
			CrateStack(g, p, _rng.RandiRange(1, 3));
		}
		Vaporator(g, new Vector3(38, 0.1f, 20));
		Vaporator(g, new Vector3(38, 0.1f, 30));
		SkipDraws(1); // A townsperson stood here.
		Skiff(g, new Vector3(14, 0.1f, 36), 0.3f);

		// Water dispenser and benches in the south-west corner.
		Box(g, new Vector3(3, 2.4f, 1.6f), new Vector3(-26, 0.1f, 30), "metal");
		Box(g, new Vector3(3.3f, 0.4f, 1.9f), new Vector3(-26, 2.5f, 30), "dark", false);
		Glow(g, new Vector3(0.8f, 0.12f, 0.06f), new Vector3(-26, 1.6f, 30.82f), Blue, 3.0f);
		Screen(g, new Vector3(-24.0f, 1.7f, 30.9f), 0.0f, "water", new Vector2(0.8f, 0.6f));
		for (var k = 0; k < 3; k++)
			Box(g, new Vector3(3, 0.5f, 0.8f), new Vector3(-34 + k * 5.0f, 0.1f, 22), "olive");
		Skiff(g, new Vector3(-14, 0.1f, 34), -0.2f);

		Vector3[] lamps = [new(-38, 0.1f, 38), new(38, 0.1f, 38), new(-10, 0.1f, -12), new(10, 0.1f, 12)];
		foreach (var p in lamps)
			StreetLamp(g, p, new Vector3(-Mathf.Sign(p.X), 0, 0));
	}

	private void BuildBay(Vector2 c, int number)
	{
		// The bay opens towards the north-south avenue at x = 0.
		var g = Group(this, new Vector3(c.X, 0, c.Y), Mathf.Atan2(-Mathf.Sign(c.X), 0.0f));
		g.Name = $"DockingBay{number}";
		const float size = 44.0f;
		const float h = 8.0f;
		const float t = 1.6f;
		const float gate = 12.0f;
		const float e = size * 0.5f;
		Box(g, new Vector3(size - 0.2f, 0.14f, size - 0.2f), Vector3.Zero, "floor", true, 0.03f);
		Box(g, new Vector3(size, h, t), new Vector3(0, 0, -e + t * 0.5f), "sandcrete");
		foreach (var s in Signs)
		{
			Box(g, new Vector3(t, h, size - 2 * t), new Vector3(s * (e - t * 0.5f), 0, 0), "sandcrete");
			const float pw = (size - gate) * 0.5f;
			Box(g, new Vector3(pw, h, t), new Vector3(s * (gate * 0.5f + pw * 0.5f), 0, e - t * 0.5f), "sandcrete");
			// Heavy gate posts with hazard stripes.
			Box(g, new Vector3(2.0f, h + 2.0f, t + 1.0f), new Vector3(s * (gate * 0.5f + 1.0f), 0, e - t * 0.5f), "dark");
			Box(g, new Vector3(0.6f, h, 0.05f), new Vector3(s * (gate * 0.5f + 1.0f), 0.6f, e + 0.53f), "hazard", false, 0.01f);
		}
		Box(g, new Vector3(size + 0.6f, 0.6f, size + 0.6f), new Vector3(0, h, 0), "dark", false);
		Box(g, new Vector3(size - 2 * t, h, 0.06f), new Vector3(0, 0, -e + t + 0.03f), "olive", false, 0.01f);
		// Pilasters round the outside.
		for (var k = -1; k <= 1; k++)
		{
			Box(g, new Vector3(2.0f, h + 0.6f, 1.2f), new Vector3(k * 14.0f, 0, -e - 0.6f), "sandcrete");
			Box(g, new Vector3(1.2f, h + 0.6f, 2.0f), new Vector3(-e - 0.6f, 0, k * 14.0f), "sandcrete");
			Box(g, new Vector3(1.2f, h + 0.6f, 2.0f), new Vector3(e + 0.6f, 0, k * 14.0f), "sandcrete");
		}
		Sign(g, new Vector3(0, h + 1.2f, e + 0.2f), new Vector2(4.0f, 1.0f), Yellow, 0.0f, 5);

		// Landing ring, hazard border, the ship, and floodlights from the corners.
		Lathe(g, [new(11.0f, 0.14f), new(12.0f, 0.14f), new(12.0f, 0.17f), new(11.0f, 0.17f)], 24, Vector3.Zero, "hazard", 0.0f, null, false);
		foreach (var s in Signs)
			Box(g, new Vector3(size - 2 * t - 1.0f, 0.03f, 0.6f), new Vector3(0, 0.14f, s * (e - t - 0.6f)), "hazard", false, 0.01f);
		Ship(g, new Vector3(0, 0.14f, -1.0f), _rng.RandfRange(-0.25f, 0.25f));
		foreach (var sx in Signs)
		{
			foreach (var sz in Signs)
			{
				var p = new Vector3(sx * (e - 2.5f), h + 0.4f, sz * (e - 2.5f));
				Box(g, new Vector3(1.4f, 0.8f, 1.4f), p, "dark", false);
				Glow(g, new Vector3(1.0f, 0.05f, 1.0f), p - new Vector3(0, 0.03f, 0), Cool, 5.0f);
				Spot(g, p, new Vector3(-sx, -1.2f, -sz), Cool, 9.0f, 45.0f, 26.0f, true, 2.5f, 120.0f);
			}
		}

		// Fuel tanks, crates, consoles and crew.
		foreach (var s in Signs)
		{
			var tank = new Vector3(s * (e - 4.5f), 0.14f, -e + 4.5f);
			Lathe(g, [new(1.6f, 0), new(1.6f, 4.2f), new(1.1f, 4.8f), new(0, 5.0f)], 12, tank, "metal", 1.6f);
			Lathe(g, [new(1.62f, 1.6f), new(1.62f, 2.2f)], 12, tank, "hazard", 0.0f, null, false);
			CrateStack(g, new Vector3(s * (e - 4.0f), 0.14f, 6.0f), 3);
			CrateStack(g, new Vector3(s * (e - 4.0f), 0.14f, 9.0f), 2);
		}
		Console(g, new Vector3(gate * 0.5f + 4.0f, 0.14f, e - t - 1.4f), Mathf.Pi, "bay");
		SkipDraws(1); // A townsperson stood here.
	}

	private void BuildAvenues()
	{
		var g = Group(this, Vector3.Zero);
		g.Name = "Avenues";
		for (var axis = 0; axis < 2; axis++)
		{
			foreach (var s in Signs)
			{
				var side = 1.0f;
				for (var d = 56.0f; d < Half - 8.0f; d += 24.0f)
				{
					var pos = axis == 0 ? new Vector3(7.0f * side, 0, s * d) : new Vector3(s * d, 0, 7.0f * side);
					var arm = axis == 0 ? new Vector3(-side, 0, 0) : new Vector3(0, 0, -side);
					StreetLamp(g, pos, arm);
					side = -side;
				}
			}
		}
	}

	private void BuildLots()
	{
		var root = Group(this, Vector3.Zero);
		root.Name = "Lots";
		var coords = new List<float>();
		for (var c = 18.0f; c < Half - 20.0f; c += LotSize)
		{
			coords.Add(c);
			coords.Add(-c);
		}
		var lots = new List<Vector2>();
		foreach (var x in coords)
		{
			foreach (var z in coords)
			{
				var p = new Vector2(x, z);
				if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < 52.0f)
					continue;
				if (Bays.All(b => p.DistanceTo(b) >= 36.0f))
					lots.Add(p);
			}
		}

		// A few buildings along the avenues can be entered.
		var enterable = new Dictionary<Vector2, string>();
		var near = lots.Where(p => Mathf.Min(Mathf.Abs(p.X), Mathf.Abs(p.Y)) < 20.0f && p.Length() < 160.0f).ToList();
		string[] kinds = ["control", "workshop", "hab", "storage", "hab", "workshop"];
		var picks = Shuffled(near);
		for (var i = 0; i < Mathf.Min(kinds.Length, picks.Count); i++)
			enterable[picks[i]] = kinds[i];

		foreach (var p in lots)
		{
			// Face the nearest avenue.
			var dir = Mathf.Abs(p.X) < Mathf.Abs(p.Y) ? new Vector3(-Mathf.Sign(p.X), 0, 0) : new Vector3(0, 0, -Mathf.Sign(p.Y));
			var g = Group(root, new Vector3(p.X, 0, p.Y), Mathf.Atan2(dir.X, dir.Z));
			if (enterable.TryGetValue(p, out var kind))
			{
				BuildEnterable(g, Vector3.Zero, 12, 12, kind == "hab" ? 4.0f : 6.0f, 0.0f, kind);
				continue;
			}
			var roll = _rng.Randf();
			if (roll < 0.32f)
				BlockHab(g);
			else if (roll < 0.6f)
				DomeLot(g);
			else if (roll < 0.7f)
				Tower(g);
			else if (roll < 0.84f)
				Yard(g);
			else if (roll < 0.92f)
				Market(g);
			else
				ScrapLot(g);
		}
	}

	private void BuildOutskirts()
	{
		var g = Group(this, Vector3.Zero);
		g.Name = "Outskirts";
		Vector2[] farms = [new(-330, 60), new(300, -270), new(90, 340), new(-280, -330)];
		foreach (var farm in farms)
		{
			for (var i = 0; i < 3; i++)
			{
				for (var j = 0; j < 3; j++)
				{
					var x = farm.X + i * 14.0f + _rng.RandfRange(-2, 2);
					var z = farm.Y + j * 14.0f + _rng.RandfRange(-2, 2);
					Vaporator(g, new Vector3(x, HeightAt(x, z) - 0.2f, z));
				}
			}
		}
		Crawler(g, new Vector3(170, HeightAt(170, 430) - 0.5f, 430), -0.5f);
		var wreck = Group(g, new Vector3(-470, HeightAt(-470, -190) - 2.5f, -190), 0.9f);
		wreck.Rotation = new Vector3(0.18f, 0.9f, 0.35f);
		Ship(wreck, Vector3.Zero, 0.0f);
		for (var i = 0; i < 16; i++)
		{
			var a = Mathf.Tau * i / 16.0f + _rng.RandfRange(-0.12f, 0.12f);
			var r = _rng.RandfRange(850.0f, 1450.0f);
			var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
			p.Y = HeightAt(p.X, p.Z) - 8.0f;
			var rad = _rng.RandfRange(60.0f, 150.0f);
			var h = _rng.RandfRange(50.0f, 120.0f);
			Vector2[] prof = [
				new(rad, -10), new(rad * 0.97f, h * 0.35f), new(rad * 0.9f, h * 0.42f),
				new(rad * 0.86f, h * 0.85f), new(rad * 0.76f, h), new(0, h),
			];
			var m = Group(g, p, _rng.Randf() * Mathf.Tau);
			Lathe(m, prof, 7, Vector3.Zero, "rock", rad);
		}
	}

	// --- buildings -----------------------------------------------------------------

	private void BlockHab(Node3D g)
	{
		var w = _rng.Randi() % 2 == 0 ? 8.0f : 12.0f;
		var d = _rng.Randi() % 2 == 0 ? 8.0f : 12.0f;
		var h = 4.0f + 2.0f * _rng.RandiRange(0, 3);
		Box(g, new Vector3(w + 0.4f, 0.6f, d + 0.4f), Vector3.Zero, "dark", false);
		Box(g, new Vector3(w, h, d), Vector3.Zero, "sandcrete");
		Box(g, new Vector3(w + 0.3f, 0.5f, d + 0.3f), new Vector3(0, h, 0), "dark", false);
		var doorX = _rng.RandfRange(-w * 0.25f, w * 0.25f);
		Door(g, new Vector3(doorX, 0, d * 0.5f), _rng.Randf() < 0.35f);
		var lights = _rng.RandiRange(0, 2);
		for (var i = 0; i < lights; i++)
		{
			var s = _rng.Randf() < 0.5f ? 1.0f : -1.0f;
			var color = _rng.Randf() < 0.7f ? Amber : Cyan;
			Glow(g, new Vector3(0.06f, 0.3f, 1.6f), new Vector3(s * (w * 0.5f + 0.02f), h - 1.8f, _rng.RandfRange(-d * 0.3f, d * 0.3f)), color, 1.6f);
		}
		var top = h + 0.5f;
		if (_rng.Randf() < 0.4f && h <= 8.0f)
		{
			var ox = _rng.RandfRange(-w * 0.2f, w * 0.2f);
			Box(g, new Vector3(w * 0.5f, 3.0f, d * 0.5f), new Vector3(ox, h + 0.5f, 0), "sandcrete");
			Box(g, new Vector3(w * 0.5f + 0.3f, 0.4f, d * 0.5f + 0.3f), new Vector3(ox, h + 3.5f, 0), "dark", false);
		}
		RoofClutter(g, w, d, top);
		if (_rng.Randf() < 0.3f)
			Sign(g, new Vector3(doorX + (doorX < 0 ? 2.2f : -2.2f), 3.8f, d * 0.5f + 0.1f), new Vector2(2.4f, 0.7f), Yellow, 0.0f, 0);
	}

	private void DomeLot(Node3D g)
	{
		(Vector3 Pos, float Radius)[][] layouts =
		[
			[(new Vector3(0, 0, -0.5f), 5.0f)],
			[(new Vector3(-3.0f, 0, 1.5f), 3.5f), (new Vector3(3.5f, 0, -3.0f), 3.0f)],
			[(new Vector3(-3.5f, 0, 2.5f), 3.0f), (new Vector3(3.5f, 0, 2.0f), 3.0f), (new Vector3(0, 0, -3.5f), 3.5f)],
		];
		foreach (var (pos, radius) in layouts[_rng.Randi() % layouts.Length])
			DomeHut(g, pos, radius);
	}

	private void DomeHut(Node3D parent, Vector3 pos, float r)
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

	private void Tower(Node3D g)
	{
		var w = _rng.Randi() % 2 == 0 ? 8.0f : 12.0f;
		var h = 14.0f + 2.0f * _rng.RandiRange(0, 4);
		Box(g, new Vector3(w + 0.4f, 0.6f, w + 0.4f), Vector3.Zero, "dark", false);
		Box(g, new Vector3(w, h, w), Vector3.Zero, "olive");
		for (var y = 4.0f; y < h - 1.0f; y += 4.0f)
			Box(g, new Vector3(w + 0.24f, 0.4f, w + 0.24f), new Vector3(0, y, 0), "dark", false);
		Box(g, new Vector3(w * 0.6f, 4.0f, w * 0.6f), new Vector3(0, h, 0), "metal");
		Box(g, new Vector3(w * 0.6f + 0.4f, 0.5f, w * 0.6f + 0.4f), new Vector3(0, h + 4.0f, 0), "dark", false);
		Glow(g, new Vector3(w * 0.4f, 0.4f, 0.06f), new Vector3(0, h + 2.4f, w * 0.3f + 0.02f), Cyan, 1.6f);
		Box(g, new Vector3(0.24f, 7.0f, 0.24f), new Vector3(1.0f, h + 4.5f, 1.0f), "dark", false);
		Glow(g, new Vector3(0.36f, 0.36f, 0.36f), new Vector3(1.0f, h + 11.5f, 1.0f), Red, 6.0f);
		var dish = Group(g, new Vector3(-w * 0.15f, h + 4.5f, -w * 0.15f), 0.6f);
		dish.Rotation = dish.Rotation with { X = 0.6f };
		Lathe(dish, [new(0.3f, 0), new(2.0f, 0.8f), new(2.0f, 1.0f), new(0, 0.5f)], 12,
			Vector3.Zero, "metal", 0.0f, null, true, true);
		Door(g, new Vector3(0, 0, w * 0.5f), true);
		// Floodlight over the street in front.
		var lamp = new Vector3(0, h - 2.0f, w * 0.5f + 0.6f);
		Box(g, new Vector3(1.4f, 0.7f, 1.2f), lamp, "dark", false);
		Glow(g, new Vector3(1.0f, 0.05f, 0.8f), lamp - new Vector3(0, 0.03f, 0), Cool, 5.0f);
		Spot(g, lamp, new Vector3(0, -1.0f, 0.9f), Cool, 5.0f, 40.0f, 30.0f, true, 2.0f, 110.0f);
	}

	private void Yard(Node3D g)
	{
		Box(g, new Vector3(14, 1.8f, 0.6f), new Vector3(0, 0, -6.7f), "sandcrete");
		foreach (var s in Signs)
		{
			Box(g, new Vector3(0.6f, 1.8f, 12.8f), new Vector3(s * 6.7f, 0, 0), "sandcrete");
			Box(g, new Vector3(0.8f, 0.2f, 13.0f), new Vector3(s * 6.7f, 1.8f, 0), "dark", false);
		}
		var stacks = _rng.RandiRange(2, 4);
		for (var i = 0; i < stacks; i++)
			CrateStack(g, new Vector3(_rng.RandfRange(-4.5f, 4.5f), 0, _rng.RandfRange(-4.5f, 2.0f)), _rng.RandiRange(1, 3));
		var barrels = _rng.RandiRange(0, 4);
		for (var i = 0; i < barrels; i++)
			Barrel(g, new Vector3(_rng.RandfRange(-5, 5), 0, _rng.RandfRange(-5, 5)), _rng.Randf() < 0.5f ? Rust : new Color(0.5f, 0.56f, 0.42f));
		if (_rng.Randf() < 0.5f)
			Vaporator(g, new Vector3(_rng.RandfRange(-4, 4), 0, -4.5f));
		if (_rng.Randf() < 0.35f)
			SkipDraws(4); // A townsperson stood here.
		if (_rng.Randf() < 0.5f)
			StreetLamp(g, new Vector3(-5.5f, 0, 5.5f), new Vector3(1, 0, 0));
	}

	private void Market(Node3D g)
	{
		var stalls = _rng.RandiRange(2, 3);
		for (var i = 0; i < stalls; i++)
			Stall(g, new Vector3(-4.5f + i * 4.5f, 0, -2.0f), 0.0f, Cloth[_rng.Randi() % Cloth.Length]);
		// Shoppers and a stallholder stood here.
		SkipDraws(3 * _rng.RandiRange(1, 3));
		if (_rng.Randf() < 0.5f)
			SkipDraws(2);
	}

	private void ScrapLot(Node3D g)
	{
		var pieces = _rng.RandiRange(3, 6);
		for (var i = 0; i < pieces; i++)
		{
			var size = new Vector3(_rng.RandfRange(1.0f, 4.0f), _rng.RandfRange(0.6f, 2.4f), _rng.RandfRange(1.0f, 3.0f));
			var rot = new Vector3(_rng.RandfRange(-0.3f, 0.3f), _rng.Randf() * Mathf.Tau, _rng.RandfRange(-0.3f, 0.3f));
			Box(g, size, new Vector3(_rng.RandfRange(-5, 5), -0.3f, _rng.RandfRange(-5, 5)), i % 2 == 1 ? "sandcrete" : "dark", true, 0.15f, null, rot);
		}
		Skiff(g, new Vector3(_rng.RandfRange(-2, 2), -0.3f, _rng.RandfRange(-2, 2)), _rng.Randf() * Mathf.Tau, true);
		if (_rng.Randf() < 0.6f)
			Vaporator(g, new Vector3(4.5f, 0, -4.5f));
	}

	/// <summary>A hollow building with a doorway in its +Z wall, lit inside, furnished by kind.</summary>
	private Node3D BuildEnterable(Node3D parent, Vector3 pos, float w, float d, float h, float rotY, string kind)
	{
		var g = Group(parent, pos, rotY);
		g.Name = kind.Capitalize();
		const float t = 0.6f;
		var big = kind == "cantina";
		var door = big ? 3.4f : 2.4f;
		var doorH = big ? 3.2f : 2.8f;
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
		switch (kind)
		{
			case "cantina": FillCantina(g, iw, id, h); break;
			case "control": FillControl(g, iw, id, h); break;
			case "workshop": FillWorkshop(g, iw, id, h); break;
			case "hab": FillHab(g, iw, id, h); break;
			case "storage": FillStorage(g, iw, id, h); break;
		}
		return g;
	}

	private void FillCantina(Node3D g, float iw, float id, float h)
	{
		const float f = 0.14f;
		var back = -id * 0.5f;
		// Bar island with a glowing front strip.
		Box(g, new Vector3(10, 1.1f, 2.6f), new Vector3(0, f, -2), "dark");
		Box(g, new Vector3(10.4f, 0.12f, 3.0f), new Vector3(0, f + 1.1f, -2), "metal", false, 0.04f);
		Glow(g, new Vector3(9.6f, 0.1f, 0.05f), new Vector3(0, f + 0.85f, -0.68f), Cyan, 2.5f);
		Spot(g, new Vector3(0, f + 0.9f, -0.4f), new Vector3(0, -0.3f, 1), Cyan, 0.6f, 4.0f, 70.0f, true, 0.3f, 35.0f);
		SkipDraws(1); // The barkeep stood here.
		SkipDraws(5); // Drinkers stood along the bar.
		// Back shelves with bottles, and two screens above.
		Color[] bottles = [Green, Amber, Cyan, Amber];
		foreach (var y in (float[])[1.2f, 1.9f])
		{
			Box(g, new Vector3(8, 0.1f, 0.6f), new Vector3(0, f + y, back + 0.3f), "dark", false, 0.03f);
			for (var k = 0; k < 14; k++)
			{
				var c = bottles[_rng.Randi() % 4];
				Glow(g, new Vector3(0.14f, 0.34f, 0.14f), new Vector3(-3.6f + k * 0.55f, f + y + 0.1f, back + 0.3f), c, 1.2f);
			}
		}
		foreach (var s in Signs)
			Screen(g, new Vector3(s * 3.4f, 3.3f, back + 0.12f), 0.0f, s < 0 ? "cantina" : "tab", new Vector2(1.6f, 1.2f));
		// Booths along both side walls.
		foreach (var s in Signs)
		{
			foreach (var z in (float[])[-5.0f, 0.5f, 6.0f])
			{
				Box(g, new Vector3(0.8f, 0.5f, 2.4f), new Vector3(s * (iw * 0.5f - 0.5f), f, z), "olive");
				Box(g, new Vector3(0.3f, 1.5f, 2.4f), new Vector3(s * (iw * 0.5f - 0.15f), f, z), "olive");
				Box(g, new Vector3(1.0f, 0.85f, 1.4f), new Vector3(s * (iw * 0.5f - 1.6f), f, z), "dark");
			}
			CeilingLamp(g, new Vector3(s * (iw * 0.5f - 1.6f), h, 3.2f), f, Amber);
		}
		// Round tables in the middle.
		foreach (var p in (Vector3[])[new(-5, f, 4.5f), new(5, f, 5.5f), new(0, f, 6.5f)])
			Lathe(g, [new(0.4f, 0), new(0.15f, 0.1f), new(0.15f, 0.8f), new(0.75f, 0.85f), new(0.75f, 0.95f), new(0, 0.95f)], 10, p, "metal", 0.75f);
		// Band stage in the back corner.
		Box(g, new Vector3(5, 0.4f, 3.5f), new Vector3(-iw * 0.5f + 3.2f, f, back + 2.4f), "metal");
		Box(g, new Vector3(5, 0.05f, 0.3f), new Vector3(-iw * 0.5f + 3.2f, f + 0.1f, back + 4.2f), "hazard", false, 0.01f);
		SkipDraws(3); // The band stood on the stage.
		// Lamps over the bar, the stage and the booths.
		foreach (var x in (float[])[-3.0f, 3.0f])
			CeilingLamp(g, new Vector3(x, h, -2), f, Warm);
		CeilingLamp(g, new Vector3(-iw * 0.5f + 3.2f, h, back + 2.4f), f + 0.4f, new Color(1.0f, 0.5f, 0.3f));
		// Sign over the door and a faceted dome on the roof.
		Sign(g, new Vector3(0, 5.0f, id * 0.5f + 0.75f), new Vector2(5.0f, 1.0f), Yellow, 0.0f, 7);
		DomeRoof(g, new Vector3(0, h + 0.6f, -2), 7.0f);
	}

	private void FillControl(Node3D g, float iw, float id, float h)
	{
		const float f = 0.14f;
		string[] keys = ["traffic", "power", "water"];
		for (var k = 0; k < 3; k++)
			Console(g, new Vector3(-3.2f + k * 3.2f, f, -id * 0.5f + 0.6f), 0.0f, keys[k]);
		Screen(g, new Vector3(-iw * 0.5f + 0.1f, 2.4f, 0.5f), Mathf.Pi * 0.5f, "traffic", new Vector2(2.0f, 1.5f));
		CeilingLamp(g, new Vector3(0, h, -0.5f), f, Cool);
	}

	private void FillWorkshop(Node3D g, float iw, float id, float h)
	{
		const float f = 0.14f;
		// Service pad under a hoist.
		Box(g, new Vector3(3.2f, 0.03f, 3.2f), new Vector3(1.5f, f, -1.5f), "hazard", false, 0.01f);
		Box(g, new Vector3(0.3f, 0.3f, iw), new Vector3(1.5f, h - 0.4f, 0), "dark", false, 0.05f, null, new Vector3(0, Mathf.Pi * 0.5f, 0));
		Box(g, new Vector3(0.06f, h - 2.6f, 0.06f), new Vector3(1.5f, 2.2f, -1.5f), "dark", false, 0.01f);
		Box(g, new Vector3(0.4f, 0.3f, 0.2f), new Vector3(1.5f, 1.9f, -1.5f), "paint", false, 0.04f, Orange);
		// Workbench with orange tools against the left wall.
		Box(g, new Vector3(1.2f, 0.95f, 3.4f), new Vector3(-iw * 0.5f + 0.7f, f, 0), "metal");
		for (var k = 0; k < 5; k++)
		{
			var size = new Vector3(_rng.RandfRange(0.2f, 0.5f), 0.12f, _rng.RandfRange(0.1f, 0.3f));
			Box(g, size, new Vector3(-iw * 0.5f + 0.7f, f + 0.95f, -1.3f + k * 0.6f), "paint", false, 0.03f, Orange);
		}
		Screen(g, new Vector3(-iw * 0.5f + 0.1f, 2.0f, 0), Mathf.Pi * 0.5f, "workshop");
		CrateStack(g, new Vector3(iw * 0.5f - 1.0f, f, 3.0f), 2);
		CrateStack(g, new Vector3(iw * 0.5f - 1.0f, f, 1.6f), 3);
		CeilingLamp(g, new Vector3(1.5f, h, -1.5f), f, Warm);
		CeilingLamp(g, new Vector3(-iw * 0.5f + 1.4f, h, 2.0f), f, Amber, false);
	}

	private void FillHab(Node3D g, float iw, float id, float h)
	{
		const float f = 0.14f;
		var back = -id * 0.5f;
		foreach (var y in (float[])[0.0f, 1.3f])
		{
			Box(g, new Vector3(2.2f, 0.3f, 1.0f), new Vector3(-iw * 0.5f + 1.2f, f + 0.3f + y, back + 0.6f), "dark");
			Box(g, new Vector3(2.0f, 0.2f, 0.9f), new Vector3(-iw * 0.5f + 1.2f, f + 0.6f + y, back + 0.6f), "paint", false, 0.06f, Cloth[0]);
		}
		Box(g, new Vector3(1.0f, 2.2f, 0.6f), new Vector3(iw * 0.5f - 0.7f, f, back + 0.4f), "metal");
		Box(g, new Vector3(1.6f, 0.8f, 1.0f), new Vector3(0.5f, f, 0.5f), "dark");
		foreach (var s in Signs)
			Box(g, new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f + s * 1.2f, f, 0.5f), "olive");
		Screen(g, new Vector3(iw * 0.5f - 0.1f, 1.9f, 0.5f), -Mathf.Pi * 0.5f, "hab");
		CeilingLamp(g, new Vector3(0.5f, h, 0.5f), f, Amber);
	}

	private void FillStorage(Node3D g, float iw, float id, float h)
	{
		const float f = 0.14f;
		Vector2[] posts = [new(-0.75f, -1.15f), new(0.75f, -1.15f), new(-0.75f, 1.15f), new(0.75f, 1.15f)];
		foreach (var s in Signs)
		{
			var x = s * 2.6f;
			foreach (var z in (float[])[-4.0f, -1.5f, 1.0f])
			{
				foreach (var y in (float[])[0.0f, 1.5f, 3.0f])
					Box(g, new Vector3(1.6f, 0.1f, 2.4f), new Vector3(x, f + y + 1.2f, z), "dark", false, 0.02f);
				foreach (var corner in posts)
					Box(g, new Vector3(0.1f, 4.4f, 0.1f), new Vector3(x + corner.X, f, z + corner.Y), "dark", false, 0.02f);
				Crate(g, new Vector3(x, f + 1.3f, z - 0.6f), 0.0f);
				if (_rng.Randf() < 0.7f)
					Crate(g, new Vector3(x, f + 2.8f, z + 0.5f), Mathf.Pi * 0.5f);
				if (_rng.Randf() < 0.6f)
					Crate(g, new Vector3(x, f, z), 0.0f);
			}
		}
		Box(g, new Vector3(0.3f, 0.03f, id - 1.0f), new Vector3(-1.4f, f, 0), "hazard", false, 0.01f);
		Box(g, new Vector3(0.3f, 0.03f, id - 1.0f), new Vector3(1.4f, f, 0), "hazard", false, 0.01f);
		SkipDraws(1); // A townsperson stood here.
		Screen(g, new Vector3(iw * 0.5f - 0.1f, 2.0f, 4.0f), -Mathf.Pi * 0.5f, "storage");
		CeilingLamp(g, new Vector3(0, h, -2.5f), f, Cool);
		CeilingLamp(g, new Vector3(0, h, 2.5f), f, Cool, false);
	}

	// --- props ---------------------------------------------------------------------

	/// <summary>A doorway (or closed door) set into a facade at pos, facing +Z.</summary>
	private void Door(Node3D parent, Vector3 pos, bool lamp, float width = 2.0f, float height = 2.8f, bool open = false)
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

	private void RoofClutter(Node3D g, float w, float d, float top)
	{
		var count = _rng.RandiRange(1, 3);
		for (var i = 0; i < count; i++)
		{
			var p = new Vector3(_rng.RandfRange(-w * 0.35f, w * 0.35f), top, _rng.RandfRange(-d * 0.35f, d * 0.35f));
			switch (_rng.Randi() % 3)
			{
				case 0:
					Box(g, new Vector3(1.2f, 0.9f, 1.2f), p, "dark", false);
					break;
				case 1:
					Lathe(g, [new(1.1f, 0), new(1.1f, 1.8f), new(0.7f, 2.1f), new(0, 2.2f)], 10, p, "metal", 0.0f);
					break;
				case 2:
					Box(g, new Vector3(0.14f, 3.0f, 0.14f), p, "dark", false, 0.03f);
					Glow(g, new Vector3(0.2f, 0.2f, 0.2f), p + new Vector3(0, 3.0f, 0), Red, 5.0f);
					break;
			}
		}
	}

	private void DomeRoof(Node3D g, Vector3 pos, float r)
	{
		Lathe(g, [
			new(r, 0), new(r, 0.8f), new(r * 0.92f, r * 0.38f + 0.8f), new(r * 0.7f, r * 0.7f + 0.8f),
			new(r * 0.38f, r * 0.92f + 0.8f), new(0, r + 0.8f),
		], 12, pos, "sandcrete", 0.0f);
	}

	private void Stall(Node3D parent, Vector3 pos, float rotY, Color cloth)
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

	private void StreetLamp(Node3D parent, Vector3 pos, Vector3 arm)
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

	private void CeilingLamp(Node3D parent, Vector3 ceiling, float floorY, Color color, bool omniShadow = true)
	{
		Box(parent, new Vector3(0.9f, 0.3f, 0.9f), ceiling - new Vector3(0, 0.36f, 0), "dark", false, 0.05f);
		Glow(parent, new Vector3(0.6f, 0.05f, 0.6f), ceiling - new Vector3(0, 0.39f, 0), color, 5.0f);
		var drop = ceiling.Y - floorY;
		Spot(parent, ceiling - new Vector3(0, 0.45f, 0), Vector3.Down, color, 5.0f, drop + 1.5f, 20.0f, true, 4.0f, 45.0f);
		Omni(parent, ceiling - new Vector3(0, 0.9f, 0), color, 0.8f, 9.0f, omniShadow, 0.0f, 45.0f);
	}

	/// <summary>A wall-mounted yellow sign with blocky dark lettering and a dim light.</summary>
	private void Sign(Node3D parent, Vector3 center, Vector2 size, Color color, float rotY, int letters)
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

	private static void Screen(Node3D parent, Vector3 center, float rotY, string key, Vector2? size = null)
	{
		parent.AddChild(new TerminalScreen
		{
			Lines = Text[key],
			ScreenSize = size ?? new Vector2(1.2f, 0.9f),
			Position = center,
			Rotation = new Vector3(0, rotY, 0),
		});
	}

	private void Console(Node3D parent, Vector3 pos, float rotY, string key)
	{
		var g = Group(parent, pos, rotY);
		Box(g, new Vector3(1.6f, 0.85f, 0.8f), Vector3.Zero, "dark");
		Box(g, new Vector3(1.7f, 0.1f, 0.9f), new Vector3(0, 0.85f, 0), "metal", false, 0.03f);
		for (var k = 0; k < 4; k++)
			Glow(g, new Vector3(0.1f, 0.03f, 0.08f), new Vector3(-0.5f + k * 0.33f, 0.95f, 0.25f), k != 2 ? Blue : Green, 4.0f);
		Box(g, new Vector3(0.12f, 0.6f, 0.12f), new Vector3(0, 0.95f, -0.25f), "dark", false, 0.03f);
		Screen(g, new Vector3(0, 1.85f, -0.25f), 0.0f, key, new Vector2(1.0f, 0.75f));
	}

	private void Kiosk(Node3D parent, Vector3 pos, float rotY, string key)
	{
		var g = Group(parent, pos, rotY);
		Box(g, new Vector3(1.0f, 1.2f, 0.6f), new Vector3(0, 0.1f, 0), "dark");
		Box(g, new Vector3(1.4f, 0.2f, 0.9f), new Vector3(0, 0.1f, 0), "metal", false, 0.04f);
		Screen(g, new Vector3(0, 1.85f, 0), 0.0f, key, new Vector2(1.0f, 0.75f));
	}

	private void Crate(Node3D parent, Vector3 pos, float rotY)
	{
		Box(parent, new Vector3(1.2f, 1.2f, 1.2f), pos, "crate", true, 0.08f, null, new Vector3(0, rotY, 0));
	}

	private void CrateStack(Node3D parent, Vector3 pos, int count)
	{
		for (var i = 0; i < count; i++)
		{
			var jitter = new Vector3(_rng.RandfRange(-0.1f, 0.1f), 0, _rng.RandfRange(-0.1f, 0.1f));
			Crate(parent, pos + jitter + new Vector3(0, i * 1.2f, 0), _rng.RandfRange(-0.15f, 0.15f) + (i % 2 == 1 ? Mathf.Pi * 0.5f : 0.0f));
		}
		if (count < 3 && _rng.Randf() < 0.5f)
			Crate(parent, pos + new Vector3(1.3f, 0, 0), _rng.RandfRange(-0.2f, 0.2f));
	}

	private void Barrel(Node3D parent, Vector3 pos, Color tint)
	{
		Lathe(parent, [
			new(0.36f, 0), new(0.4f, 0.05f), new(0.4f, 0.46f), new(0.43f, 0.48f), new(0.43f, 0.56f),
			new(0.4f, 0.58f), new(0.4f, 1.0f), new(0.34f, 1.05f), new(0, 1.05f),
		], 10, pos, "paint", 0.43f, tint);
	}

	private void Vaporator(Node3D parent, Vector3 pos)
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
	private void Skiff(Node3D parent, Vector3 pos, float rotY, bool wrecked = false)
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
	private void Ship(Node3D parent, Vector3 pos, float rotY)
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

	private void Crawler(Node3D parent, Vector3 pos, float rotY)
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

	private static Node3D Group(Node3D parent, Vector3 pos, float rotY = 0.0f)
	{
		var g = new Node3D { Position = pos, Rotation = new Vector3(0, rotY, 0) };
		parent.AddChild(g);
		return g;
	}

	/// <summary>A chamfered box standing on pos (pos is the bottom centre).</summary>
	private Node3D Box(Node3D parent, Vector3 size, Vector3 pos, string surface, bool collide = true, float bevel = 0.12f,
		Color? tint = null, Vector3? rot = null)
	{
		var mi = new MeshInstance3D { Mesh = Kit.Box(size, bevel), MaterialOverride = Kit.Mat(surface, tint) };
		return Place(parent, mi, pos, rot ?? Vector3.Zero, size.Y * 0.5f, collide ? BoxShape(size) : null,
			Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)));
	}

	private static void Glow(Node3D parent, Vector3 size, Vector3 pos, Color color, float energy = 3.0f)
	{
		var mi = new MeshInstance3D
		{
			Mesh = Kit.Box(size, Mathf.Min(0.03f, Mathf.Min(size.X, Mathf.Min(size.Y, size.Z)) * 0.3f)),
			MaterialOverride = Kit.Glow(color, energy),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		Place(parent, mi, pos, Vector3.Zero, size.Y * 0.5f, null, Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)));
	}

	private static void Lathe(Node3D parent, Vector2[] profile, int sides, Vector3 pos, string surface,
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
	private static Node3D Place(Node3D parent, MeshInstance3D mi, Vector3 pos, Vector3 rot, float lift, Shape3D? shape, float extent)
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


	private static void ColliderShape(Node3D parent, Shape3D shape, Vector3 center)
	{
		var body = new StaticBody3D { Position = center };
		body.AddChild(new CollisionShape3D { Shape = shape });
		parent.AddChild(body);
	}

	private BoxShape3D BoxShape(Vector3 size)
	{
		var key = Kit.Key(size);
		if (!_shapes.TryGetValue(key, out var shape))
		{
			shape = new BoxShape3D { Size = size };
			_shapes[key] = shape;
		}
		return (BoxShape3D)shape;
	}

	private static void Spot(Node3D parent, Vector3 pos, Vector3 dir, Color color, float energy, float range, float angle,
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

	private static void Omni(Node3D parent, Vector3 pos, Color color, float energy, float range, bool shadow, float fog, float fade)
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
	private static void Fade(Light3D l, float dist)
	{
		l.DistanceFadeEnabled = true;
		l.DistanceFadeBegin = dist;
		l.DistanceFadeLength = dist * 0.25f;
		l.DistanceFadeShadow = dist * 0.6f;
	}

	/// <summary>
	/// Uses up <paramref name="count"/> random numbers. The townspeople that used to be baked into the level drew
	/// these; drawing them still keeps the rest of the layout exactly as it was. NPCs now come from the server.
	/// </summary>
	private void SkipDraws(int count)
	{
		for (var i = 0; i < count; i++)
			_rng.Randf();
	}

	private List<T> Shuffled<T>(List<T> items)
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
