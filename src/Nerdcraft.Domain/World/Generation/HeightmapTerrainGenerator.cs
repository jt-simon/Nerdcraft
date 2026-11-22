// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;

namespace Nerdcraft.Domain.World.Generation;

/// <summary>
/// Generates rolling terrain from two octaves of value noise: grass on the surface, a few layers of dirt
/// beneath it, then stone. The same seed always yields the same world.
/// </summary>
/// <param name="seed">Seed for the noise; different seeds give different terrain.</param>
public sealed class HeightmapTerrainGenerator(int seed) : ITerrainGenerator
{
    private const int BaseHeight = 24;
    private const int Amplitude = 20;
    private const int DirtDepth = 3;

    /// <inheritdoc/>
    public void Generate(ChunkPosition position, Span<BlockId> blocks)
    {
        BlockPosition origin = position.Origin;
        blocks.Clear();

        for (int z = 0; z < Chunk.Size; z++)
        {
            for (int x = 0; x < Chunk.Size; x++)
            {
                int surface = SurfaceHeight(origin.X + x, origin.Z + z);
                for (int y = 0; y < Chunk.Size; y++)
                {
                    int worldY = origin.Y + y;
                    if (worldY > surface)
                    {
                        break;
                    }

                    blocks[Chunk.Index(x, y, z)] = worldY == surface ? BlockTypes.Grass
                        : worldY >= surface - DirtDepth ? BlockTypes.Dirt
                        : BlockTypes.Stone;
                }
            }
        }
    }

    /// <summary>World Y of the topmost (grass) block in the column at the given world X and Z.</summary>
    public int SurfaceHeight(int worldX, int worldZ)
    {
        float broad = ValueNoise(worldX / 48f, worldZ / 48f);
        float detail = ValueNoise(worldX / 12f, worldZ / 12f);
        float n = (broad * 0.75f) + (detail * 0.25f);
        return BaseHeight + (int)(n * Amplitude);
    }

    private float ValueNoise(float x, float z)
    {
        int x0 = (int)MathF.Floor(x);
        int z0 = (int)MathF.Floor(z);
        float fx = Smooth(x - x0);
        float fz = Smooth(z - z0);

        float a = Hash(x0, z0);
        float b = Hash(x0 + 1, z0);
        float c = Hash(x0, z0 + 1);
        float d = Hash(x0 + 1, z0 + 1);

        float top = a + ((b - a) * fx);
        float bottom = c + ((d - c) * fx);
        return top + ((bottom - top) * fz);
    }

    private static float Smooth(float t) => t * t * (3f - (2f * t));

    // Returns a value in [0, 1).
    private float Hash(int x, int z)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)x * 0x9E3779B1u;
            h = (h << 13) | (h >> 19);
            h ^= (uint)z * 0x85EBCA77u;
            h *= 0xC2B2AE3Du;
            h ^= h >> 16;
            h *= 0x27D4EB2Fu;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
