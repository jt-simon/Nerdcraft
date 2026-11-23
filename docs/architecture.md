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

Each chunk meshes to a `ChunkMesh`: an opaque `MeshData` and a translucent one (water). `ChunkStreamer.Draw`
submits every opaque mesh first with `IRenderer.DrawMesh`, then the translucent meshes from farthest to
nearest with `IRenderer.DrawTranslucentMesh`, which blends and does not write depth. Which blocks are
translucent is a presentation choice, so it lives in `BlockPalette`, not in the domain.

## Threading model (target: 60 FPS on old CPUs and integrated GPUs)

- **The main thread owns the GPU.** Every `IRenderer` call happens inside `NerdcraftGame.Render`, on the
  thread that calls it. Background workers never touch the renderer.
- **CPU-heavy work runs on workers.** `ChunkPipeline` generates terrain and meshes chunks on up to 4 tasks
  fed by a bounded `Channel`. Terrain is generated a whole column (every chunk row at one X and Z) per job,
  so the heightmap is computed once per column rather than once per chunk. Results return on channels and are drained by the main thread.
- **Workers never see live chunk data.** Meshing runs on a pooled `ChunkSnapshot` (the chunk plus a
  one-block border from all 26 neighbors, so ambient occlusion works across chunk edges and corners).
  Each chunk has a `Version`; a mesh built from an outdated version is discarded and rebuilt.
- **Per-frame work is bounded.** Each frame installs at most `GameSettings.MaxChunkInstallsPerFrame` generated
  chunks, starts at most `MaxMeshRequestsPerFrame` mesh requests and uploads at most `MaxUploadsPerFrame` meshes;
  the rest wait for the next frame. Chunks outside the camera frustum aren't drawn, and buffers are pooled to keep
  the GC quiet.
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
