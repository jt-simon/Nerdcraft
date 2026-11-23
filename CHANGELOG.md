# Changelog

All notable changes to Nerdcraft are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the version is 0.x, a minor bump may
contain breaking changes.

## [Unreleased]

### Added

- Player movement instead of a free camera: walking with gravity and block collision, jumping, sneaking (slower, lower eyes, no stepping off ledges), double-tap W sprinting, swimming in water, flight (F; still collides, ends on landing) and noclip (N). Controls are WASD, Space, Shift, F and N.
- The movement rules are a pure, fixed-rate (20 Hz) simulation in Domain (`PlayerMovement`, `VoxelCollision`, `PlayerState`, `PlayerCommand`, `PlayerAbilities`), so a server can run the same code later.
- `BlockTypes.IsSolid` and `BlockTypes.IsLiquid`.
- Procedural terrain from layered simplex noise (continents, mountain ranges and hills) around sea level at Y = 0.
- World height range of Y = -50 to Y = 200, set by `GameSettings.Bounds`.
- Bedrock block that always fills the bottom layer of the world.
- `Chunk.IsEmpty`, which reports whether a chunk holds only air.
- Ambient occlusion: block faces darken in corners and creases, including across chunk borders.
- Caves: large caverns and winding tunnels carved from 3D noise, sealed below the surface except for occasional openings. The layers just above bedrock are never carved.
- F3 toggles a debug overlay showing FPS and the camera position.
- The F3 overlay also shows chunks drawn / meshed and the vertices drawn per frame (`DebugOverlay`, `DrawStats`).
- Cave culling: chunks hidden behind solid terrain are no longer drawn. Each chunk records which of its faces are joined by open space (`ChunkVisibility`), and `ChunkStreamer.Draw` walks outward from the camera's chunk through those connections.

- Translucent water fills the terrain up to sea level (`HeightmapTerrainGenerator.SeaLevel`, Y = 0).
- Surface materials by height: gravel on the deep seabed, sand on the shallow seabed and beaches, grass over dirt in between, bare stone on high ground and snow on the peaks. Band edges are shifted by noise so they wander. New block types `Sand`, `Gravel`, `Snow` and `Water`.
- `IRenderer.DrawTranslucentMesh`, a blended draw pass without depth writes, and `BlockPalette.IsTranslucent`.
- `VoxelWorld.Revision`, a counter that increases whenever a chunk is loaded or unloaded or a block changes.
- `GameSettings.MaxChunkInstallsPerFrame` (default 64) and `MaxMeshRequestsPerFrame` (default 16), which spread the main-thread work of a burst of finished chunks over several frames instead of stalling one.

### Fixed

- Malformed chunk loads leave the world untouched, and unloading a chunk invalidates its neighbors' geometry and ambient occlusion.
- Collision and liquid queries keep working far from spawn, and long camera jumps release chunks that are out of range.
- Stopping the chunk pipeline returns pooled buffers from abandoned work.

### Changed

- World-owned chunks are exposed as read-only views. Block edits go through `VoxelWorld.SetBlock` so streaming sees every change.
- Streaming bounds outstanding results and budgets individual mesh uploads, including water. Mixed chunks appear after both uploads are ready, and stale results and mesh releases are spread across frames.
- Meshing allocates material buffers on demand, the debug mesh count is computed only when displayed, and Raylib batches depth-write changes across the water pass.

- `InputState` now has `Move` as a `Vector2` and `Jump`, `Sneak`, `ToggleFly` and `ToggleNoclip` in place of `Sprint`; `FlyCamera` is replaced by `LocalPlayer`.
- `NerdcraftGame`'s `spawnHeight` is now the height of the player's feet, and the player spawns standing on the terrain instead of 20 blocks above it.
- `IRenderer.EndFrame` now takes an optional `DebugOverlay` (camera position plus `DrawStats`) and draws the debug overlay when it is set (`RaylibRenderer.ShowFps` is removed); `ChunkStreamer.Draw` returns the `DrawStats`.
- `ChunkMesh` gains a `Visibility` member, and `IChunkMesher.Mesh` must set it.
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
- Terrain is generated a whole column at a time, so the heightmap is computed once per column instead of once per chunk, which is about 2x faster. `IChunkWorkQueue.TryRequestGenerate` is replaced by `TryRequestGenerateColumn`, and `ITerrainGenerator` gains `GenerateColumn` (the default implementation calls `Generate` per chunk).
- The chunk streamer skips its per-frame scan of every chunk in range unless the camera crossed a chunk border, the world changed, or a mesh request finished or was refused.
- Meshing is about 20% faster: the ambient occlusion lookups for each face corner use precomputed offsets. The meshes are unchanged.
- `RaylibRenderer` no longer uploads vertex normals, which the default material does not use, saving about 40% of each mesh's video memory and upload size.
- Default `GameSettings.MaxUploadsPerFrame` is now 8 (was 4), which roughly halves the time to fill the view at the default render distance.
- Startup is smoother: the worst frame while the first chunks load dropped from about 40-60 ms to about 12-22 ms in a headless simulation.

## [0.1.0] - 2026-11-22

### Added

- Domain layer: blocks, chunks, voxel world and terrain generation.
- Application layer: game loop, chunk meshing and streaming, and the ports the game needs from the outside world.
- Raylib renderer adapter and desktop host with a fly camera (WASD, mouse look, Esc to quit).
- Continuous integration, CodeQL and Dependabot workflows.
- README, architecture docs, contributing guide, security policy and agent instructions.

[Unreleased]: https://github.com/jt-simon/Nerdcraft/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/jt-simon/Nerdcraft/releases/tag/v0.1.0
