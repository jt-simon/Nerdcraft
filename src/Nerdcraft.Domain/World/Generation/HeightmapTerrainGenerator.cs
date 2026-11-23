// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;

namespace Nerdcraft.Domain.World.Generation;

/// <summary>
/// Generates terrain from layered simplex noise: a continent layer sets the base height around sea level
/// (Y = 0), a mountain mask decides how much relief an area gets, and hill noise adds the relief. Each
/// column is grass on the surface, <c>DirtDepth</c> (3) layers of dirt, then stone, with bedrock on the
/// bottom layer of the world. There is no water and no caves yet. The same seed and bounds always yield the same world.
/// </summary>
public sealed class HeightmapTerrainGenerator : ITerrainGenerator
{
    private const int DirtDepth = 3;

    private const int ContinentSeedOffset = 101;
    private const int MountainSeedOffset = 202;
    private const int HillSeedOffset = 303;

    private readonly int _seed;
    private readonly WorldBounds _bounds;

    /// <summary>Creates a generator for one world.</summary>
    /// <param name="seed">Seed for the noise; different seeds give different terrain.</param>
    /// <param name="bounds">Vertical extent of the world; the bottom layer is bedrock and nothing generates outside it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bounds"/> has <c>MaxY</c> not above <c>MinY</c>.</exception>
    public HeightmapTerrainGenerator(int seed, WorldBounds bounds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bounds.MaxY, bounds.MinY);
        _seed = seed;
        _bounds = bounds;
    }

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

                    if (worldY < _bounds.MinY)
                    {
                        continue;
                    }

                    blocks[Chunk.Index(x, y, z)] = worldY == _bounds.MinY ? BlockTypes.Bedrock
                        : worldY == surface ? BlockTypes.Grass
                        : worldY >= surface - DirtDepth ? BlockTypes.Dirt
                        : BlockTypes.Stone;
                }
            }
        }
    }

    /// <summary>
    /// World Y of the topmost (grass) block in the column at the given world X and Z, relative to sea level at Y = 0.
    /// Always in [<c>MinY + 1</c>, <c>MaxY</c>] so bedrock stays below the surface.
    /// </summary>
    public int SurfaceHeight(int worldX, int worldZ)
    {
        // The noise only reaches about +/-0.7, so every range below is narrower than the full scale suggests.
        // Continent: base height from about -15 (basins) to +7, before relief is added.
        float continent = SimplexNoise.Fbm(_seed + ContinentSeedOffset, worldX / 1000f, worldZ / 1000f, 3);
        float baseHeight = (continent * 16f) - 4f;

        // Mountain mask: 0 on plains, 1 in mountain ranges.
        float rawMask = (SimplexNoise.Fbm(_seed + MountainSeedOffset, worldX / 800f, worldZ / 800f, 2) + 1f) * 0.5f;
        float mask = SmoothStep(0.35f, 0.75f, rawMask);

        float hills = SimplexNoise.Fbm(_seed + HillSeedOffset, worldX / 300f, worldZ / 300f, 3);
        // Relief is added on top of the base: up to about +/-4 on plains and +/-65 in mountain ranges.
        float relief = hills * (6f + (mask * 90f));

        int height = (int)MathF.Floor(baseHeight + relief);
        return Math.Clamp(height, _bounds.MinY + 1, _bounds.MaxY);
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }
}
