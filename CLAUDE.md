# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Repository Layout

- `src/` — Source code.
- `etc/` — Scripts (build, tooling, utilities, etc.).
- `docs/` — Claude memory notes. Use this folder for notes, decisions, and context that should persist across sessions.
- `assets/` — Non-code files: Blender files, 3D models, textures, and other art/media assets.

Keep files in their appropriate directory; don't put code in `assets/` or binary assets in `src/`.

## Client

`src/client/` is the game client: a Godot 4.7 (.NET) project in C# (Forward+, Jolt physics, D3D12). Target Godot 4.7 conventions and APIs; don't use deprecated or older 4.x/3.x patterns.

- `Galaxies.csproj` / `Galaxies.sln`: the C# project (`Godot.NET.Sdk/4.7.2`, net8.0, root namespace `Galaxies`).
- `scenes/main.tscn`: the main scene, with `scripts/Main.cs`.
- `env/style_environment.tres`: the shared art-style environment. Use it in every interior and space scene.

Godot lives at `C:\Program Files\Godot_v4.7.2-stable_mono_win64\`. Use the `_console.exe` build from the shell.

- Build C#: `dotnet build src/client`
- Import assets: `godot --headless --path src/client --import`
- Run: `godot --path src/client`

## Art style

All visual work follows the 0x10c-style art direction in [docs/art-style.md](docs/art-style.md): crisp early-2000s sci-fi, a muted world with saturated accents, dark rooms lit by a few strong lights, and low-res pixel-sharp screens. Read it before making scenes, materials, lights, textures, models, screens or UI.
