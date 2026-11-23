# Changelog

All notable changes to Nerdcraft are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the version is 0.x, a minor bump may
contain breaking changes.

## [Unreleased]

### Added

- Procedural terrain from layered simplex noise (continents, mountain ranges and hills) around sea level at Y = 0.
- World height range of Y = -50 to Y = 200, set by `GameSettings.Bounds`.
- Bedrock block that always fills the bottom layer of the world.
- `Chunk.IsEmpty`, which reports whether a chunk holds only air.
- Ambient occlusion: block faces darken in corners and creases, including across chunk borders.
- Caves: large caverns and winding tunnels carved from 3D noise, sealed below the surface except for occasional openings. The layers just above bedrock are never carved.
- F3 toggles a debug overlay showing FPS and the camera position.

- Translucent water fills the terrain up to sea level (`HeightmapTerrainGenerator.SeaLevel`, Y = 0).
- Surface materials by height: gravel on the deep seabed, sand on the shallow seabed and beaches, grass over dirt in between, bare stone on high ground and snow on the peaks. Band edges are shifted by noise so they wander. New block types `Sand`, `Gravel`, `Snow` and `Water`.
- `IRenderer.DrawTranslucentMesh`, a blended draw pass without depth writes, and `BlockPalette.IsTranslucent`.

### Changed

- `IRenderer.EndFrame` now takes an optional camera position and draws the debug overlay when it is set; `RaylibRenderer.ShowFps` is removed.
- `IChunkMesher.Mesh` returns a `ChunkMesh` (opaque and translucent meshes) instead of a `MeshData`, and `MeshedChunk.Mesh` has the same type.
- `ChunkStreamer.Draw` takes the camera position, to draw translucent meshes from farthest to nearest.
- Every seed generates a different world than before, and caves no longer open into the seabed.
- The camera spawns above sea level even over a seabed.
- `GameSettings.WorldHeightInChunks` is replaced by `GameSettings.Bounds`.
- Default `GameSettings.RenderDistance` is now 12 chunks (was 6).
- `HeightmapTerrainGenerator` now requires a `WorldBounds` argument in its constructor.
- The camera starts 20 blocks above the terrain surface instead of at a fixed height. `NerdcraftGame`'s constructor takes an optional `spawnHeight`.
- Chunks with no solid blocks are no longer meshed, and terrain generation skips noise sampling for chunks above the highest possible surface.
- Terrain noise now uses FastNoiseLite (OpenSimplex2), vendored under `Nerdcraft.Domain`. Every seed generates a different world than before.
- Terrain now sits higher: about 30% of columns are below sea level (Y = 0), down from about two-thirds, so a future ocean covers a minority of the map.

## [0.1.0] - 2026-11-22

### Added

- Domain layer: blocks, chunks, voxel world and terrain generation.
- Application layer: game loop, chunk meshing and streaming, and the ports the game needs from the outside world.
- Raylib renderer adapter and desktop host with a fly camera (WASD, mouse look, Esc to quit).
- Continuous integration, CodeQL and Dependabot workflows.
- README, architecture docs, contributing guide, security policy and agent instructions.

[Unreleased]: https://github.com/jt-simon/Nerdcraft/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/jt-simon/Nerdcraft/releases/tag/v0.1.0
