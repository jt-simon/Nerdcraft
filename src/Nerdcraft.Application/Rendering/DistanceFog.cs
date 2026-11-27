// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Application.Rendering;

/// <summary>Distances over which world meshes fade toward the frame's clear color.</summary>
/// <param name="StartBlocks">Horizontal distance from the camera in world blocks at which fog starts.</param>
/// <param name="EndBlocks">Horizontal distance from the camera in world blocks at which fog reaches full strength. Greater than <paramref name="StartBlocks"/>.</param>
public readonly record struct DistanceFog(float StartBlocks, float EndBlocks);
