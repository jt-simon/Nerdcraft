// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Physics;

/// <summary>
/// Collision between boxes and the blocks of a <see cref="VoxelWorld"/>. A block inside the world's vertical
/// bounds whose chunk is not loaded counts as solid, so nothing can walk into terrain that has not streamed in;
/// space outside the bounds counts as air. Pure and deterministic: the same world and inputs always give the same result.
/// </summary>
public static class VoxelCollision
{
    // Movement treats a face left a few float steps inside a wall by rounding as still touching it. Float
    // spacing grows with distance from the origin, independently on each axis.
    private const float MinimumTolerance = 1e-5f;

    /// <summary>
    /// Clips a movement so <paramref name="box"/> stops against solid blocks. Axes are resolved Y, then X, then Z,
    /// each against the box as already moved on the previous axes, so the box slides along walls and floors.
    /// A block the box already overlaps does not block it, so a box stuck inside terrain can leave. Any distance is
    /// handled without tunnelling.
    /// </summary>
    /// <param name="world">The world to collide with. Read only.</param>
    /// <param name="bounds">The world's vertical extent, used to decide which unloaded space counts as solid.</param>
    /// <param name="box">The moving box before the move.</param>
    /// <param name="delta">The wanted movement.</param>
    /// <returns>The movement that can be made: each component has the sign of the wanted one and is no larger.</returns>
    public static Vector3 Move(VoxelWorld world, WorldBounds bounds, Aabb box, Vector3 delta)
    {
        Vector3 sweepMin = Vector3.Min(box.Min, box.Min + delta);
        Vector3 sweepMax = Vector3.Max(box.Max, box.Max + delta);
        BlockPosition min = new(Floor(sweepMin.X), Floor(sweepMin.Y), Floor(sweepMin.Z));
        BlockPosition max = new(Floor(sweepMax.X), Floor(sweepMax.Y), Floor(sweepMax.Z));

        float dy = Clip(world, bounds, box, min, max, 1, delta.Y);
        box = box.Offset(new Vector3(0f, dy, 0f));
        float dx = Clip(world, bounds, box, min, max, 0, delta.X);
        box = box.Offset(new Vector3(dx, 0f, 0f));
        float dz = Clip(world, bounds, box, min, max, 2, delta.Z);
        return new Vector3(dx, dy, dz);
    }

    /// <summary>True when <paramref name="box"/> overlaps a solid block, counting unloaded space as described on <see cref="VoxelCollision"/>.</summary>
    public static bool Collides(VoxelWorld world, WorldBounds bounds, Aabb box)
        => AnyBlock(box, new SolidBlocks(world, bounds));

    /// <summary>True when <paramref name="box"/> overlaps a liquid block such as water.</summary>
    public static bool OverlapsLiquid(VoxelWorld world, Aabb box)
        => AnyBlock(box, new LiquidBlocks(world));

    private static bool AnyBlock<TBlocks>(Aabb box, TBlocks blocks)
        where TBlocks : struct, IBlockTest
    {
        if (box.Min.X >= box.Max.X || box.Min.Y >= box.Max.Y || box.Min.Z >= box.Max.Z)
        {
            return false;
        }

        // The maximum face is exclusive. Taking the ceiling before subtracting preserves sub-block
        // overlap without including a block that only touches the box.
        int lastX = (int)(Math.Ceiling(box.Max.X) - 1);
        int lastY = (int)(Math.Ceiling(box.Max.Y) - 1);
        int lastZ = (int)(Math.Ceiling(box.Max.Z) - 1);
        for (long y = Floor(box.Min.Y); y <= lastY; y++)
        {
            for (long z = Floor(box.Min.Z); z <= lastZ; z++)
            {
                for (long x = Floor(box.Min.X); x <= lastX; x++)
                {
                    if (blocks.Matches((int)x, (int)y, (int)z))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // Shortens a movement of d along one axis (0 = X, 1 = Y, 2 = Z) so it stops at the nearest solid block face.
    private static float Clip(VoxelWorld world, WorldBounds bounds, Aabb box, BlockPosition min, BlockPosition max, int axis, float d)
    {
        if (d == 0f)
        {
            return 0f;
        }

        Vector3 minTolerance = ContactTolerance(box.Min);
        Vector3 maxTolerance = ContactTolerance(box.Max);
        int otherA = (axis + 1) % 3;
        int otherB = (axis + 2) % 3;
        for (int y = min.Y; y <= max.Y; y++)
        {
            for (int z = min.Z; z <= max.Z; z++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    if (!IsSolidAt(world, bounds, x, y, z))
                    {
                        continue;
                    }

                    Vector3 blockMin = new(x, y, z);
                    Vector3 blockMax = blockMin + Vector3.One;
                    bool overlapsOthers =
                        box.Max[otherA] > blockMin[otherA] + maxTolerance[otherA] && box.Min[otherA] < blockMax[otherA] - minTolerance[otherA]
                        && box.Max[otherB] > blockMin[otherB] + maxTolerance[otherB] && box.Min[otherB] < blockMax[otherB] - minTolerance[otherB];
                    if (!overlapsOthers)
                    {
                        continue;
                    }

                    if (d > 0f && box.Max[axis] <= blockMin[axis] + maxTolerance[axis])
                    {
                        d = Math.Min(d, Math.Max(0f, blockMin[axis] - box.Max[axis]));
                    }
                    else if (d < 0f && box.Min[axis] >= blockMax[axis] - minTolerance[axis])
                    {
                        d = Math.Max(d, Math.Min(0f, blockMax[axis] - box.Min[axis]));
                    }
                }
            }
        }

        return d;
    }

    private static bool IsSolidAt(VoxelWorld world, WorldBounds bounds, int x, int y, int z)
    {
        BlockPosition position = new(x, y, z);
        ChunkPosition chunkPosition = position.ChunkPosition;
        if (chunkPosition.Y < bounds.MinChunkY || chunkPosition.Y > bounds.MaxChunkY)
        {
            return false;
        }

        return !world.TryGetChunk(chunkPosition, out ChunkView chunk)
            || BlockTypes.IsSolid(chunk.Get(position.LocalX, position.LocalY, position.LocalZ));
    }

    private static Vector3 ContactTolerance(Vector3 point)
        => new(ContactTolerance(point.X), ContactTolerance(point.Y), ContactTolerance(point.Z));

    private static float ContactTolerance(float coordinate)
    {
        float magnitude = MathF.Abs(coordinate);
        return MathF.Max(MinimumTolerance, 2f * (MathF.BitIncrement(magnitude) - magnitude));
    }

    private static int Floor(float value) => (int)MathF.Floor(value);

    // Struct implementations keep the block scan free of delegate and closure allocations.
    private interface IBlockTest
    {
        bool Matches(int x, int y, int z);
    }

    private readonly struct SolidBlocks(VoxelWorld world, WorldBounds bounds) : IBlockTest
    {
        public bool Matches(int x, int y, int z) => IsSolidAt(world, bounds, x, y, z);
    }

    private readonly struct LiquidBlocks(VoxelWorld world) : IBlockTest
    {
        public bool Matches(int x, int y, int z) => BlockTypes.IsLiquid(world.GetBlock(new BlockPosition(x, y, z)));
    }
}
