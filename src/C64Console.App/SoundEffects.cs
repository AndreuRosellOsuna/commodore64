using System.IO;
using System.Media;
using C64Console.Core;

namespace C64Console.App;

/// <summary>
/// Plays the synthesised sounds. The WAV data is built once and kept in memory. Sound is optional:
/// any failure (no audio device, a busy device) is ignored.
/// </summary>
internal sealed class SoundEffects : IDisposable
{
    private readonly SoundPlayer _click = Load(C64Sounds.KeyClick());
    private readonly SoundPlayer _chirp = Load(C64Sounds.StartupChirp());

    /// <summary>A new click cuts off the previous one, which is what fast typing should sound like.</summary>
    public void Click() => Play(_click);

    public void Chirp() => Play(_chirp);

    public void Dispose()
    {
        _click.Dispose();
        _chirp.Dispose();
    }

    private static SoundPlayer Load(byte[] wav)
    {
        var player = new SoundPlayer(new MemoryStream(wav));
        player.Load();
        return player;
    }

    private static void Play(SoundPlayer player)
    {
        try
        {
            player.Play(); // asynchronous
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException or IOException)
        {
            // No usable audio device: stay silent.
        }
    }
}
