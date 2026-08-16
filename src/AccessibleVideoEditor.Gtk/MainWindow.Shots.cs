using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Review;
using AccessibleVideoEditor.Engine;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// What is on screen, said as you move.
///
/// Cards and transitions announce themselves because the document knows what
/// they are. A shot of footage is the one thing on the timeline the document
/// knows nothing about - it is a rectangle of pixels, and only something with
/// eyes can say what is in it. This closes that gap the only way it can be
/// closed: have every shot described once, up front, and then reading the
/// answer is as fast as reading a card's text.
///
/// <b>The cost is stated and the work is asked for.</b> Each shot is a call to
/// a language model, so describing a twenty minute take is tens of them and
/// minutes of waiting. Doing that quietly on import would be spending someone's
/// quota on a question they did not ask, so it happens on a key, it says how
/// many shots it found before it starts, and it counts as it goes.
/// </summary>
public sealed partial class MainWindow
{
    private readonly ShotIndex _shots = new();
    private readonly ShotAnnouncer _shotAnnouncer = new();
    private bool _describingShots;

    private void RegisterShotActions()
    {
        Action("describeShots", () => _ = DescribeShotsAsync());
        Action("nextShot", () => JumpShot(forward: true));
        Action("previousShot", () => JumpShot(forward: false));
        Action("shotDetail", SpeakShotDetail);
    }

    /// <summary>
    /// The source under the cursor, and where in it the cursor is sitting.
    /// Everything here needs both, and the answer is null over a card, a hole
    /// or the end of the programme.
    /// </summary>
    private (Source Source, double At)? SourceUnderCursor()
    {
        if (_session.Map.ToSource(_cursor.ProgrammeTime) is not { } point) return null;

        return Project.SourceOf(point.Source) is { } source ? (source, point.Time) : null;
    }

    private async Task DescribeShotsAsync()
    {
        if (_describingShots)
        {
            Announce("already describing shots", urgent: true);
            return;
        }

        if (SourceUnderCursor() is not { } here)
        {
            Announce("no footage under the cursor to describe", urgent: true);
            return;
        }

        var (source, _) = here;
        var path = ResolvePath(source.Path);

        if (!File.Exists(path))
        {
            Announce($"{System.IO.Path.GetFileName(source.Path)} is not on disk", urgent: true);
            return;
        }

        var describer = new ShotDescriber(CacheDirectory(), _settings.Tools.Ffmpeg, _settings.Tools.Claude);

        // A source described before, and unchanged since, costs nothing at all.
        if (describer.Cached(source) is { Count: > 0 } cached)
        {
            _shots.Set(source.Id, cached);
            _shotAnnouncer.Reset();

            Announce(
                $"{Named(source)} was already described. {cached.Count} shots, from the cache",
                urgent: true);

            return;
        }

        if (!new FrameDescriber(_settings.Tools.Ffmpeg, _settings.Tools.Claude).IsAvailable)
        {
            Announce("the claude command is not installed, so shots cannot be described", urgent: true);
            return;
        }

        _describingShots = true;

        try
        {
            Announce($"looking for the shot changes in {Named(source)}", urgent: true);

            var starts = await Task.Run(() => describer.DetectShotsAsync(path)).ConfigureAwait(true);

            // Said before the work starts, not after: this is minutes and a
            // model call per shot, and the moment to decline is now.
            Announce(
                starts.Count == 1
                    ? $"{Named(source)} is one unbroken shot. Describing it"
                    : $"{starts.Count} shots in {Named(source)}. Describing each one, "
                      + "which takes a few seconds apiece",
                urgent: true);

            var done = 0;

            var shots = await Task.Run(() => describer.DescribeShotsAsync(
                path,
                starts,
                source.Duration,
                progress: (count, total) =>
                {
                    // Every fifth, so a long run proves it is alive without
                    // talking over you the whole time.
                    if (count % 5 != 0 || count == total) return;

                    done = count;

                    GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
                    {
                        Announce($"{done} of {total}", urgent: false);
                        return false;
                    });
                })).ConfigureAwait(true);

            describer.Cache(source, shots);

            _shots.Set(source.Id, shots);
            _shotAnnouncer.Reset();

            Announce(
                $"{shots.Count} shots described in {Named(source)}. "
                + "Moving the cursor now says what is on screen when it changes",
                urgent: true);

            Refresh();
        }
        catch (Exception exception)
        {
            Announce($"describing the shots failed: {exception.Message}", urgent: true);
        }
        finally
        {
            _describingShots = false;
        }
    }

    /// <summary>
    /// Moves to the next or previous point where the picture changes.
    ///
    /// This is the reason the feature is worth building rather than a novelty:
    /// a cut inside a take is invisible and, until now, unfindable - you could
    /// only guess at where the camera angle changed. It lands the cursor on the
    /// change itself, which is exactly where a marker wants to go.
    /// </summary>
    private void JumpShot(bool forward)
    {
        if (SourceUnderCursor() is not { } here)
        {
            Announce("no footage under the cursor", urgent: true);
            return;
        }

        var (source, at) = here;

        if (!_shots.Has(source.Id))
        {
            // The specific thing, not "no shots": one means the work has not
            // been done and the other would mean the footage has no cuts.
            Announce(
                $"{Named(source)} has not been described yet. Control F8 describes its shots",
                urgent: true);

            return;
        }

        var shot = forward ? _shots.Next(source.Id, at) : _shots.Previous(source.Id, at);

        if (shot is null)
        {
            Announce(forward ? "no further shot change in this take" : "no earlier shot change in this take",
                urgent: true);
            return;
        }

        // Back through the map: the shot's time is a source time and the cursor
        // lives in programme time, and the two only coincide on an untrimmed
        // first segment.
        if (_session.Map.FromSource(source.Id, shot.At) is not { } programmeTime)
        {
            Announce("that shot change is not in the edit", urgent: true);
            return;
        }

        _cursor.MoveTo(programmeTime);
        Refresh();

        Announce($"{Timecode.Speak(programmeTime)}, {shot.Label}", urgent: true);

        ScrubHere();
    }

    /// <summary>
    /// The whole description of the shot the cursor is in, on demand - the
    /// sentences the terse label is the short form of.
    /// </summary>
    private void SpeakShotDetail()
    {
        if (SourceUnderCursor() is not { } here)
        {
            Announce("no footage under the cursor", urgent: true);
            return;
        }

        var (source, at) = here;

        if (_shots.At(source.Id, at) is not { } shot)
        {
            Announce(
                _shots.Has(source.Id)
                    ? "no shot described at this point"
                    : $"{Named(source)} has not been described yet. Control F8 describes its shots",
                urgent: true);

            return;
        }

        Announce(
            shot.Detail.Length > 0 ? $"{shot.Label}. {shot.Detail}" : shot.Label,
            urgent: true);
    }

    /// <summary>
    /// The label to add to the cursor readout, or null - which is every move
    /// that stays inside one shot, and every project where nothing has been
    /// described.
    /// </summary>
    private string? ShotLabelHere()
    {
        if (_shots.BySource.Count == 0) return null;

        if (SourceUnderCursor() is not { } here)
        {
            return _shotAnnouncer.Moved(_shots, null, 0);
        }

        return _shotAnnouncer.Moved(_shots, here.Source.Id, here.At);
    }

    /// <summary>
    /// A source said aloud. The file name without its extension: a path read
    /// out in full is unusable, and the extension is the same on every file in
    /// the bin.
    /// </summary>
    private static string Named(Source source) =>
        System.IO.Path.GetFileNameWithoutExtension(source.Path) is { Length: > 0 } name
            ? name
            : source.Path;

    private string CacheDirectory() =>
        _settings.Tools.CacheDirectory is { Length: > 0 } configured
            ? configured
            : System.IO.Path.Combine(Project.RootPath ?? System.IO.Path.GetTempPath(), "work");
}
