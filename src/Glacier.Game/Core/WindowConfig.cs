namespace Glacier.Game.Core;

/// <summary>
/// Configuration parameters for the window and engine lifecycle.
/// </summary>
public record WindowConfig
{
    public string Title { get; init; } = "Glacier Game Engine (.NET 10 SIMD)";
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public int TargetFps { get; init; } = 240;
    public int FixedTimeStepHz { get; init; } = 240;
    public bool IsVsync { get; init; } = false;
    public bool IsFullscreen { get; init; } = false;
    public bool Headless { get; init; } = false;
}
