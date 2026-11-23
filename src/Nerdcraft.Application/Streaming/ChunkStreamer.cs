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
            using MeshData mesh = result.Mesh;
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

            entry.MeshedVersion = result.Version;
            if (mesh.VertexCount > 0)
            {
                entry.Handle = renderer.UploadMesh(mesh);
                uploads++;
            }
        }
    }

    /// <summary>Draws every uploaded chunk mesh that intersects <paramref name="frustum"/>, translated to its world position.</summary>
    public void Draw(IRenderer renderer, Frustum frustum)
    {
        foreach ((ChunkPosition position, Entry entry) in _entries)
        {
            if (entry.Handle is not { } handle)
            {
                continue;
            }

            Vector3 min = new(position.X * Chunk.Size, position.Y * Chunk.Size, position.Z * Chunk.Size);
            if (frustum.Intersects(min, min + new Vector3(Chunk.Size)))
            {
                renderer.DrawMesh(handle, Matrix4x4.CreateTranslation(min));
            }
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
            if (_entries.Remove(position, out Entry? entry) && entry.Handle is { } handle)
            {
                _pendingRelease.Add(handle);
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
                    if (!queue.TryRequestGenerate(position))
                    {
                        return;
                    }

                    _entries[position] = new Entry();
                    continue;
                }

                if (entry.Loaded
                    && entry.MeshRequestedVersion is null
                    && NeedsMesh(position, entry)
                    && NeighborsSettled(position, cameraX, cameraZ)
                    && !TryRequestMesh(position, entry))
                {
                    return;
                }
            }
        }
    }

    private bool NeedsMesh(ChunkPosition position, Entry entry)
        => world.TryGetChunk(position, out Chunk chunk) && chunk.Version != entry.MeshedVersion;

    // Meshing before in-range neighbors exist would draw faces that get hidden a moment later, then mesh twice.
    private bool NeighborsSettled(ChunkPosition position, int cameraX, int cameraZ)
    {
        for (int face = 0; face < 6; face++)
        {
            ChunkPosition neighbor = face switch
            {
                0 => position.Offset(-1, 0, 0),
                1 => position.Offset(1, 0, 0),
                2 => position.Offset(0, -1, 0),
                3 => position.Offset(0, 1, 0),
                4 => position.Offset(0, 0, -1),
                _ => position.Offset(0, 0, 1),
            };

            bool wanted = neighbor.Y >= settings.Bounds.MinChunkY
                && neighbor.Y <= settings.Bounds.MaxChunkY
                && DistanceSquared(neighbor.X - cameraX, neighbor.Z - cameraZ)
                    <= settings.RenderDistance * settings.RenderDistance;

            if (wanted && !(_entries.TryGetValue(neighbor, out Entry? other) && other.Loaded))
            {
                return false;
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
    }
}
