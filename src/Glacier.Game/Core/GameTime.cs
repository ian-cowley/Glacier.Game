namespace Glacier.Game.Core;

using System;

/// <summary>
/// Provides high-precision time values and frame rate metrics for the game loop.
/// </summary>
public sealed class GameTime
{
    private double _fpsAccumulator;
    private int _fpsFrames;

    /// <summary>
    /// Total elapsed time since game engine startup.
    /// </summary>
    public TimeSpan TotalTime { get; internal set; }

    /// <summary>
    /// Time elapsed since the last frame.
    /// </summary>
    public TimeSpan ElapsedTime { get; internal set; }

    /// <summary>
    /// Delta time in seconds for physics and simulation updates.
    /// </summary>
    public float DeltaTime => (float)ElapsedTime.TotalSeconds;

    /// <summary>
    /// Total number of rendered frames.
    /// </summary>
    public long FrameCount { get; internal set; }

    /// <summary>
    /// Real-time measured frames per second (smoothed).
    /// </summary>
    public float FramesPerSecond { get; private set; }

    public GameTime()
    {
        TotalTime = TimeSpan.Zero;
        ElapsedTime = TimeSpan.Zero;
        FrameCount = 0;
        FramesPerSecond = 0f;
    }

    internal void Update(TimeSpan elapsed)
    {
        ElapsedTime = elapsed;
        TotalTime += elapsed;
        FrameCount++;

        _fpsAccumulator += elapsed.TotalSeconds;
        _fpsFrames++;

        if (_fpsAccumulator >= 0.5) // Update FPS reading every 500ms
        {
            FramesPerSecond = (float)(_fpsFrames / _fpsAccumulator);
            _fpsAccumulator = 0.0;
            _fpsFrames = 0;
        }
    }
}
