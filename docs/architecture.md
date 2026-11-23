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

- **Domain**: `VoxelWorld`, `Chunk`, `BlockId`, `WorldBounds`, `ITerrainGenerator`. Pure C#, no I/O.
- **Application**: `NerdcraftGame` (the host calls `Update` then `Render` each frame), `IRenderer` (the
  port every graphics backend implements), meshing, chunk streaming, camera, `InputState`.
- **Rendering.Raylib**: implements `IRenderer`, reads raylib input into `InputState`.

Math uses `System.Numerics`. The core produces plain `MeshData` (positions, normals, colors, indices) and
its own `Color`, so no engine types leak inward.

## Threading model (target: 60 FPS on old CPUs and integrated GPUs)

- **The main thread owns the GPU.** Every `IRenderer` call happens inside `NerdcraftGame.Render`, on the
  thread that calls it. Background workers never touch the renderer.
- **CPU-heavy work runs on workers.** `ChunkPipeline` generates terrain and meshes chunks on up to 4 tasks
  fed by a bounded `Channel`. Results return on channels and are drained by the main thread.
- **Workers never see live chunk data.** Meshing runs on a pooled `ChunkSnapshot` (the chunk plus a
  one-block border from its neighbors). Each chunk has a `Version`; a mesh built from an outdated
  version is discarded and rebuilt.
- **Per-frame work is bounded.** At most `GameSettings.MaxUploadsPerFrame` meshes are uploaded per frame,
  chunks outside the camera frustum aren't drawn, and buffers are pooled to keep the GC quiet.
- `async`/`await` is used for worker loops and shutdown only; the per-frame path is synchronous.

`GameSettings.RenderDistance` is the main knob for weak hardware.

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
