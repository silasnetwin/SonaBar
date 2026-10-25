using NAudio.Wave;

namespace SonaBar.Audio;

public enum FeedbackSound
{
    Started,
    Finished,
    Error
}

/// <summary>
/// Plays SonaBar's original, generated feedback tones. Playback is deliberately
/// best-effort: a missing output device or a sound file must never interrupt a
/// dictation.
/// </summary>
public sealed class FeedbackSounds : IDisposable
{
    private readonly string root;
    private readonly object gate = new();
    private WaveOutEvent? output;
    private WaveFileReader? reader;

    public FeedbackSounds(string? root = null)
    {
        this.root = root ?? Path.Combine(AppContext.BaseDirectory, "Sounds");
    }

    public static bool ShouldPlay(string? mode, FeedbackSound sound) => mode switch
    {
        "Standard" => true,
        "Minimal" => sound == FeedbackSound.Finished,
        _ => false
    };

    public static string FileNameFor(FeedbackSound sound) => sound switch
    {
        FeedbackSound.Started => "start.wav",
        FeedbackSound.Finished => "done.wav",
        _ => "error.wav"
    };

    public void Play(string? mode, FeedbackSound sound)
    {
        if (!ShouldPlay(mode, sound)) return;
        var path = Path.Combine(root, FileNameFor(sound));
        if (!File.Exists(path)) return;
        lock (gate)
        {
            try
            {
                DisposePlayers();
                reader = new WaveFileReader(path);
                output = new WaveOutEvent { DesiredLatency = 90, NumberOfBuffers = 2 };
                output.Init(reader);
                output.Play();
            }
            catch (Exception)
            {
                // Sound is optional feedback. Never let an unavailable output
                // device turn a successful dictation into an error.
                DisposePlayers();
            }
        }
    }

    private void DisposePlayers()
    {
        try { output?.Stop(); } catch (Exception) { }
        output?.Dispose();
        output = null;
        reader?.Dispose();
        reader = null;
    }

    public void Dispose()
    {
        lock (gate) DisposePlayers();
    }
}
