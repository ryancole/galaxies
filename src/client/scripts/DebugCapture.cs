using System.Collections.Generic;
using Godot;

namespace Galaxies;

/// <summary>
/// Dev helper for checking the look without playing. Does nothing unless run
/// with user args, e.g.:
///   godot --path src/client -- --shot=C:/tmp/a.png --cam=0,1.7,230,0,0 --frames=90
/// --cam is x,y,z,yaw,pitch (degrees) and freezes the player there, looking through their eyes (--third looks from
/// behind them instead). --delay=N waits N frames first, e.g. for the
/// server to place the player.
/// </summary>
public partial class DebugCapture : Node
{
	public override async void _Ready()
	{
		var args = new Dictionary<string, string>();
		foreach (var arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--"))
			{
				var kv = arg[2..].Split('=', 2);
				args[kv[0]] = kv.Length > 1 ? kv[1] : "";
			}
		}
		if (!args.TryGetValue("shot", out var shot))
			return;
		var tree = GetTree();
		var delay = args.TryGetValue("delay", out var d) ? d.ToInt() : 0;
		for (var i = 0; i <= delay; i++)
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		if (tree.GetFirstNodeInGroup("player") is Player player && args.TryGetValue("cam", out var cam))
		{
			var v = cam.SplitFloats(",");
			player.SetPhysicsProcess(false);
			player.FirstPerson = !args.ContainsKey("third");
			player.Teleport(new Vector3(v[0], v[1], v[2]), Mathf.DegToRad(v[3]), Mathf.DegToRad(v[4]));
		}
		var frames = args.TryGetValue("frames", out var f) ? f.ToInt() : 90;
		for (var i = 0; i < frames; i++)
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		GD.Print("fps: ", Engine.GetFramesPerSecond());
		GetViewport().GetTexture().GetImage().SavePng(shot);
		tree.Quit();
	}
}
