namespace Glacier.Game.Tests;

using System;
using Glacier.Game.Core;
using Glacier.Game.Ecs;
using Glacier.Game.Physics;
using Glacier.Game.Rendering;
using Xunit;

public class EngineTests
{
    [Fact]
    public void GameEngine_DiscreteStep_UpdatesSystemsAndMetrics()
    {
        var config = new WindowConfig { Headless = true, Width = 800, Height = 600 };
        using var engine = new GameEngine(config);

        engine.AddSystem(new SimdPhysicsSystem(0f, 1000f, 0f, 1000f));

        var entity = engine.World.CreateEntity(new Position2D(100f, 100f), new Velocity2D(50f, 50f));

        engine.Step(0.1f); // 100ms step

        ref readonly var pos = ref engine.World.GetComponent<Position2D>(entity);
        Assert.Equal(105f, pos.X, 3); // 100 + 50 * 0.1
        Assert.Equal(105f, pos.Y, 3);

        Assert.Equal(1, engine.Time.FrameCount);
        Assert.Equal(0.1f, engine.Time.DeltaTime, 3);
    }
}
