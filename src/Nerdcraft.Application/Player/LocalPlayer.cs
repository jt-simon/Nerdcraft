// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Numerics;

using Nerdcraft.Application.Input;
using Nerdcraft.Application.Rendering;
using Nerdcraft.Domain.Entities;
using Nerdcraft.Domain.Physics;
using Nerdcraft.Domain.World;

namespace Nerdcraft.Application.Player;

/// <summary>
/// The player this game instance controls. It turns per-frame <see cref="InputState"/> into <see cref="PlayerCommand"/>s
/// and runs <see cref="PlayerMovement.Tick"/> at a fixed <see cref="PlayerMovement.TicksPerSecond"/>, whatever the frame rate.
/// Look follows the pointer every frame; the eye position is interpolated between ticks so motion stays smooth.
/// </summary>
public sealed class LocalPlayer
{
    /// <summary>Largest allowed pitch in either direction, just short of straight up or down.</summary>
    public const float MaxPitchDegrees = 89f;

    /// <summary>How far, in blocks, the player can target a block.</summary>
    public const float ReachBlocks = 5f;

    private const float DegreesPerPixel = 0.1f;
    private const float DoubleTapSeconds = 0.3f;
    private const int MaxTicksPerFrame = 5;
    private const float TickSeconds = 1f / PlayerMovement.TicksPerSecond;

    private readonly VoxelWorld _world;
    private readonly WorldBounds _bounds;
    private readonly PlayerAbilities _abilities;

    private PlayerState _previous;
    private PlayerState _current;
    private float _yawDegrees;
    private float _pitchDegrees;
    private float _tickLeftoverSeconds;

    private Vector2 _move;
    private bool _jumpHeld;
    private bool _sneak;
    private bool _forwardHeld;
    private float _secondsSinceForwardPress = float.PositiveInfinity;

    // Requests made on a frame without a tick are kept until a tick acts on them, so none are lost.
    private bool _jumpTapped;
    private bool _sprintRequested;
    private bool _toggleFly;
    private bool _toggleNoclip;

    /// <summary>Creates a player standing, or falling, in <paramref name="world"/>.</summary>
    /// <param name="initial">Where the player starts; its yaw and pitch are the starting look direction.</param>
    /// <param name="world">The world to move through. Read only; use it from the thread that owns it.</param>
    /// <param name="bounds">The world's vertical extent.</param>
    /// <param name="abilities">What the player may do.</param>
    public LocalPlayer(PlayerState initial, VoxelWorld world, WorldBounds bounds, PlayerAbilities abilities)
    {
        _previous = initial;
        _current = initial;
        _world = world;
        _bounds = bounds;
        _abilities = abilities;
        _yawDegrees = initial.YawDegrees;
        _pitchDegrees = Math.Clamp(initial.PitchDegrees, -MaxPitchDegrees, MaxPitchDegrees);
    }

    /// <summary>The player as of the latest movement tick.</summary>
    public PlayerState State => _current;

    /// <summary>Yaw 0 looks toward -Z; positive yaw turns right.</summary>
    public float YawDegrees => _yawDegrees;

    /// <summary>Look elevation in degrees; positive looks up, within ±<see cref="MaxPitchDegrees"/>.</summary>
    public float PitchDegrees => _pitchDegrees;

    /// <summary>Unit vector in the direction the player is looking.</summary>
    public Vector3 Forward
    {
        get
        {
            float yaw = _yawDegrees * (MathF.PI / 180f);
            float pitch = _pitchDegrees * (MathF.PI / 180f);
            float horizontal = MathF.Cos(pitch);
            return new Vector3(MathF.Sin(yaw) * horizontal, MathF.Sin(pitch), -MathF.Cos(yaw) * horizontal);
        }
    }

    /// <summary>Eye position in world space, interpolated between the last two movement ticks.</summary>
    public Vector3 EyePosition
        => Vector3.Lerp(_previous.EyePosition, _current.EyePosition, Math.Clamp(_tickLeftoverSeconds / TickSeconds, 0f, 1f));

    /// <summary>
    /// Applies look immediately, then runs as many movement ticks as <paramref name="deltaSeconds"/> allows (at most five;
    /// time beyond that is dropped). A jump tapped between two ticks still counts, and toggle and sprint requests wait for a tick that can act on them.
    /// Tapping forward twice within 0.3 s requests sprinting.
    /// </summary>
    /// <param name="input">This frame's input.</param>
    /// <param name="deltaSeconds">Time since the previous frame, in seconds.</param>
    public void Update(InputState input, float deltaSeconds)
    {
        _yawDegrees = (_yawDegrees + (input.LookDelta.X * DegreesPerPixel)) % 360f;
        _pitchDegrees = Math.Clamp(
            _pitchDegrees - (input.LookDelta.Y * DegreesPerPixel), -MaxPitchDegrees, MaxPitchDegrees);

        _secondsSinceForwardPress += deltaSeconds;
        bool forward = input.Move.Y > 0f;
        if (forward && !_forwardHeld)
        {
            _sprintRequested |= _secondsSinceForwardPress <= DoubleTapSeconds;
            _secondsSinceForwardPress = 0f;
        }

        _forwardHeld = forward;
        _toggleFly |= input.ToggleFly;
        _toggleNoclip |= input.ToggleNoclip;
        _move = input.Move;
        _jumpHeld = input.Jump;
        _jumpTapped |= input.Jump;
        _sneak = input.Sneak;

        _tickLeftoverSeconds += deltaSeconds;
        int ticks = 0;
        while (_tickLeftoverSeconds >= TickSeconds && ticks < MaxTicksPerFrame)
        {
            Tick();
            _tickLeftoverSeconds -= TickSeconds;
            ticks++;
        }

        _tickLeftoverSeconds = Math.Min(_tickLeftoverSeconds, TickSeconds);
    }

    /// <summary>
    /// Forgets held keys, the double-tap timer and any request still waiting for a tick, so input from before a pause can't act
    /// after it. Look direction and position are kept.
    /// </summary>
    public void ClearPendingInput()
    {
        _move = Vector2.Zero;
        _jumpHeld = false;
        _sneak = false;
        _forwardHeld = false;
        _secondsSinceForwardPress = float.PositiveInfinity;
        _jumpTapped = false;
        _sprintRequested = false;
        _toggleFly = false;
        _toggleNoclip = false;
    }

    /// <summary>Builds the view description a renderer needs for the current look direction and interpolated eye position.</summary>
    public CameraView ToView(float fovYDegrees, float nearPlane, float farPlane)
    {
        Vector3 eye = EyePosition;
        return new CameraView(eye, eye + Forward, Vector3.UnitY, fovYDegrees, nearPlane, farPlane);
    }

    /// <summary>
    /// The solid block the player is looking at within <see cref="ReachBlocks"/>, or <see langword="null"/> when there is none.
    /// Water is looked through, and the block the eye is in is ignored, so in noclip inside terrain the target is the block
    /// ahead, not the one around the camera. The ray starts at the interpolated <see cref="EyePosition"/> along <see cref="Forward"/>,
    /// so the result matches what <see cref="ToView"/> renders.
    /// </summary>
    public RaycastHit? Target() => VoxelRaycast.Cast(_world, EyePosition, Forward, ReachBlocks, ignoreStartBlock: true);

    private void Tick()
    {
        PlayerCommand command = new(
            _move, _yawDegrees, _pitchDegrees, _jumpHeld || _jumpTapped, _sneak, _sprintRequested, _toggleFly, _toggleNoclip);
        _jumpTapped = false;

        // While the chunk under the player is missing the tick ignores the command, so keep the one-tick requests for later.
        if (PlayerMovement.IsReady(_current, _world, _bounds))
        {
            _sprintRequested = false;
            _toggleFly = false;
            _toggleNoclip = false;
        }

        _previous = _current;
        _current = PlayerMovement.Tick(_current, command, _world, _bounds, _abilities);
    }
}
