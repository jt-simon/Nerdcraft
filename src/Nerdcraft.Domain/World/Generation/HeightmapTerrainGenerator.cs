// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.Blocks;
using Nerdcraft.Domain.World.Generation.ThirdParty;

namespace Nerdcraft.Domain.World.Generation;

/// <summary>
/// Generates terrain from layered OpenSimplex2 noise: a continent layer sets the base height around sea level
/// (Y = 0), a mountain mask decides how much relief an area gets, and hill noise adds the relief. Each
/// column's top layers depend on its height: gravel on the deep seabed, sand on the shallow seabed and beaches,
/// grass over dirt in between, bare stone on high ground and snow on the peaks. Each band edge is shifted by a
/// little noise so the boundaries wander. <c>DirtDepth</c> (3) layers of that material sit over stone, with bedrock
/// on the bottom layer of the world. Water fills every column up to <see cref="SeaLevel"/>. Caves are carved out
/// of the stone afterwards from 3D noise: large caverns and winding tunnels, which stay sealed below the surface
/// except for occasional openings, and never open into the seabed.
/// The same seed and bounds always yield the same world.
/// </summary>
public sealed class HeightmapTerrainGenerator : ITerrainGenerator
{
    /// <summary>World Y of the water surface: water fills every column's air up to and including this height.</summary>
    public const int SeaLevel = 0;

    private const int DirtDepth = 3;

    // Surface material bands, as surface heights. Each edge is moved by up to BandJitter blocks of noise.
    private const int DeepWaterDepth = 6; // Surfaces this far below sea level or deeper are gravel.
    private const int BeachHeight = 2; // Surfaces up to this far above sea level (and the shallow seabed) are sand.
    private const int RockLine = 26; // Surfaces from here up are bare stone.
    private const int SnowLine = 38; // Surfaces from here up are snow.
    private const float BandJitter = 3f;

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
    private const int BandSeedOffset = 707;
    private const int CavernSeedOffset = 404;
    private const int TunnelASeedOffset = 505;
    private const int TunnelBSeedOffset = 606;

    // Caves. The noise is sampled on a lattice every LatticeStep blocks and interpolated, which is about 64 times
    // cheaper than sampling every block and stays continuous across chunk borders because the lattice is aligned
    // to world coordinates (chunk origins are multiples of LatticeStep).
    private const int LatticeStep = 4;
    private const int LatticePoints = (Chunk.Size / LatticeStep) + 1;
    private const int CaveFloorMargin = 3; // Blocks within this distance above the world's bottom are never carved.
    private const float CavernThreshold = 0.55f; // A cavern is where its noise exceeds this.
    private const float TunnelWidth = 0.09f; // A tunnel is where both tunnel noises are within this of zero.
    private const int OpeningDepth = 8; // Within this many blocks of the surface, carving needs a stronger signal.
    private const float OpeningStrictness = 0.85f; // The score a block exactly at the surface needs to be carved.

    private readonly WorldBounds _bounds;

    // Configured once in the constructor and never changed afterwards, so workers may share them: GetNoise only reads.
    private readonly FastNoiseLite _continent;
    private readonly FastNoiseLite _mountains;
    private readonly FastNoiseLite _hills;
    private readonly FastNoiseLite _bands;
    private readonly FastNoiseLite _caverns;
    private readonly FastNoiseLite _tunnelsA;
    private readonly FastNoiseLite _tunnelsB;

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
        _bands = CreateLayer(seed + BandSeedOffset, scale: 40f, octaves: 2);
        _caverns = CreateCaveLayer(seed + CavernSeedOffset, scale: 55f, octaves: 2);
        _tunnelsA = CreateCaveLayer(seed + TunnelASeedOffset, scale: 48f, octaves: 1);
        _tunnelsB = CreateCaveLayer(seed + TunnelBSeedOffset, scale: 48f, octaves: 1);
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

        int waterTop = Math.Min(SeaLevel, _bounds.MaxY);
        Span<int> surfaces = stackalloc int[Chunk.Size * Chunk.Size];
        for (int z = 0; z < Chunk.Size; z++)
        {
            for (int x = 0; x < Chunk.Size; x++)
            {
                int surface = SurfaceHeight(origin.X + x, origin.Z + z);
                surfaces[(z * Chunk.Size) + x] = surface;
                (BlockId top, BlockId filler) = SurfaceMaterials(surface, origin.X + x, origin.Z + z);
                for (int y = 0; y < Chunk.Size; y++)
                {
                    int worldY = origin.Y + y;
                    if (worldY > Math.Max(surface, waterTop))
                    {
                        break;
                    }

                    if (worldY < _bounds.MinY)
                    {
                        continue;
                    }

                    blocks[Chunk.Index(x, y, z)] = worldY == _bounds.MinY ? BlockTypes.Bedrock
                        : worldY > surface ? BlockTypes.Water
                        : worldY == surface ? top
                        : worldY >= surface - DirtDepth ? filler
                        : BlockTypes.Stone;
                }
            }
        }

        CarveCaves(origin, surfaces, blocks);
    }

    /// <summary>
    /// World Y of the topmost solid block in the column at the given world X and Z, relative to sea level at Y = 0.
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

    // The top block and the three layers under it for a column, chosen by surface height. Bands are checked from the
    // top down; each edge is moved by band noise so the boundaries are not flat contour lines.
    private (BlockId Top, BlockId Filler) SurfaceMaterials(int surface, int worldX, int worldZ)
    {
        float jitter = Sample(_bands, worldX, worldZ) * (BandJitter / 0.7f);
        float height = surface - jitter;
        if (height >= SnowLine)
        {
            return (BlockTypes.Snow, BlockTypes.Stone);
        }

        if (height >= RockLine)
        {
            return (BlockTypes.Stone, BlockTypes.Stone);
        }

        if (height <= SeaLevel - DeepWaterDepth)
        {
            return (BlockTypes.Gravel, BlockTypes.Gravel);
        }

        if (height <= SeaLevel + BeachHeight)
        {
            return (BlockTypes.Sand, BlockTypes.Sand);
        }

        return (BlockTypes.Grass, BlockTypes.Dirt);
    }

    // Turns blocks to air where the cave noise is strong enough. Each block gets a score in (0, 1] from the cavern and
    // tunnel noise (0 means "not cave"); it is carved when the score beats a bar that is 0 deep underground and rises
    // to OpeningStrictness at the surface. So caves are solid-roofed except where the noise is at its very strongest.
    private void CarveCaves(BlockPosition origin, ReadOnlySpan<int> surfaces, Span<BlockId> blocks)
    {
        int lowestCarved = _bounds.MinY + CaveFloorMargin;
        if (origin.Y + Chunk.Size - 1 < lowestCarved)
        {
            return;
        }

        Span<float> caverns = stackalloc float[LatticePoints * LatticePoints * LatticePoints];
        Span<float> tunnelsA = stackalloc float[caverns.Length];
        Span<float> tunnelsB = stackalloc float[caverns.Length];
        SampleLattice(_caverns, origin, caverns);
        SampleLattice(_tunnelsA, origin, tunnelsA);
        SampleLattice(_tunnelsB, origin, tunnelsB);

        // Interpolated values never leave the range of the lattice values around them, so a chunk whose lattice
        // shows no cavern above the threshold and no place where both tunnel noises can be near zero has no cave.
        (float minA, float maxA) = Range(tunnelsA);
        (float minB, float maxB) = Range(tunnelsB);
        bool cavernPossible = Range(caverns).Max > CavernThreshold;
        bool tunnelPossible = minA < TunnelWidth && maxA > -TunnelWidth && minB < TunnelWidth && maxB > -TunnelWidth;
        if (!cavernPossible && !tunnelPossible)
        {
            return;
        }

        for (int z = 0; z < Chunk.Size; z++)
        {
            for (int x = 0; x < Chunk.Size; x++)
            {
                int surface = surfaces[(z * Chunk.Size) + x];
                int first = Math.Max(lowestCarved - origin.Y, 0);
                int highestCarved = surface < SeaLevel ? surface - OpeningDepth : surface; // Never open the seabed.
                int last = Math.Min(highestCarved - origin.Y, Chunk.Size - 1);
                for (int y = first; y <= last; y++)
                {
                    int depth = surface - (origin.Y + y);
                    float bar = depth >= OpeningDepth ? 0f : OpeningStrictness * (1f - ((float)depth / OpeningDepth));

                    float cavern = (Interpolate(caverns, x, y, z) - CavernThreshold) / (1f - CavernThreshold);
                    float tunnel = 1f - (MathF.Max(MathF.Abs(Interpolate(tunnelsA, x, y, z)), MathF.Abs(Interpolate(tunnelsB, x, y, z))) / TunnelWidth);
                    float score = MathF.Max(cavern, tunnel);
                    if (score > 0f && score > bar)
                    {
                        blocks[Chunk.Index(x, y, z)] = BlockId.Air;
                    }
                }
            }
        }
    }

    private static (float Min, float Max) Range(ReadOnlySpan<float> values)
    {
        float min = values[0];
        float max = values[0];
        foreach (float value in values)
        {
            min = MathF.Min(min, value);
            max = MathF.Max(max, value);
        }

        return (min, max);
    }

    // Samples a noise at every lattice point of the chunk: LatticePoints per axis, LatticeStep blocks apart, in world coordinates.
    private static void SampleLattice(FastNoiseLite noise, BlockPosition origin, Span<float> lattice)
    {
        for (int j = 0; j < LatticePoints; j++)
        {
            for (int k = 0; k < LatticePoints; k++)
            {
                for (int i = 0; i < LatticePoints; i++)
                {
                    lattice[LatticeIndex(i, j, k)] = noise.GetNoise(
                        origin.X + (i * LatticeStep),
                        origin.Y + (j * LatticeStep),
                        origin.Z + (k * LatticeStep));
                }
            }
        }
    }

    // Trilinear interpolation of a sampled lattice at a chunk-local block coordinate.
    private static float Interpolate(ReadOnlySpan<float> lattice, int x, int y, int z)
    {
        int i = x / LatticeStep;
        int j = y / LatticeStep;
        int k = z / LatticeStep;
        float fx = (x % LatticeStep) / (float)LatticeStep;
        float fy = (y % LatticeStep) / (float)LatticeStep;
        float fz = (z % LatticeStep) / (float)LatticeStep;

        float x00 = Lerp(lattice[LatticeIndex(i, j, k)], lattice[LatticeIndex(i + 1, j, k)], fx);
        float x10 = Lerp(lattice[LatticeIndex(i, j + 1, k)], lattice[LatticeIndex(i + 1, j + 1, k)], fx);
        float x01 = Lerp(lattice[LatticeIndex(i, j, k + 1)], lattice[LatticeIndex(i + 1, j, k + 1)], fx);
        float x11 = Lerp(lattice[LatticeIndex(i, j + 1, k + 1)], lattice[LatticeIndex(i + 1, j + 1, k + 1)], fx);
        return Lerp(Lerp(x00, x10, fy), Lerp(x01, x11, fy), fz);
    }

    private static int LatticeIndex(int i, int j, int k) => (((j * LatticePoints) + k) * LatticePoints) + i;

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static FastNoiseLite CreateCaveLayer(int seed, float scale, int octaves)
    {
        FastNoiseLite noise = new(seed);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXZPlanes); // Y is up: keeps horizontal slices free of grid artifacts.
        noise.SetFractalType(octaves > 1 ? FastNoiseLite.FractalType.FBm : FastNoiseLite.FractalType.None);
        noise.SetFractalOctaves(octaves);
        noise.SetFrequency(1f / scale);
        return noise;
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
