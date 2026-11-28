namespace Glacier.Game.Audio;

using System;
using System.Collections.Generic;
using Glacier.Game.Ecs;

/// <summary>
/// Audio component representing sound triggers on entities.
/// Zero-heap unmanaged struct layout compatible with ECS Archetype tables.
/// </summary>
public record struct AudioEmitter(int ClipId, bool PlayOnSpawn = false, bool Loop = false, float Volume = 1.0f);

/// <summary>
/// ECS system that ticks audio emitter components and dispatches to AudioManager with per-emitter volume and loop scheduling.
/// </summary>
public sealed class AudioSystem : ISystem
{
    private readonly AudioManager _audio;
    private readonly Dictionary<int, float> _loopCooldowns = new();

    public AudioSystem(AudioManager audio)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
    }

    public void Update(World world, float dt)
    {
        foreach (var chunk in world.Query<AudioEmitter>())
        {
            var span = chunk.Component1Span;
            var entities = chunk.Entities;
            for (int i = 0; i < span.Length; i++)
            {
                ref var emitter = ref span[i];
                int entityId = entities[i].Id;

                if (emitter.PlayOnSpawn)
                {
                    _audio.PlayClip(emitter.ClipId, emitter.Volume);
                    emitter.PlayOnSpawn = false;
                    if (emitter.Loop)
                    {
                        _loopCooldowns[entityId] = GetLoopPeriodSeconds(emitter.ClipId);
                    }
                }
                else if (emitter.Loop)
                {
                    float remaining = _loopCooldowns.TryGetValue(entityId, out float prev) ? prev - dt : 0f;
                    if (remaining <= 0f)
                    {
                        _audio.PlayClip(emitter.ClipId, emitter.Volume);
                        _loopCooldowns[entityId] = GetLoopPeriodSeconds(emitter.ClipId);
                    }
                    else
                    {
                        _loopCooldowns[entityId] = remaining;
                    }
                }
            }
        }
    }

    private float GetLoopPeriodSeconds(int clipId)
    {
        float dur = _audio.GetClip(clipId)?.DurationSec ?? 0f;
        return dur > 0f ? dur : 0.016f;
    }
}
