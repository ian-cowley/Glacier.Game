namespace Glacier.Game.Audio;

using System;

/// <summary>
/// Immutable 32-bit floating-point PCM audio buffer for game sound effects and ambient tracks.
/// </summary>
public sealed class AudioClip
{
    private readonly float[] _samples;

    public int SampleRate { get; }
    public int Channels { get; }
    public int SampleCount => _samples.Length;
    public float DurationSec => Channels > 0 && SampleRate > 0 ? (float)_samples.Length / (SampleRate * Channels) : 0f;
    public ReadOnlySpan<float> Samples => _samples;
    public ReadOnlyMemory<float> Memory => _samples;

    public AudioClip(float[] samples, int sampleRate = 48000, int channels = 2)
    {
        _samples = samples ?? throw new ArgumentNullException(nameof(samples));
        SampleRate = sampleRate;
        Channels = channels;
    }

    public AudioClip(ReadOnlySpan<float> samples, int sampleRate = 48000, int channels = 2)
    {
        _samples = samples.ToArray();
        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>
    /// Generates a synthesized sine wave tone for sound effects.
    /// </summary>
    public static AudioClip CreateSineWave(float frequencyHz, float durationSec, float volume = 0.5f, int sampleRate = 48000, int channels = 2)
    {
        int totalFrames = (int)(sampleRate * durationSec);
        int totalSamples = totalFrames * channels;
        float[] buffer = new float[totalSamples];

        float step = 2.0f * MathF.PI * frequencyHz / sampleRate;
        float phase = 0f;
        float vol = Math.Clamp(volume, 0f, 1f);

        for (int i = 0; i < totalFrames; i++)
        {
            float s = MathF.Sin(phase) * vol;
            phase += step;
            if (phase >= 2.0f * MathF.PI) phase -= 2.0f * MathF.PI;

            for (int c = 0; c < channels; c++)
            {
                buffer[i * channels + c] = s;
            }
        }

        return new AudioClip(buffer, sampleRate, channels);
    }

    /// <summary>
    /// Generates a synthesized noise burst for impact or explosion sound effects.
    /// </summary>
    public static AudioClip CreateNoise(float durationSec, float volume = 0.5f, int sampleRate = 48000, int channels = 2)
    {
        int totalFrames = (int)(sampleRate * durationSec);
        int totalSamples = totalFrames * channels;
        float[] buffer = new float[totalSamples];
        var rng = new Random(42);

        float vol = Math.Clamp(volume, 0f, 1f);
        for (int i = 0; i < totalSamples; i++)
        {
            buffer[i] = ((float)rng.NextDouble() * 2f - 1f) * vol;
        }

        return new AudioClip(buffer, sampleRate, channels);
    }
}
