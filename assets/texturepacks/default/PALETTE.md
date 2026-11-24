# Flat voxel texture pack — color reference

Each asset is a 16 × 16 RGB PNG (sRGB colors). Hex values below are the exact colors stored in the PNGs, listed by pixel frequency (most common first). There are no baked gradients, transparency, or lighting.

## Per-texture palettes

### `dirt.png`

All faces of dirt; bottom face of a grass block.

| Color | Hex | Pixels |
|---|---|---:|
| Base | `#97673C` | 241 |
| Accent 1 | `#8A5E36` | 9 |
| Accent 2 | `#A87444` | 6 |

### `grass_top.png`

Top face of a grass block; green only.

| Color | Hex | Pixels |
|---|---|---:|
| Base | `#589140` | 233 |
| Accent 1 | `#6CAA4C` | 12 |
| Accent 2 | `#488037` | 11 |

### `grass_side.png`

Four lateral faces of a grass block; green cap is 3–5 texels deep, approximately one quarter of the tile. Uses the exact dirt and grass-top palettes.

| Color | Hex | Pixels |
|---|---|---:|
| Base | `#97673C` | 180 |
| Accent 1 | `#589140` | 59 |
| Accent 2 | `#8A5E36` | 6 |
| Accent 3 | `#6CAA4C` | 4 |
| Accent 4 | `#A87444` | 4 |
| Accent 5 | `#488037` | 3 |

### `stone.png`

All faces of stone.

| Color | Hex | Pixels |
|---|---|---:|
| Base | `#868A8E` | 230 |
| Accent 1 | `#7A7D81` | 15 |
| Accent 2 | `#94979B` | 11 |

### `sand.png`

All faces of sand.

| Color | Hex | Pixels |
|---|---|---:|
| Base | `#DDC79F` | 242 |
| Accent 1 | `#D0BA92` | 8 |
| Accent 2 | `#E8D2AA` | 6 |

### `gravel.png`

All faces of gravel.

| Color | Hex | Pixels |
|---|---|---:|
| Base | `#848483` | 220 |
| Accent 1 | `#949392` | 16 |
| Accent 2 | `#747473` | 13 |
| Accent 3 | `#8E8884` | 7 |

### `snow.png`

All faces of a full snow block.

| Color | Hex | Pixels |
|---|---|---:|
| Base | `#EEF7FE` | 231 |
| Accent 1 | `#E1EEFA` | 14 |
| Accent 2 | `#D0DEEA` | 11 |

### `bedrock.png`

All faces of bedrock.

| Color | Hex | Pixels |
|---|---|---:|
| Base | `#2F2F34` | 225 |
| Accent 1 | `#424247` | 14 |
| Accent 2 | `#28282D` | 12 |
| Accent 3 | `#4E4E52` | 5 |

### `water_still.png`

Static water surface; RGB color only, with opacity controlled in the renderer if desired.

| Color | Hex | Pixels |
|---|---|---:|
| Base | `#62A9D2` | 237 |
| Accent 1 | `#6CB8DB` | 12 |
| Accent 2 | `#589AC1` | 7 |

## Face mapping

| Block | Top | Sides | Bottom |
|---|---|---|---|
| Grass-covered dirt | `grass_top.png` | `grass_side.png` | `dirt.png` |
| Other solid blocks | Same texture | Same texture | Same texture |
| Water | `water_still.png` | `water_still.png` | `water_still.png` |

