using Godot;

namespace Galaxies;

/// <summary>
/// An in-world monitor. Draws into its own 128x96 viewport with a blocky,
/// unantialiased monospace font, shown pixel-sharp on an emissive panel, with
/// a dim shadowed light in front so the screen lights what it faces.
/// Lines starting with "#" are green headers, "!" orange warnings, the rest cyan.
/// </summary>
[Tool]
[GlobalClass]
public partial class TerminalScreen : Node3D
{
	private static readonly Vector2I Resolution = new(128, 96);
	private const int FontSize = 8;
	private const int LineHeight = 10;
	private static readonly Color Background = new(0.02f, 0.05f, 0.17f);
	private static readonly Color Text = new(0.35f, 0.85f, 1.0f);
	private static readonly Color Header = new(0.4f, 1.0f, 0.45f);
	private static readonly Color Warning = new(1.0f, 0.55f, 0.12f);

	[Export] public string[] Lines { get; set; } = ["#TERMINAL", "READY"];
	[Export] public Vector2 ScreenSize { get; set; } = new(1.2f, 0.9f);
	[Export] public Color LightColor { get; set; } = new(0.3f, 0.7f, 1.0f);
	[Export] public float LightEnergy { get; set; } = 0.6f;

	private static SystemFont? _font;

	private SubViewport _viewport = null!;
	private Control _canvas = null!;
	private bool _cursorOn = true;
	private double _blink;

	public override void _Ready()
	{
		_viewport = new SubViewport
		{
			Size = Resolution,
			Disable3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
			CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
		};
		AddChild(_viewport);
		_canvas = new Control { Size = Resolution };
		_canvas.Draw += DrawTerminal;
		_viewport.AddChild(_canvas);

		AddChild(new MeshInstance3D
		{
			Mesh = Kit.Box(new Vector3(ScreenSize.X + 0.24f, ScreenSize.Y + 0.24f, 0.16f), 0.05f),
			MaterialOverride = Kit.Mat("dark"),
		});

		var m = new StandardMaterial3D
		{
			AlbedoColor = Colors.Black,
			Roughness = 0.35f,
			EmissionEnabled = true,
			Emission = Colors.White,
			EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply,
			EmissionTexture = _viewport.GetTexture(),
			EmissionEnergyMultiplier = 2.6f,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
		};
		AddChild(new MeshInstance3D
		{
			Mesh = new QuadMesh { Size = ScreenSize },
			MaterialOverride = m,
			Position = new Vector3(0, 0, 0.081f),
		});

		AddChild(new SpotLight3D
		{
			Position = new Vector3(0, 0, 0.3f),
			Rotation = new Vector3(0, Mathf.Pi, 0), // spot lights shine along -Z; turn it out of the screen
			LightColor = LightColor,
			LightEnergy = LightEnergy,
			SpotRange = 4.0f,
			SpotAngle = 70.0f,
			ShadowEnabled = true,
			ShadowBlur = 0.4f,
			LightVolumetricFogEnergy = 0.3f,
			DistanceFadeEnabled = true,
			DistanceFadeBegin = 30.0f,
			DistanceFadeShadow = 15.0f,
			DistanceFadeLength = 8.0f,
		});
	}

	public override void _Process(double delta)
	{
		_blink += delta;
		if (_blink >= 0.5)
		{
			_blink = 0.0;
			_cursorOn = !_cursorOn;
			_canvas.QueueRedraw();
			_viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
		}
	}

	private void DrawTerminal()
	{
		var font = GetFont();
		_canvas.DrawRect(new Rect2(Vector2.Zero, Resolution), Background);
		var y = 9;
		foreach (var raw in Lines)
		{
			var color = Text;
			var text = raw;
			if (raw.StartsWith('#'))
			{
				color = Header;
				text = raw[1..];
				_canvas.DrawRect(new Rect2(4, y + 2, Resolution.X - 8, 1), Header);
			}
			else if (raw.StartsWith('!'))
			{
				color = Warning;
				text = raw[1..];
			}
			_canvas.DrawString(font, new Vector2(4, y), text, HorizontalAlignment.Left, -1, FontSize, color);
			y += LineHeight;
		}
		if (_cursorOn && y < Resolution.Y)
			_canvas.DrawRect(new Rect2(4, y - 7, 5, 8), Text);
	}

	private static SystemFont GetFont()
	{
		return _font ??= new SystemFont
		{
			FontNames = ["Consolas", "Lucida Console", "Courier New", "monospace"],
			Antialiasing = TextServer.FontAntialiasing.None,
			Hinting = TextServer.Hinting.Normal,
			SubpixelPositioning = TextServer.SubpixelPositioning.Disabled,
		};
	}
}
