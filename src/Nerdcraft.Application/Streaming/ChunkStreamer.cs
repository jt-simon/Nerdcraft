// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;
using System.Diagnostics.Metrics;
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
/// <param name="store">Optional storage that receives edited chunks when they unload and on <see cref="SaveModified"/>.</param>
public sealed class ChunkStreamer(VoxelWorld world, IChunkWorkQueue queue, GameSettings settings, IChunkStore? store = null)
{
    // Chunk neighbor offsets in ChunkFace order; face ^ 1 is the opposite direction.
    private static readonly (int Dx, int Dy, int Dz)[] FaceDirections = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    private static readonly Histogram<long> DrawCalls = StreamingMetrics.Meter.CreateHistogram<long>("nerdcraft.draw.calls", "{call}");
    private static readonly Histogram<int> UploadCalls = StreamingMetrics.Meter.CreateHistogram<int>("nerdcraft.mesh.upload.calls", "{call}");
    private static readonly Histogram<int> ReleaseCalls = StreamingMetrics.Meter.CreateHistogram<int>("nerdcraft.mesh.release.calls", "{call}");
    private static readonly Histogram<double> ReleaseTime = StreamingMetrics.Meter.CreateHistogram<double>("nerdcraft.mesh.release.duration", "ms");

    private readonly Dictionary<ChunkPosition, Entry> _entries = [];
    private readonly Queue<MeshHandle> _pendingRelease = new();
    private MeshedChunk? _stagedMesh;
    private MeshHandle? _stagedOpaque;
    private readonly List<TranslucentDraw> _translucentScratch = [];
    private readonly List<(ChunkPosition Position, Entry Entry)> _visibleScratch = [];
    private readonly Queue<WalkStep> _walkQueue = new();
    private int _drawFrame;
    private readonly List<ChunkPosition> _unloadScratch = [];
    private readonly List<ChunkPosition> _saveScratch = [];
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

    /// <summary>
    /// Uploads up to <see cref="GameSettings.MaxUploadsPerFrame"/> individual meshes. Takes at most
    /// <see cref="GameSettings.MaxMeshRequestsPerFrame"/> + <see cref="GameSettings.MaxUploadsPerFrame"/> finished results,
    /// counting discarded stale ones, and releases at most twice <see cref="GameSettings.MaxUploadsPerFrame"/> old meshes. A chunk's opaque and translucent handles become visible together once both
    /// are ready. Meshes built from outdated chunk contents are discarded.
    /// </summary>
    public void UploadCompleted(IRenderer renderer)
    {
        int uploads = 0;
        // Bounds results examined per frame, stale ones included. No more than one frame's requests plus one frame's
        // uploads can be useful, so this drains the queue at the rate it fills without a dedicated setting.
        long remainingResults = (long)settings.MaxMeshRequestsPerFrame + settings.MaxUploadsPerFrame;
        while (uploads < settings.MaxUploadsPerFrame && remainingResults-- > 0)
        {
            if (_stagedMesh is null)
            {
                if (!queue.TryTakeMeshed(out MeshedChunk next))
                {
                    break;
                }

                _stagedMesh = next;
            }

            MeshedChunk result = _stagedMesh.Value;
            if (!_entries.TryGetValue(result.Position, out Entry? entry)
                || entry.MeshRequestedVersion != result.Version)
            {
                DiscardStagedMesh();
                continue;
            }

            if (!world.TryGetChunk(result.Position, out ChunkView chunk) || chunk.Version != result.Version)
            {
                FinishRequest(entry);
                DiscardStagedMesh();
                continue;
            }

            ChunkMesh mesh = result.Mesh;
            if (_stagedOpaque is null && mesh.Opaque.VertexCount > 0)
            {
                _stagedOpaque = renderer.UploadMesh(mesh.Opaque);
                uploads++;
                mesh.Opaque.Dispose();
            }

            MeshHandle? translucent = null;
            if (mesh.Translucent.VertexCount > 0)
            {
                if (uploads == settings.MaxUploadsPerFrame)
                {
                    break; // Keep the current handles visible until both replacements are ready.
                }

                translucent = renderer.UploadMesh(mesh.Translucent);
                uploads++;
            }

            ReleaseLater(entry);
            entry.Handle = _stagedOpaque;
            entry.TranslucentHandle = translucent;
            entry.VertexCount = mesh.Opaque.VertexCount;
            entry.TranslucentVertexCount = mesh.Translucent.VertexCount;
            entry.MeshedVersion = result.Version;
            entry.Visibility = mesh.Visibility;
            FinishRequest(entry);
            _stagedOpaque = null;
            DiscardStagedMesh();
        }

        UploadCalls.Record(uploads);
        long releaseStarted = StreamingMetrics.Start(ReleaseTime);
        int releases = 0;
        long releaseLimit = 2L * settings.MaxUploadsPerFrame; // One completed replacement can retire both old materials.
        while (releases < releaseLimit && _pendingRelease.TryPeek(out MeshHandle handle))
        {
            renderer.ReleaseMesh(handle);
            _pendingRelease.Dequeue();
            releases++;
        }

        StreamingMetrics.RecordElapsed(ReleaseTime, releaseStarted);
        ReleaseCalls.Record(releases);
    }

    // The renderer owns native handles through its own disposal. The streamer owns this in-flight CPU result.
    internal void DiscardStagedMesh()
    {
        if (_stagedOpaque is { } handle)
        {
            _pendingRelease.Enqueue(handle);
            _stagedOpaque = null;
        }

        _stagedMesh?.Mesh.Dispose();
        _stagedMesh = null;
    }

    private void FinishRequest(Entry entry)
    {
        entry.MeshRequestedVersion = null;
        _rescan = true; // The previous scan may have skipped a change while this request was outstanding.
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
    /// <param name="countMeshedChunks">Whether to count all chunks with a mesh, including those outside the view.</param>
    /// <returns>How many chunks and vertices were drawn, and the total meshed chunk count when requested (zero otherwise).</returns>
    public DrawStats Draw(IRenderer renderer, Frustum frustum, Vector3 cameraPosition, bool countMeshedChunks = true)
    {
        _translucentScratch.Clear();
        _visibleScratch.Clear();
        int meshed = 0;
        if (countMeshedChunks)
        {
            foreach (Entry entry in _entries.Values)
            {
                if (entry.HasMesh)
                {
                    meshed++;
                }
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
        long opaqueDraws = 0;
        foreach ((ChunkPosition position, Entry entry) in _visibleScratch)
        {
            Vector3 min = ChunkMin(position);
            vertices += entry.VertexCount + entry.TranslucentVertexCount;

            if (entry.Handle is { } handle)
            {
                renderer.DrawMesh(handle, Matrix4x4.CreateTranslation(min));
                opaqueDraws++;
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

        DrawCalls.Record(opaqueDraws + _translucentScratch.Count);
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
                    || (step.EnteredFace >= 0 && !step.Entry.DrawVisibility.Connects((ChunkFace)step.EnteredFace, (ChunkFace)exit)))
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
            // Keep drawing retained meshes while dirty, but do not let stale connectivity hide other chunks.
            entry.DrawVisibility = entry.Visibility != ChunkVisibility.All
                && world.TryGetChunk(position, out ChunkView chunk)
                && entry.MeshedVersion == chunk.Version
                ? entry.Visibility
                : ChunkVisibility.All;
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
        long unloadRadius = (long)settings.RenderDistance + 1;
        foreach (ChunkPosition position in _entries.Keys)
        {
            if (DistanceSquared((long)position.X - cameraX, (long)position.Z - cameraZ) > unloadRadius * unloadRadius)
            {
                _unloadScratch.Add(position);
            }
        }

        try
        {
            foreach (ChunkPosition position in _unloadScratch)
            {
                // Saved first: if the store fails, the chunk and its entry are both still there.
                SaveIfModified(position);
                if (_entries.Remove(position, out Entry? entry))
                {
                    ReleaseLater(entry);
                }

                world.UnloadChunk(position);
            }
        }
        finally
        {
            _unloadScratch.Clear();
        }
    }

    /// <summary>
    /// Saves every edited chunk to the store and marks it saved, so each edit is written once. Does nothing without a store.
    /// Only copies the blocks and queues them, so it is cheap enough to call from the frame loop.
    /// </summary>
    public void SaveModified()
    {
        if (store is null || world.ModifiedChunks.Count == 0)
        {
            return;
        }

        _saveScratch.AddRange(world.ModifiedChunks);
        foreach (ChunkPosition position in _saveScratch)
        {
            SaveIfModified(position);
        }

        _saveScratch.Clear();
    }

    private void SaveIfModified(ChunkPosition position)
    {
        if (store is not null
            && world.ModifiedChunks.Contains(position)
            && world.TryGetChunk(position, out ChunkView chunk))
        {
            store.Save(position, chunk.Blocks);
            world.MarkSaved(position);
        }
    }

    private void InstallGeneratedChunks()
    {
        for (int processed = 0; processed < settings.MaxChunkInstallsPerFrame && queue.TryTakeGenerated(out GeneratedChunk result); processed++)
        {
            try
            {
                if (_entries.TryGetValue(result.Position, out Entry? entry) && !entry.Loaded)
                {
                    world.LoadChunk(result.Position, result.Blocks.AsSpan(0, Chunk.Volume));
                    entry.Loaded = true;
                }
            }
            finally
            {
                ArrayPool<BlockId>.Shared.Return(result.Blocks);
            }
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
        if (!world.TryGetChunk(position, out ChunkView chunk) || !chunk.IsEmpty)
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
            _pendingRelease.Enqueue(handle);
            entry.Handle = null;
            entry.VertexCount = 0;
        }

        if (entry.TranslucentHandle is { } translucent)
        {
            _pendingRelease.Enqueue(translucent);
            entry.TranslucentHandle = null;
            entry.TranslucentVertexCount = 0;
        }
    }

    private bool NeedsMesh(ChunkPosition position, Entry entry)
        => world.TryGetChunk(position, out ChunkView chunk) && chunk.Version != entry.MeshedVersion;

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
                        && DistanceSquared((long)neighbor.X - cameraX, (long)neighbor.Z - cameraZ)
                            <= (long)settings.RenderDistance * settings.RenderDistance;

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
                if (DistanceSquared(dx, dz) <= (long)radius * radius)
                {
                    offsets.Add((dx, dz));
                }
            }
        }

        offsets.Sort((a, b) => DistanceSquared(a.Dx, a.Dz).CompareTo(DistanceSquared(b.Dx, b.Dz)));
        return [.. offsets];
    }

    private static long DistanceSquared(long dx, long dz) => (dx * dx) + (dz * dz);

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
        public ChunkVisibility DrawVisibility { get; set; }

        public int VisitFrame { get; set; }

        public int VisitedFaces { get; set; }

        public bool HasMesh => Handle is not null || TranslucentHandle is not null;
    }

    private readonly record struct WalkStep(ChunkPosition Position, Entry Entry, int EnteredFace);

    private readonly record struct TranslucentDraw(MeshHandle Handle, Vector3 Min, float DistanceSquared);
}
