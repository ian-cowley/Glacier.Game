namespace Glacier.Game.Audio;

using System;
using Glacier.Game.Ecs;

/// <summary>
/// Audio component representing sound triggers on entities.
/// Zero-heap unmanaged struct layout compatible with ECS Archetype tables.
/// </summary>
public record struct AudioEmitter(int ClipId, bool PlayOnSpawn = false, bool Loop = false, float Volume = 1.0f);

/// <summary>
/// ECS system that ticks audio emitter components and dispatches to AudioManager.
/// </summary>
public sealed class AudioSystem : ISystem
{
    private readonly AudioManager _audio;

    public AudioSystem(AudioManager audio)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
    }

    public void Update(World world, float dt)
    {
        var query = world.Query<AudioEmitter>();
        for (int i = 0; i < query.Count; i++)
        {
            ref var emitter = ref query.Component1Span[i];
            if (emitter.PlayOnSpawn)
            {
                _audio.PlayClip(emitter.ClipId);
                emitter.PlayOnSpawn = false;
            }
        }
    }
}
