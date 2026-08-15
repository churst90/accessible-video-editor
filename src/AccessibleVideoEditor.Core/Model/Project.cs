using System.Text.Json.Serialization;

namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// The canonical project document. JSON on disk; <c>edit.md</c> is an export
/// that round-trips back in, so the CLI, pluma and the Claude skill all keep
/// working.
///
/// Shape: <b>one ordered spine plus anchored overlays</b>. The spine defines
/// programme time; everything else names a spine element and an offset.
/// </summary>
public sealed class Project
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Name { get; set; } = "Untitled";

    /// <summary>Absolute path to the project directory. Not serialised.</summary>
    [JsonIgnore]
    public string? RootPath { get; set; }

    public ProjectSettings Settings { get; set; } = new();

    public List<Source> Sources { get; set; } = [];
    public List<Track> Tracks { get; set; } = [];

    /// <summary>Transitions you made yourself, kept by name.</summary>
    public List<CustomTransition> CustomTransitions { get; set; } = [];

    /// <summary>Ordered. Order is the edit.</summary>
    public List<SpineElement> Spine { get; set; } = [];

    public List<OverlayItem> Overlays { get; set; } = [];
    public List<Marker> Markers { get; set; } = [];

    public SpineElement? Element(ElementId id) => Spine.FirstOrDefault(e => e.Id == id);

    public Source? SourceOf(SourceId id) => Sources.FirstOrDefault(s => s.Id == id);

    public Track? TrackOf(TrackId id) => Tracks.FirstOrDefault(t => t.Id == id);

    public Track ProgrammeTrack =>
        Tracks.First(t => t.Kind == TrackKind.Programme);

    public IEnumerable<Track> InOrder => Tracks.OrderBy(t => t.Order);

    public IEnumerable<OverlayItem> ItemsOn(TrackId track) =>
        Overlays.Where(o => o.Track == track);

    /// <summary>Holes block the master render; this is what the To-Do pane lists.</summary>
    public IEnumerable<HoleElement> Holes =>
        Spine.OfType<HoleElement>().Where(h => h.Enabled);

    public static Project CreateDefault(string name)
    {
        var project = new Project { Name = name };

        project.Tracks.AddRange(
        [
            new Track
            {
                Id = Ids.NewTrack(), Name = "Programme", Kind = TrackKind.Programme,
                Media = TrackMedia.Mixed, Order = 0,
            },
            new Track
            {
                Id = Ids.NewTrack(), Name = "B-roll", Kind = TrackKind.Overlay,
                Media = TrackMedia.Video, Order = 1,
            },
            new Track
            {
                Id = Ids.NewTrack(), Name = "Graphics", Kind = TrackKind.Graphics,
                Media = TrackMedia.Image, Order = 2,
            },
            new Track
            {
                Id = Ids.NewTrack(), Name = "Music", Kind = TrackKind.Audio,
                Media = TrackMedia.Audio, Order = 3,
            },
        ]);

        return project;
    }
}
