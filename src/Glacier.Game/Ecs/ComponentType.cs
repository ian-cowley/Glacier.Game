namespace Glacier.Game.Ecs;

using System;
using System.Collections.Concurrent;
using System.Threading;

/// <summary>
/// Global registry assigning sequential integer IDs to unmanaged component types.
/// </summary>
public static class ComponentTypeRegistry
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<Type, int> _typeToId = new();

    public static int GetId<T>() where T : unmanaged
    {
        return _typeToId.GetOrAdd(typeof(T), _ => Interlocked.Increment(ref _nextId) - 1);
    }

    public static int Count => _nextId;
}

/// <summary>
/// Static metadata and constant ID for component type T.
/// </summary>
public static unsafe class ComponentType<T> where T : unmanaged
{
    public static readonly int Id = ComponentTypeRegistry.GetId<T>();
    public static readonly int Size = sizeof(T);
    public static readonly string Name = typeof(T).Name;
}
