// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Nerdcraft.Application.Rendering;

/// <summary>Engine-neutral RGBA color, so no rendering library type leaks into the core.</summary>
/// <param name="R">Red, 0-255.</param>
/// <param name="G">Green, 0-255.</param>
/// <param name="B">Blue, 0-255.</param>
/// <param name="A">Alpha, 0-255; 255 is opaque.</param>
public readonly record struct Color(byte R, byte G, byte B, byte A = 255);
