using System.Globalization;
using System.Text;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Playback;

/// <summary>
/// Builds an mpv <c>edl://</c> playlist from the timeline, together with the
/// map between programme time and playback time.
/// </summary>
public static class MpvEdl
{
    public static PlaybackMap Build(Project project, TimelineMap map)
    {
        var builder = new StringBuilder("edl://");
        var spans = new List<PlaybackSpan>();

        var edlTime = 0.0;
        var first = true;

        foreach (var placed in map.Elements)
        {
            if (placed.Media is not { } media) continue;

            var source = project.SourceOf(media.Source);
            if (source is null) continue;

            var path = project.RootPath is { } root && !Path.IsPathRooted(source.Path)
                ? Path.Combine(root, source.Path)
                : source.Path;

            if (!first) builder.Append(';');
            first = false;

            // %<byte-length>%<path> is mpv's escaping for paths containing
            // commas or semicolons - which project directories often do.
            builder.Append(CultureInfo.InvariantCulture, $"%{Encoding.UTF8.GetByteCount(path)}%{path}");
            builder.Append(CultureInfo.InvariantCulture, $",{Number(media.In)}");
            builder.Append(CultureInfo.InvariantCulture, $",{Number(placed.Duration)}");

            spans.Add(new PlaybackSpan(placed.ProgrammeStart, placed.ProgrammeEnd, edlTime));
            edlTime += placed.Duration;
        }

        return new PlaybackMap(builder.ToString(), spans, map.Duration);
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>One playable stretch, and where it sits in each clock.</summary>
public readonly record struct PlaybackSpan(double ProgrammeStart, double ProgrammeEnd, double EdlStart)
{
    public double Duration => ProgrammeEnd - ProgrammeStart;

    public double EdlEnd => EdlStart + Duration;
}

public sealed class PlaybackMap
{
    private readonly List<PlaybackSpan> _spans;

    public PlaybackMap(string uri, List<PlaybackSpan> spans, double programmeDuration)
    {
        Uri = uri;
        _spans = spans;
        ProgrammeDuration = programmeDuration;
    }

    public string Uri { get; }

    public double ProgrammeDuration { get; }

    public bool HasPlayableMedia => _spans.Count > 0;

    /// <summary>
    /// Programme time to playback time. Null when that moment has nothing to
    /// play - inside a card, a hole or a pause - which the caller handles by
    /// skipping forward and saying so, rather than seeking somewhere wrong.
    /// </summary>
    public double? ToPlayback(double programmeTime)
    {
        foreach (var span in _spans)
        {
            if (programmeTime >= span.ProgrammeStart && programmeTime < span.ProgrammeEnd)
            {
                return span.EdlStart + (programmeTime - span.ProgrammeStart);
            }
        }

        return null;
    }

    /// <summary>Playback time back to programme time, for following the player.</summary>
    public double ToProgramme(double playbackTime)
    {
        foreach (var span in _spans)
        {
            if (playbackTime >= span.EdlStart && playbackTime < span.EdlEnd)
            {
                return span.ProgrammeStart + (playbackTime - span.EdlStart);
            }
        }

        return _spans.Count > 0 && playbackTime >= _spans[^1].EdlEnd
            ? ProgrammeDuration
            : 0;
    }

    /// <summary>
    /// The next moment that can actually be played at or after this one. Used
    /// when playback starts inside a card: preview cannot render a card, so it
    /// skips to the next real media and announces what it skipped.
    /// </summary>
    public double? NextPlayable(double programmeTime)
    {
        if (ToPlayback(programmeTime) is not null) return programmeTime;

        foreach (var span in _spans)
        {
            if (span.ProgrammeStart >= programmeTime) return span.ProgrammeStart;
        }

        return null;
    }
}
