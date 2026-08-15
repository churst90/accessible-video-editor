using System.Globalization;

namespace AccessibleVideoEditor.Playback;

/// <summary>
/// Playback of the decision list, and audio scrub.
///
/// Two mpv instances, deliberately:
/// <list type="bullet">
/// <item>the <b>preview</b> plays the edit from the cursor;</item>
/// <item>the <b>scrub</b> plays a fraction of a second wherever the cursor
/// lands. It has to be separate, because scrubbing must not disturb where
/// playback is parked, and because it is interrupted on every arrow press.</item>
/// </list>
///
/// <b>Both are audio-only.</b> There is no embedded video surface yet, so
/// enabling video just makes mpv open a window of its own - which steals
/// keyboard focus mid-edit and shows something nobody asked for. A video
/// preview belongs with the visual timeline, where it can be placed
/// deliberately.
/// </summary>
public sealed class PreviewPlayer : IDisposable
{
    private readonly MpvClient? _preview;
    private readonly MpvClient? _scrub;

    private PlaybackMap? _map;
    private string? _loadedUri;
    private CancellationTokenSource? _scrubStop;
    private CancellationTokenSource? _rangeStop;

    public PreviewPlayer()
    {
        _preview = MpvClient.TryCreate(audioOnly: true);
        _scrub = MpvClient.TryCreate(audioOnly: true);
    }

    public bool IsAvailable => _preview is { IsOpen: true };

    /// <summary>
    /// Sends monitoring to a specific output. Null returns to the system
    /// default. Applied to both instances so scrub and playback are never heard
    /// in different places.
    /// </summary>
    public void SetOutput(string? sinkId)
    {
        var value = sinkId is { Length: > 0 } ? $"pulse/{sinkId}" : "auto";

        _preview?.SetProperty("audio-device", value);
        _scrub?.SetProperty("audio-device", value);
    }

    public bool IsPlaying { get; private set; }

    /// <summary>Where playback is, in <b>programme</b> time.</summary>
    public double Position =>
        _map is null ? 0 : _map.ToProgramme(_preview?.GetDouble("time-pos") ?? 0);

    /// <summary>True once the player has run past the end of the playable media.</summary>
    public bool ReachedEnd =>
        _preview is not null && _map is not null && _preview.GetFlag("eof-reached");

    /// <summary>
    /// Points both instances at the decision list. Called after every edit;
    /// cheap, because nothing is encoded - mpv just reopens a list of in and
    /// out points.
    /// </summary>
    public void Load(PlaybackMap map)
    {
        _map = map;

        if (!IsAvailable || map.Uri == _loadedUri) return;

        _loadedUri = map.Uri;

        _preview!.Command("loadfile", map.Uri, "replace");
        _preview.SetProperty("pause", "yes");

        _scrub?.Command("loadfile", map.Uri, "replace");
        _scrub?.SetProperty("pause", "yes");

        // loadfile is asynchronous. Seeking before the file is open silently
        // does nothing, which is exactly the bug where pressing Home and then
        // Space played from the wrong place, or not at all.
        _preview.WaitUntilLoaded(TimeSpan.FromMilliseconds(700));
        _scrub?.WaitUntilLoaded(TimeSpan.FromMilliseconds(700));
    }

    /// <summary>
    /// Starts at a programme time. Returns the time actually started from,
    /// which differs when the cursor was inside a card or a hole - preview
    /// cannot render those, so it skips to the next real media.
    /// </summary>
    public double? Play(double programmeTime)
    {
        if (!IsAvailable || _map is null) return null;

        var target = _map.NextPlayable(programmeTime);
        if (target is null) return null;

        CancelRange();
        SeekProgramme(target.Value);

        _preview!.SetProperty("speed", "1.0");
        _preview.SetProperty("pause", "no");
        IsPlaying = true;

        return target;
    }

    public void Pause()
    {
        if (!IsAvailable) return;

        CancelRange();
        _preview!.SetProperty("pause", "yes");
        IsPlaying = false;
    }

    public void SeekProgramme(double programmeTime)
    {
        if (_map?.ToPlayback(programmeTime) is not { } playbackTime) return;

        _preview?.Command("seek", playbackTime.ToString("0.###", CultureInfo.InvariantCulture), "absolute");
    }

    /// <summary>J and L: shuttle.</summary>
    public void SetRate(double rate)
    {
        if (!IsAvailable) return;

        if (rate <= 0)
        {
            Pause();
            return;
        }

        _preview!.SetProperty("speed", rate.ToString("0.##", CultureInfo.InvariantCulture));
        _preview.SetProperty("pause", "no");
        IsPlaying = true;
    }

    /// <summary>Plays a bounded range and stops. How a cut or transition is auditioned.</summary>
    public async Task PlayRangeAsync(double from, double to)
    {
        if (!IsAvailable || to <= from) return;

        CancelRange();
        var stop = new CancellationTokenSource();
        _rangeStop = stop;

        if (Play(from) is null) return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(to - from), stop.Token).ConfigureAwait(false);
            Pause();
        }
        catch (TaskCanceledException)
        {
            // Superseded by another audition or by playback starting.
        }
    }

    /// <summary>
    /// A blip of the real audio at this point. Each call cancels the last, so
    /// holding an arrow key down does not queue a backlog of overlapping blips.
    /// </summary>
    public void Scrub(double programmeTime, double length)
    {
        if (_scrub is not { IsOpen: true } || _map is null) return;
        if (_map.ToPlayback(programmeTime) is not { } playbackTime) return;

        _scrubStop?.Cancel();
        var stop = new CancellationTokenSource();
        _scrubStop = stop;

        _scrub.Command("seek", playbackTime.ToString("0.###", CultureInfo.InvariantCulture), "absolute");
        _scrub.SetProperty("pause", "no");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(length), stop.Token).ConfigureAwait(false);
                _scrub.SetProperty("pause", "yes");
            }
            catch (TaskCanceledException)
            {
                // A newer scrub took over; it will stop itself.
            }
        });
    }

    public void StopScrub()
    {
        _scrubStop?.Cancel();
        _scrub?.SetProperty("pause", "yes");
    }

    private void CancelRange()
    {
        _rangeStop?.Cancel();
        _rangeStop = null;
    }

    public void Dispose()
    {
        _scrubStop?.Cancel();
        _rangeStop?.Cancel();
        _preview?.Dispose();
        _scrub?.Dispose();
    }
}
