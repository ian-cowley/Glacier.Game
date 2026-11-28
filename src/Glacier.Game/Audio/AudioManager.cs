namespace Glacier.Game.Audio;

using System;
using System.Buffers;
using System.Collections.Generic;
using Glacier.Desktop.Audio;

/// <summary>
/// High-performance audio subsystem for Glacier.Game driving sub-3ms WASAPI streaming and hardware output.
/// </summary>
public sealed class AudioManager : IDisposable
{
    private readonly IAudioDevice _device;
    private readonly IAudioStream? _stream;
    private readonly List<AudioClip> _registeredClips = new();
    private float _masterVolume = 1.0f;
    private bool _disposed;

    public IAudioDevice Device => _device;
    public IAudioStream? Stream => _stream;
    public int SampleRate => _device.SampleRate;
    public int Channels => _device.Channels;
    public float MasterVolume => _masterVolume;

    public AudioManager(IAudioDevice device, IAudioStream? stream = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _stream = stream;
        _stream?.Start();
    }

    /// <summary>
    /// Registers an AudioClip and returns its assigned integer clip ID.
    /// </summary>
    public int RegisterClip(AudioClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        _registeredClips.Add(clip);
        return _registeredClips.Count - 1;
    }

    /// <summary>
    /// Gets a registered AudioClip by ID.
    /// </summary>
    public AudioClip? GetClip(int id)
    {
        return (uint)id < (uint)_registeredClips.Count ? _registeredClips[id] : null;
    }

    /// <summary>
    /// Plays an audio clip directly through the low-latency hardware device.
    /// </summary>
    public void PlayClip(AudioClip clip) => PlayClip(clip, 1.0f);

    /// <summary>
    /// Plays an audio clip directly through the hardware device scaled by the specified clip volume [0, 1].
    /// </summary>
    public void PlayClip(AudioClip clip, float volume)
    {
        ArgumentNullException.ThrowIfNull(clip);
        float vol = Math.Clamp(volume, 0f, 1f);
        if (vol <= 0f) return;

        ReadOnlySpan<float> samples = clip.Samples;
        if (samples.IsEmpty) return;

        if (vol >= 0.9999f)
        {
            _device.Play(samples);
            return;
        }

        float[] rented = ArrayPool<float>.Shared.Rent(samples.Length);
        try
        {
            Span<float> scaled = rented.AsSpan(0, samples.Length);
            for (int i = 0; i < samples.Length; i++)
                scaled[i] = samples[i] * vol;

            _device.Play(scaled);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Plays a registered audio clip by ID through the hardware device.
    /// </summary>
    public void PlayClip(int clipId, float volume = 1.0f)
    {
        var clip = GetClip(clipId);
        if (clip != null)
        {
            PlayClip(clip, volume);
        }
    }

    /// <summary>
    /// Plays raw PCM floating point samples directly through the hardware device.
    /// </summary>
    public void PlaySamples(ReadOnlySpan<float> pcmSamples)
    {
        _device.Play(pcmSamples);
    }

    /// <summary>
    /// Streams PCM samples into the lock-free audio stream ring buffer.
    /// </summary>
    public bool StreamSamples(ReadOnlySpan<float> pcmSamples)
    {
        return _stream?.Write(pcmSamples) ?? false;
    }

    /// <summary>
    /// Plays a synthesized sine wave tone.
    /// </summary>
    public void PlayTone(float frequencyHz, float durationSec, float volume = 0.5f)
    {
        var clip = AudioClip.CreateSineWave(frequencyHz, durationSec, volume * _masterVolume, _device.SampleRate, _device.Channels);
        _device.Play(clip.Samples);
    }

    /// <summary>
    /// Sets master volume for the device.
    /// </summary>
    public void SetMasterVolume(float volume)
    {
        _masterVolume = Math.Clamp(volume, 0f, 1f);
        _device.SetMasterVolume(_masterVolume);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _stream?.Stop();
            _stream?.Dispose();
            _device.Dispose();
        }
    }
}
