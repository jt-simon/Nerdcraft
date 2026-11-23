# Changelog

All notable changes to Nerdcraft are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the version is 0.x, a minor bump may
contain breaking changes.

## [Unreleased]

### Changed

- Chunks with no solid blocks are no longer meshed, and terrain generation skips noise sampling for chunks above the highest possible surface.
- `Chunk.IsEmpty` reports whether a chunk holds only air.

### Added

- Procedural terrain from layered simplex noise (continents, mountain ranges and hills) around sea level at Y = 0.
- World height range of Y = -50 to Y = 200, set by `GameSettings.Bounds`.
- Bedrock block that always fills the bottom layer of the world.

### Changed

- `GameSettings.WorldHeightInChunks` is replaced by `GameSettings.Bounds`.
- Default `GameSettings.RenderDistance` is now 12 chunks (was 6).
- `HeightmapTerrainGenerator` now requires a `WorldBounds` argument in its constructor.
- The camera starts 20 blocks above the terrain surface instead of at a fixed height. `NerdcraftGame`'s constructor takes an optional `spawnHeight`.

## [0.1.0] - 2026-11-22

### Added

- Domain layer: blocks, chunks, voxel world and terrain generation.
- Application layer: game loop, chunk meshing and streaming, and the ports the game needs from the outside world.
- Raylib renderer adapter and desktop host with a fly camera (WASD, mouse look, Esc to quit).
- Continuous integration, CodeQL and Dependabot workflows.
- README, architecture docs, contributing guide, security policy and agent instructions.

[Unreleased]: https://github.com/jt-simon/Nerdcraft/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/jt-simon/Nerdcraft/releases/tag/v0.1.0
