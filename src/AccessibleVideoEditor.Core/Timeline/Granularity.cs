using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Timeline;

/// <summary>
/// How far Left/Right moves. Up/Down changes track, so granularity lives on
/// Ctrl+Up/Ctrl+Down - the mental model being "vertical changes something,
/// Ctrl changes the ruler".
///
/// This is structural navigation, the same idea as Orca's navigation levels,
/// rather than a pixel-denominated slider.
/// </summary>
public enum Granularity
{
    Frame,
    Tenth,
    Second,
    Word,
    Element,
    Boundary,
    Marker,
}

public static class GranularityExtensions
{
    public static string Describe(this Granularity granularity) => granularity switch
    {
        Granularity.Frame => "frame",
        Granularity.Tenth => "tenth of a second",
        Granularity.Second => "second",
        Granularity.Word => "word",
        Granularity.Element => "element",
        Granularity.Boundary => "boundary",
        Granularity.Marker => "marker",
        _ => granularity.ToString().ToLowerInvariant(),
    };

    public static Granularity Coarser(this Granularity granularity) =>
        granularity == Granularity.Marker ? granularity : granularity + 1;

    public static Granularity Finer(this Granularity granularity) =>
        granularity == Granularity.Frame ? granularity : granularity - 1;
}

/// <summary>Moves the cursor along the timeline at the current granularity.</summary>
public sealed class TimelineNavigator
{
    private readonly Project _project;
    private readonly TimelineMap _map;

    public TimelineNavigator(Project project, TimelineMap map)
    {
        _project = project;
        _map = map;
    }

    /// <summary>
    /// <paramref name="direction"/> is +1 or -1. Fixed granularities step by a
    /// fixed amount; structural ones jump to the next candidate position, which
    /// is what makes Left/Right feel like reading rather than dragging.
    /// </summary>
    public double Move(double from, Granularity granularity, int direction)
    {
        var step = granularity switch
        {
            Granularity.Frame => 1.0 / Math.Max(1, _project.Settings.Fps),
            Granularity.Tenth => 0.1,
            Granularity.Second => 1.0,
            _ => 0.0,
        };

        if (step > 0)
        {
            return Math.Clamp(from + step * direction, 0, _map.Duration);
        }

        var candidates = CandidatePositions(granularity);
        return NextCandidate(candidates, from, direction) ?? Math.Clamp(from, 0, _map.Duration);
    }

    /// <summary>Every position the cursor can land on at this granularity, ascending.</summary>
    public IReadOnlyList<double> CandidatePositions(Granularity granularity)
    {
        var positions = new List<double>();

        switch (granularity)
        {
            case Granularity.Word:
                foreach (var placed in _map.Elements)
                {
                    if (placed.Element is not SpanElement span || placed.Media is not { } media) continue;
                    foreach (var word in span.Words)
                    {
                        positions.Add(placed.ProgrammeStart + (word.Start - media.In));
                    }
                }

                break;

            case Granularity.Element:
                positions.AddRange(_map.Elements.Select(p => p.ProgrammeStart));
                break;

            case Granularity.Boundary:
                positions.AddRange(_map.Elements.Skip(1).Select(p => p.ProgrammeStart));
                break;

            case Granularity.Marker:
                positions.AddRange(_project.Markers
                    .Select(m => _map.ResolveAnchor(m.At))
                    .Where(t => t.HasValue)
                    .Select(t => t!.Value));
                break;
        }

        positions.Sort();
        return positions;
    }

    private static double? NextCandidate(IReadOnlyList<double> candidates, double from, int direction)
    {
        const double epsilon = 1e-4;

        if (direction > 0)
        {
            foreach (var candidate in candidates)
            {
                if (candidate > from + epsilon) return candidate;
            }

            return candidates.Count > 0 ? candidates[^1] : null;
        }

        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            if (candidates[i] < from - epsilon) return candidates[i];
        }

        return candidates.Count > 0 ? candidates[0] : null;
    }
}
