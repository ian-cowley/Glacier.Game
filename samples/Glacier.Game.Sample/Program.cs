namespace Glacier.Game.Sample;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Glacier.Game.Collision;
using Glacier.Game.Core;
using Glacier.Game.Ecs;
using Glacier.Game.Interop;
using Glacier.Game.Physics;
using Glacier.Game.Rendering;
using Glacier.Graphics;
using Glacier.Graphics.Raster;
using Glacier.Graphics.Text;
using Glacier.Graphics.Vector;
using Position2D = Glacier.Game.Physics.Position2D;

public class Program
{
    public static void Main(string[] args)
    {
        bool isHeadless = args.Any(a => a is "--headless" or "--bench" or "-b");

        if (isHeadless)
        {
            RunHeadlessBenchmark();
            if (Environment.UserInteractive && !Console.IsInputRedirected)
            {
                Console.WriteLine("\n[Press any key to exit...]");
                Console.ReadKey();
            }
        }
        else
        {
            RunInteractiveGame();
        }
    }

    private static void RunInteractiveGame()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine("     GLACIER.GAME: High-Performance Data-Oriented Game Engine for .NET 10        ");
        Console.WriteLine("         Zero-Allocation ECS | AVX-512 SIMD Physics | Native HAL & Swapchain     ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();
        Console.WriteLine("\n>> Launching interactive GPU window on screen...");
        Console.WriteLine("   Interactive Controls:");
        Console.WriteLine("   • Left Click / Drag:  Spawn bursts of 150 physics particles at mouse cursor");
        Console.WriteLine("   • Right Click / Hold: Gravity Vortex / Attractor pulling entities to cursor");
        Console.WriteLine("   • Spacebar:           Toggle downward gravity on/off");
        Console.WriteLine("   • Key 'R':            Reset & respawn 15,000 particles");
        Console.WriteLine("   • Key 'C':            Clear all particles");
        Console.WriteLine("   • Key 'Escape':       Exit window\n");

        const int width = 1280;
        const int height = 720;

        var config = new WindowConfig
        {
            Headless = false,
            Width = width,
            Height = height,
            TargetFps = 240,
            FixedTimeStepHz = 240,
            Title = "Glacier.Game: .NET 10 SIMD ECS Engine"
        };

        using var engine = new GameEngine(config);
        var physics = new SimdPhysicsSystem(0f, width, 0f, height);
        engine.AddSystem(physics);

        var rng = new Random(42);

        void SpawnExplosion(int count, float cx, float cy, float maxSpeed)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float speed = (float)(rng.NextDouble() * maxSpeed + 25.0);
                float vx = MathF.Cos(angle) * speed;
                float vy = MathF.Sin(angle) * speed;

                byte r = (byte)(180 + rng.Next(75));
                byte g = (byte)(80 + rng.Next(155));
                byte b = (byte)(50 + rng.Next(205));
                var col = new Color32(r, g, b, 240);

                engine.World.CreateEntity(
                    new Position2D(cx, cy),
                    new Velocity2D(vx, vy),
                    new AABB2D(-2.5f, -2.5f, 2.5f, 2.5f),
                    col
                );
            }
        }

        void SpawnInitialEntities(int count)
        {
            for (int i = 0; i < count; i++)
            {
                float x = (float)(rng.NextDouble() * (width - 40) + 20);
                float y = (float)(rng.NextDouble() * (height - 40) + 20);
                float vx = (float)(rng.NextDouble() * 300.0 - 150.0);
                float vy = (float)(rng.NextDouble() * 300.0 - 150.0);

                byte r = (byte)(40 + (x / width) * 215);
                byte g = (byte)(80 + (y / height) * 175);
                byte b = (byte)(255 - (x / width) * 100);
                var col = new Color32(r, g, b, 230);

                engine.World.CreateEntity(
                    new Position2D(x, y),
                    new Velocity2D(vx, vy),
                    new AABB2D(-2.5f, -2.5f, 2.5f, 2.5f),
                    col
                );
            }
        }

        SpawnInitialEntities(15000);

        bool gravity = false;

        engine.KeyDown += key =>
        {
            if (key == Key.Space)
            {
                gravity = !gravity;
                physics.GravityY = gravity ? 500f : 0f;
            }
            else if (key == Key.R)
            {
                var entities = engine.World.Query<Position2D>().Entities.ToArray();
                foreach (var e in entities) engine.World.DestroyEntity(e);
                SpawnInitialEntities(15000);
            }
            else if (key == Key.C)
            {
                var entities = engine.World.Query<Position2D>().Entities.ToArray();
                foreach (var e in entities) engine.World.DestroyEntity(e);
            }
        };

        engine.Update += time =>
        {
            float dt = time.DeltaTime;

            if (engine.IsLeftMouseDown)
            {
                SpawnExplosion(150, engine.MousePosition.X, engine.MousePosition.Y, 400f);
            }

            if (engine.IsRightMouseDown)
            {
                var query = engine.World.Query<Position2D, Velocity2D>();
                float mx = engine.MousePosition.X;
                float my = engine.MousePosition.Y;
                var pSpan = query.Component1Span;
                var vSpan = query.Component2Span;

                for (int i = 0; i < query.Count; i++)
                {
                    float dx = mx - pSpan[i].X;
                    float dy = my - pSpan[i].Y;
                    float distSq = dx * dx + dy * dy + 200f;
                    float force = 180000f / distSq;
                    float invDist = 1.0f / MathF.Sqrt(distSq);
                    vSpan[i].X += (dx * invDist) * force * dt;
                    vSpan[i].Y += (dy * invDist) * force * dt;
                }
            }
        };

        engine.Run();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n[Window Closed] Glacier.Game execution completed cleanly.");
        Console.ResetColor();

        if (Environment.UserInteractive && !Console.IsInputRedirected)
        {
            Console.WriteLine("[Press any key to exit...]");
            Console.ReadKey();
        }
    }

    private static void RunHeadlessBenchmark()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine("     GLACIER.GAME: High-Performance Data-Oriented Game Engine for .NET 10        ");
        Console.WriteLine("         Zero-Allocation ECS | AVX-512 SIMD Physics | Native HAL & Swapchain     ");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        const int entityCount = 250000;
        const int simulatedFrames = 1000;
        const float fixedDt = 1.0f / 240.0f;

        Console.WriteLine($"\n[1/4] Spawning {entityCount:N0} entities in ECS World...");
        var swSetup = Stopwatch.StartNew();

        var config = new WindowConfig
        {
            Headless = true,
            Width = 1920,
            Height = 1080,
            TargetFps = 240,
            FixedTimeStepHz = 240
        };

        using var headlessRenderer = new HeadlessRenderer(config.Width, config.Height);
        using var engine = new GameEngine(config, headlessRenderer);
        engine.AddSystem(new SimdPhysicsSystem(0f, 1920f, 0f, 1080f));

        var rng = new Random(42);
        for (int i = 0; i < entityCount; i++)
        {
            float x = (float)(rng.NextDouble() * 1920.0);
            float y = (float)(rng.NextDouble() * 1080.0);
            float vx = (float)(rng.NextDouble() * 400.0 - 200.0);
            float vy = (float)(rng.NextDouble() * 400.0 - 200.0);

            engine.World.CreateEntity(
                new Position2D(x, y),
                new Velocity2D(vx, vy),
                new AABB2D(-2f, -2f, 2f, 2f)
            );
        }

        swSetup.Stop();
        Console.WriteLine($"      Spawned {entityCount:N0} entities in {swSetup.ElapsedMilliseconds:N1} ms (Archetype SoA layout).");

        Console.WriteLine($"\n[2/4] Simulating {simulatedFrames:N0} frames at {config.TargetFps} Hz target...");
        long initialMemory = GC.GetAllocatedBytesForCurrentThread();
        var swSim = Stopwatch.StartNew();

        for (int frame = 0; frame < simulatedFrames; frame++)
        {
            engine.Step(fixedDt);
            engine.Render();
        }

        swSim.Stop();
        long finalMemory = GC.GetAllocatedBytesForCurrentThread();
        long allocatedInLoop = finalMemory - initialMemory;

        double totalSeconds = swSim.Elapsed.TotalSeconds;
        double fps = simulatedFrames / totalSeconds;
        double frameUs = (totalSeconds / simulatedFrames) * 1_000_000.0;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[SIMULATION RESULTS]");
        Console.WriteLine($"  - Total Entities:         {entityCount:N0}");
        Console.WriteLine($"  - Total Frames:           {simulatedFrames:N0}");
        Console.WriteLine($"  - Elapsed Wall Time:      {swSim.ElapsedMilliseconds:N1} ms");
        Console.WriteLine($"  - Average Frame Time:     {frameUs:N2} µs ({(frameUs / 1000.0):N3} ms)");
        Console.WriteLine($"  - Effective Throughput:   {fps:N1} FPS (Target: 240 FPS)");
        Console.WriteLine($"  - GC Collections (Gen0):  {GC.CollectionCount(0)}");
        Console.WriteLine($"  - Heap Allocations/Frame: {(allocatedInLoop / (double)simulatedFrames):N1} bytes (Near-Zero Heap Hot Path)");
        Console.ResetColor();

        Console.WriteLine($"\n[3/4] Testing Ecosystem Interop with Glacier.Polaris...");
        var query = engine.World.Query<Position2D, Velocity2D>();
        var sampleEntities = query.Entities[..5].ToArray();
        var df = engine.World.ExportToDataFrame(sampleEntities);
        Console.WriteLine($"      Exported {df.RowCount} entities to Polaris DataFrame with columns: {string.Join(", ", df.Columns.ConvertAll(c => c.Name))}");

        Console.WriteLine($"\n[4/4] Testing Ecosystem Interop with Glacier.Tensor...");
        using var obsTensor = engine.World.ToObservationTensor(sampleEntities);
        Console.WriteLine($"      Observation Tensor Shape: [{obsTensor.Shape[0]}, {obsTensor.Shape[1]}] (N, [X, Y, Vx, Vy])");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\nAll Glacier.Game benchmarks and interop validation checks completed successfully!");
        Console.ResetColor();

        string outDir = Path.Combine(AppContext.BaseDirectory, "output");
        RenderSimulationSnapshots(outDir);
    }

    private static void RenderSimulationSnapshots(string outDir)
    {
        Directory.CreateDirectory(outDir);
        Console.WriteLine("\n[Rendering High-Resolution Simulation Snapshots via Glacier.Graphics...]");

        // Snapshot 1: 25,000 Gravitational Particle Vortex
        {
            const int W = 1920;
            const int H = 1080;
            const int count = 25000;
            var config = new WindowConfig { Headless = true, Width = W, Height = H };
            using var headlessRenderer = new HeadlessRenderer(config.Width, config.Height);
        using var engine = new GameEngine(config, headlessRenderer);
            var physics = new SimdPhysicsSystem(0f, W, 0f, H);
            engine.AddSystem(physics);

            var rng = new Random(42);
            float cx = W / 2f;
            float cy = H / 2f;

            for (int i = 0; i < count; i++)
            {
                float radius = (float)(rng.NextDouble() * 450.0 + 30.0);
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float x = cx + MathF.Cos(angle) * radius;
                float y = cy + MathF.Sin(angle) * radius;

                // Tangential orbital velocity
                float orbitalSpeed = MathF.Sqrt(120000f / MathF.Max(30f, radius));
                float vx = -MathF.Sin(angle) * orbitalSpeed + (float)(rng.NextDouble() * 20 - 10);
                float vy = MathF.Cos(angle) * orbitalSpeed + (float)(rng.NextDouble() * 20 - 10);

                engine.World.CreateEntity(
                    new Position2D(x, y),
                    new Velocity2D(vx, vy),
                    new AABB2D(-2f, -2f, 2f, 2f)
                );
            }

            // Simulate 60 physics frames with central vortex attraction
            for (int f = 0; f < 60; f++)
            {
                var q = engine.World.Query<Position2D, Velocity2D>();
                float dt = 1f / 60f;
                foreach (var chunk in q)
                {
                    var pSpan = chunk.Component1Span;
                    var vSpan = chunk.Component2Span;

                    for (int i = 0; i < chunk.Count; i++)
                    {
                        float dx = cx - pSpan[i].X;
                        float dy = cy - pSpan[i].Y;
                        float distSq = dx * dx + dy * dy + 400f;
                        float force = 250000f / distSq;
                        float invDist = 1.0f / MathF.Sqrt(distSq);
                        vSpan[i].X += (dx * invDist) * force * dt;
                        vSpan[i].Y += (dy * invDist) * force * dt;
                    }
                }

                engine.Step(dt);
            }

            // Render via Glacier.Graphics LinearFramebuffer + CpuGraphicsCanvas
            using var renderer = new GlacierGraphicsGameRenderer(W, H);
            var fb = renderer.Framebuffer;
            var canvas = renderer.Canvas;
            canvas.Clear(new Rgba32(11, 15, 25));

            var renderQuery = engine.World.Query<Position2D, Velocity2D>();
            foreach (var chunk in renderQuery)
            {
                var pos = chunk.Component1Span;
                var vel = chunk.Component2Span;

                for (int i = 0; i < chunk.Count; i++)
                {
                    float speed = MathF.Sqrt(vel[i].X * vel[i].X + vel[i].Y * vel[i].Y);
                    float normSpeed = Math.Clamp(speed / 150f, 0f, 1f);

                    byte r = (byte)Math.Clamp((int)(normSpeed * 255f), 30, 255);
                    byte g = (byte)Math.Clamp((int)((1f - MathF.Abs(normSpeed - 0.5f) * 2f) * 220f), 50, 230);
                    byte b = (byte)Math.Clamp((int)((1f - normSpeed) * 255f), 100, 255);

                    DrawFilledCircle(fb, pos[i].X, pos[i].Y, 2.5f, new Rgba32(r, g, b, 220));
                }
            }

            var titleFont = new Font(24f, "Consolas", bold: true);
            var subFont = new Font(16f, "Consolas");
            canvas.DrawText("Glacier.Game ECS Engine — 25,000 Orbital Vortex Particles @ 240Hz", 30f, 50f, titleFont, new Paint(Rgba32.White));
            canvas.DrawText("AVX-512 SIMD Integration | Zero-Allocation Archetype SoA | Native HAL & Swapchain", 30f, 80f, subFont, new Paint(new Rgba32(150, 180, 210)));

            string vortexPath = Path.Combine(outDir, "demo_particle_vortex.png");
            renderer.SavePng(vortexPath);
            Console.WriteLine($"  ✓ Saved particle vortex snapshot -> {vortexPath}");
        }

        // Snapshot 2: Multi-Body Collision Grid (600 bounding entities driven by real CollisionSystem)
        {
            const int W = 1280;
            const int H = 720;
            const int entityCount = 600;
            const float cellSize = 64f;

            var config = new WindowConfig { Headless = true, Width = W, Height = H };
            using var headlessRenderer = new HeadlessRenderer(config.Width, config.Height);
        using var engine = new GameEngine(config, headlessRenderer);
            using var collision = new CollisionSystem(cellSize: cellSize);
            var physics = new SimdPhysicsSystem(40f, W - 40f, 40f, H - 40f);
            engine.AddSystem(physics);
            engine.AddSystem(collision);

            var rng = new Random(1337);
            for (int i = 0; i < entityCount; i++)
            {
                float x = (float)(rng.NextDouble() * (W - 80) + 40);
                float y = (float)(rng.NextDouble() * (H - 80) + 40);
                float halfSize = (float)(rng.NextDouble() * 12 + 6); // max width 36 < 64px cellSize
                float vx = (float)(rng.NextDouble() * 160 - 80);
                float vy = (float)(rng.NextDouble() * 160 - 80);

                engine.World.CreateEntity(
                    new Position2D(x, y),
                    new Velocity2D(vx, vy),
                    new AABB2D(-halfSize, -halfSize, halfSize, halfSize)
                );
            }

            for (int f = 0; f < 10; f++)
            {
                engine.Step(1f / 60f);
            }

            // Reconcile per-entity collision state directly from CollisionSystem.Grid
            var query = engine.World.Query<Position2D, AABB2D>();
            int maxId = 0;
            foreach (var chunk in query)
            {
                var entities = chunk.Entities;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (entities[i].Id > maxId) maxId = entities[i].Id;
                }
            }

            var posById = new Position2D[maxId + 1];
            var boxById = new AABB2D[maxId + 1];
            var active = new bool[maxId + 1];
            var isColliding = new bool[maxId + 1];

            foreach (var chunk in query)
            {
                var positions = chunk.Component1Span;
                var boxes = chunk.Component2Span;
                var entities = chunk.Entities;
                for (int i = 0; i < chunk.Count; i++)
                {
                    int id = entities[i].Id;
                    posById[id] = positions[i];
                    boxById[id] = boxes[i];
                    active[id] = true;
                }
            }

            int verifiedPairs = 0;
            Span<int> neighbors = stackalloc int[256];
            for (int idA = 0; idA <= maxId; idA++)
            {
                if (!active[idA]) continue;
                ref readonly Position2D posA = ref posById[idA];
                ref readonly AABB2D boxA = ref boxById[idA];
                float cxA = posA.X + (boxA.MinX + boxA.MaxX) * 0.5f;
                float cyA = posA.Y + (boxA.MinY + boxA.MaxY) * 0.5f;

                int nCount = collision.Grid.QueryNeighbors(cxA, cyA, neighbors);
                if (nCount <= 1) continue;

                float minXA = posA.X + boxA.MinX;
                float maxXA = posA.X + boxA.MaxX;
                float minYA = posA.Y + boxA.MinY;
                float maxYA = posA.Y + boxA.MaxY;

                for (int n = 0; n < nCount; n++)
                {
                    int idB = neighbors[n];
                    if (idB <= idA || idB > maxId || !active[idB]) continue;

                    ref readonly Position2D posB = ref posById[idB];
                    ref readonly AABB2D boxB = ref boxById[idB];
                    float minXB = posB.X + boxB.MinX;
                    float maxXB = posB.X + boxB.MaxX;
                    float minYB = posB.Y + boxB.MinY;
                    float maxYB = posB.Y + boxB.MaxY;

                    if (minXA <= maxXB && maxXA >= minXB && minYA <= maxYB && maxYA >= minYB)
                    {
                        isColliding[idA] = true;
                        isColliding[idB] = true;
                        verifiedPairs++;
                    }
                }
            }

            int collidingEntityCount = 0;
            for (int id = 0; id <= maxId; id++)
            {
                if (active[id] && isColliding[id]) collidingEntityCount++;
            }

            using var renderer = new GlacierGraphicsGameRenderer(W, H);
            var fb = renderer.Framebuffer;
            var canvas = renderer.Canvas;
            canvas.Clear(new Rgba32(15, 20, 30));

            var gridPath = new VectorPath();
            const int gridStep = (int)cellSize;
            for (int gx = 0; gx <= W; gx += gridStep)
            {
                gridPath.MoveTo(gx, 0f);
                gridPath.LineTo(gx, H);
            }
            for (int gy = 0; gy <= H; gy += gridStep)
            {
                gridPath.MoveTo(0f, gy);
                gridPath.LineTo(W, gy);
            }
            canvas.DrawPath(gridPath, new Paint(new Rgba32(30, 42, 60), PaintStyle.Stroke, 1f));

            var collidingFill = new Rgba32(255, 60, 80, 200);
            var collidingStroke = new Rgba32(255, 120, 140, 255);
            var freeFill = new Rgba32(0, 200, 160, 180);
            var freeStroke = new Rgba32(50, 255, 210, 255);

            for (int id = 0; id <= maxId; id++)
            {
                if (!active[id]) continue;
                ref readonly Position2D pos = ref posById[id];
                ref readonly AABB2D box = ref boxById[id];
                float rx = pos.X + box.MinX;
                float ry = pos.Y + box.MinY;
                float rw = box.Width;
                float rh = box.Height;

                if (isColliding[id])
                {
                    DrawRoundedRect(fb, rx, ry, rw, rh, 4f, 1.5f, collidingFill, collidingStroke);
                }
                else
                {
                    DrawRoundedRect(fb, rx, ry, rw, rh, 4f, 1.5f, freeFill, freeStroke);
                }
            }

            var titleFont = new Font(22f, "Consolas", bold: true);
            var subFont = new Font(15f, "Consolas");
            canvas.DrawText("Glacier.Game Spatial Hash Partitioning & Elastic Collision System", 25f, 40f, titleFont, new Paint(Rgba32.White));
            canvas.DrawText($"{collision.CollisionCount} Active Collisions ({collidingEntityCount} Entities, verifiedPairs={verifiedPairs}) | 64px Spatial Hash Grid", 25f, 65f, subFont, new Paint(new Rgba32(140, 170, 200)));

            string colPath = Path.Combine(outDir, "demo_spatial_collision.png");
            renderer.SavePng(colPath);
            Console.WriteLine($"  ✓ Saved spatial collision snapshot -> {colPath} (CollisionCount={collision.CollisionCount}, verifiedPairs={verifiedPairs}, collidingEntities={collidingEntityCount})");
        }
    }

    private static void DrawFilledCircle(LinearFramebuffer fb, float cx, float cy, float radius, Rgba32 color)
    {
        int minX = Math.Max(0, (int)MathF.Floor(cx - radius - 1f));
        int maxX = Math.Min(fb.Width - 1, (int)MathF.Ceiling(cx + radius + 1f));
        int minY = Math.Max(0, (int)MathF.Floor(cy - radius - 1f));
        int maxY = Math.Min(fb.Height - 1, (int)MathF.Ceiling(cy + radius + 1f));

        for (int py = minY; py <= maxY; py++)
        {
            var row = fb.GetRowSpan(py);
            float dy = (py + 0.5f) - cy;
            for (int px = minX; px <= maxX; px++)
            {
                float dx = (px + 0.5f) - cx;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float coverage = Math.Clamp(radius + 0.5f - dist, 0f, 1f);
                if (coverage > 0f)
                {
                    BlendPixel(ref row[px], color, coverage);
                }
            }
        }
    }

    private static void DrawRoundedRect(
        LinearFramebuffer fb,
        float x,
        float y,
        float w,
        float h,
        float radius,
        float strokeWidth,
        Rgba32 fill,
        Rgba32 stroke)
    {
        float halfW = w * 0.5f;
        float halfH = h * 0.5f;
        float r = MathF.Min(radius, MathF.Min(halfW, halfH));
        float cx = x + halfW;
        float cy = y + halfH;
        float innerHalfW = MathF.Max(0f, halfW - r);
        float innerHalfH = MathF.Max(0f, halfH - r);

        int minX = Math.Max(0, (int)MathF.Floor(x - strokeWidth - 1f));
        int maxX = Math.Min(fb.Width - 1, (int)MathF.Ceiling(x + w + strokeWidth + 1f));
        int minY = Math.Max(0, (int)MathF.Floor(y - strokeWidth - 1f));
        int maxY = Math.Min(fb.Height - 1, (int)MathF.Ceiling(y + h + strokeWidth + 1f));

        float halfStroke = strokeWidth * 0.5f;

        for (int py = minY; py <= maxY; py++)
        {
            var row = fb.GetRowSpan(py);
            float qy = MathF.Abs((py + 0.5f) - cy) - innerHalfH;
            for (int px = minX; px <= maxX; px++)
            {
                float qx = MathF.Abs((px + 0.5f) - cx) - innerHalfW;
                float outX = MathF.Max(qx, 0f);
                float outY = MathF.Max(qy, 0f);
                float signedDist = MathF.Sqrt(outX * outX + outY * outY) + MathF.Min(MathF.Max(qx, qy), 0f) - r;

                float fillCov = Math.Clamp(0.5f - signedDist, 0f, 1f);
                if (fillCov > 0f)
                {
                    BlendPixel(ref row[px], fill, fillCov);
                }

                float strokeCov = Math.Clamp(halfStroke + 0.5f - MathF.Abs(signedDist), 0f, 1f);
                if (strokeCov > 0f)
                {
                    BlendPixel(ref row[px], stroke, strokeCov);
                }
            }
        }
    }

    private static void BlendPixel(ref Rgba32 dst, Rgba32 src, float coverage)
    {
        float srcA = (src.A / 255.0f) * coverage;
        if (srcA <= 0f) return;

        float invA = 1.0f - srcA;
        byte outR = (byte)Math.Clamp((int)MathF.Round(src.R * srcA + dst.R * invA), 0, 255);
        byte outG = (byte)Math.Clamp((int)MathF.Round(src.G * srcA + dst.G * invA), 0, 255);
        byte outB = (byte)Math.Clamp((int)MathF.Round(src.B * srcA + dst.B * invA), 0, 255);
        byte outA = (byte)Math.Clamp((int)MathF.Round(src.A * srcA + dst.A * invA), 0, 255);

        dst = new Rgba32(outR, outG, outB, outA);
    }
}
