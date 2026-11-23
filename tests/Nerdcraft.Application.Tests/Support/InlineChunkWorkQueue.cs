// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;

using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Streaming;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;
using Nerdcraft.Domain.World.Generation;

namespace Nerdcraft.Application.Tests.Support;

/// <summary>Runs work synchronously at request time, making streamer behavior deterministic.</summary>
public sealed class InlineChunkWorkQueue(ITerrainGenerator generator, IChunkMesher mesher) : IChunkWorkQueue
{
    private readonly Queue<GeneratedChunk> _generated = [];
    private readonly Queue<MeshedChunk> _meshed = [];

    public bool TryRequestGenerateColumn(int chunkX, int chunkZ, int minChunkY, int maxChunkY)
    {
        for (int y = minChunkY; y <= maxChunkY; y++)
        {
            ChunkPosition position = new(chunkX, y, chunkZ);
            BlockId[] blocks = ArrayPool<BlockId>.Shared.Rent(Chunk.Volume);
            generator.Generate(position, blocks.AsSpan(0, Chunk.Volume));
            _generated.Enqueue(new GeneratedChunk(position, blocks));
        }

        return true;
    }

    /// <summary>Number of mesh requests accepted so far.</summary>
    public int MeshRequests { get; private set; }

    public bool TryRequestMesh(ChunkSnapshot snapshot)
    {
        MeshRequests++;
        _meshed.Enqueue(new MeshedChunk(snapshot.Position, snapshot.Version, mesher.Mesh(snapshot)));
        snapshot.Dispose();
        return true;
    }

    public bool TryTakeGenerated(out GeneratedChunk result) => _generated.TryDequeue(out result);

    public bool TryTakeMeshed(out MeshedChunk result) => _meshed.TryDequeue(out result);
}
