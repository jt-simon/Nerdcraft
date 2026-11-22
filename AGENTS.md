# AGENTS.md

Rules for coding agents in this repo. Humans: see [README.md](README.md).
Nerdcraft is a voxel game in C# / .NET 10 (prototype stage). Clean architecture keeps the renderer
swappable (raylib today). Read the **Hard rules** and **Before you finish** sections every time.

## Hard rules

1. **Dependencies point inward only:** Desktop -> Rendering.* -> Application -> Domain. Never add a
   `ProjectReference` or package that breaks this. A test checks it.
2. **Engine types stay in `Nerdcraft.Rendering.Raylib`.** No `using Raylib_cs` anywhere else. The core uses
   `System.Numerics`, `MeshData` and its own `Color`.
3. **Domain is pure:** no I/O, `Task`/threads/`lock`, `DateTime.Now`, unseeded `Random`, or engine types.
4. **Never weaken a check to make it pass.** Do not delete, skip or loosen a test (including
   `DependencyTests`), add `#pragma warning disable`, `[SuppressMessage]` or `!`, or edit
   `.editorconfig` / `Directory.*.props` / `.github/workflows`. Fix the code instead.
5. **No new package, project or reference** without asking the user first.
6. **Every public member in `src/` has an accurate XML doc comment.** When you change behavior (parameters,
   return value, exceptions, threading, ownership, units), update its comment and any `<see cref>` /
   `<inheritdoc/>` dependents in the same change.
7. **Stay in scope.** No refactors, renames or reformatting outside the task. Smallest change that works.
8. **If a rule blocks the task, stop and ask.** Do not work around it.

## Commands

```bash
dotnet build Nerdcraft.slnx                              # zero warnings required (warnings are errors)
dotnet test --solution Nerdcraft.slnx                    # whole suite
dotnet test --solution Nerdcraft.slnx --filter-method "*Set_SameBlock*" --ignore-exit-code 8   # narrow run
dotnet format whitespace Nerdcraft.slnx                  # fixes formatting
dotnet format --verify-no-changes Nerdcraft.slnx         # must exit 0
dotnet run --project src/Nerdcraft.Desktop               # needs a display
```

`--ignore-exit-code 8` is needed on narrow runs because projects with no matching test otherwise fail.
`--filter-class` also exists. Run the narrow test first, then the full suite. CI (`.github/workflows/ci.yml`)
runs build, format check and tests, so all three must pass locally first.

## Where does this code go?

| You are adding... | It goes in |
| --- | --- |
| A game rule or world data (blocks, chunks, terrain) | `Nerdcraft.Domain` |
| Something the game needs from the outside world | A port interface in `Nerdcraft.Application` (like `IRenderer`, `IChunkWorkQueue`), implemented in an adapter project |
| Any call into raylib or another engine | `Nerdcraft.Rendering.*` only |
| Object construction and wiring | `Desktop/Program.cs` or `NerdcraftGame.Create` only |
| A test fake | `tests/Nerdcraft.Application.Tests/Support` |

Adapters translate, they don't decide. Layer and threading detail: [docs/architecture.md](docs/architecture.md).

## Workflow for any change

1. Find the closest existing file and copy its shape (naming, comments, structure).
2. Make the smallest change that solves the task.
3. Decide about tests with the checklist below. Bug fix: write the failing test first.
4. Build, then run the narrow test, then the full suite.
5. Update docs affected by the change (XML comments, `docs/architecture.md` if layers or threading changed).
6. Stop. Do not "improve" nearby code.

## Do I need a test?

Write one only if **all three** are yes: (1) can a realistic mistake make it fail? (2) do callers rely on
this behavior? (3) is it uncovered today?

- **Good:** `SetBlock_OnChunkBorder_BumpsNeighborVersion` (border logic can break),
  `UploadCompleted_ChunkReloadedAfterUnload_DiscardsMeshesFromTheEarlierLoad` (a real bug).
- **Skip:** asserting `BlockTypes.Stone.Value == 1`, `GameSettings` defaults, `BlockId.IsAir`, `Chunk.Size`,
  or that a fake recorded a call. These are constants, getters and wiring.
- Test public behavior. Use fakes (`FakeRenderer`, `InlineChunkWorkQueue`, `HalfSlabGenerator`,
  `TestWorlds`), never a mocking library. Copy the shape of `VoxelWorldTests` (Domain) and
  `ChunkStreamerTests` (streaming).
- One behavior per test, named `Member_Condition_Outcome`. If it overlaps an existing test, extend that one.
- No `Thread.Sleep`, `.Result` or `.Wait()`. For async waits copy `WaitUntilAsync` in `ChunkPipelineTests`.

## Gotchas

- Negative coordinates must floor: `BlockPosition.ChunkPosition` uses `>>`; never use `/` or `%`.
- `VoxelWorld.SetBlock` returns false when the chunk isn't loaded; only `LoadChunk` creates chunks.
- Arrays from `ArrayPool` must be returned; dispose `MeshData` and `ChunkSnapshot`.
- `System.Numerics` matrices are row-major; raylib needs a transpose. Y is up; yaw 0 looks toward -Z.
- `Chunk.Version` identifies one load of a chunk and keeps rising across unload and reload.
- Style is build-enforced: file-scoped namespaces, usings outside the namespace, braces on every block,
  explicit types instead of `var`, and every `.cs` file starting with `// SPDX-License-Identifier: AGPL-3.0-or-later`
  followed by a blank line (copy it from any existing file).

## Stop and ask the user when

- A change seems to need a new package, project or reference.
- You would change a port interface (`IRenderer`, `IChunkWorkQueue`, `IChunkMesher`, `ITerrainGenerator`)
  and the task did not clearly require it. If the task does require it, go ahead, but in the same change
  update every implementation and fake (for `IRenderer`: `RaylibRenderer` and `FakeRenderer`), document the
  new member's contract, and name the signature change in your final summary.
- You would edit `Directory.*.props`, `.editorconfig`, `global.json`, `.github/` or `AGENTS.md`.
- Any hard rule seems to block the task.

## Before you finish

- [ ] `dotnet build Nerdcraft.slnx` has zero warnings.
- [ ] `dotnet test --solution Nerdcraft.slnx` passes.
- [ ] `dotnet format --verify-no-changes Nerdcraft.slnx` exits 0.
- [ ] No new package or project reference.
- [ ] Every new or changed public member has an accurate XML doc comment.
- [ ] No unrelated files changed and no test weakened.

## Recipes

**Add a renderer:** create `Nerdcraft.Rendering.<Engine>` referencing `Nerdcraft.Application`; implement
`IRenderer` and fill `InputState` from that engine's input; add a host that calls `NerdcraftGame.Update`
then `Render` each frame on the render thread. Domain and Application don't change.

**Add a block type:** add it to `BlockTypes` (Domain), give it a color in `BlockPalette` (Application). Change terrain
generation only if the task says where the block should appear; otherwise leave it out and say so in your
summary. No new test is needed for the constant.
