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
/// <param name="settings">Supplies render distance, world bounds and the per-frame upload budget.</param>
public sealed class ChunkStreamer(VoxelWorld world, IChunkWorkQueue queue, GameSettings settings)
{
    private readonly Dictionary<ChunkPosition, Entry> _entries = [];
    private readonly List<MeshHandle> _pendingRelease = [];
    private readonly List<TranslucentDraw> _translucentScratch = [];
    private readonly List<ChunkPosition> _unloadScratch = [];
    private readonly (int Dx, int Dz)[] _loadOffsets = BuildLoadOffsets(settings.RenderDistance);
    private (int X, int Z)? _lastCameraChunk;

    /// <summary>Unloads chunks that left range, installs finished terrain, and requests new work around the camera. Makes no renderer calls.</summary>
    /// <param name="cameraPosition">Camera position in world space.</param>
    public void Update(Vector3 cameraPosition)
    {
        int cameraX = FloorToChunk(cameraPosition.X);
        int cameraZ = FloorToChunk(cameraPosition.Z);

        if (_lastCameraChunk != (cameraX, cameraZ))
        {
            _lastCameraChunk = (cameraX, cameraZ);
            UnloadDistantChunks(cameraX, cameraZ);
        }

        InstallGeneratedChunks();
        RequestWork(cameraX, cameraZ);
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
            if (chunk.Version != result.Version)
            {
                continue; // Blocks changed while meshing; the next scan requests a fresh mesh.
            }

            if (entry.Handle is { } old)
            {
                renderer.ReleaseMesh(old);
                entry.Handle = null;
            }

            if (entry.TranslucentHandle is { } oldTranslucent)
            {
                renderer.ReleaseMesh(oldTranslucent);
                entry.TranslucentHandle = null;
            }

            entry.MeshedVersion = result.Version;
            if (mesh.Opaque.VertexCount > 0)
            {
                entry.Handle = renderer.UploadMesh(mesh.Opaque);
            }

            if (mesh.Translucent.VertexCount > 0)
            {
                entry.TranslucentHandle = renderer.UploadMesh(mesh.Translucent);
            }

            if (entry.Handle is not null || entry.TranslucentHandle is not null)
            {
                uploads++;
            }
        }
    }

    /// <summary>
    /// Draws every uploaded chunk mesh that intersects <paramref name="frustum"/>, translated to its world position:
    /// all opaque meshes first, then the translucent ones from the farthest chunk to the nearest.
    /// </summary>
    /// <param name="renderer">Receives the draw calls.</param>
    /// <param name="frustum">The view volume; chunks outside it are skipped.</param>
    /// <param name="cameraPosition">Camera position in world space, used to order the translucent meshes.</param>
    public void Draw(IRenderer renderer, Frustum frustum, Vector3 cameraPosition)
    {
        _translucentScratch.Clear();
        foreach ((ChunkPosition position, Entry entry) in _entries)
        {
            if (entry.Handle is null && entry.TranslucentHandle is null)
            {
                continue;
            }

            Vector3 min = new(position.X * Chunk.Size, position.Y * Chunk.Size, position.Z * Chunk.Size);
            if (!frustum.Intersects(min, min + new Vector3(Chunk.Size)))
            {
                continue;
            }

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
        while (queue.TryTakeGenerated(out GeneratedChunk result))
        {
            if (_entries.TryGetValue(result.Position, out Entry? entry) && !entry.Loaded)
            {
                world.LoadChunk(result.Position, result.Blocks.AsSpan(0, Chunk.Volume));
                entry.Loaded = true;
            }

            ArrayPool<BlockId>.Shared.Return(result.Blocks);
        }
    }

    // Walks columns nearest-first so the area around the camera fills in before the horizon.
    private void RequestWork(int cameraX, int cameraZ)
    {
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
                        return;
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

                if (NeedsMesh(position, entry)
                    && NeighborsSettled(position, cameraX, cameraZ)
                    && !TryRequestMesh(position, entry))
                {
                    return;
                }
            }
        }
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
        }

        if (entry.TranslucentHandle is { } translucent)
        {
            _pendingRelease.Add(translucent);
            entry.TranslucentHandle = null;
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
    }

    private readonly record struct TranslucentDraw(MeshHandle Handle, Vector3 Min, float DistanceSquared);
}
