// SPDX-License-Identifier: AGPL-3.0-or-later

using Nerdcraft.Domain.World;

namespace Nerdcraft.Application;

/// <summary>
/// Tunable game configuration. All values have defaults suited to modest hardware.
/// <see cref="NerdcraftGame"/> rejects out-of-range values with <see cref="ArgumentOutOfRangeException"/>.
/// </summary>
public sealed record GameSettings
{
    /// <summary>Horizontal radius, in chunks, kept loaded around the camera. At least 0.</summary>
    public int RenderDistance { get; init; } = 12;

    /// <summary>Vertical extent of the world in blocks, with sea level at Y = 0. <c>MaxY</c> must be above <c>MinY</c>.</summary>
    public WorldBounds Bounds { get; init; } = WorldBounds.Default;

    /// <summary>Background workers for generation and meshing. Capped so weak machines keep a core free. At least 1.</summary>
    public int WorkerCount { get; init; } = Math.Clamp(Environment.ProcessorCount - 1, 1, 4);

    /// <summary>
    /// Maximum individual mesh uploads per frame, counting opaque and translucent meshes separately. At least 1.
    /// </summary>
    public int MaxUploadsPerFrame { get; init; } = 8;

    /// <summary>
    /// Maximum generated results processed per frame, including stale results. Installing a chunk copies it and
    /// invalidates its neighbors, so results beyond the budget wait for the next frame. At least 1.
    /// </summary>
    public int MaxChunkInstallsPerFrame { get; init; } = 64;

    /// <summary>
    /// Meshing requests started per frame. Each copies a chunk and its borders on the main thread before a worker meshes
    /// it, so starting hundreds in one frame would stall it; the rest wait for the next. Together with <see cref="MaxUploadsPerFrame"/>
    /// it also caps the finished meshes taken per frame, including stale ones that are discarded. At least 1.
    /// </summary>
    public int MaxMeshRequestsPerFrame { get; init; } = 16;

    /// <summary>Seed for terrain generation; the same seed produces the same world.</summary>
    public int Seed { get; init; } = 1337;

    /// <summary>Vertical field of view of the camera, in degrees. Greater than 0 and less than 180.</summary>
    public float FovYDegrees { get; init; } = 70f;

    /// <summary>
    /// Real seconds between saves of edited chunks to the chunk store, counted on the uncapped UI time so it also runs while
    /// paused. Greater than 0. Has no effect without a store.
    /// </summary>
    public float AutosaveSeconds { get; init; } = 5f;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(RenderDistance);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Bounds.MaxY, Bounds.MinY);
        ArgumentOutOfRangeException.ThrowIfLessThan(WorkerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxUploadsPerFrame, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxChunkInstallsPerFrame, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxMeshRequestsPerFrame, 1);
        if (!(AutosaveSeconds > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(AutosaveSeconds), AutosaveSeconds, "The autosave interval must be greater than 0.");
        }

        if (!(FovYDegrees is > 0f and < 180f))
        {
            throw new ArgumentOutOfRangeException(nameof(FovYDegrees), FovYDegrees, "Field of view must be between 0 and 180 degrees, exclusive.");
        }
    }
}
