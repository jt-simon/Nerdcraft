// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;
using System.Numerics;

using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Ports;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Streaming;

/// <summary>
/// Keeps the chunks around the camera generated, meshed, and uploaded. Main thread only.
/// <see cref="Update"/> does CPU-side bookkeeping; every renderer call happens in
/// <see cref="UploadCompleted"/> and <see cref="Draw"/>.
/// </summary>
/// <param name="world">The world to fill with generated chunks.</param>
/// <param name="queue">Source of background generation and meshing.</param>
/// <param name="settings">Supplies render distance, world bounds and the per-frame budgets for uploads, chunk installs and mesh requests.</param>
public sealed class ChunkStreamer(VoxelWorld world, IChunkWorkQueue queue, GameSettings settings)
{
    // Chunk neighbor offsets in ChunkFace order; face ^ 1 is the opposite direction.
    private static readonly (int Dx, int Dy, int Dz)[] FaceDirections = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    private readonly Dictionary<ChunkPosition, Entry> _entries = [];
    private readonly List<MeshHandle> _pendingRelease = [];
    private readonly List<TranslucentDraw> _translucentScratch = [];
    private readonly List<(ChunkPosition Position, Entry Entry)> _visibleScratch = [];
    private readonly Queue<WalkStep> _walkQueue = new();
    private int _drawFrame;
    private readonly List<ChunkPosition> _unloadScratch = [];
    private readonly (int Dx, int Dz)[] _loadOffsets = BuildLoadOffsets(settings.RenderDistance);
    private (int X, int Z)? _lastCameraChunk;

    // The scan in RequestWork only has new work to find after one of these changes: the camera crossing a chunk border,
    // the world changing (Revision), or a mesh request finishing or being refused. Otherwise it is skipped.
    private int _scannedRevision = -1;
    private bool _rescan = true;

    /// <summary>
    /// Unloads chunks that left range, installs finished terrain, and requests new work around the camera. Makes no
    /// renderer calls. Installs at most <see cref="GameSettings.MaxChunkInstallsPerFrame"/> chunks and starts at most
    /// <see cref="GameSettings.MaxMeshRequestsPerFrame"/> mesh requests per call, so a burst of finished work spreads
    /// over several frames.
    /// </summary>
    /// <param name="cameraPosition">Camera position in world space.</param>
    public void Update(Vector3 cameraPosition)
    {
        int cameraX = FloorToChunk(cameraPosition.X);
        int cameraZ = FloorToChunk(cameraPosition.Z);

        bool cameraMoved = _lastCameraChunk != (cameraX, cameraZ);
        if (cameraMoved)
        {
            _lastCameraChunk = (cameraX, cameraZ);
            UnloadDistantChunks(cameraX, cameraZ);
        }

        InstallGeneratedChunks();
        if (cameraMoved || _rescan || world.Revision != _scannedRevision)
        {
            _scannedRevision = world.Revision;
            _rescan = !RequestWork(cameraX, cameraZ);
        }
    }

    /// <summary>Releases meshes that were replaced or unloaded, then uploads up to <see cref="GameSettings.MaxUploadsPerFrame"/> finished meshes. Meshes built from outdated chunk contents are discarded.</summary>
    public void UploadCompleted(IRenderer renderer)
    {
        foreach (MeshHandle handle in _pendingRelease)
        {
            renderer.ReleaseMesh(handle);
        }

        _pendingRelease.Clear();

        int uploads = 0;
        while (uploads < settings.MaxUploadsPerFrame && queue.TryTakeMeshed(out MeshedChunk result))
        {
            using ChunkMesh mesh = result.Mesh;
            if (!_entries.TryGetValue(result.Position, out Entry? entry)
                || !world.TryGetChunk(result.Position, out Chunk chunk))
            {
                continue;
            }

            if (entry.MeshRequestedVersion != result.Version)
            {
                continue; // Built for an earlier load of this chunk; not the mesh we are waiting for.
            }

            entry.MeshRequestedVersion = null;
            _rescan = true; // An edit made while this mesh was in flight was skipped by the scan; scan again to request the new mesh.
            if (chunk.Version != result.Version)
            {
                continue; // Blocks changed while meshing; the next scan requests a fresh mesh.
            }

            if (entry.Handle is { } old)
            {
                renderer.ReleaseMesh(old);
                entry.Handle = null;
                entry.VertexCount = 0;
            }

            if (entry.TranslucentHandle is { } oldTranslucent)
            {
                renderer.ReleaseMesh(oldTranslucent);
                entry.TranslucentHandle = null;
                entry.TranslucentVertexCount = 0;
            }

            entry.MeshedVersion = result.Version;
            entry.Visibility = mesh.Visibility; // Even for a chunk with no faces: solid rock still hides what is behind it.
            if (mesh.Opaque.VertexCount > 0)
            {
                entry.Handle = renderer.UploadMesh(mesh.Opaque);
                entry.VertexCount = mesh.Opaque.VertexCount;
            }

            if (mesh.Translucent.VertexCount > 0)
            {
                entry.TranslucentHandle = renderer.UploadMesh(mesh.Translucent);
                entry.TranslucentVertexCount = mesh.Translucent.VertexCount;
            }

            if (entry.Handle is not null || entry.TranslucentHandle is not null)
            {
                uploads++;
            }
        }
    }

    /// <summary>
    /// Draws the uploaded chunk meshes that intersect <paramref name="frustum"/> and are not hidden behind solid
    /// terrain, translated to their world positions: all opaque meshes first, then the translucent ones from the
    /// farthest chunk to the nearest. Chunks are reached by walking outward from the camera's chunk through the
    /// faces each chunk's <see cref="ChunkVisibility"/> connects, so a chunk behind solid rock is skipped. When
    /// the camera is not inside a loaded chunk, only the frustum decides.
    /// </summary>
    /// <param name="renderer">Receives the draw calls.</param>
    /// <param name="frustum">The view volume; chunks outside it are skipped.</param>
    /// <param name="cameraPosition">Camera position in world space; starts the visibility walk and orders the translucent meshes.</param>
    /// <returns>How many chunks and vertices were drawn, and how many chunks hold a mesh.</returns>
    public DrawStats Draw(IRenderer renderer, Frustum frustum, Vector3 cameraPosition)
    {
        _translucentScratch.Clear();
        _visibleScratch.Clear();
        int meshed = 0;
        foreach (Entry entry in _entries.Values)
        {
            if (entry.HasMesh)
            {
                meshed++;
            }
        }

        ChunkPosition cameraChunk = new(FloorToChunk(cameraPosition.X), FloorToChunk(cameraPosition.Y), FloorToChunk(cameraPosition.Z));
        if (_entries.TryGetValue(cameraChunk, out Entry? start))
        {
            CollectVisible(frustum, cameraChunk, start);
        }
        else
        {
            CollectInFrustum(frustum);
        }

        long vertices = 0;
        foreach ((ChunkPosition position, Entry entry) in _visibleScratch)
        {
            Vector3 min = ChunkMin(position);
            vertices += entry.VertexCount + entry.TranslucentVertexCount;

            if (entry.Handle is { } handle)
            {
                renderer.DrawMesh(handle, Matrix4x4.CreateTranslation(min));
            }

            if (entry.TranslucentHandle is { } translucent)
            {
                float distance = Vector3.DistanceSquared(min + new Vector3(Chunk.Size / 2f), cameraPosition);
                _translucentScratch.Add(new TranslucentDraw(translucent, min, distance));
            }
        }

        _translucentScratch.Sort(static (a, b) => b.DistanceSquared.CompareTo(a.DistanceSquared));
        foreach (TranslucentDraw draw in _translucentScratch)
        {
            renderer.DrawTranslucentMesh(draw.Handle, Matrix4x4.CreateTranslation(draw.Min));
        }

        return new DrawStats(_visibleScratch.Count, meshed, vertices);
    }

    private static Vector3 ChunkMin(ChunkPosition position) => new(position.X * Chunk.Size, position.Y * Chunk.Size, position.Z * Chunk.Size);

    // Fallback without a starting chunk: every meshed chunk inside the frustum.
    private void CollectInFrustum(Frustum frustum)
    {
        foreach ((ChunkPosition position, Entry entry) in _entries)
        {
            Vector3 min = ChunkMin(position);
            if (entry.HasMesh && frustum.Intersects(min, min + new Vector3(Chunk.Size)))
            {
                _visibleScratch.Add((position, entry));
            }
        }
    }

    // Breadth-first walk from the camera's chunk, so near chunks come first. A step leaves a chunk through a face only
    // if open space joins that face to the one it was entered through. Each chunk is entered at most once per face
    // per frame, which keeps the walk bounded without letting a dead-end path hide a chunk another path reaches.
    private void CollectVisible(Frustum frustum, ChunkPosition cameraChunk, Entry start)
    {
        _drawFrame++;
        _walkQueue.Clear();

        // The walk always starts here, but the camera's own chunk is drawn only if it is in view like any other.
        Vector3 startMin = ChunkMin(cameraChunk);
        Visit(cameraChunk, start, -1, frustum.Intersects(startMin, startMin + new Vector3(Chunk.Size)));

        while (_walkQueue.TryDequeue(out WalkStep step))
        {
            for (int exit = 0; exit < FaceDirections.Length; exit++)
            {
                if (exit == step.EnteredFace
                    || (step.EnteredFace >= 0 && !step.Entry.Visibility.Connects((ChunkFace)step.EnteredFace, (ChunkFace)exit)))
                {
                    continue;
                }

                (int dx, int dy, int dz) = FaceDirections[exit];
                ChunkPosition next = step.Position.Offset(dx, dy, dz);
                int entered = exit ^ 1;
                if (!_entries.TryGetValue(next, out Entry? entry)
                    || (entry.VisitFrame == _drawFrame && (entry.VisitedFaces & (1 << entered)) != 0))
                {
                    continue;
                }

                Vector3 min = ChunkMin(next);
                if (frustum.Intersects(min, min + new Vector3(Chunk.Size)))
                {
                    Visit(next, entry, entered, inView: true);
                }
            }
        }
    }

    // Records that the walk reached the chunk through enteredFace (-1 for the camera's own chunk) and queues it.
    // The chunk is queued for drawing on its first visit of the frame, if it has a mesh and is in view.
    private void Visit(ChunkPosition position, Entry entry, int enteredFace, bool inView)
    {
        if (entry.VisitFrame != _drawFrame)
        {
            entry.VisitFrame = _drawFrame;
            entry.VisitedFaces = 0;
            if (inView && entry.HasMesh)
            {
                _visibleScratch.Add((position, entry));
            }
        }

        if (enteredFace >= 0)
        {
            entry.VisitedFaces |= 1 << enteredFace;
        }

        _walkQueue.Enqueue(new WalkStep(position, entry, enteredFace));
    }

    private void UnloadDistantChunks(int cameraX, int cameraZ)
    {
        // Unload farther out than we load, so a camera hovering at a boundary doesn't thrash.
        int unloadRadius = settings.RenderDistance + 1;
        foreach (ChunkPosition position in _entries.Keys)
        {
            if (DistanceSquared(position.X - cameraX, position.Z - cameraZ) > unloadRadius * unloadRadius)
            {
                _unloadScratch.Add(position);
            }
        }

        foreach (ChunkPosition position in _unloadScratch)
        {
            if (_entries.Remove(position, out Entry? entry))
            {
                ReleaseLater(entry);
            }

            world.UnloadChunk(position);
        }

        _unloadScratch.Clear();
    }

    private void InstallGeneratedChunks()
    {
        // Results beyond the budget stay queued for the next frame; stale ones are cheap and do not count.
        int installed = 0;
        while (installed < settings.MaxChunkInstallsPerFrame && queue.TryTakeGenerated(out GeneratedChunk result))
        {
            if (_entries.TryGetValue(result.Position, out Entry? entry) && !entry.Loaded)
            {
                world.LoadChunk(result.Position, result.Blocks.AsSpan(0, Chunk.Volume));
                entry.Loaded = true;
                installed++;
            }

            ArrayPool<BlockId>.Shared.Return(result.Blocks);
        }
    }

    // Walks columns nearest-first so the area around the camera fills in before the horizon. Returns false when the
    // queue refused a request or the per-frame mesh budget ran out, so the walk stopped early and must run again.
    private bool RequestWork(int cameraX, int cameraZ)
    {
        int meshRequests = 0;
        foreach ((int dx, int dz) in _loadOffsets)
        {
            for (int y = settings.Bounds.MinChunkY; y <= settings.Bounds.MaxChunkY; y++)
            {
                ChunkPosition position = new(cameraX + dx, y, cameraZ + dz);

                if (!_entries.TryGetValue(position, out Entry? entry))
                {
                    // A column is requested whole so its workers share the heightmap; every row gets an entry at once.
                    if (!queue.TryRequestGenerateColumn(position.X, position.Z, settings.Bounds.MinChunkY, settings.Bounds.MaxChunkY))
                    {
                        return false;
                    }

                    for (int row = settings.Bounds.MinChunkY; row <= settings.Bounds.MaxChunkY; row++)
                    {
                        _entries[position with { Y = row }] = new Entry();
                    }

                    continue;
                }

                if (!entry.Loaded || entry.MeshRequestedVersion is not null)
                {
                    continue;
                }

                if (SkipIfEmpty(position, entry))
                {
                    continue;
                }

                if (NeedsMesh(position, entry) && NeighborsSettled(position, cameraX, cameraZ))
                {
                    if (meshRequests == settings.MaxMeshRequestsPerFrame || !TryRequestMesh(position, entry))
                    {
                        return false; // Over budget or refused: the scan stops here and runs again next frame.
                    }

                    meshRequests++;
                }
            }
        }

        return true;
    }

    // An all-air chunk emits no faces whatever its neighbors hold, so it needs no snapshot or mesh. If it was
    // emptied after a mesh was uploaded, that mesh is stale and is released.
    private bool SkipIfEmpty(ChunkPosition position, Entry entry)
    {
        if (!world.TryGetChunk(position, out Chunk chunk) || !chunk.IsEmpty)
        {
            return false;
        }

        if (chunk.Version != entry.MeshedVersion)
        {
            ReleaseLater(entry);
            entry.MeshedVersion = chunk.Version;
            entry.Visibility = ChunkVisibility.All; // All air: nothing blocks the view.
        }

        return true;
    }

    // Queues the entry's uploaded meshes for release on the next UploadCompleted.
    private void ReleaseLater(Entry entry)
    {
        if (entry.Handle is { } handle)
        {
            _pendingRelease.Add(handle);
            entry.Handle = null;
            entry.VertexCount = 0;
        }

        if (entry.TranslucentHandle is { } translucent)
        {
            _pendingRelease.Add(translucent);
            entry.TranslucentHandle = null;
            entry.TranslucentVertexCount = 0;
        }
    }

    private bool NeedsMesh(ChunkPosition position, Entry entry)
        => world.TryGetChunk(position, out Chunk chunk) && chunk.Version != entry.MeshedVersion;

    // Meshing before in-range neighbors exist would draw faces that get hidden a moment later, then mesh twice.
    private bool NeighborsSettled(ChunkPosition position, int cameraX, int cameraZ)
    {
        // All 26 neighbors matter: the mesher's ambient occlusion reads across edges and corners too.
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    ChunkPosition neighbor = position.Offset(dx, dy, dz);
                    bool wanted = (dx | dy | dz) != 0
                        && neighbor.Y >= settings.Bounds.MinChunkY
                        && neighbor.Y <= settings.Bounds.MaxChunkY
                        && DistanceSquared(neighbor.X - cameraX, neighbor.Z - cameraZ)
                            <= settings.RenderDistance * settings.RenderDistance;

                    if (wanted && !(_entries.TryGetValue(neighbor, out Entry? other) && other.Loaded))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private bool TryRequestMesh(ChunkPosition position, Entry entry)
    {
        ChunkSnapshot snapshot = ChunkSnapshot.Create(world, position);
        int version = snapshot.Version;
        if (!queue.TryRequestMesh(snapshot))
        {
            snapshot.Dispose();
            return false;
        }

        entry.MeshRequestedVersion = version;
        return true;
    }

    private static (int Dx, int Dz)[] BuildLoadOffsets(int radius)
    {
        List<(int Dx, int Dz)> offsets = [];
        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (DistanceSquared(dx, dz) <= radius * radius)
                {
                    offsets.Add((dx, dz));
                }
            }
        }

        offsets.Sort((a, b) => DistanceSquared(a.Dx, a.Dz).CompareTo(DistanceSquared(b.Dx, b.Dz)));
        return [.. offsets];
    }

    private static int DistanceSquared(int dx, int dz) => (dx * dx) + (dz * dz);

    private static int FloorToChunk(float worldCoordinate) => (int)MathF.Floor(worldCoordinate) >> Chunk.SizeShift;

    private sealed class Entry
    {
        public bool Loaded { get; set; }

        // The chunk version a queued mesh was built from; null when no mesh request is outstanding.
        public int? MeshRequestedVersion { get; set; }

        public int MeshedVersion { get; set; } = -1;

        public MeshHandle? Handle { get; set; }

        public MeshHandle? TranslucentHandle { get; set; }

        // Vertex counts of the uploaded meshes, for DrawStats; 0 while the matching handle is null.
        public int VertexCount { get; set; }

        public int TranslucentVertexCount { get; set; }

        // Which faces the chunk's open space joins, from its last mesh. All until meshed, so an unknown chunk never hides others.
        public ChunkVisibility Visibility { get; set; } = ChunkVisibility.All;

        // Bookkeeping for the visibility walk in Draw: the frame it last reached this chunk and the faces it entered through.
        public int VisitFrame { get; set; }

        public int VisitedFaces { get; set; }

        public bool HasMesh => Handle is not null || TranslucentHandle is not null;
    }

    private readonly record struct WalkStep(ChunkPosition Position, Entry Entry, int EnteredFace);

    private readonly record struct TranslucentDraw(MeshHandle Handle, Vector3 Min, float DistanceSquared);
}
