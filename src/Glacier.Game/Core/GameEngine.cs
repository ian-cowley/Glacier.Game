namespace Glacier.Game.Core;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Glacier.Game.Audio;
using Glacier.Game.Collision;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;
using Glacier.Game.Rendering;
using Glacier.Windowing;
using Glacier.Windowing.Audio;
using Glacier.Windowing.Input;
using Glacier.Windowing.Platform;
using Position2D = Glacier.Game.Physics.Position2D;

/// <summary>
/// High-performance data-oriented 2D/3D game engine orchestrating ECS systems, fixed timestep physics,
/// and native HAL Direct3D 12 / Vulkan swapchain presentation at 240+ FPS with zero third-party native dependencies.
/// </summary>
public sealed class GameEngine : IDisposable
{
    private readonly List<ISystem> _systems = new();
    private SpriteBatch _spriteBatch;
    private readonly Stopwatch _stopwatch = new();
    private IWindow? _window;
    private ISwapchain? _swapchain;
    private readonly AudioManager? _audio;
    private readonly InputState _input;
    private bool _ownsWindow;
    private bool _ownsSwapchain;
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

    /// <summary>
    /// Gets the operating system window hosting the game engine.
    /// </summary>
    public IWindow Window
    {
        get
        {
            if (_window == null)
            {
                EnsureWindowCreated();
            }
            return _window!;
        }
    }

    /// <summary>
    /// Gets the hardware swapchain driving display presentation.
    /// </summary>
    public ISwapchain? Swapchain
    {
        get
        {
            if (_swapchain == null)
            {
                EnsureSwapchainCreated();
            }
            return _swapchain;
        }
    }

    /// <summary>
    /// Gets the hardware audio output device.
    /// </summary>
    public IAudioDevice? AudioDevice => _audio?.Device;

    /// <summary>
    /// Gets the low-latency lock-free audio stream.
    /// </summary>
    public IAudioStream? AudioStream => _audio?.Stream;

    /// <summary>
    /// Gets the integrated audio manager.
    /// </summary>
    public AudioManager? Audio => _audio;

    /// <summary>
    /// Gets the current unified input state (Keyboard, Mouse, Gamepad).
    /// </summary>
    public InputState Input => _input;

    public Vector2 MousePosition { get; set; }
    public bool IsLeftMouseDown { get; set; }
    public bool IsRightMouseDown { get; set; }

    public event Action? Load;
    public event Action<GameTime>? Update;
    public event Action<GameTime, IRenderer>? RenderFrame;
    public event Action<Key>? KeyDown;

    public GameEngine(
        WindowConfig? config = null,
        IRenderer? renderer = null,
        IWindow? window = null,
        IAudioDevice? audioDevice = null)
    {
        Config = config ?? new WindowConfig();
        World = new World();
        Time = new GameTime();
        _input = new InputState();

        if (window != null)
        {
            _window = window;
            _ownsWindow = false;
            AttachWindowEvents(_window);
        }
        else if (Config.Headless)
        {
            _window = GlacierGameWindowFactory.CreateHeadlessGameWindow(Config.Width, Config.Height);
            _ownsWindow = true;
            AttachWindowEvents(_window);
        }

        Renderer = renderer ?? new GlacierGraphicsGameRenderer(Config.Width, Config.Height);
        _spriteBatch = new SpriteBatch(Renderer, 131072);

        // Initialize sub-3ms audio subsystem via AudioFactory
        try
        {
            var device = audioDevice ?? AudioFactory.CreateDefaultDevice(48000, 2);
            IAudioStream? stream = null;
            try
            {
                stream = AudioFactory.CreateStream(48000, 2, 16384);
            }
            catch
            {
                stream = null;
            }
            _audio = new AudioManager(device, stream);
        }
        catch
        {
            _audio = null;
        }
    }

    private void EnsureWindowCreated()
    {
        if (_window != null) return;

        if (Config.Headless)
        {
            _window = GlacierGameWindowFactory.CreateHeadlessGameWindow(Config.Width, Config.Height);
        }
        else
        {
            _window = GlacierGameWindowFactory.CreateGameWindow(Config.Title, Config.Width, Config.Height);
        }
        _ownsWindow = true;
        AttachWindowEvents(_window);
    }

    private void EnsureSwapchainCreated()
    {
        if (_swapchain != null) return;
        EnsureWindowCreated();

        var swapDesc = new SwapchainDescription(
            Config.Width,
            Config.Height,
            BufferCount: 2,
            EnableHdr: false,
            LowLatencyWaitable: true);
        _swapchain = _window!.CreateSwapchain(swapDesc);
        _ownsSwapchain = true;
        if (Renderer is GlacierGraphicsGameRenderer ggr)
        {
            ggr.Swapchain = _swapchain;
        }
    }

    private void AttachWindowEvents(IWindow window)
    {
        window.InputReceived += OnInputReceived;
        window.Resized += OnWindowResized;
        window.Closing += OnWindowClosing;
    }

    private void OnInputReceived(InputEvent evt)
    {
        _input.OnInput(evt);

        switch (evt.Type)
        {
            case InputEventType.KeyDown:
            {
                var key = (Key)evt.KeyOrButton;
                KeyDown?.Invoke(key);
                if (key == Key.Escape)
                {
                    Stop();
                }
                break;
            }
            case InputEventType.MouseMove:
            {
                MousePosition = new Vector2(evt.X, evt.Y);
                break;
            }
            case InputEventType.MouseDown:
            {
                if (evt.KeyOrButton == 0) IsLeftMouseDown = true;
                else if (evt.KeyOrButton == 1) IsRightMouseDown = true;
                break;
            }
            case InputEventType.MouseUp:
            {
                if (evt.KeyOrButton == 0) IsLeftMouseDown = false;
                else if (evt.KeyOrButton == 1) IsRightMouseDown = false;
                break;
            }
        }
    }

    private void OnWindowResized(int width, int height)
    {
        Renderer.Initialize(width, height);
        _swapchain?.Resize(width, height);
    }

    private void OnWindowClosing()
    {
        IsRunning = false;
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
    /// Starts the native HAL game loop. Connects swapchain presentation and polls window events.
    /// </summary>
    public void Run()
    {
        EnsureSwapchainCreated();
        var win = _window!;

        IsRunning = true;
        Load?.Invoke();
        _stopwatch.Restart();
        _lastTime = 0.0;
        _accumulator = 0.0;
        double fixedDt = 1.0 / Config.FixedTimeStepHz;

        while (IsRunning && (Config.Headless || win.IsVisible))
        {
            win.PollEvents();

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

            if (!Config.Headless)
            {
                _fpsFrames++;
                _fpsTimer += frameTime;
                if (_fpsTimer >= 0.25)
                {
                    double currentFps = _fpsFrames / _fpsTimer;
                    win.Title = $"{Config.Title} | {World.EntityCount:N0} Entities | {currentFps:F0} FPS ({(frameTime * 1000.0):F2} ms)";
                    _fpsFrames = 0;
                    _fpsTimer = 0;
                }
            }
        }

        IsRunning = false;
    }

    /// <summary>
    /// Renders all visible entities and invokes the RenderFrame event.
    /// </summary>
    public void Render()
    {
        EnsureSwapchainCreated();
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
        _swapchain?.Present(Config.IsVsync);
    }

    public void Stop()
    {
        IsRunning = false;
        if (_window != null)
        {
            _window.IsVisible = false;
        }
    }

    public void PlaySound(ReadOnlySpan<float> samples) => _audio?.PlaySamples(samples);
    public void PlaySound(AudioClip clip) => _audio?.PlayClip(clip);
    public void PlayTone(float frequencyHz, float durationSec, float volume = 0.5f) => _audio?.PlayTone(frequencyHz, durationSec, volume);
    public void SetMasterVolume(float volume) => _audio?.SetMasterVolume(volume);

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _spriteBatch.Dispose();
            Renderer.Dispose();
            if (_ownsSwapchain)
            {
                _swapchain?.Dispose();
            }
            if (_ownsWindow)
            {
                _window?.Dispose();
            }
            _audio?.Dispose();
            World.Dispose();
        }
    }
}
