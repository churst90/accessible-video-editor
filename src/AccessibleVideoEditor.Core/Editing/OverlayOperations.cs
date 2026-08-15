using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// Markers, and the overlay items that could be rendered and described but not
/// created.
/// </summary>
public static class OverlayOperations
{
    public const double DefaultTitleLength = 4;

    public static EditResult AddMarker(
        Project project,
        double programmeTime,
        string label,
        MarkerKind kind = MarkerKind.User)
    {
        var map = TimelineMap.Build(project);

        if (map.ToAnchor(programmeTime) is not { } anchor)
        {
            return EditResult.NoChange("there is nothing here to anchor a marker to");
        }

        project.Markers.Add(new Marker { Id = Ids.NewMarker(), At = anchor, Label = label, Kind = kind });

        var number = project.Markers.Count;

        return EditResult.Ok(
            label.Length > 0
                ? $"marker {number}, {label}, at {Timecode.FormatShort(programmeTime)}"
                : $"marker {number} at {Timecode.FormatShort(programmeTime)}");
    }

    public static EditResult RemoveMarker(Project project, double programmeTime, double within = 0.5)
    {
        var map = TimelineMap.Build(project);

        var nearest = project.Markers
            .Select(m => (Marker: m, At: map.ResolveAnchor(m.At)))
            .Where(m => m.At is not null && Math.Abs(m.At.Value - programmeTime) <= within)
            .OrderBy(m => Math.Abs(m.At!.Value - programmeTime))
            .FirstOrDefault();

        if (nearest.Marker is null) return EditResult.NoChange("no marker here");

        project.Markers.Remove(nearest.Marker);

        return EditResult.Ok($"removed {nearest.Marker.Describe()}");
    }

    public static EditResult RenameMarker(Project project, double programmeTime, string label)
    {
        var map = TimelineMap.Build(project);

        var nearest = project.Markers
            .Select(m => (Marker: m, At: map.ResolveAnchor(m.At)))
            .Where(m => m.At is not null && Math.Abs(m.At.Value - programmeTime) <= 0.5)
            .OrderBy(m => Math.Abs(m.At!.Value - programmeTime))
            .FirstOrDefault();

        if (nearest.Marker is null) return EditResult.NoChange("no marker here");

        nearest.Marker.Label = label;

        return EditResult.Ok($"renamed to {label}");
    }

    /// <summary>Markers in programme order, for a list you can step through.</summary>
    public static IReadOnlyList<(Marker Marker, double At)> MarkersInOrder(Project project)
    {
        var map = TimelineMap.Build(project);

        return project.Markers
            .Select(m => (Marker: m, At: map.ResolveAnchor(m.At)))
            .Where(m => m.At is not null)
            .Select(m => (m.Marker, m.At!.Value))
            .OrderBy(m => m.Item2)
            .ToList();
    }

    // ---- overlay items -----------------------------------------------------

    public static EditResult AddTitle(
        Project project,
        double programmeTime,
        string text,
        Placement? placement = null,
        double length = DefaultTitleLength) =>
        Place(project, TrackKind.Graphics, programmeTime, length, (track, anchor) => new TitleItem
        {
            Id = Ids.NewItem(),
            Track = track,
            Start = anchor,
            Length = length,
            Text = text,
            Placement = placement ?? Placement.LowerThird,
        });

    public static EditResult AddGraphic(
        Project project,
        double programmeTime,
        SourceId source,
        Placement? placement = null,
        double length = DefaultTitleLength) =>
        Place(project, TrackKind.Graphics, programmeTime, length, (track, anchor) => new GraphicItem
        {
            Id = Ids.NewItem(),
            Track = track,
            Start = anchor,
            Length = length,
            Source = source,
            Placement = placement ?? Placement.Centre,
        });

    /// <summary>
    /// B-roll covers the picture and leaves the programme's sound alone, which
    /// is the whole point of it.
    /// </summary>
    public static EditResult AddBroll(
        Project project,
        double programmeTime,
        SourceId source,
        double sourceIn,
        double length) =>
        Place(project, TrackKind.Overlay, programmeTime, length, (track, anchor) => new BrollItem
        {
            Id = Ids.NewItem(),
            Track = track,
            Start = anchor,
            Length = length,
            Source = source,
            SourceIn = sourceIn,
        });

    private static EditResult Place(
        Project project,
        TrackKind kind,
        double programmeTime,
        double length,
        Func<TrackId, TimeAnchor, OverlayItem> build)
    {
        if (length <= 0) return EditResult.NoChange("that would have no length");

        var track = project.Tracks.FirstOrDefault(t => t.Kind == kind);

        if (track is null) return EditResult.NoChange($"there is no {kind.ToString().ToLowerInvariant()} track");
        if (track.Locked) return EditResult.NoChange($"{track.Name} is locked");

        var map = TimelineMap.Build(project);

        if (map.ToAnchor(programmeTime) is not { } anchor)
        {
            return EditResult.NoChange("there is nothing here to anchor it to");
        }

        var item = build(track.Id, anchor);
        project.Overlays.Add(item);

        return EditResult.Ok($"{item.Describe()} on {track.Name}, {Timecode.Speak(length)}");
    }
}
