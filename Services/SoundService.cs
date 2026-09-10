using System.IO;
using System.Media;

namespace ClipDesk.Services;

public sealed class SoundService
{
    private readonly SoundPlayer _added = CreatePlayer([(587, 0.24), (880, 0.08)], 82, 0.055);
    private readonly SoundPlayer _copied = CreatePlayer([(660, 0.2)], 58, 0.045);
    private readonly SoundPlayer _deleted = CreatePlayer([(196, 0.18), (294, 0.05)], 92, 0.05);
    private readonly SoundPlayer _folderChanged = CreatePlayer([(440, 0.18), (659, 0.08)], 86, 0.05);
    private readonly SoundPlayer _error = CreatePlayer([(247, 0.18), (185, 0.06)], 110, 0.045);

    public void Added() => Play(_added);
    public void Copied() => Play(_copied);
    public void Deleted() => Play(_deleted);
    public void FolderChanged() => Play(_folderChanged);
    public void Error() => Play(_error);

    private static SoundPlayer CreatePlayer((double Frequency, double Weight)[] tones, int durationMs, double volume)
    {
        var stream = new MemoryStream(CreateChord(tones, durationMs, volume));
        var player = new SoundPlayer(stream);
        player.Load();
        return player;
    }

    private static void Play(SoundPlayer player)
    {
        try
        {
            player.Stop();
            player.Play();
        }
        catch
        {
            // Sound feedback is nice to have, but it should never block the app action.
        }
    }

    private static byte[] CreateChord((double Frequency, double Weight)[] tones, int durationMs, double volume)
    {
        const int sampleRate = 44100;
        const short bitsPerSample = 16;
        const short channels = 1;

        var sampleCount = sampleRate * durationMs / 1000;
        var data = new byte[sampleCount * 2];

        for (var sample = 0; sample < sampleCount; sample++)
        {
            var time = sample / (double)sampleRate;
            var progress = sample / (double)Math.Max(1, sampleCount - 1);
            var attack = Math.Min(1, progress / 0.08);
            var release = Math.Pow(1 - progress, 2.8);
            var envelope = attack * release;
            var value = 0d;

            foreach (var tone in tones)
            {
                value += Math.Sin(2 * Math.PI * tone.Frequency * time) * tone.Weight;
            }

            value *= envelope * volume;
            var pcm = (short)Math.Clamp(value * short.MaxValue, short.MinValue, short.MaxValue);
            data[sample * 2] = (byte)(pcm & 0xFF);
            data[(sample * 2) + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + data.Length);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(data.Length);
        writer.Write(data);

        return stream.ToArray();
    }
}
