# Changelog

All notable changes to Nerdcraft are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the version is 0.x, a minor bump may
contain breaking changes.

## [Unreleased]

### Added

- Distance fog fades terrain and water into the sky. Lower render distances bring the fade closer.
- Textured blocks. Every block face is drawn with a 16 by 16 texture from the new default texture pack in `assets/texturepacks/default` (grass has its own top, side and bottom), and the hotbar shows the block's texture instead of a flat color. A texture pack is a folder of `textures/<name>.png`; a missing or unreadable file shows as magenta and is reported on standard error. Lighting and ambient occlusion are unchanged.
- `TextureAtlas`, `UvRect`, `BlockPalette.GetTexture`, `BlockPalette.TextureNames` and `BlockPalette.Atlas` (Application): texture names and the atlas layout shared by the mesher and the renderer. `MeshData.TexCoords`; `MeshData.CopyFrom` takes texture coordinates.
- Licensing: artwork, textures, documentation and other non-code text are licensed under Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0), see `LICENSE-CC-BY-SA-4.0.txt`. Code stays AGPL-3.0-or-later.
- The world is saved. Every generated chunk and every edit is stored in a SQLite database (WAL mode, one compressed row per chunk, keyed by dimension, X, Z and Y), so edits survive chunk unloading and restarts, and the same terrain and edits come back on the next launch. It lives in `~/.local/share/Nerdcraft/worlds/default/world.db` on Linux (`%LOCALAPPDATA%` on Windows) and keeps the seed and height range it was created with. Edited chunks are saved every 5 seconds (`GameSettings.AutosaveSeconds`), when they unload and on quit; after a crash at most the last few seconds are lost. The player's position and hotbar are not saved yet.
- Automatic upkeep: a daily backup in `backups/` (newest three kept), an integrity check after an unclean shutdown, and trimming of the write-ahead log and free pages. A second copy of the game cannot open the same world.
- Saving never slows the game down: edits are queued for a single writer, generated terrain waits for the disk when too much is queued (so memory stays bounded), a backup still running when you quit is abandoned after 2 seconds and retried next launch, and Ctrl+C in the terminal quits and saves like Esc > Quit does (or cancels the open if the world is still opening). If the game stops because of an error (for example an unreadable chunk), it prints a short message naming the problem, writes the details to `crash.log` in the world folder, saves what it can and exits with code 1. After a failed read the world is left marked unclean, so the next launch checks the database. New `Nerdcraft.Persistence` meter for the writer's queue depth, batch size and commit time.
- `IChunkStore` (Application), the port for chunk storage, and `Nerdcraft.Persistence.Sqlite` (`SqliteWorld.OpenAsync`), its adapter. New packages: `Microsoft.Data.Sqlite` and `ZstdSharp.Port`.
- `VoxelWorld.ModifiedChunks` and `VoxelWorld.MarkSaved` (Domain) track which chunks changed since they were last stored. `NerdcraftGame.Create` and the constructor take an optional `IChunkStore`; `ChunkPipeline` and `ChunkStreamer` take one too.
- Hotbar hover text: when the selected slot changes (keys 1–9 or mouse wheel), the block's name appears above the slot for 3 seconds, fading out over the last second. Empty slots show no text.
- `BlockPalette.GetDisplayName` maps block types to display names for the hover text.
- `NerdcraftGame.Update(input, deltaSeconds, uiDeltaSeconds)`: HUD timers run on the uncapped frame time, so they keep running while paused or when the host clamps the simulation step. The two-argument overload uses one delta for both. `Hud.ComputeHoverOpacity` and `Hud.HoverDuration` compute the fade.
- Block breaking and placing. Left button breaks the targeted block into a 9-slot hotbar; right button places the selected block against the targeted face; held buttons repeat every 0.25 s. Keys 1-9 and the mouse wheel select a slot. Bedrock cannot be broken, water is replaced when placed into, and a block cannot be placed in mid-air or inside the player. Breaking is instant, and a block broken with a full hotbar is lost.
- A hotbar HUD (`Hud`, `HudView`, `HotbarSlotView`), drawn over the scene and kept visible while paused.
- `BlockInteraction.Apply`, `BlockAction` and `BlockActionResult` (Domain): the break and place rules as a pure function over a serializable request, so a server can run them and say why it refused. `Inventory` and `ItemStack` (Domain), `BlockInteractor` (Application), `BlockTypes.IsBreakable` and `IsReplaceable`.
- `InputState.Break`, `Place`, `SelectSlot` and `HotbarScroll`.
- A crosshair, and an outline on the solid block the player is looking at within 5 blocks (`LocalPlayer.Target()`, `LocalPlayer.ReachBlocks`). Water is looked through, not targeted.
- `VoxelRaycast` and `RaycastHit` (Domain), a block raycast that returns the hit block and the face it was entered through.
- `IRenderer.DrawBoxOutline`.
- Esc opens a pause menu with Resume and Quit buttons and releases the mouse; the player stands still while it is open. F11 toggles maximize.
- `PauseMenu`, `PauseMenuView`, `MenuButton` and `UiRect` (Application), and `NerdcraftGame.IsPaused` / `ExitRequested`.
- `InputState.TogglePause`, `Pointer`, `Click` and `ScreenSize`.
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

- `HotbarSlotView.Swatch` (a color) is now `Icon` (a texture name), `BlockPalette.GetColor` returns a tint (white, or white with alpha for water) instead of the block's color, and `RaylibRenderer` takes the texture pack folder in its constructor.
- `HudView` has two new members, `HoverText` and `HoverOpacity`, and `Hud.Layout` takes optional hover arguments. Custom renderers must draw the hover text above the selected slot at that opacity.
- `IRenderer.EndFrame` takes a `HudView?` instead of the `crosshair` flag; the crosshair is `HudView.Crosshair`. `LocalPlayer.ReachBlocks` now equals `BlockInteraction.ReachBlocks`.
- Esc no longer quits the game directly; use the pause menu's Quit.
- `IRenderer.EndFrame` now takes a `PauseMenuView?` to draw the pause menu.
- `IRenderer.EndFrame` takes a leading `crosshair` flag. Custom renderers must draw a centered crosshair when it is set.
- `ChunkPipeline`'s constructor takes the `WorldBounds`, which size its outstanding-generation limit.
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
