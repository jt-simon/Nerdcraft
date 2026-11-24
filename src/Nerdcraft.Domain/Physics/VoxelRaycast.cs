// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Domain.Physics;

/// <summary>The block a ray hit, and where.</summary>
/// <param name="Block">The solid block that was hit.</param>
/// <param name="Normal">Unit axis vector of the face the ray entered through, pointing out of the block; zero when the ray started inside the block.</param>
/// <param name="Distance">Distance from the ray origin to the hit, in blocks; zero when the ray started inside the block.</param>
public readonly record struct RaycastHit(BlockPosition Block, Vector3 Normal, float Distance);

/// <summary>
/// Finds the first solid block along a ray through a <see cref="VoxelWorld"/>. Liquids and air are passed through,
/// so a ray reaches the seabed through water. A ray that enters a block whose chunk is not loaded, or leaves the
/// loaded world, stops there without a hit. Pure and deterministic: the same world and inputs always give the same result.
/// </summary>
public static class VoxelRaycast
{
    /// <summary>
    /// Walks the ray one block at a time (Amanatides-Woo) and returns the first block that
    /// <see cref="BlockTypes.IsSolid"/> accepts. A ray that starts inside a solid block hits that block at distance zero.
    /// </summary>
    /// <param name="world">The world to search. Read only.</param>
    /// <param name="origin">Ray start in world space.</param>
    /// <param name="direction">Ray direction; it need not be normalized.</param>
    /// <param name="maxDistance">Longest distance to search, in blocks; a block entered farther away is not a hit.</param>
    /// <param name="ignoreStartBlock">
    /// When true, the block containing <paramref name="origin"/> is never a hit, and it need not be loaded, so a ray that
    /// starts inside terrain reports the first block it enters instead.
    /// </param>
    /// <returns>The hit, or <see langword="null"/> when nothing solid lies within reach before an unloaded chunk.</returns>
    /// <exception cref="ArgumentException"><paramref name="origin"/> or <paramref name="direction"/> is not finite, or <paramref name="direction"/> has zero length.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDistance"/> is negative or not a number.</exception>
    public static RaycastHit? Cast(VoxelWorld world, Vector3 origin, Vector3 direction, float maxDistance, bool ignoreStartBlock = false)
    {
        if (!IsFinite(origin))
        {
            throw new ArgumentException("The origin must be finite.", nameof(origin));
        }

        if (!IsFinite(direction) || direction == Vector3.Zero)
        {
            throw new ArgumentException("The direction must be finite and not zero.", nameof(direction));
        }

        if (!(maxDistance >= 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance), maxDistance, "The distance must not be negative.");
        }

        Vector3 d = Vector3.Normalize(direction);
        if (!IsFinite(d))
        {
            throw new ArgumentException("The direction is too small or too large to normalize.", nameof(direction));
        }

        int x = (int)MathF.Floor(origin.X);
        int y = (int)MathF.Floor(origin.Y);
        int z = (int)MathF.Floor(origin.Z);

        int stepX = Math.Sign(d.X);
        int stepY = Math.Sign(d.Y);
        int stepZ = Math.Sign(d.Z);
        float deltaX = stepX == 0 ? float.PositiveInfinity : 1f / MathF.Abs(d.X);
        float deltaY = stepY == 0 ? float.PositiveInfinity : 1f / MathF.Abs(d.Y);
        float deltaZ = stepZ == 0 ? float.PositiveInfinity : 1f / MathF.Abs(d.Z);
        float maxX = FirstBoundary(origin.X, x, d.X, stepX);
        float maxY = FirstBoundary(origin.Y, y, d.Y, stepY);
        float maxZ = FirstBoundary(origin.Z, z, d.Z, stepZ);

        Vector3 normal = Vector3.Zero;
        float distance = 0f;
        bool skip = ignoreStartBlock;
        while (true)
        {
            BlockPosition position = new(x, y, z);
            if (!skip)
            {
                if (!world.TryGetChunk(position.ChunkPosition, out ChunkView chunk))
                {
                    return null;
                }

                if (BlockTypes.IsSolid(chunk.Get(position.LocalX, position.LocalY, position.LocalZ)))
                {
                    return new RaycastHit(position, normal, distance);
                }
            }

            skip = false;

            if (maxX <= maxY && maxX <= maxZ)
            {
                distance = maxX;
                x += stepX;
                maxX += deltaX;
                normal = new Vector3(-stepX, 0f, 0f);
            }
            else if (maxY <= maxZ)
            {
                distance = maxY;
                y += stepY;
                maxY += deltaY;
                normal = new Vector3(0f, -stepY, 0f);
            }
            else
            {
                distance = maxZ;
                z += stepZ;
                maxZ += deltaZ;
                normal = new Vector3(0f, 0f, -stepZ);
            }

            if (distance > maxDistance)
            {
                return null;
            }
        }
    }

    // Distance along the ray from the origin to the first block boundary on one axis.
    private static float FirstBoundary(float origin, int cell, float direction, int step)
        => step switch
        {
            > 0 => (cell + 1 - origin) / direction,
            < 0 => (origin - cell) / -direction,
            _ => float.PositiveInfinity,
        };

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
