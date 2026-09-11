namespace Glacier.Game.Core;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Glacier.Game.Collision;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;
using Glacier.Game.Rendering;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using Silk.NET.Input;
using Position2D = Glacier.Game.Physics.Position2D;

/// <summary>
/// High-performance data-oriented 2D game engine orchestrating ECS systems, fixed timestep physics,
/// and batched Silk.NET GPU/headless rendering at 240+ FPS.
/// </summary>
public sealed class GameEngine : IDisposable
{
    private readonly List<ISystem> _systems = new();
    private SpriteBatch _spriteBatch;
    private readonly Stopwatch _stopwatch = new();
    private IWindow? _window;
    private GL? _gl;
    private IInputContext? _input;
    private bool _disposed;
    private double _lastTime;
    private double _accumulator;
    private double _fpsTimer;
    private int _fpsFrames;

    public World World { get; }
    public IRenderer Renderer { get; private set; }
    public GameTime Time { get; }
    public WindowConfig Config { get; }
    public bool IsRunning { get; private set; }

    public IInputContext? Input => _input;
    public Vector2 MousePosition { get; set; }
    public bool IsLeftMouseDown { get; set; }
    public bool IsRightMouseDown { get; set; }

    public event Action? Load;
    public event Action<GameTime>? Update;
    public event Action<GameTime, IRenderer>? RenderFrame;
    public event Action<Key>? KeyDown;

    public GameEngine(WindowConfig? config = null, IRenderer? renderer = null)
    {
        Config = config ?? new WindowConfig();
        World = new World();
        Time = new GameTime();

        Renderer = renderer ?? new HeadlessRenderer(Config.Width, Config.Height);
        _spriteBatch = new SpriteBatch(Renderer, 131072);
    }

    /// <summary>
    /// Registers an ECS system to be ticked on each simulation step.
    /// </summary>
    public GameEngine AddSystem(ISystem system)
    {
        _systems.Add(system);
        return this;
    }

    /// <summary>
    /// Executes a single discrete simulation step with the specified delta time.
    /// </summary>
    public void Step(float dt)
    {
        Time.Update(TimeSpan.FromSeconds(dt));

        for (int i = 0; i < _systems.Count; i++)
        {
            _systems[i].Update(World, dt);
        }

        Update?.Invoke(Time);
    }

    /// <summary>
    /// Starts the game loop. If Headless is configured, runs headless; otherwise opens Silk.NET window.
    /// </summary>
    public void Run()
    {
        if (Config.Headless)
        {
            RunHeadless();
            return;
        }

        var options = WindowOptions.Default;
        options.Title = Config.Title;
        options.Size = new Silk.NET.Maths.Vector2D<int>(Config.Width, Config.Height);
        options.FramesPerSecond = Config.TargetFps;
        options.UpdatesPerSecond = Config.FixedTimeStepHz;
        options.VSync = Config.IsVsync;

        _window = Window.Create(options);

        _window.Load += OnWindowLoad;
        _window.Update += OnWindowUpdate;
        _window.Render += OnWindowRender;
        _window.Closing += OnWindowClosing;

        IsRunning = true;
        _stopwatch.Restart();
        _window.Run();
    }

    private void RunHeadless()
    {
        IsRunning = true;
        Load?.Invoke();
        _stopwatch.Restart();
        _lastTime = 0.0;
        _accumulator = 0.0;
        double fixedDt = 1.0 / Config.FixedTimeStepHz;

        while (IsRunning)
        {
            double currentTime = _stopwatch.Elapsed.TotalSeconds;
            double frameTime = currentTime - _lastTime;
            _lastTime = currentTime;

            if (frameTime > 0.25) frameTime = 0.25;
            _accumulator += frameTime;

            while (_accumulator >= fixedDt)
            {
                Step((float)fixedDt);
                _accumulator -= fixedDt;
            }

            Render();
        }
    }

    private void OnWindowLoad()
    {
        if (_window != null)
        {
            _gl = _window.CreateOpenGL();
            Renderer = new SilkRenderer(_gl, Config.Width, Config.Height);
            _spriteBatch.Dispose();
            _spriteBatch = new SpriteBatch(Renderer, 131072);

            _input = _window.CreateInput();
            foreach (var mouse in _input.Mice)
            {
                mouse.MouseMove += (_, pos) => MousePosition = new Vector2(pos.X, pos.Y);
                mouse.MouseDown += (_, btn) =>
                {
                    if (btn == MouseButton.Left) IsLeftMouseDown = true;
                    if (btn == MouseButton.Right) IsRightMouseDown = true;
                };
                mouse.MouseUp += (_, btn) =>
                {
                    if (btn == MouseButton.Left) IsLeftMouseDown = false;
                    if (btn == MouseButton.Right) IsRightMouseDown = false;
                };
            }
            foreach (var kb in _input.Keyboards)
            {
                kb.KeyDown += (_, key, _) =>
                {
                    KeyDown?.Invoke(key);
                    if (key == Key.Escape) Stop();
                };
            }
        }

        Load?.Invoke();
    }

    private void OnWindowUpdate(double dt)
    {
        Step((float)dt);

        _fpsFrames++;
        _fpsTimer += dt;
        if (_fpsTimer >= 0.25)
        {
            double currentFps = _fpsFrames / _fpsTimer;
            if (_window != null)
            {
                _window.Title = $"{Config.Title} | {World.EntityCount:N0} Entities | {currentFps:F0} FPS ({(dt * 1000.0):F2} ms)";
            }
            _fpsFrames = 0;
            _fpsTimer = 0;
        }
    }

    private void OnWindowRender(double dt)
    {
        Render();
    }

    /// <summary>
    /// Renders all visible entities and invokes the RenderFrame event.
    /// </summary>
    public void Render()
    {
        _spriteBatch.Begin();

        // 1. Render entities having Position2D, AABB2D, and Color32
        var query3 = World.Query<Position2D, AABB2D, Color32>();
        if (query3.Count > 0)
        {
            for (int i = 0; i < query3.Count; i++)
            {
                ref readonly var p = ref query3.Component1Span[i];
                ref readonly var box = ref query3.Component2Span[i];
                ref readonly var col = ref query3.Component3Span[i];
                _spriteBatch.DrawQuad(p.X + box.MinX, p.Y + box.MinY, box.Width, box.Height, col);
            }
        }
        else
        {
            // 2. Fallback for entities with Position2D and AABB2D
            var query2 = World.Query<Position2D, AABB2D>();
            for (int i = 0; i < query2.Count; i++)
            {
                ref readonly var p = ref query2.Component1Span[i];
                ref readonly var box = ref query2.Component2Span[i];
                _spriteBatch.DrawQuad(p.X + box.MinX, p.Y + box.MinY, box.Width, box.Height, new Color32(50, 180, 255, 255));
            }
        }

        _spriteBatch.Flush();
        _spriteBatch.End();

        RenderFrame?.Invoke(Time, Renderer);

        Renderer.Present();
    }

    private void OnWindowClosing()
    {
        IsRunning = false;
    }

    public void Stop()
    {
        IsRunning = false;
        _window?.Close();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _spriteBatch.Dispose();
            Renderer.Dispose();
            _input?.Dispose();
            _gl?.Dispose();
            _window?.Dispose();
            World.Dispose();
            _disposed = true;
        }
    }
}
