namespace AccessibleVideoEditor.Playback;

/// <summary>
/// Music while you stream, played locally.
///
/// Deliberately <b>not</b> mixed into the encoder. Changing a source inside a
/// running ffmpeg means restarting the encode, which is a visible hitch for
/// everyone watching - so the music plays on this machine like any other
/// application, and reaches the stream through a desktop-audio source in the
/// scene. That is how streamers actually do it, and it means skipping a track
/// costs nothing on air.
///
/// The consequence is worth stating out loud, and the streamer view does: if no
/// scene is capturing desktop audio, you can hear the music and your viewers
/// cannot.
/// </summary>
public sealed class MusicPlayer : IDisposable
{
    private MpvClient? _mpv;

    public string? NowPlaying { get; private set; }

    public bool IsPlaying { get; private set; }

    public bool IsAvailable => _mpv is not null || MpvClient.TryCreate(audioOnly: true) is not null;

    /// <summary>Fraction of full volume, 0 to 1, remembered across tracks.</summary>
    public double Volume { get; private set; } = 0.6;

    public string Play(string path)
    {
        if (!File.Exists(path)) return "that file is not there";

        _mpv ??= MpvClient.TryCreate(audioOnly: true);

        if (_mpv is null) return "there is no audio player available";

        try
        {
            _mpv.Command("loadfile", path, "replace");
            _mpv.SetProperty("volume", ((int)Math.Round(Volume * 100)).ToString());
            _mpv.SetProperty("pause", "no");

            NowPlaying = path;
            IsPlaying = true;

            return "playing";
        }
        catch (Exception exception)
        {
            return $"could not play it: {exception.Message}";
        }
    }

    /// <summary>
    /// True once the current track has run out. Polled rather than pushed,
    /// because the poll already exists for playback and one timer is easier to
    /// reason about than two.
    /// </summary>
    public bool HasFinished()
    {
        if (_mpv is null || !IsPlaying) return false;

        try
        {
            return _mpv.GetFlag("eof-reached") || _mpv.GetFlag("idle-active");
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string SetVolume(double fraction)
    {
        Volume = Math.Clamp(fraction, 0, 1);

        try
        {
            _mpv?.SetProperty("volume", ((int)Math.Round(Volume * 100)).ToString());
        }
        catch (Exception)
        {
        }

        return $"music at {Math.Round(Volume * 100)} percent";
    }

    public string Stop()
    {
        IsPlaying = false;
        NowPlaying = null;

        try
        {
            _mpv?.Command("stop");
        }
        catch (Exception)
        {
        }

        return "music stopped";
    }

    public void Dispose()
    {
        _mpv?.Dispose();
        _mpv = null;
    }
}
