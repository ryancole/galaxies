using Godot;

namespace Galaxies;

/// <summary>
/// The ground of the whole desert planet map, shared by every level on it: flat under Kessa Outpost, dunes
/// beyond, a high rim at the edge, and the basin of Halun Springs to the south.
///
/// The map is a grid of Cells × Cells coarse cells. Kessa builds all of it except the oasis cells
/// (<see cref="OasisCells"/>), which the oasis level builds itself: coarse while it's far away, finely once it has
/// streamed in. Both sample the same height function, and the fine patch matches the coarse grid along its border,
/// so the seams never open.
/// </summary>
public sealed class DesertTerrain
{
	/// <summary>The terrain's own seed, so a level's layout seed can change without moving the ground.</summary>
	public const int Seed = 7;

	public const float Size = 3600.0f;
	public const int Cells = 150;
	public const float Cell = Size / Cells;
	public const float Origin = -Size * 0.5f;

	private const float FlatRadius = 370.0f;

	/// <summary>The coarse cells the oasis level owns: a 336 m square round <see cref="Oasis"/>.</summary>
	public static readonly Rect2I OasisCells = new(70, 101, 14, 14);

	/// <summary>The middle of the oasis pool, in world x/z.</summary>
	public static readonly Vector2 Oasis = new(Origin + (70 + 7) * Cell, Origin + (101 + 7) * Cell);

	/// <summary>The little island in the pool, relative to <see cref="Oasis"/>.</summary>
	public static readonly Vector2 Island = new(10.0f, 14.0f);

	/// <summary>How far the fine patch blends from the coarse grid at its border to full detail.</summary>
	private const float BlendWidth = 30.0f;

	private readonly FastNoiseLite _noise = new()
	{
		Seed = Seed,
		NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
		Frequency = 0.004f,
		FractalOctaves = 3,
	};

	/// <summary>Height of the oasis water surface.</summary>
	public float WaterLevel { get; }

	public DesertTerrain()
	{
		// Sit the water a little below the lowest dune trough round the basin, so the pool never spills.
		var low = float.MaxValue;
		for (var k = 0; k < 48; k++)
		{
			var a = Mathf.Tau * k / 48.0f;
			foreach (var r in (float[])[120.0f, 150.0f, 180.0f])
				low = Mathf.Min(low, DunesAt(Oasis.X + Mathf.Cos(a) * r, Oasis.Y + Mathf.Sin(a) * r));
		}
		WaterLevel = low - 3.0f;
	}

	/// <summary>Ground height at a world position.</summary>
	public float HeightAt(float x, float z)
	{
		var dunes = DunesAt(x, z);
		var rr = BasinRadius(x, z);
		if (rr >= 165.0f)
			return dunes;
		// A shallow pool about 50 m across, a gentle shore, then the dunes rising round it.
		var floor = WaterLevel - 3.2f + 4.8f * Mathf.SmoothStep(18.0f, 78.0f, rr);
		var island = new Vector2(x, z).DistanceTo(Oasis + Island);
		floor += 4.0f * (1.0f - Mathf.SmoothStep(2.0f, 11.0f, island));
		return Mathf.Lerp(floor, dunes, Mathf.SmoothStep(70.0f, 165.0f, rr));
	}

	/// <summary>Distance from the oasis, wobbled so the shore isn't a circle.</summary>
	private float BasinRadius(float x, float z)
	{
		var r = new Vector2(x, z).DistanceTo(Oasis);
		return r * (1.0f + 0.15f * _noise.GetNoise2D(x * 2.5f + 5000.0f, z * 2.5f));
	}

	/// <summary>The open desert: flat under the city, dunes beyond, a high rim at the edge.</summary>
	private float DunesAt(float x, float z)
	{
		var r = new Vector2(x, z).Length();
		var t = Mathf.SmoothStep(FlatRadius, FlatRadius + 320.0f, r);
		if (t <= 0.0f)
			return 0.0f;
		var dunes = (_noise.GetNoise2D(x, z) * 0.5f + 0.5f) * 30.0f;
		var ridges = (1.0f - Mathf.Abs(_noise.GetNoise2D(x * 0.5f + 900.0f, z * 0.5f))) * 16.0f;
		return t * (dunes + ridges) + Mathf.SmoothStep(1350.0f, 1750.0f, r) * 170.0f;
	}

	/// <summary>Smooth normal of the height field, sampled step metres either side.</summary>
	private Vector3 NormalAt(float x, float z, float step) =>
		new Vector3(HeightAt(x - step, z) - HeightAt(x + step, z), 2.0f * step,
			HeightAt(x, z - step) - HeightAt(x, z + step)).Normalized();

	/// <summary>Vertex tint: mottled sand, redder on steep slopes, damp and greener down by the water.</summary>
	private Color ColorAt(float x, float z, float h, Vector3 n)
	{
		var shade = 0.86f + 0.24f * (_noise.GetNoise2D(x * 3.0f + 300.0f, z * 3.0f) * 0.5f + 0.5f);
		var c = new Color(shade, shade * 0.97f, shade * 0.93f);
		c = c.Lerp(new Color(0.82f, 0.62f, 0.5f), Mathf.Clamp((0.95f - n.Y) * 4.0f, 0.0f, 1.0f));
		var near = 1.0f - Mathf.SmoothStep(120.0f, 165.0f, new Vector2(x, z).DistanceTo(Oasis));
		if (near <= 0.0f)
			return c;
		var grass = (1.0f - Mathf.SmoothStep(WaterLevel + 1.0f, WaterLevel + 4.5f, h)) * near;
		c = c.Lerp(new Color(0.42f, 0.55f, 0.26f), grass * (0.7f + 0.3f * _noise.GetNoise2D(x * 8.0f, z * 8.0f)));
		var damp = 1.0f - Mathf.SmoothStep(WaterLevel - 0.2f, WaterLevel + 0.7f, h);
		return c.Lerp(new Color(0.42f, 0.36f, 0.28f), damp * near);
	}

	/// <summary>
	/// A mesh of the coarse cells in <paramref name="cells"/>, each split into sub × sub quads, leaving out any
	/// coarse cell inside <paramref name="hole"/>. A fine patch (sub &gt; 1) fades from the coarse surface at the
	/// border of <paramref name="patch"/> (cells, by default) to full detail over <see cref="BlendWidth"/>, so it
	/// meets the coarse cells round it exactly. Build a big patch a strip at a time by passing the whole patch.
	/// </summary>
	public ArrayMesh Build(Rect2I cells, int sub = 1, Rect2I? hole = null, Rect2I? patch = null)
	{
		var nx = cells.Size.X * sub;
		var nz = cells.Size.Y * sub;
		var step = Cell / sub;
		var x0 = Origin + cells.Position.X * Cell;
		var z0 = Origin + cells.Position.Y * Cell;
		var whole = patch ?? cells;
		var px0 = Origin + whole.Position.X * Cell;
		var pz0 = Origin + whole.Position.Y * Cell;
		var px1 = px0 + whole.Size.X * Cell;
		var pz1 = pz0 + whole.Size.Y * Cell;

		// Corner samples of the coarse cells, and fine heights with a border of one for the normals.
		var corners = new (float H, Vector3 N, Color C)[(cells.Size.X + 1) * (cells.Size.Y + 1)];
		for (var j = 0; j <= cells.Size.Y; j++)
		{
			for (var i = 0; i <= cells.Size.X; i++)
			{
				var x = x0 + i * Cell;
				var z = z0 + j * Cell;
				var h = HeightAt(x, z);
				var n = NormalAt(x, z, Cell);
				corners[j * (cells.Size.X + 1) + i] = (h, n, ColorAt(x, z, h, n));
			}
		}
		var fine = sub > 1 ? new float[(nx + 3) * (nz + 3)] : null;
		if (fine is not null)
		{
			for (var j = -1; j <= nz + 1; j++)
			{
				for (var i = -1; i <= nx + 1; i++)
					fine[(j + 1) * (nx + 3) + i + 1] = HeightAt(x0 + i * step, z0 + j * step);
			}
		}

		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		for (var j = 0; j <= nz; j++)
		{
			for (var i = 0; i <= nx; i++)
			{
				var x = x0 + i * step;
				var z = z0 + j * step;
				var w = 0.0f;
				if (fine is not null)
				{
					var edge = Mathf.Min(Mathf.Min(x - px0, px1 - x), Mathf.Min(z - pz0, pz1 - z));
					w = Mathf.SmoothStep(0.0f, BlendWidth, edge);
				}
				float h;
				Vector3 n;
				Color c;
				if (w >= 1.0f)
				{
					(h, n, c) = Fine(fine!, nx, i, j, x, z, step);
				}
				else
				{
					// Interpolate the coarse cell this vertex lies in, the way the coarse mesh does along its edges.
					var ci = Mathf.Min(i / sub, cells.Size.X - 1);
					var cj = Mathf.Min(j / sub, cells.Size.Y - 1);
					var tx = (float)(i - ci * sub) / sub;
					var tz = (float)(j - cj * sub) / sub;
					var row = cells.Size.X + 1;
					var a = corners[cj * row + ci];
					var b = corners[cj * row + ci + 1];
					var d = corners[(cj + 1) * row + ci];
					var e = corners[(cj + 1) * row + ci + 1];
					h = Mathf.Lerp(Mathf.Lerp(a.H, b.H, tx), Mathf.Lerp(d.H, e.H, tx), tz);
					n = a.N.Lerp(b.N, tx).Lerp(d.N.Lerp(e.N, tx), tz).Normalized();
					c = a.C.Lerp(b.C, tx).Lerp(d.C.Lerp(e.C, tx), tz);
					if (w > 0.0f)
					{
						var f = Fine(fine!, nx, i, j, x, z, step);
						h = Mathf.Lerp(h, f.H, w);
						n = n.Lerp(f.N, w).Normalized();
						c = c.Lerp(f.C, w);
					}
				}
				st.SetNormal(n);
				st.SetColor(c);
				st.AddVertex(new Vector3(x, h, z));
			}
		}
		for (var j = 0; j < nz; j++)
		{
			for (var i = 0; i < nx; i++)
			{
				if (hole is { } r && r.HasPoint(cells.Position + new Vector2I(i / sub, j / sub)))
					continue;
				var a = j * (nx + 1) + i;
				foreach (var k in (int[])[a, a + 1, a + nx + 2, a, a + nx + 2, a + nx + 1])
					st.AddIndex(k);
			}
		}
		return st.Commit();
	}

	/// <summary>A fine sample from the padded height grid.</summary>
	private (float H, Vector3 N, Color C) Fine(float[] fine, int nx, int i, int j, float x, float z, float step)
	{
		var row = nx + 3;
		var k = (j + 1) * row + i + 1;
		var h = fine[k];
		var n = new Vector3(fine[k - 1] - fine[k + 1], 2.0f * step, fine[k - row] - fine[k + row]).Normalized();
		return (h, n, ColorAt(x, z, h, n));
	}
}
