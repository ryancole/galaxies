# Levels and streaming

Decided 2026-10-11.

## The map

One desert map, 3600 m square, centred on Kessa Outpost. `DesertTerrain` owns the ground for the whole map:
flat under the city, dunes beyond, a high rim at the edge, and the basin of Halun Springs. Every level samples
the same height function, so levels never disagree about where the ground is. The terrain has its own seed
(`DesertTerrain.Seed`), separate from any level's layout seed.

The ground is a grid of 150 × 150 coarse cells (24 m). Kessa builds all of it except the cells a streamed level
owns (`DesertTerrain.OasisCells`). That level builds those cells itself: coarse while it's far away, fine
(3 m) once it has streamed in. The fine patch matches the coarse grid along its border and blends to full
detail over 30 m, so no seams open.

## Streaming (WoW-style)

Kessa is the hub and is always loaded. Other places are `StreamedLevel`s:

- **Far half**: built once and always there. The big shapes you can see across the desert (palms, rocks, the
  water tower, buildings, water, coarse ground) with their colliders. It's cheap, so the place shows on the
  skyline from anywhere.
- **Near half**: everything small (fine ground, props, reeds and shrubs, most lights, screens, interiors). When
  the camera comes within `LoadRadius` (400 m) it's built into the tree hidden, a few milliseconds a frame, and
  shown once it's whole. It's freed beyond `UnloadRadius` (500 m). If you arrive right inside (a teleport, or the
  server placing you there) it's built at once.

The near half must come back the same every time, so it reseeds `_rng`. Anything the two halves both need, like
where the palms are, is planned once up front.

## Halun Springs

The second level. An oasis in a dune basin between two mesas, about 550 m south of Kessa's south gate (centre
(48, 792)). A line of beacon posts leads there from the gate. Its features:

- A pool with an island.
- Palm groves round the shore.
- A pump house and jetty on the north shore, with a water tower (the landmark: cyan band, red beacon) beside it.
- A traders' camp on the west shore.
- Dome huts on the east shore.
- Sandstone outcrops round the south.

## Not done yet

- The server doesn't know about levels or geometry, so nothing stops walking through the oasis walls either.
- Kessa itself isn't streamed. If the map grows, Kessa's detail could become a `StreamedLevel` too.
- Remote players in an unloaded level are drawn where the server says, so they may float or sink a little against the coarse ground.
