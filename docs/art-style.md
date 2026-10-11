# Art style

Crisp early-2000s real-time 3D sci-fi, like the prototype builds of Notch's cancelled game 0x10c, which look a lot like Halo: Combat Evolved. Chunky, bevelled sci-fi architecture in mostly dark rooms, lit by a few strong lights, with bright glowing screens and saturated accents against a muted world.

## Principles

- **Muted world, saturated things.** Structures are grey-green, olive, gunmetal and dull steel, with light grey-white floor plates. Colour is kept for what matters: orange and red-orange tools and robots, yellow crew and signs, cyan/blue terminal light, green displays, small blue console lights. If everything is colourful, nothing is.
- **Darkness is a feature.** Very little ambient light. Rooms are lit by placed lights, and corners go properly dark.
- **Solid and industrial.** Blocky modular pieces, chamfered edges, inset panel seams. Nothing organic.
- **Crisp, not retro.** Clean 2003-era 3D, not a PS1/lo-fi filter. The only deliberately pixelated things are screens.

## Geometry

- Low-poly, built from modular kits: tall wall panels, floor plates, ceiling grids, railings, floor trenches, doors and heavy frames, consoles, all on a consistent grid so pieces snap together.
- Every hard edge is chamfered or bevelled, and panels have inset seams. Chunky proportions: thick walls, heavy doorframes, raised walkways.

## Textures and materials

- Small textures (about 128–256 px per panel) with smooth (linear) filtering and mipmaps. Detail is painted in: panel lines, rivets, scuffs, grime and corners darkened in the texture itself rather than by screen-space effects.
- Mostly matte (high roughness), with a little specular on metal. The texture carries the look; no complex PBR.

## Lighting

- A few strong spot and omni lights per room, with fairly hard-edged shadows (crisp shadows from railings, crates and held items).
- Very low ambient light. No fill light that flattens the room.
- Glow (bloom) on, so light sources and screens bloom slightly.
- Light shafts from a thin volumetric fog that only lights up where a shadowed light shines through it, so the shafts under hatches and lamps are real and anything standing in them shadows them. A ceiling lamp is a narrow downward spotlight for the shaft, plus a point light for the room that doesn't light the fog, so the shaft keeps its edges.
- Screens, indicator lights and lamps use emissive materials. Emission doesn't light anything, so each screen or light strip also gets a dim, shadowed spotlight in its colour just in front of it, shining out over the side it faces. Round glowing objects get a ring of these lights facing outward.

## Screens

- In-world monitors are low resolution and pixel-sharp: each one is rendered into its own small 128×96 viewport and shown with nearest-neighbour filtering on an emissive material.
- Simple terminal look: a solid dark-blue background, a blocky unantialiased monospace font, few colours (cyan text, green headers, orange warnings).

## Characters, first-person view and HUD

- Crew and robots are boxy and chunky, with simple silhouettes and saturated colours. A screen for a face fits the style.
- First-person held items are chunky and colourful (Halo-style), drawn by a separate camera layer so they never clip into walls.
- HUD is minimal: a tiny "+" crosshair and plain white text in a corner, no ornate frames. Most information is on in-world screens instead.

## Outside

Pure black space with simple, readable planets.

## Godot 4 settings to start from

- Renderer: Forward+.
- WorldEnvironment: very low ambient light, glow on, a filmic-style tonemap, no auto-exposure, a thin volumetric fog with no ambient inject. SSAO off or subtle (grime is in the textures).
- Lights: shadows on, low blur. Size the positional shadow atlas for a handful of shadowed lights per room.
- Anti-aliasing: MSAA 2× or 4×. Edges should be crisp, not smeared.
- Camera field of view: about 70–75°.

## Not this

- PS1-style vertex jitter, dithering, or pixelated rendering of the whole screen.
- Bright, evenly lit rooms; heavy ambient occlusion or depth of field.
- Glossy modern PBR, high-poly or photoreal assets, ornate HUDs.
