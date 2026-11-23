// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

namespace Nerdcraft.Application.Ui;

/// <summary>An axis-aligned rectangle in window pixels, measured from the top-left corner.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    /// <summary>True when <paramref name="point"/> lies inside the rectangle (left and top edges inclusive).</summary>
    public bool Contains(Vector2 point)
        => point.X >= X && point.X < X + Width && point.Y >= Y && point.Y < Y + Height;
}

/// <summary>One button of the pause menu, ready to draw.</summary>
/// <param name="Label">Text shown on the button.</param>
/// <param name="Bounds">Where the button sits, in window pixels.</param>
/// <param name="Hovered">True when the pointer is over the button.</param>
public readonly record struct MenuButton(string Label, UiRect Bounds, bool Hovered);

/// <summary>What a renderer draws for the pause menu.</summary>
/// <param name="Buttons">The buttons, top to bottom.</param>
public readonly record struct PauseMenuView(IReadOnlyList<MenuButton> Buttons);

/// <summary>Layout of the pause menu. Pure arithmetic, so every renderer shows the same menu.</summary>
public static class PauseMenu
{
    /// <summary>Index of the Resume button in <see cref="PauseMenuView.Buttons"/>.</summary>
    public const int ResumeIndex = 0;

    /// <summary>Index of the Quit button in <see cref="PauseMenuView.Buttons"/>.</summary>
    public const int QuitIndex = 1;

    private const float ButtonWidth = 300f;
    private const float ButtonHeight = 50f;
    private const float Gap = 16f;

    private static readonly string[] Labels = ["Resume", "Quit"];

    /// <summary>Lays the buttons out centered in the window.</summary>
    /// <param name="screenSize">Window size in pixels. Hosts must supply it; a zero size puts the buttons off-screen.</param>
    /// <param name="pointer">Pointer position in pixels; decides which button is hovered.</param>
    public static PauseMenuView Layout(Vector2 screenSize, Vector2 pointer)
    {
        float totalHeight = (Labels.Length * ButtonHeight) + ((Labels.Length - 1) * Gap);

        // Whole pixels, so what a renderer draws and what Contains tests are the same rectangle.
        float left = MathF.Floor((screenSize.X - ButtonWidth) / 2f);
        float top = MathF.Floor((screenSize.Y - totalHeight) / 2f);

        MenuButton[] buttons = new MenuButton[Labels.Length];
        for (int i = 0; i < Labels.Length; i++)
        {
            UiRect bounds = new(left, top + (i * (ButtonHeight + Gap)), ButtonWidth, ButtonHeight);
            buttons[i] = new MenuButton(Labels[i], bounds, bounds.Contains(pointer));
        }

        return new PauseMenuView(buttons);
    }
}
