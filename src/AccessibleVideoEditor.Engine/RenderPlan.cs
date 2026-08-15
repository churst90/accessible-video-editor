using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Engine;

/// <summary>
/// How the rendered segments are joined.
///
/// Segments separated by a plain cut are concatenated, which is nearly free.
/// Only where a transition actually exists does anything have to be
/// re-encoded - so a video with three dissolves in it costs three dissolves,
/// not a full re-render.
///
/// Pure, because the arithmetic of overlapping transitions is exactly the sort
/// of thing that is wrong by a frame and impossible to notice by ear.
/// </summary>
public static class RenderPlan
{
    /// <summary>
    /// Splits the timeline into runs of segments joined by cuts, with the
    /// transition that leads into each run.
    /// </summary>
    public static IReadOnlyList<RenderRun> Runs(Project project, TimelineMap map)
    {
        var runs = new List<RenderRun>();
        var current = new List<PlacedElement>();
        var leadIn = (Transition?)null;

        foreach (var placed in map.Elements)
        {
            var transition = TransitionFor(project, placed, map);

            if (transition is not null && current.Count > 0)
            {
                runs.Add(new RenderRun([.. current], leadIn));
                current.Clear();
                leadIn = transition;
            }

            current.Add(placed);
        }

        if (current.Count > 0) runs.Add(new RenderRun([.. current], leadIn));

        return runs;
    }

    /// <summary>
    /// The transition entering this segment, or null for a plain cut. The
    /// project default only counts between different shots; back-to-back speech
    /// from one take is a jump cut, and hiding it is a separate decision.
    /// </summary>
    public static Transition? TransitionFor(Project project, PlacedElement placed, TimelineMap map)
    {
        if (placed.Element.TransitionIn is { } explicitTransition)
        {
            return explicitTransition.Type == TransitionType.Cut || explicitTransition.Duration <= 0
                ? null
                : explicitTransition;
        }

        // The first segment has nothing to transition from.
        if (map.Elements.Count > 0 && ReferenceEquals(map.Elements[0], placed)) return null;

        return placed.TransitionIn > 0
            ? new Transition { Type = TransitionType.Fade, Duration = placed.TransitionIn }
            : null;
    }

    /// <summary>
    /// Where an <c>xfade</c> between two runs begins, measured from the start of
    /// the joined output so far.
    ///
    /// xfade overlaps the two inputs, so the offset is the accumulated length
    /// minus the transition - get this wrong and every later transition drifts.
    /// </summary>
    public static double OffsetFor(double accumulated, double transitionSeconds) =>
        Math.Max(0, accumulated - transitionSeconds);
}

public sealed record RenderRun(IReadOnlyList<PlacedElement> Segments, Transition? LeadIn)
{
    public double Duration => Segments.Sum(s => s.Duration);
}
