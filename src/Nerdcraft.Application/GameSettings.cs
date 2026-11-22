// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Application;

/// <summary>
/// Tunable game configuration. All values have defaults suited to modest hardware.
/// <see cref="NerdcraftGame"/> rejects out-of-range values with <see cref="ArgumentOutOfRangeException"/>.
/// </summary>
public sealed record GameSettings
{
    /// <summary>Horizontal radius, in chunks, kept loaded around the camera. At least 0.</summary>
    public int RenderDistance { get; init; } = 6;

    /// <summary>Vertical extent of the world in chunks; chunk Y runs from 0 to this value - 1. At least 1.</summary>
    public int WorldHeightInChunks { get; init; } = 4;

    /// <summary>Background workers for generation and meshing. Capped so weak machines keep a core free. At least 1.</summary>
    public int WorkerCount { get; init; } = Math.Clamp(Environment.ProcessorCount - 1, 1, 4);

    /// <summary>Completed meshes uploaded to the GPU per frame, to avoid hitches when many finish at once. At least 1.</summary>
    public int MaxUploadsPerFrame { get; init; } = 4;

    /// <summary>Seed for terrain generation; the same seed produces the same world.</summary>
    public int Seed { get; init; } = 1337;

    /// <summary>Vertical field of view of the camera, in degrees. Greater than 0 and less than 180.</summary>
    public float FovYDegrees { get; init; } = 70f;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(RenderDistance);
        ArgumentOutOfRangeException.ThrowIfLessThan(WorldHeightInChunks, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(WorkerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxUploadsPerFrame, 1);
        if (!(FovYDegrees is > 0f and < 180f))
        {
            throw new ArgumentOutOfRangeException(nameof(FovYDegrees), FovYDegrees, "Field of view must be between 0 and 180 degrees, exclusive.");
        }
    }
}
