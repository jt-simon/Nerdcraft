// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Streaming;

namespace Nerdcraft.Application.Rendering;

/// <summary>What a renderer shows in the F3 debug overlay, besides its own frame rate.</summary>
/// <param name="CameraPosition">Camera position in world space.</param>
/// <param name="Stats">What the frame's chunk draw submitted.</param>
public readonly record struct DebugOverlay(Vector3 CameraPosition, DrawStats Stats);
