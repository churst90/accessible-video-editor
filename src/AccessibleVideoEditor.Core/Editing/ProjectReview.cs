using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// The two questions asked about a project as a whole: what still needs doing,
/// and what is actually in it.
/// </summary>
public static class ProjectReview
{
    /// <summary>
    /// Everything unfinished, in programme order. Holes, issue markers, missing
    /// media and empty cards all end up here rather than in four places.
    /// </summary>
    public static IReadOnlyList<Issue> Issues(Project project)
    {
        var map = TimelineMap.Build(project);
        var issues = new List<Issue>();

        foreach (var placed in map.Elements)
        {
            if (placed.Element is HoleElement hole)
            {
                issues.Add(new Issue(
                    placed.ProgrammeStart,
                    IssueKind.Hole,
                    hole.Note.Length > 0 ? $"hole: {hole.Note}" : "hole"));
            }

            if (placed.Media is { } media && project.SourceOf(media.Source) is { } source
                && !File.Exists(source.Path))
            {
                issues.Add(new Issue(
                    placed.ProgrammeStart,
                    IssueKind.MissingMedia,
                    $"missing: {Path.GetFileName(source.Path)}"));
            }

            if (placed.Element is CardElement card && card.Composition.PlainText().Trim().Length == 0)
            {
                issues.Add(new Issue(placed.ProgrammeStart, IssueKind.EmptyCard, "a card with no words on it"));
            }
        }

        foreach (var (marker, at) in OverlayOperations.MarkersInOrder(project)
                     .Where(m => m.Marker.Kind == MarkerKind.Issue))
        {
            issues.Add(new Issue(at, IssueKind.Marked, marker.Label.Length > 0 ? marker.Label : "marked"));
        }

        return issues.OrderBy(i => i.At).ToList();
    }

    public static string DescribeIssues(Project project)
    {
        var issues = Issues(project);

        if (issues.Count == 0) return "nothing outstanding";

        var holes = issues.Count(i => i.Kind == IssueKind.Hole);
        var missing = issues.Count(i => i.Kind == IssueKind.MissingMedia);

        var parts = new List<string> { $"{issues.Count} to fix" };

        if (holes > 0) parts.Add($"{holes} {(holes == 1 ? "hole" : "holes")}");
        if (missing > 0) parts.Add($"{missing} missing {(missing == 1 ? "file" : "files")}");

        return string.Join(", ", parts);
    }

    /// <summary>
    /// The edit read back as prose: how long, what is on it, and where it
    /// changes. The thing a sighted editor gets from scrolling through it.
    /// </summary>
    public static string Describe(Project project)
    {
        var map = TimelineMap.Build(project);

        if (map.Elements.Count == 0) return "the timeline is empty";

        var spans = map.Elements.Count(p => p.Element is SpanElement);
        var cards = map.Elements.Count(p => p.Element is CardElement);
        var holes = map.Elements.Count(p => p.Element is HoleElement);
        var muted = map.Elements.Count(p => p.Element.Muted);
        var hidden = map.Elements.Count(p => p.Element.Hidden);
        var transitions = map.Elements.Count(p => p.TransitionIn > 0);
        var disabled = project.Spine.Count(e => !e.Enabled);

        var lines = new List<string>
        {
            $"{project.Name}, {Timecode.Speak(map.Duration)}, {map.Elements.Count} segments",
        };

        var made = new List<string>();

        if (spans > 0) made.Add($"{spans} spoken");
        if (cards > 0) made.Add($"{cards} {(cards == 1 ? "card" : "cards")}");
        if (holes > 0) made.Add($"{holes} {(holes == 1 ? "hole" : "holes")}");

        if (made.Count > 0) lines.Add(string.Join(", ", made));

        var states = new List<string>();

        if (muted > 0) states.Add($"{muted} muted");
        if (hidden > 0) states.Add($"{hidden} hidden");
        if (disabled > 0) states.Add($"{disabled} disabled");
        if (transitions > 0) states.Add($"{transitions} with transitions");

        if (states.Count > 0) lines.Add(string.Join(", ", states));

        foreach (var track in project.InOrder.Where(t => t.Kind != TrackKind.Programme))
        {
            var items = project.ItemsOn(track.Id).Count();

            if (items > 0) lines.Add($"{track.Name}: {items} {(items == 1 ? "item" : "items")}");
        }

        var markers = OverlayOperations.MarkersInOrder(project).Count;
        if (markers > 0) lines.Add($"{markers} {(markers == 1 ? "marker" : "markers")}");

        lines.Add(DescribeIssues(project));

        return string.Join(". ", lines);
    }

    /// <summary>
    /// Where a phrase is spoken. Search is over the transcript rather than over
    /// the file names, because the transcript is what the project is made of.
    /// </summary>
    public static IReadOnlyList<(double At, string Text)> Find(Project project, string phrase)
    {
        if (phrase.Trim().Length == 0) return [];

        var map = TimelineMap.Build(project);

        return map.Elements
            .Where(p => p.Element is SpanElement span
                        && span.Text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .Select(p => (At: p.ProgrammeStart, Text: ((SpanElement)p.Element).Text))
            .ToList();
    }
}

public readonly record struct Issue(double At, IssueKind Kind, string Text)
{
    public string Describe() => $"{Timecode.FormatShort(At)}, {Text}";
}

public enum IssueKind
{
    Hole,
    MissingMedia,
    EmptyCard,
    Marked,
}
