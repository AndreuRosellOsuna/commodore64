namespace C64Console.Core;

/// <summary>
/// Synthesises the app's sounds as WAV files in memory, so no audio assets are shipped:
/// a short key click, and a startup chirp in the style of the C64's SID chip (a pulse wave
/// arpeggio with a fast attack and decay).
/// </summary>
public static class C64Sounds
{
    public const int SampleRate = 22050;

    /// <summary>A 15 ms click: a tiny decaying noise burst over a high tick, like a mechanical key.</summary>
    public static byte[] KeyClick() => Wave.ToWav(KeyClickSamples(), SampleRate);

    /// <summary>A rising C major arpeggio, about half a second long.</summary>
    public static byte[] StartupChirp() => Wave.ToWav(StartupChirpSamples(), SampleRate);

    public static short[] KeyClickSamples()
    {
        var length = SampleRate * 15 / 1000;
        var samples = new short[length];
        var noise = new Random(64); // fixed seed: the same click every time
        for (var i = 0; i < length; i++)
        {
            var t = (double)i / SampleRate;
            var envelope = Math.Exp(-t * 400);
            var tick = Math.Sin(2 * Math.PI * 1800 * t);
            var value = (0.6 * tick + 0.4 * (noise.NextDouble() * 2 - 1)) * envelope;
            samples[i] = ToSample(value * 0.35);
        }

        return samples;
    }

    public static short[] StartupChirpSamples()
    {
        // C5, E5, G5, then C6 held a little longer.
        (double Frequency, double Seconds)[] notes = [(523.25, 0.07), (659.25, 0.07), (783.99, 0.07), (1046.50, 0.28)];
        var samples = new List<short>();
        foreach (var (frequency, seconds) in notes)
        {
            var length = (int)(SampleRate * seconds);
            for (var i = 0; i < length; i++)
            {
                var t = (double)i / SampleRate;

                // SID-style pulse wave with a 35% duty cycle, which sounds less hollow than a square wave.
                var phase = t * frequency % 1.0;
                var pulse = phase < 0.35 ? 1.0 : -1.0;

                // ADSR-like envelope: 3 ms attack, then decay towards a sustain level, released at the end.
                var attack = Math.Min(1, t / 0.003);
                var decay = 0.55 + 0.45 * Math.Exp(-t * 30);
                var release = Math.Min(1, (seconds - t) / 0.02);
                samples.Add(ToSample(pulse * attack * decay * release * 0.22));
            }
        }

        return samples.ToArray();
    }

    private static short ToSample(double value) => (short)Math.Round(Math.Clamp(value, -1, 1) * short.MaxValue);
}

/// <summary>Writes 16-bit mono PCM samples as a WAV (RIFF) file.</summary>
public static class Wave
{
    public const int HeaderSize = 44;

    public static byte[] ToWav(short[] samples, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        const short blockAlign = channels * bitsPerSample / 8;
        var dataSize = samples.Length * blockAlign;

        using var stream = new MemoryStream(HeaderSize + dataSize);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);            // size of the fmt chunk
        writer.Write((short)1);      // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * blockAlign); // bytes per second
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
