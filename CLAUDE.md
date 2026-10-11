# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Repository Layout

- `src/` — Source code.
- `etc/` — Scripts (build, tooling, utilities, etc.).
- `docs/` — Claude memory notes. Use this folder for notes, decisions, and context that should persist across sessions.
- `assets/` — Non-code files: Blender files, 3D models, textures, and other art/media assets.

Keep files in their appropriate directory; don't put code in `assets/` or binary assets in `src/`.

## Server authority

The server owns the game state; the client only does what the server allows. Movement is WoW-style (decided 2026-10-11): the client moves its own avatar and reports where it went, and the server refuses impossible moves and snaps the client back with a correction. The client can't move until the server has placed it. See [docs/protocol.md](docs/protocol.md). The server doesn't know the level geometry yet, so it can't stop walking through walls.

## Client

`src/client/` is the game client: a Godot 4.7 (.NET) project in C# (Forward+, Jolt physics, D3D12). Target Godot 4.7 conventions and APIs; don't use deprecated or older 4.x/3.x patterns.

- `Galaxies.csproj` / `Galaxies.sln`: the C# project (`Godot.NET.Sdk/4.7.2`, net8.0, root namespace `Galaxies`).
- `scenes/desert_city.tscn`: the main scene, Kessa Outpost (ported from the what2 sandbox). Twin low suns, the `CityBuilder` node, the player and `DebugCapture`.
- `scripts/CityBuilder.cs`: a `[Tool]` node that generates the whole desert city (terrain, wall, plaza and cantina, docking bays, lots, outskirts) from `CitySeed` at load, in the editor too, with a Rebuild button in the inspector. Generated nodes aren't saved into the scene. Keep the order of `_rng` calls stable: changing it reshuffles the whole layout.
- `scripts/Kit.cs`: the modular kit. Chamfered boxes, faceted lathes and the shared matte triplanar materials. Keep building sizes and positions on the 2 m grid so the painted panel seams line up.
- `scripts/Player.cs`, `scenes/player.tscn`: first-person `CharacterBody3D` with physics interpolation. The camera is top-level and follows the interpolated body. Input actions (`move_*`, `jump`, `run`) are in `project.godot`. `Frozen` holds it still until the server places it.
- `scripts/NetworkClient.cs`: the `Network` node in the main scene. Connects to the server (`localhost:5090`, token `dev-player-1`; override with `-- --host= --port= --token=`, or `--offline` to play without a server), reports moves every 50 ms, applies corrections and draws other players as `RemoteAvatar`s (Meshy humans, drawn 100 ms in the past and interpolated). `scripts/Net/ServerConnection.cs` is the Godot-free UDP connection. The client references `src/server/Galaxies.Protocol`.
- `scripts/TerminalScreen.cs`: in-world screens (128×96 viewport, nearest filtering). Lines starting with `#` are green headers and `!` orange warnings.
- `models/`: the Meshy humans (`meshy_human_10k`, `meshy_female_10k`) from the what3 character repo. `CityBuilder.Human` places them as townspeople, looping `Idle` from a random point.
- `textures/`: painted panel textures, copied from what2 (generated there by `tools/gen_textures.gd`). Keep `mipmaps/generate=true` in the `.import` files.
- `env/`: `style_environment.tres` for interiors and space; `desert_environment.tres` (plus `desert_sky.gdshader`) for planet surfaces. Don't add ambient or fill lights to brighten rooms.
- Emissive surfaces get a dim, shadowed light in their colour, and every positional light gets a distance fade (`CityBuilder.Fade`).

Godot lives at `C:\Program Files\Godot_v4.7.2-stable_mono_win64\`. Use the `_console.exe` build from the shell.

- Build C#: `dotnet build src/client`
- Import assets: `godot --headless --path src/client --import`
- Run: `godot --path src/client`
- Screenshot without playing: `godot --path src/client -- --shot=C:/tmp/a.png --cam=x,y,z,yaw_deg,pitch_deg --frames=150`. Yaw 0 looks north (−Z). The player spawns at the south gate, (0, 0.2, 236).

## Art style

All visual work follows the 0x10c-style art direction in [docs/art-style.md](docs/art-style.md): crisp early-2000s sci-fi, a muted world with saturated accents, dark rooms lit by a few strong lights, and low-res pixel-sharp screens. Read it before making scenes, materials, lights, textures, models, screens or UI.
