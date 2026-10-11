using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Galaxies;

/// <summary>
/// The modular kit from docs/art-style.md: chamfered and lathed low-poly meshes,
/// and the shared matte materials built on the painted panel textures.
/// </summary>
public static class Kit
{
	private const string Textures = "res://textures/";

	/// <summary>
	/// A kit surface. World-aligned surfaces put their panel seams on the 2 m
	/// building grid; the others are mapped in object space so props carry their own seams.
	/// </summary>
	private readonly record struct Surface(string Texture, float Roughness, float Metallic, float Tile, bool World);

	private static readonly Dictionary<string, Surface> Surfaces = new()
	{
		["terrain"] = new("sand.png", 1.0f, 0.0f, 4.0f, true),
		["sand"] = new("sand.png", 1.0f, 0.0f, 4.0f, true),
		["rock"] = new("rock.png", 0.95f, 0.0f, 16.0f, true),
		["sandcrete"] = new("sandcrete.png", 0.92f, 0.0f, 2.0f, true),
		["metal"] = new("metal.png", 0.6f, 0.35f, 2.0f, true),
		["olive"] = new("olive.png", 0.75f, 0.2f, 2.0f, true),
		["dark"] = new("dark_metal.png", 0.6f, 0.35f, 2.0f, true),
		["floor"] = new("floor.png", 0.8f, 0.1f, 2.0f, true),
		["hazard"] = new("hazard.png", 0.8f, 0.1f, 1.0f, true),
		["crate"] = new("crate.png", 0.8f, 0.05f, 1.2f, false),
		["paint"] = new("paint.png", 0.65f, 0.15f, 1.0f, false),
	};

	private static readonly Dictionary<string, ArrayMesh> Meshes = new();
	private static readonly Dictionary<string, StandardMaterial3D> Materials = new();

	/// <summary>Matte, triplanar-mapped material for a kit surface, optionally tinted.</summary>
	public static StandardMaterial3D Mat(string surface, Color? tint = null)
	{
		var color = tint ?? Colors.White;
		var key = surface + color.ToHtml();
		if (Materials.TryGetValue(key, out var m))
			return m;
		var def = Surfaces[surface];
		var s = 1.0f / def.Tile;
		m = new StandardMaterial3D
		{
			AlbedoTexture = GD.Load<Texture2D>(Textures + def.Texture),
			AlbedoColor = color,
			Roughness = def.Roughness,
			Metallic = def.Metallic,
			MetallicSpecular = def.Metallic > 0.0f ? 0.5f : 0.25f,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
			Uv1Triplanar = true,
			Uv1WorldTriplanar = def.World,
			Uv1TriplanarSharpness = 8.0f,
			Uv1Scale = new Vector3(s, s, s),
			VertexColorUseAsAlbedo = surface == "terrain",
		};
		if (!def.World)
			m.Uv1Offset = new Vector3(0.5f, 0.5f, 0.5f);
		Materials[key] = m;
		return m;
	}

	/// <summary>
	/// Emissive material for screens, lamps and indicator lights. Emission lights
	/// nothing by itself; pair it with a dim light (see docs/art-style.md).
	/// </summary>
	public static StandardMaterial3D Glow(Color color, float energy = 3.0f)
	{
		var key = $"glow{color.ToHtml()}{energy:F2}";
		if (Materials.TryGetValue(key, out var m))
			return m;
		m = new StandardMaterial3D
		{
			AlbedoColor = color * 0.25f,
			Roughness = 0.5f,
			EmissionEnabled = true,
			Emission = color,
			EmissionEnergyMultiplier = energy,
		};
		Materials[key] = m;
		return m;
	}

	/// <summary>
	/// Untextured, double-sided matte material for palms, reeds and shrubs. Takes its colour from the mesh's
	/// vertex colours (or a MultiMesh's instance colours), so the facets carry the look.
	/// </summary>
	public static StandardMaterial3D Foliage()
	{
		if (Materials.TryGetValue("foliage", out var m))
			return m;
		m = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			Roughness = 0.9f,
			MetallicSpecular = 0.2f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		Materials["foliage"] = m;
		return m;
	}

	/// <summary>A box centred on the origin with every edge chamfered.</summary>
	public static ArrayMesh Box(Vector3 size, float bevel = 0.1f)
	{
		var key = $"box{Key(size)}{bevel:F3}";
		if (Meshes.TryGetValue(key, out var cached))
			return cached;
		var h = size * 0.5f;
		var b = Mathf.Min(bevel, Mathf.Min(h.X, Mathf.Min(h.Y, h.Z)) * 0.8f);
		var i = h - Vector3.One * b;
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		float[] signs = [-1.0f, 1.0f];
		Vector2[] corners = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];
		for (var a = 0; a < 3; a++)
		{
			var u = (a + 1) % 3;
			var v = (a + 2) % 3;
			foreach (var sa in signs)
			{
				var face = new Vector3[4];
				for (var k = 0; k < 4; k++)
				{
					var p = Vector3.Zero;
					p[a] = sa * h[a];
					p[u] = corners[k].X * i[u];
					p[v] = corners[k].Y * i[v];
					face[k] = p;
				}
				var outward = Vector3.Zero;
				outward[a] = sa;
				AddPoly(st, face, outward);
			}
			if (b < 0.001f)
				continue;
			// The chamfer strips along the edges parallel to axis a.
			foreach (var su in signs)
			{
				foreach (var sv in signs)
				{
					var p0 = Vector3.Zero;
					var p1 = Vector3.Zero;
					p0[u] = su * h[u];
					p0[v] = sv * i[v];
					p1[u] = su * i[u];
					p1[v] = sv * h[v];
					var lo = Vector3.Zero;
					lo[a] = -i[a];
					var hi = -lo;
					var outward = Vector3.Zero;
					outward[u] = su;
					outward[v] = sv;
					AddPoly(st, [p0 + lo, p1 + lo, p1 + hi, p0 + hi], outward);
				}
			}
		}
		if (b >= 0.001f)
		{
			foreach (var sx in signs)
			foreach (var sy in signs)
			foreach (var sz in signs)
			{
				var sg = new Vector3(sx, sy, sz);
				AddPoly(st, [
					new Vector3(h.X, i.Y, i.Z) * sg, new Vector3(i.X, h.Y, i.Z) * sg, new Vector3(i.X, i.Y, h.Z) * sg,
				], sg);
			}
		}
		var mesh = st.Commit();
		Meshes[key] = mesh;
		return mesh;
	}

	/// <summary>
	/// A faceted solid of revolution around +Y. profile is (radius, height) from
	/// bottom to top along the outer surface.
	/// </summary>
	public static ArrayMesh Lathe(Vector2[] profile, int sides, bool capTop = true, bool capBottom = false)
	{
		var key = $"lathe{string.Join(";", profile.Select(p => $"{p.X:F3},{p.Y:F3}"))}{sides}{capTop}{capBottom}";
		if (Meshes.TryGetValue(key, out var cached))
			return cached;
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		var top = profile[^1];
		var bottom = profile[0];
		var topCap = new Vector3[sides];
		var bottomCap = new Vector3[sides];
		for (var k = 0; k < sides; k++)
		{
			var a0 = Mathf.Tau * k / sides;
			var a1 = Mathf.Tau * (k + 1) / sides;
			var d0 = new Vector3(Mathf.Cos(a0), 0.0f, Mathf.Sin(a0));
			var d1 = new Vector3(Mathf.Cos(a1), 0.0f, Mathf.Sin(a1));
			var dm = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0.0f, Mathf.Sin((a0 + a1) * 0.5f));
			for (var j = 0; j < profile.Length - 1; j++)
			{
				var p = profile[j];
				var q = profile[j + 1];
				AddPoly(st, [
					d0 * p.X + Vector3.Up * p.Y, d1 * p.X + Vector3.Up * p.Y,
					d1 * q.X + Vector3.Up * q.Y, d0 * q.X + Vector3.Up * q.Y,
				], dm * (q.Y - p.Y) - Vector3.Up * (q.X - p.X));
			}
			topCap[k] = d0 * top.X + Vector3.Up * top.Y;
			bottomCap[k] = d0 * bottom.X + Vector3.Up * bottom.Y;
		}
		if (capTop && top.X > 0.001f)
			AddPoly(st, topCap, Vector3.Up);
		if (capBottom && bottom.X > 0.001f)
			AddPoly(st, bottomCap, Vector3.Down);
		var mesh = st.Commit();
		Meshes[key] = mesh;
		return mesh;
	}

	/// <summary>
	/// Adds a convex polygon as front-facing triangles whose normal points along
	/// outward. Flat-shaded unless per-vertex normals are given.
	/// </summary>
	public static void AddPoly(SurfaceTool st, Vector3[] pts, Vector3 outward,
		Vector3[]? normals = null, Color[]? colors = null)
	{
		var idx = new List<int>(pts.Length);
		for (var i = 0; i < pts.Length; i++)
		{
			if (pts[i].DistanceSquaredTo(pts[(i + 1) % pts.Length]) > 1e-10f)
				idx.Add(i);
		}
		if (idx.Count < 3)
			return;
		// Newell's method: a robust normal for the (counter-clockwise) polygon.
		var n = Vector3.Zero;
		for (var k = 0; k < idx.Count; k++)
		{
			var a = pts[idx[k]];
			var b = pts[idx[(k + 1) % idx.Count]];
			n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
		}
		if (n.LengthSquared() < 1e-14f)
			return;
		n = n.Normalized();
		if (n.Dot(outward) < 0.0f)
		{
			n = -n;
			idx.Reverse();
		}
		// Godot treats clockwise triangles as front-facing.
		for (var k = 1; k < idx.Count - 1; k++)
		{
			foreach (var i in (int[])[idx[0], idx[k + 1], idx[k]])
			{
				st.SetNormal(normals is null ? n : normals[i]);
				if (colors is not null)
					st.SetColor(colors[i]);
				st.AddVertex(pts[i]);
			}
		}
	}

	/// <summary>A stable cache key for a size.</summary>
	public static string Key(Vector3 v) => $"{v.X:F3},{v.Y:F3},{v.Z:F3}";
}
