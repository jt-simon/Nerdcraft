// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Buffers;

using Nerdcraft.Application.Meshing;
using Nerdcraft.Application.Streaming;
using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;
using Nerdcraft.Domain.World.Generation;
using Nerdcraft.Persistence.Sqlite.Tests.Support;

namespace Nerdcraft.Persistence.Sqlite.Tests;

public sealed class ChunkPipelineStorageTests : IDisposable
{
    private static readonly WorldBounds Bounds = new(-50, 200);

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task GenerateColumn_AfterReopen_LoadsStoredChunksWithoutGenerating()
    {
        SqliteWorld world = await SqliteWorld.OpenAsync(_directory.Path, 5, Bounds, null, TestContext.Current.CancellationToken);
        Dictionary<ChunkPosition, BlockId[]> generated = await GenerateColumnAsync(new HeightmapTerrainGenerator(5, Bounds), world);
        await world.DisposeAsync();

        await using SqliteWorld reopened = await SqliteWorld.OpenAsync(_directory.Path, 5, Bounds, null, TestContext.Current.CancellationToken);
        Dictionary<ChunkPosition, BlockId[]> loaded = await GenerateColumnAsync(new ThrowingGenerator(), reopened);

        Assert.Equal(Bounds.MaxChunkY - Bounds.MinChunkY + 1, loaded.Count);
        foreach ((ChunkPosition position, BlockId[] blocks) in generated)
        {
            Assert.Equal(blocks, loaded[position]);
        }
    }

    private static async Task<Dictionary<ChunkPosition, BlockId[]>> GenerateColumnAsync(ITerrainGenerator generator, SqliteWorld world)
    {
        await using ChunkPipeline pipeline = new(generator, new CulledChunkMesher(), workerCount: 1, Bounds, store: world.Chunks(0));
        Assert.True(pipeline.TryRequestGenerateColumn(2, -3, Bounds.MinChunkY, Bounds.MaxChunkY));

        int rows = Bounds.MaxChunkY - Bounds.MinChunkY + 1;
        Dictionary<ChunkPosition, BlockId[]> received = [];
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (received.Count < rows)
        {
            while (pipeline.TryTakeGenerated(out GeneratedChunk chunk))
            {
                received[chunk.Position] = chunk.Blocks.AsSpan(0, Chunk.Volume).ToArray();
                ArrayPool<BlockId>.Shared.Return(chunk.Blocks);
            }

            await Task.Delay(5, timeout.Token);
        }

        return received;
    }

    private sealed class ThrowingGenerator : ITerrainGenerator
    {
        public void Generate(ChunkPosition position, Span<BlockId> blocks)
            => throw new InvalidOperationException("The chunk should have come from storage.");
    }
}
