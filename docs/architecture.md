# Architecture

Nerdcraft uses clean architecture so the renderer can be swapped (raylib-cs today; MonoGame, Godot, or a
custom renderer later) without touching game code.

## Layers

Dependencies point inward; project references enforce it.

```
Nerdcraft.Desktop            composition root (the only place a renderer meets the game)
  -> Nerdcraft.Rendering.Raylib   adapter: IRenderer + input + window for raylib
  -> Nerdcraft.Application        use cases and ports
       -> Nerdcraft.Domain        blocks, chunks, world, terrain generation (no dependencies)
```

- **Domain**: `VoxelWorld`, `Chunk`, `BlockId`, `WorldBounds`, `ITerrainGenerator`, and the player movement
  rules (`PlayerMovement`, `VoxelCollision`). Pure C#, no I/O.
- **Application**: `NerdcraftGame` (the host calls `Update` then `Render` each frame), `IRenderer` (the
  port every graphics backend implements), meshing, chunk streaming, `LocalPlayer`, `InputState`.
- **Rendering.Raylib**: implements `IRenderer`, reads raylib input into `InputState`.

Math uses `System.Numerics`. The core produces plain `MeshData` (positions, normals, colors, indices) and
its own `Color`, so no engine types leak inward.

Each chunk meshes to a `ChunkMesh`: an opaque `MeshData`, a translucent one (water), and a `ChunkVisibility`
saying which pairs of the chunk's faces are joined by open space (computed on the worker, by flood fill).
`ChunkStreamer.Draw` walks outward from the camera's chunk through those connections, inside the frustum, so chunks
hidden behind solid terrain are skipped; without a camera chunk it falls back to the frustum alone. It
submits every visible opaque mesh first with `IRenderer.DrawMesh`, then the translucent meshes from farthest to
nearest with `IRenderer.DrawTranslucentMesh`, which blends and does not write depth. Which blocks are
translucent is a presentation choice, so it lives in `BlockPalette`, not in the domain.

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

For performance captures, subscribe to the `Nerdcraft.Streaming` and `Nerdcraft.Raylib` .NET meters. Frame duration
is measured between consecutive `Update` calls and includes presentation in the Desktop loop. The upload phase
includes release processing, which also has its own duration and call-count metrics. Raylib reports uploaded bytes
per upload call. Queue metrics cover results awaiting consumption.

For integrated-GPU comparisons, use a Release desktop build and record the GPU, driver, CPU, resolution, seed,
render distance, and presentation settings. Repeat the same settled view with the overlay off, turn over water,
and flight into new terrain before and after each change. Report cold and warm results separately, including median,
p95, and p99 frame times and the share of frames over 16.67 ms. Capture CPU phase times, upload bytes, draw and release
counts, result queue depth and age, and GPU time when available.

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
