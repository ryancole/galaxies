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
- `scenes/desert_city.tscn` also holds the `Oasis` node: Halun Springs, the second level, about 550 m south of the south gate (centre (48, 792), water at about y 13.7). See [docs/levels.md](docs/levels.md).
- `scripts/LevelBuilder.cs`: the base class for level builders. It holds the palette, the shared props (doors, lamps, screens, crates, vaporators, dome huts, skiffs, the `Enterable` building shell) and the node helpers (`Box`, `Glow`, `Lathe`, `Spot`, `Omni`...). All randomness goes through `_rng`.
- `scripts/CityBuilder.cs`: a `[Tool]` node that generates the whole desert city (terrain, wall, plaza and cantina, docking bays, lots, outskirts) from `CitySeed` at load, in the editor too, with a Rebuild button in the inspector. Generated nodes aren't saved into the scene. Keep the order of `_rng` calls stable: changing it reshuffles the whole layout.
- `scripts/DesertTerrain.cs`: the ground of the whole map (height, colour, meshes), shared by every level on it. It has its own fixed seed. Kessa builds every coarse cell except `OasisCells`, which the oasis builds itself.
- `scripts/StreamedLevel.cs`: a level that streams in WoW-style. A far half (big silhouettes and their colliders) is always there; the near half (detail) is built hidden a few ms a frame when the camera comes within `LoadRadius`, shown once it's whole, and freed beyond `UnloadRadius`.
- `scripts/OasisBuilder.cs`: Halun Springs, a `StreamedLevel`. `Plan()` places everything once so the far and near halves agree; the near half reseeds `_rng` so it comes back the same each time it streams in.
- `scripts/Kit.cs`: the modular kit. Chamfered boxes, faceted lathes and the shared matte triplanar materials. Keep building sizes and positions on the 2 m grid so the painted panel seams line up.
- `scripts/Player.cs`, `scenes/player.tscn`: the local player, a `CharacterBody3D` with physics interpolation, drawn as the male Meshy human (`Model`). Third person by default: the camera sits behind the head and pulls in front of walls. `V` toggles first person, the mouse wheel zooms. The camera and model are top-level and follow the interpolated body. Input actions (`move_*`, `jump`, `run`, `toggle_view`) are in `project.godot`. `Frozen` holds it still until the server places it.
- `scripts/NetworkClient.cs`: the `Network` node in the main scene. Connects to the server (`localhost:5090`, token `dev-player-1`; override with `-- --host= --port= --token=`, or `--offline` to play without a server), reports moves every 50 ms, applies corrections and draws other players as `RemoteAvatar`s (Meshy humans, drawn 100 ms in the past and interpolated). `scripts/Net/ServerConnection.cs` is the Godot-free UDP connection. The client references `src/server/Galaxies.Protocol`.
- `scripts/TerminalScreen.cs`: in-world screens (128×96 viewport, nearest filtering). Lines starting with `#` are green headers and `!` orange warnings.
- `models/`: the Meshy humans (`meshy_human_10k`, `meshy_female_10k`) from the what3 character repo, used for other players (`RemoteAvatar`).
- The level has no NPCs baked in: characters come from the server. The townspeople that what2 placed were removed, and `CityBuilder.SkipDraws` still uses up the random numbers they drew, so the rest of the layout matches what2 exactly.
- `textures/`: painted panel textures, copied from what2 (generated there by `tools/gen_textures.gd`). Keep `mipmaps/generate=true` in the `.import` files.
- `env/`: `style_environment.tres` for interiors and space; `desert_environment.tres` (plus `desert_sky.gdshader`) for planet surfaces. Don't add ambient or fill lights to brighten rooms. `oasis_water.gdshader` mirrors its own copy of the sky gradient, because the desert environment has sky reflections off.
- Emissive surfaces get a dim, shadowed light in their colour, and every positional light gets a distance fade (`CityBuilder.Fade`).

Godot lives at `C:\Program Files\Godot_v4.7.2-stable_mono_win64\`. Use the `_console.exe` build from the shell.

- Build C#: `dotnet build src/client`
- Import assets: `godot --headless --path src/client --import`
- Run: `godot --path src/client`
- Screenshot without playing: `godot --path src/client -- --shot=C:/tmp/a.png --cam=x,y,z,yaw_deg,pitch_deg --frames=150`. `--cam` looks through the player's eyes; add `--third` to look from behind them. Yaw 0 looks north (−Z). The player spawns at the south gate, (0, 0.2, 236).

## Art style

All visual work follows the 0x10c-style art direction in [docs/art-style.md](docs/art-style.md): crisp early-2000s sci-fi, a muted world with saturated accents, dark rooms lit by a few strong lights, and low-res pixel-sharp screens. Read it before making scenes, materials, lights, textures, models, screens or UI.
