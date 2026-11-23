// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World.Generation.ThirdParty;

namespace Nerdcraft.Domain.World.Generation;

/// <summary>
/// Generates terrain from layered OpenSimplex2 noise: a continent layer sets the base height around sea level
/// (Y = 0), a mountain mask decides how much relief an area gets, and hill noise adds the relief. Each
/// column is grass on the surface, <c>DirtDepth</c> (3) layers of dirt, then stone, with bedrock on the
/// bottom layer of the world. There is no water and no caves yet. The same seed and bounds always yield the same world.
/// </summary>
public sealed class HeightmapTerrainGenerator : ITerrainGenerator
{
    private const int DirtDepth = 3;

    // FastNoiseLite spans about +/-0.96 where the terrain was tuned against noise spanning about +/-0.7.
    // Scaling by this keeps the same height statistics.
    private const float NoiseScale = 0.71f;

    // Terrain height constants, shared by SurfaceHeight and the upper bound used to skip empty chunks.
    private const float ContinentAmplitude = 16f;
    private const float ContinentOffset = 5f;
    private const float PlainsRelief = 6f;
    private const float MountainRelief = 90f;

    private const int ContinentSeedOffset = 101;
    private const int MountainSeedOffset = 202;
    private const int HillSeedOffset = 303;

    private readonly WorldBounds _bounds;

    // Configured once in the constructor and never changed afterwards, so workers may share them: GetNoise only reads.
    private readonly FastNoiseLite _continent;
    private readonly FastNoiseLite _mountains;
    private readonly FastNoiseLite _hills;

    // Highest block Y any column can reach, assuming full-range noise; chunks above it are all air.
    private readonly int _maxSurface;

    /// <summary>Creates a generator for one world.</summary>
    /// <param name="seed">Seed for the noise; different seeds give different terrain.</param>
    /// <param name="bounds">Vertical extent of the world; the bottom layer is bedrock and nothing generates outside it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bounds"/> has <c>MaxY</c> not above <c>MinY</c>.</exception>
    public HeightmapTerrainGenerator(int seed, WorldBounds bounds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bounds.MaxY, bounds.MinY);
        _bounds = bounds;
        _continent = CreateLayer(seed + ContinentSeedOffset, scale: 1000f, octaves: 3);
        _mountains = CreateLayer(seed + MountainSeedOffset, scale: 800f, octaves: 2);
        _hills = CreateLayer(seed + HillSeedOffset, scale: 300f, octaves: 3);
        _maxSurface = Math.Min(
            (int)MathF.Ceiling(ContinentOffset + (NoiseScale * (ContinentAmplitude + PlainsRelief + MountainRelief))),
            bounds.MaxY);
    }

    /// <inheritdoc/>
    public void Generate(ChunkPosition position, Span<BlockId> blocks)
    {
        BlockPosition origin = position.Origin;
        blocks.Clear();
        if (origin.Y > _maxSurface)
        {
            return;
        }

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
        // Each layer's noise is scaled to about +/-0.7, so every range below is narrower than the full scale suggests.
        // Continent: base height from about -6 (basins) to +16, before relief is added.
        float continent = Sample(_continent, worldX, worldZ);
        float baseHeight = (continent * ContinentAmplitude) + ContinentOffset;

        // Mountain mask: 0 on plains, 1 in mountain ranges.
        float rawMask = (Sample(_mountains, worldX, worldZ) + 1f) * 0.5f;
        float mask = SmoothStep(0.35f, 0.75f, rawMask);

        float hills = Sample(_hills, worldX, worldZ);
        // Relief is added on top of the base: up to about +/-4 on plains and +/-65 in mountain ranges.
        float relief = hills * (PlainsRelief + (mask * MountainRelief));

        int height = (int)MathF.Floor(baseHeight + relief);
        return Math.Clamp(height, _bounds.MinY + 1, _bounds.MaxY);
    }

    private static FastNoiseLite CreateLayer(int seed, float scale, int octaves)
    {
        FastNoiseLite noise = new(seed);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(octaves);
        noise.SetFrequency(1f / scale);
        return noise;
    }

    private static float Sample(FastNoiseLite noise, int worldX, int worldZ) => noise.GetNoise(worldX, worldZ) * NoiseScale;

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }
}
