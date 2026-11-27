// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.Metrics;
using System.Numerics;

using Nerdcraft.Application.Ports;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Application.Ui;
using Nerdcraft.Domain.Physics;

using Raylib_cs;

using Color = Raylib_cs.Color;
using NerdcraftColor = Nerdcraft.Application.Rendering.Color;

namespace Nerdcraft.Rendering.Raylib;

/// <summary>Draws with raylib. Must be created and used on the thread that owns the window.</summary>
public sealed unsafe class RaylibRenderer : IRenderer, IDisposable
{
    private const int TileSize = 16;

    private const string TerrainVertexShader = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec4 vertexColor;
        uniform mat4 mvp;
        uniform mat4 matModel;
        uniform vec4 fogParameters;
        out vec2 fragTexCoord;
        out vec4 fragColor;
        out vec2 cameraOffset;

        void main()
        {
            vec4 position = vec4(vertexPosition, 1.0);
            gl_Position = mvp * position;
            fragTexCoord = vertexTexCoord;
            fragColor = vertexColor;
            cameraOffset = (matModel * position).xz - fogParameters.xy;
        }
        """;

    private const string TerrainFragmentShader = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        in vec2 cameraOffset;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform vec4 fogParameters;
        uniform vec3 fogColor;
        out vec4 finalColor;

        void main()
        {
            vec4 surface = texture(texture0, fragTexCoord) * colDiffuse * fragColor;
            float distanceSquared = dot(cameraOffset, cameraOffset);
            float fog = smoothstep(fogParameters.z, fogParameters.w, distanceSquared);
            finalColor = vec4(mix(surface.rgb, fogColor, fog), surface.a);
        }
        """;

    private static readonly Meter Metrics = new("Nerdcraft.Raylib");
    private static readonly Histogram<long> UploadBytes = Metrics.CreateHistogram<long>(
        "nerdcraft.mesh.upload.bytes", "By", "Vertex positions, texture coordinates, colors and indices sent to the GPU; normals are not uploaded");

    private readonly Dictionary<int, Mesh> _meshes = [];
    private readonly Material _material = Raylib_cs.Raylib.LoadMaterialDefault();
    private readonly TextureAtlas _atlas = BlockPalette.Atlas;
    private readonly Texture2D _atlasTexture;
    private readonly int _fogParametersLocation = -1;
    private readonly int _fogColorLocation;
    private int _nextId;
    private bool _drawingTranslucent;

    /// <summary>
    /// Builds the texture atlas from a texture pack and makes every block mesh use it. For each name in <see cref="BlockPalette.Atlas"/>
    /// it reads <c>textures/&lt;name&gt;.png</c> from <paramref name="texturePackDirectory"/>, scales it to 16 by 16 pixels if it has another
    /// size, and places it in the tile the atlas assigns. A file that is missing or cannot be read leaves its tile magenta; so do unknown
    /// names. Each such file is reported on standard error, and one message says so if the pack has no <c>textures</c> folder.
    /// Uses the default material shader if the terrain fog shader is unavailable. Must be called on the thread that owns the window,
    /// after the window is created.
    /// </summary>
    /// <param name="texturePackDirectory">The folder of the pack: the one that contains <c>textures</c>.</param>
    public RaylibRenderer(string texturePackDirectory)
    {
        if (!Directory.Exists(Path.Combine(texturePackDirectory, "textures")))
        {
            Console.Error.WriteLine($"Texture pack not found: no 'textures' folder in {texturePackDirectory}. Blocks will be magenta.");
        }

        Image atlas = Raylib_cs.Raylib.GenImageColor(TileSize, TileSize * _atlas.TileCount, new Color(255, 0, 255, 255));
        for (int i = 0; i < _atlas.Names.Count; i++)
        {
            string path = Path.Combine(texturePackDirectory, "textures", _atlas.Names[i] + ".png");
            if (!File.Exists(path))
            {
                if (Directory.Exists(Path.Combine(texturePackDirectory, "textures")))
                {
                    Console.Error.WriteLine($"Texture missing: {path}");
                }

                continue;
            }

            Image tile = Raylib_cs.Raylib.LoadImage(path);
            if (!Raylib_cs.Raylib.IsImageValid(tile))
            {
                Console.Error.WriteLine($"Texture unreadable: {path}");
                continue;
            }

            if (tile.Width != TileSize || tile.Height != TileSize)
            {
                Raylib_cs.Raylib.ImageResizeNN(ref tile, TileSize, TileSize);
            }

            Raylib_cs.Raylib.ImageDraw(
                ref atlas,
                tile,
                new Rectangle(0, 0, TileSize, TileSize),
                new Rectangle(0, (i + 1) * TileSize, TileSize, TileSize),
                Color.White);
            Raylib_cs.Raylib.UnloadImage(tile);
        }

        _atlasTexture = Raylib_cs.Raylib.LoadTextureFromImage(atlas);
        Raylib_cs.Raylib.UnloadImage(atlas);

        // Pixel-sharp, and no mipmaps, which would blend neighboring tiles of the strip.
        Raylib_cs.Raylib.SetTextureFilter(_atlasTexture, TextureFilter.Point);
        Raylib_cs.Raylib.SetMaterialTexture(ref _material, MaterialMapIndex.Albedo, _atlasTexture);

        Shader shader = Raylib_cs.Raylib.LoadShaderFromMemory(TerrainVertexShader, TerrainFragmentShader);
        // Raylib can return its default shader on failure.
        if (Raylib_cs.Raylib.IsShaderValid(shader) && shader.Id != Rlgl.GetShaderIdDefault())
        {
            int parametersLocation = Raylib_cs.Raylib.GetShaderLocation(shader, "fogParameters");
            int colorLocation = Raylib_cs.Raylib.GetShaderLocation(shader, "fogColor");
            if (parametersLocation >= 0 && colorLocation >= 0)
            {
                _material.Shader = shader;
                _fogParametersLocation = parametersLocation;
                _fogColorLocation = colorLocation;
                return;
            }

            Raylib_cs.Raylib.UnloadShader(shader);
        }

        Console.Error.WriteLine("Terrain fog shader unavailable. Drawing without distance fog.");
    }

    /// <inheritdoc/>
    public float AspectRatio
        => Raylib_cs.Raylib.GetRenderWidth() / (float)Math.Max(1, Raylib_cs.Raylib.GetRenderHeight());

    /// <inheritdoc/>
    /// <remarks>
    /// Retains only the index data on the CPU after upload; the vertex data lives on the GPU. Texture coordinates address the atlas
    /// of <see cref="BlockPalette.Atlas"/>; vertex colors tint the texture. Normals are not uploaded:
    /// the terrain shader does not read them, so they would only cost video memory and upload time.
    /// </remarks>
    public MeshHandle UploadMesh(MeshData data)
    {
        Mesh mesh = default;
        mesh.VertexCount = data.VertexCount;
        mesh.TriangleCount = data.IndexCount / 3;
        mesh.Vertices = CopyToRaylibMemory(data.Positions);
        mesh.TexCoords = CopyToRaylibMemory(data.TexCoords);
        mesh.Colors = CopyToRaylibMemory(data.Colors);
        mesh.Indices = CopyToRaylibMemory(data.Indices);

        Raylib_cs.Raylib.UploadMesh(ref mesh, false);

        // The GPU now holds the data; drop the large CPU copies (matters on integrated graphics).
        // Indices must stay: DrawMesh draws non-indexed when mesh.Indices is null. UnloadMesh frees them.
        Raylib_cs.Raylib.MemFree(mesh.Vertices);
        Raylib_cs.Raylib.MemFree(mesh.TexCoords);
        Raylib_cs.Raylib.MemFree(mesh.Colors);
        mesh.Vertices = null;
        mesh.TexCoords = null;
        mesh.Colors = null;

        int id = ++_nextId;
        _meshes[id] = mesh;
        UploadBytes.Record(((long)data.VertexCount * ((5 * sizeof(float)) + 4)) + ((long)data.IndexCount * sizeof(ushort)));
        return new MeshHandle(id);
    }

    /// <inheritdoc/>
    public void ReleaseMesh(MeshHandle handle)
    {
        if (_meshes.Remove(handle.Id, out Mesh mesh))
        {
            Raylib_cs.Raylib.UnloadMesh(mesh);
        }
    }

    /// <inheritdoc/>
    public void BeginFrame(CameraView camera, NerdcraftColor clearColor, DistanceFog fog)
    {
        Rlgl.SetClipPlanes(camera.NearPlane, camera.FarPlane);
        Raylib_cs.Raylib.BeginDrawing();
        Raylib_cs.Raylib.ClearBackground(new Color(clearColor.R, clearColor.G, clearColor.B, clearColor.A));
        Raylib_cs.Raylib.BeginMode3D(new Camera3D(
            camera.Position, camera.Target, camera.Up, camera.FovYDegrees, CameraProjection.Perspective));

        if (_fogParametersLocation >= 0)
        {
            Vector4 fogParameters = new(camera.Position.X, camera.Position.Z, fog.StartBlocks * fog.StartBlocks, fog.EndBlocks * fog.EndBlocks);
            Vector3 fogColor = new Vector3(clearColor.R, clearColor.G, clearColor.B) / 255f;
            Raylib_cs.Raylib.SetShaderValue(_material.Shader, _fogParametersLocation, fogParameters, ShaderUniformDataType.Vec4);
            Raylib_cs.Raylib.SetShaderValue(_material.Shader, _fogColorLocation, fogColor, ShaderUniformDataType.Vec3);
        }
    }

    /// <inheritdoc/>
    public void DrawMesh(MeshHandle handle, Matrix4x4 transform)
    {
        EndTranslucentPass();
        DrawNativeMesh(handle, transform);
    }

    /// <inheritdoc/>
    /// <remarks>Disables depth writes across consecutive translucent draws and restores them for opaque drawing or frame end.</remarks>
    public void DrawTranslucentMesh(MeshHandle handle, Matrix4x4 transform)
    {
        if (!_drawingTranslucent)
        {
            Rlgl.DrawRenderBatchActive();
            Rlgl.DisableDepthMask();
            _drawingTranslucent = true;
        }

        DrawNativeMesh(handle, transform);
    }

    // System.Numerics is row-major (translation in M41..M43); raylib expects column-major.
    private void DrawNativeMesh(MeshHandle handle, Matrix4x4 transform)
        => Raylib_cs.Raylib.DrawMesh(_meshes[handle.Id], _material, Matrix4x4.Transpose(transform));

    private void EndTranslucentPass()
    {
        if (_drawingTranslucent)
        {
            Rlgl.DrawRenderBatchActive();
            Rlgl.EnableDepthMask();
            _drawingTranslucent = false;
        }
    }

    /// <inheritdoc/>
    public void DrawBoxOutline(Aabb box, NerdcraftColor color)
    {
        EndTranslucentPass();
        Vector3 size = box.Max - box.Min;
        Raylib_cs.Raylib.DrawCubeWires(
            box.Min + (size * 0.5f), size.X, size.Y, size.Z, new Color(color.R, color.G, color.B, color.A));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Draws two short bars centered on the screen when the HUD's crosshair is set, then the hotbar: each slot a dark box with
    /// a white border (thicker when selected), an inset picture of the block's texture and its count when above one. Draws the FPS counter,
    /// camera position, chunks drawn / meshed and vertices drawn (in millions) when
    /// <paramref name="debug"/> is set. When <paramref name="menu"/> is set, dims the frame and draws each button
    /// with its label centered.
    /// </remarks>
    public void EndFrame(HudView? hud, DebugOverlay? debug, PauseMenuView? menu)
    {
        EndTranslucentPass();
        Raylib_cs.Raylib.EndMode3D();
        if (hud is { } view)
        {
            DrawHud(view);
        }

        if (debug is { } overlay)
        {
            Vector3 position = overlay.CameraPosition;
            Raylib_cs.Raylib.DrawFPS(10, 10);
            Raylib_cs.Raylib.DrawText(
                $"Pos: {position.X:F1} {position.Y:F1} {position.Z:F1}", 10, 34, 20, Color.White);
            Raylib_cs.Raylib.DrawText(
                $"Chunks: {overlay.Stats.ChunksDrawn} / {overlay.Stats.ChunksMeshed}  Verts: {overlay.Stats.VerticesDrawn / 1_000_000d:F2}M",
                10, 58, 20, Color.White);
        }

        if (menu is { } pause)
        {
            Raylib_cs.Raylib.DrawRectangle(
                0, 0, Raylib_cs.Raylib.GetScreenWidth(), Raylib_cs.Raylib.GetScreenHeight(), new Color(0, 0, 0, 150));
            foreach (MenuButton button in pause.Buttons)
            {
                int x = (int)button.Bounds.X;
                int y = (int)button.Bounds.Y;
                int width = (int)button.Bounds.Width;
                int height = (int)button.Bounds.Height;
                Raylib_cs.Raylib.DrawRectangle(x, y, width, height, button.Hovered ? new Color(110, 110, 110, 255) : new Color(70, 70, 70, 255));
                Raylib_cs.Raylib.DrawRectangleLines(x, y, width, height, Color.White);
                int textWidth = Raylib_cs.Raylib.MeasureText(button.Label, 24);
                Raylib_cs.Raylib.DrawText(button.Label, x + ((width - textWidth) / 2), y + ((height - 24) / 2), 24, Color.White);
            }
        }

        Raylib_cs.Raylib.EndDrawing();
    }

    private void DrawHud(HudView hud)
    {
        if (hud.Crosshair)
        {
            int centerX = Raylib_cs.Raylib.GetScreenWidth() / 2;
            int centerY = Raylib_cs.Raylib.GetScreenHeight() / 2;
            Color crosshairColor = new(255, 255, 255, 200);
            Raylib_cs.Raylib.DrawRectangle(centerX - 1, centerY - 8, 2, 16, crosshairColor);
            Raylib_cs.Raylib.DrawRectangle(centerX - 8, centerY - 1, 16, 2, crosshairColor);
        }

        TextureAtlas atlas = _atlas;
        foreach (HotbarSlotView slot in hud.Hotbar)
        {
            int x = (int)slot.Bounds.X;
            int y = (int)slot.Bounds.Y;
            int width = (int)slot.Bounds.Width;
            int height = (int)slot.Bounds.Height;
            Raylib_cs.Raylib.DrawRectangle(x, y, width, height, new Color(0, 0, 0, 120));
            if (slot.Icon is { } icon)
            {
                Raylib_cs.Raylib.DrawTexturePro(
                    _atlasTexture,
                    new Rectangle(0, atlas.IndexOf(icon) * TileSize, TileSize, TileSize),
                    new Rectangle(x + 8, y + 8, width - 16, height - 16),
                    Vector2.Zero,
                    0f,
                    Color.White);
            }

            Raylib_cs.Raylib.DrawRectangleLines(x, y, width, height, Color.White);
            if (slot.Selected)
            {
                Raylib_cs.Raylib.DrawRectangleLines(x - 1, y - 1, width + 2, height + 2, Color.White);
                Raylib_cs.Raylib.DrawRectangleLines(x + 1, y + 1, width - 2, height - 2, Color.White);
            }

            if (slot.Count > 1)
            {
                string count = slot.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Raylib_cs.Raylib.DrawText(count, x + width - Raylib_cs.Raylib.MeasureText(count, 20) - 3, y + height - 22, 20, Color.White);
            }
        }

        if (hud.HoverText is { } hoverText && hud.HoverOpacity > 0f)
        {
            foreach (HotbarSlotView slot in hud.Hotbar)
            {
                if (slot.Selected)
                {
                    DrawHoverText(hoverText, slot.Bounds, hud.HoverOpacity);
                    break;
                }
            }
        }
    }

    // Centered above the slot, with a one-pixel black shadow.
    private static void DrawHoverText(string text, UiRect slot, float opacity)
    {
        const int FontSize = 24;
        const int Gap = 4;
        int x = (int)(slot.X + (slot.Width / 2f)) - (Raylib_cs.Raylib.MeasureText(text, FontSize) / 2);
        int y = (int)slot.Y - FontSize - Gap;
        byte alpha = (byte)MathF.Round(opacity * 255f);

        Raylib_cs.Raylib.DrawText(text, x + 1, y + 1, FontSize, new Color((byte)0, (byte)0, (byte)0, alpha));
        Raylib_cs.Raylib.DrawText(text, x, y, FontSize, new Color((byte)255, (byte)255, (byte)255, alpha));
    }

    /// <summary>On the graphics thread, unloads remaining meshes and the shared material, including its atlas texture and any custom shader.</summary>
    public void Dispose()
    {
        foreach (Mesh mesh in _meshes.Values)
        {
            Raylib_cs.Raylib.UnloadMesh(mesh);
        }

        _meshes.Clear();
        Raylib_cs.Raylib.UnloadMaterial(_material);
    }

    // Raylib frees mesh memory with its own allocator, so buffers must come from it.
    private static T* CopyToRaylibMemory<T>(ReadOnlySpan<T> source)
        where T : unmanaged
    {
        T* destination = (T*)Raylib_cs.Raylib.MemAlloc((uint)(source.Length * sizeof(T)));
        source.CopyTo(new Span<T>(destination, source.Length));
        return destination;
    }
}
