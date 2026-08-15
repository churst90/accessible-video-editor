namespace AccessibleVideoEditor.Playback;

/// <summary>
/// Preview playback, backed by libmpv (<c>libmpv.so.2</c> is already present).
///
/// v1 drives mpv in its own window over IPC rather than embedding a video
/// surface. Embedding is the fiddliest part of any toolkit and buys nothing for
/// the primary user; it can be added later for sighted collaborators without
/// changing this interface.
/// </summary>
public interface IPreviewPlayer : IDisposable
{
    bool IsPlaying { get; }

    /// <summary>Where playback currently is, in programme time.</summary>
    double Position { get; }

    event EventHandler<double>? PositionChanged;

    /// <summary>Reloads the decision list. Called after every edit; cheap by design.</summary>
    Task LoadAsync(string edlUri, CancellationToken ct = default);

    Task PlayAsync(CancellationToken ct = default);
    Task PauseAsync(CancellationToken ct = default);
    Task SeekAsync(double programmeTime, CancellationToken ct = default);

    /// <summary>Plays a bounded range and stops. Used to audition a transition or a selection.</summary>
    Task PlayRangeAsync(double from, double to, CancellationToken ct = default);

    /// <summary>Shuttle, as in J/K/L. Negative reverses.</summary>
    Task SetRateAsync(double rate, CancellationToken ct = default);
}
