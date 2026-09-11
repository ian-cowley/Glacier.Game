namespace Glacier.Game.Ecs;

/// <summary>
/// Contract for an ECS system executing over World data.
/// </summary>
public interface ISystem
{
    void Update(World world, float deltaTime);
}
