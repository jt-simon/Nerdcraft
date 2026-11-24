# Architecture

Nerdcraft uses clean architecture so the renderer can be swapped (raylib-cs today; MonoGame, Godot, or a
custom renderer later) without touching game code.

## Layers

Dependencies point inward; project references enforce it.

```
Nerdcraft.Desktop            composition root (the only place a renderer or a store meets the game)
  -> Nerdcraft.Rendering.Raylib   adapter: IRenderer + input + window for raylib
  -> Nerdcraft.Persistence.Sqlite adapter: IChunkStore on a SQLite world file (the only project that uses SQLite or zstd)
  -> Nerdcraft.Application        use cases and ports
       -> Nerdcraft.Domain        blocks, chunks, world, terrain generation (no dependencies)
```

- **Domain**: `VoxelWorld`, `Chunk`, `BlockId`, `WorldBounds`, `ITerrainGenerator`, and the player movement
  rules (`PlayerMovement`, `VoxelCollision`), and block targeting (`VoxelRaycast`). Pure C#, no I/O.
- **Application**: `NerdcraftGame` (the host calls `Update` then `Render` each frame), `IRenderer` (the
  port every graphics backend implements), meshing, chunk streaming, `LocalPlayer`, `InputState`.
- **Pause menu**: pause state, button layout (`PauseMenu`) and hit-testing live in Application; a renderer only draws the
  `PauseMenuView` it is handed. The adapter owns window concerns: capturing the cursor and F11 maximize.
- **Rendering.Raylib**: implements `IRenderer`, reads raylib input into `InputState`.

Math uses `System.Numerics`. The core produces plain `MeshData` (positions, normals, texture coordinates, colors, indices) and
its own `Color`, so no engine types leak inward.

Textures: blocks name their textures (`BlockPalette.GetTexture`, one name per face) and never point at files or atlas
slots. `TextureAtlas` (Application) lays the names out as a vertical strip of square tiles, with slot 0 reserved for a
magenta "missing" tile. The mesher writes UVs from that layout and the renderer builds the image from it, so they cannot
disagree. A texture pack is a folder of `textures/<name>.png`; the default one is `assets/texturepacks/default`, copied next
to the Desktop executable. The renderer reads the pack once at startup and draws a missing or unreadable file as magenta.
Vertex colors only tint (white, with alpha for water) and carry the baked shade and ambient occlusion. Artwork is CC BY-SA 4.0;
code is AGPL-3.0-or-later.

Each chunk meshes to a `ChunkMesh`: an opaque `MeshData`, a translucent one (water), and a `ChunkVisibility`
saying which pairs of the chunk's faces are joined by open space (computed on the worker, by flood fill).
`ChunkStreamer.Draw` walks outward from the camera's chunk through those connections, inside the frustum, so chunks
hidden behind solid terrain are skipped; without a camera chunk it falls back to the frustum alone. It
submits every visible opaque mesh first with `IRenderer.DrawMesh`, then the translucent meshes from farthest to
nearest with `IRenderer.DrawTranslucentMesh`, which blends and does not write depth. Which blocks are
translucent is a presentation choice, so it lives in `BlockPalette`, not in the domain.

Targeting is decided in Application (`LocalPlayer.Target` casts from the eye through `VoxelRaycast`, skipping water, within
5 blocks). The renderer only draws what it is handed: `IRenderer.DrawBoxOutline` for the targeted block and the
`HudView` (crosshair and hotbar) given to `EndFrame`.

The host supplies simulation and elapsed UI deltas to `NerdcraftGame.Update`. The Desktop host caps the
simulation delta to limit gameplay steps after a hitch and passes uncapped frame time as the UI delta.
Application advances HUD timers that continue during pause using the UI delta and computes label visibility
and opacity. Renderers draw the resulting `HudView`.

## Block interaction

Breaking and placing follow the same shape as movement, so a server can reuse the rules. `BlockAction` is a small value
(break or place, a block position, a hotbar slot) and `BlockInteraction.Apply` checks it against the world, an `Inventory`, the
eye position (reach) and the boxes of entities a new block must not overlap. It checks everything before changing anything and
returns a `BlockActionResult`, so a refusal carries its reason. Rules: bedrock, air and water cannot be broken; a block may
replace air or water; it needs a solid neighbor; it cannot overlap an entity.

`Inventory` (a 9-slot hotbar, stacks of 64) is per-player Domain state. `BlockInteractor` in Application owns the local
player's inventory, reads `InputState`, builds the action from `LocalPlayer.Target()` and applies it; sending the action to a
server instead would change only that spot. The hotbar is laid out by `Hud.Layout` (like `PauseMenu`) and a renderer draws the
`HudView` it is handed. `VoxelWorld.SetBlock` marks the chunk and its neighbors dirty, so `ChunkStreamer` remeshes them, and
marks the edited chunk as modified so it is saved (see Persistence).

## Player movement

The rules live in Domain so a future server can run exactly the code a client does: `PlayerMovement.Tick` takes a
`PlayerState` and a `PlayerCommand` and returns the next `PlayerState`. It reads no device, clock or renderer, and
`PlayerAbilities` (fly, noclip) are checked inside it, so an authority can refuse what a client asks for.

- **Fixed 20 Hz tick.** Physics steps in whole ticks (`PlayerMovement.TicksPerSecond`), never by frame time, so two
  machines given the same commands agree. `LocalPlayer` accumulates frame time, runs up to five ticks per frame, and
  interpolates the eye between the last two ticks. Look (yaw, pitch) is applied every frame, not per tick.
- **`PlayerCommand` is the only thing input hands to the simulation:** move axes, held jump and sneak, one-tick
  sprint and toggle requests, and *absolute* yaw and pitch. It is a small value type that can be serialized later.
  Adapters only report keys; `LocalPlayer` turns them into commands (it detects the double-tap on forward that starts a sprint).
- **Collision** (`VoxelCollision`) clips a box against solid blocks one axis at a time (Y, X, Z). Space inside the world's
  vertical bounds whose chunk is not loaded counts as solid, so the player can't walk or fall into terrain that has not
  streamed in; `Tick` also holds still while the chunk under the feet is missing. Solidity is a rule in `BlockTypes`.
- Floating-point results may differ slightly between machines. That is fine for prediction, because the server's
  state wins when the two disagree.

## Threading model (target: 60 FPS on old CPUs and integrated GPUs)

- **The main thread owns the GPU.** Every `IRenderer` call happens inside `NerdcraftGame.Render`, on the
  thread that calls it. Background workers never touch the renderer.
- **CPU-heavy work runs on workers.** `ChunkPipeline` generates terrain and meshes chunks on up to 4 tasks
  fed by a bounded `Channel`. Terrain is generated a whole column (every chunk row at one X and Z) per job,
  so the heightmap is computed once per column. Admission also limits outstanding generation rows and meshes,
  including completed results waiting on the main thread. Shutdown stops workers before returning buffers and
  snapshots left in the channels.
- **Workers never see live chunk data.** Meshing runs on a pooled `ChunkSnapshot` (the chunk plus a
  one-block border from all 26 neighbors, so ambient occlusion works across chunk edges and corners).
  Each chunk has a `Version`. World-owned chunks are exposed through read-only views, and the world controls
  invalidation when chunks are loaded, unloaded, or edited. A mesh built from an outdated version is discarded
  and rebuilt. A dirty chunk can still draw its previous mesh, with open visibility for the culling walk.
- **Per-frame streaming work is bounded.** Each frame processes up to `GameSettings.MaxChunkInstallsPerFrame`
  generated results, starts up to `MaxMeshRequestsPerFrame` mesh requests, and makes up to `MaxUploadsPerFrame`
  native mesh uploads. The release limit is twice the upload budget. The result-processing limit is the sum of
  the mesh request and upload budgets, including stale and empty results. A mixed chunk may stage an opaque
  upload across frames and publishes both materials together.
  The streamer releases superseded staged handles on the render thread and disposes staged CPU results at shutdown.
  The Desktop host disposes the renderer and window on the graphics thread before awaiting worker shutdown.
- `async`/`await` is used for worker loops and shutdown only; the per-frame path is synchronous.

`GameSettings.RenderDistance` is the main knob for weak hardware.

For performance captures, subscribe to the `Nerdcraft.Streaming`, `Nerdcraft.Raylib` and `Nerdcraft.Persistence` .NET meters
(the last reports the writer's queue depth, batch size and commit time, and the time to read a column). Frame duration
is measured between consecutive `Update` calls and includes presentation in the Desktop loop. The upload phase
includes release processing, which also has its own duration and call-count metrics. Raylib reports uploaded bytes
per upload call. Queue metrics cover results awaiting consumption.

For integrated-GPU comparisons, use a Release desktop build and record the GPU, driver, CPU, resolution, seed,
render distance, and presentation settings. Repeat the same settled view with the overlay off, turn over water,
and flight into new terrain before and after each change. Report cold and warm results separately, including median,
p95, and p99 frame times and the share of frames over 16.67 ms. Capture CPU phase times, upload bytes, draw and release
counts, result queue depth and age, and GPU time when available.

## Persistence

`IChunkStore` (Application) is the port: `ReadColumn` for a column of chunks, `Save` for an edited chunk and `SaveGeneratedAsync`
for fresh terrain. `Nerdcraft.Persistence.Sqlite` implements it; `SqliteWorld.OpenAsync` is called only from `Program.cs`, and the
host passes `world.Chunks(0)` (dimension 0) to `NerdcraftGame.Create`. A server host would wire the same pipeline and store, and
clients would never open the file.

- **Every generated chunk is stored.** `ChunkPipeline` reads a column from the store on its worker, generates only the rows
  that are missing and writes them with `SaveGeneratedAsync`, which never replaces a chunk that is already stored or waiting to be
  (a late worker cannot overwrite an edit). Stored worlds therefore stay as they were when terrain generation changes, and a
  server never regenerates what it already made. The block ids in `BlockTypes` are part of the file format: never renumber them.
- **Edits are saved on the main thread, cheaply.** `VoxelWorld` records which chunks were modified. `ChunkStreamer` saves them
  when they unload, and `NerdcraftGame` saves all of them every `GameSettings.AutosaveSeconds` (real time, so also while paused)
  and in `DisposeAsync`. `Save` only copies the blocks and queues them; it never waits for the disk.
- **One writer, many readers.** `SqliteChunkStore` has a single writer task that commits queued chunks in batches of up to 512
  per transaction, and encodes them there. Until an edit's `Save` is committed it also sits in an in-memory overlay that reads
  check first, so a read always sees the latest save. Overlay arrays are never modified once published, so readers copy them
  outside the lock and the main thread's `Save` never waits behind a worker's read. Reads use pooled read-only connections,
  which WAL mode lets run beside the writer. A writer failure is rethrown from the next save, read or dispose, like a pipeline
  worker's, and wakes any worker waiting for room.
- **Backpressure and cancellation.** Microsoft.Data.Sqlite's async methods run synchronously, so SQLite calls stay synchronous on
  background threads and `async` is used where something really waits. `SaveGeneratedAsync` waits (on a `SemaphoreSlim`, so no
  thread is blocked) when 1024 generated chunks, about 8 MB, are already queued; workers therefore generate no faster than the
  disk can take, and a worker stuck waiting is released by the pipeline's cancellation token at shutdown. Edits are not
  limited: they come from the player and must never block the main thread. Tokens reach `ReadColumn`, `SaveGeneratedAsync`,
  the backup and `SqliteWorld.OpenAsync`; they are checked between steps, not inside a running statement. `Save` takes none (it
  only enqueues), `ITerrainGenerator` takes none (pure CPU, checked between rows by the pipeline), and `DisposeAsync` cannot,
  because cancelling the writer's last commits would lose saves.
- **Shutdown order:** dispose the game first (it saves its edits and stops the workers), then the `SqliteWorld` (it commits the
  queue, gives a running backup 2 seconds to finish and then abandons it, empties the log and marks the shutdown clean).

The world directory holds `world.db`, `backups/` and `session.lock`. The lock is held while the world is open, so a second
process fails to open it instead of corrupting it. `world.db` has two tables:

- `world_meta`: seed, height range, generator name and version, creation and backup times, and a `clean_shutdown` flag, so a world
  reopens with its own seed and bounds. `PRAGMA application_id` marks the file and `user_version` is the schema version;
  a file from a newer version, or one that is not a world, is refused untouched.
- `chunks (key INTEGER PRIMARY KEY, data BLOB)`: a rowid table, not `WITHOUT ROWID`, because compressed chunks are often larger
  than the roughly 200 bytes a `WITHOUT ROWID` row suits. The key packs dimension (8 bits), X (22), Z (22) and Y (12) in that
  order (`ChunkKey`), so a whole column is one key range and `ReadColumn` is one range scan. Coordinates outside about
  +/-2 million chunks horizontally or +/-2048 chunks vertically throw.
- Each blob (`ChunkCodec`) is a format byte, a compression byte and a palette of the block ids present, then a bit-packed index per
  block, compressed with zstd when that is smaller. A chunk of one block type is a few bytes.

Upkeep is automatic (`SqliteMaintenance`, on the writer or in the background, timed by an injected `TimeProvider`):

- After an unclean shutdown the next open runs `PRAGMA quick_check` and refuses a damaged database, naming the newest backup.
- When the last backup is 24 hours old, `VACUUM INTO` copies the database in the background while the game keeps writing.
  It writes to a `.tmp` name and renames when done, so an abandoned backup leaves no partial file (the next backup deletes the
  leftover). The newest three copies are kept, and the time is recorded only on success, so an abandoned one is retried next launch.
- When the writer has been idle for 2 seconds, at most every 5 minutes, it moves the log into the database without blocking and
  returns a bounded number of free pages to the file system (`auto_vacuum = INCREMENTAL`).
- On dispose it returns free pages, empties the log (`wal_checkpoint(TRUNCATE)`) and marks the shutdown clean, unless a column
  read failed (unreadable blob or damaged page), in which case it stays unclean so the next open runs the integrity check. A read
  failure is never papered over by regenerating the chunk, since that would discard the player's edits; it is reported as an
  `InvalidDataException` (or `IOException` for other database errors) naming the chunk or column. `Program.cs` reports any
  failure of the game loop or of shutdown on the console and in `crash.log`, then still saves and closes the world.

## Third-party code

Terrain noise comes from [FastNoiseLite](https://github.com/Auburn/FastNoiseLite), vendored as a single
`internal` file under `Nerdcraft.Domain/World/Generation/ThirdParty/` rather than added as a package: it is pure
math, so Domain keeps no framework or package dependencies, and the pinned version means a library update can
never silently change what a seed generates. The file records its upstream release and local edits in its header,
its license is in `THIRD-PARTY-NOTICES.md`, and a nested `.editorconfig` exempts only that file from the repo's
style rules. `Generate_FixedSeedRegion_MatchesRecordedHash` pins the generated terrain.

## API documentation

Every public type and member under `src/` carries XML doc comments. `src/Directory.Build.props` turns on
`GenerateDocumentationFile` and warnings are errors, so a missing comment (CS1591) fails the build. The
generated `*.xml` files land next to each assembly in `bin/` for a doc generator such as DocFX to consume.
