using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Segment navigation. A <b>segment</b> is one piece of content on one track;
/// splitting one segment gives two, two splits give three.
///
/// Two distinct movements, deliberately:
/// Shift+comma / Shift+period walk the edges of the segment you are in, while
/// Ctrl+left / Ctrl+right walk segment <i>starts</i> only - stepping onto an
/// end and then a start would make one press feel like two.
/// </summary>
public class SegmentNavigationTests
{
    /// <summary>Three five-second segments back to back: 0-5, 5-10, 10-15.</summary>
    private static Project ThreeSegments()
    {
        var project = Project.CreateDefault("segments");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(source);

        for (var i = 0; i < 3; i++)
        {
            project.Spine.Add(new SpanElement
            {
                Id = Ids.NewElement(),
                Source = source.Id,
                SourceIn = i * 10,
                SourceOut = i * 10 + 5,
                Text = $"sentence {i}",
            });
        }

        return project;
    }

    // ---- Shift+comma / Shift+period --------------------------------------

    [Fact]
    public void Segment_start_from_inside_lands_on_this_segments_start()
    {
        var project = ThreeSegments();
        var map = TimelineMap.Build(project);

        Assert.Equal(5, TrackProbe.SegmentStart(project, map, project.ProgrammeTrack.Id, 7.5)!.Value, 3);
    }

    [Fact]
    public void Segment_end_from_inside_lands_on_this_segments_end()
    {
        var project = ThreeSegments();
        var map = TimelineMap.Build(project);

        Assert.Equal(10, TrackProbe.SegmentEnd(project, map, project.ProgrammeTrack.Id, 7.5)!.Value, 3);
    }

    [Fact]
    public void Pressing_segment_start_again_walks_back_a_segment()
    {
        var project = ThreeSegments();
        var map = TimelineMap.Build(project);
        var track = project.ProgrammeTrack.Id;

        var first = TrackProbe.SegmentStart(project, map, track, 7.5)!.Value;
        var second = TrackProbe.SegmentStart(project, map, track, first)!.Value;

        Assert.Equal(5, first, 3);
        Assert.Equal(0, second, 3);
    }

    [Fact]
    public void Pressing_segment_end_again_walks_forward_a_segment()
    {
        var project = ThreeSegments();
        var map = TimelineMap.Build(project);
        var track = project.ProgrammeTrack.Id;

        var first = TrackProbe.SegmentEnd(project, map, track, 2)!.Value;
        var second = TrackProbe.SegmentEnd(project, map, track, first)!.Value;

        Assert.Equal(5, first, 3);
        Assert.Equal(10, second, 3);
    }

    [Fact]
    public void There_is_nowhere_to_go_past_either_end_of_the_track()
    {
        var project = ThreeSegments();
        var map = TimelineMap.Build(project);
        var track = project.ProgrammeTrack.Id;

        Assert.Null(TrackProbe.SegmentStart(project, map, track, 0));
        Assert.Null(TrackProbe.SegmentEnd(project, map, track, 15));
    }

    // ---- Ctrl+left / Ctrl+right ------------------------------------------

    [Fact]
    public void Ctrl_arrows_visit_segment_starts_only()
    {
        // Walking forward from the very beginning should give 5 then 10 - never
        // stopping at 5 twice because it is also the end of the first segment.
        var project = ThreeSegments();
        var map = TimelineMap.Build(project);
        var track = project.ProgrammeTrack.Id;

        var visited = new List<double>();
        double? at = 0;

        while (TrackProbe.AdjacentSegmentStart(project, map, track, at!.Value, forward: true) is { } next)
        {
            visited.Add(next);
            at = next;
        }

        Assert.Equal([5, 10], visited);
    }

    [Fact]
    public void Ctrl_arrows_go_backwards_too()
    {
        var project = ThreeSegments();
        var map = TimelineMap.Build(project);
        var track = project.ProgrammeTrack.Id;

        // From inside the third segment, back goes to that segment's own start
        // first - the nearest boundary behind you - and only then to the one
        // before it. Skipping straight past the segment you are in would make
        // it impossible to get to its head with this key.
        var first = TrackProbe.AdjacentSegmentStart(project, map, track, 12, forward: false)!.Value;
        var second = TrackProbe.AdjacentSegmentStart(project, map, track, first, forward: false)!.Value;

        Assert.Equal(10, first, 3);
        Assert.Equal(5, second, 3);
        Assert.Null(TrackProbe.AdjacentSegmentStart(project, map, track, 0, forward: false));
    }

    // ---- per track --------------------------------------------------------

    [Fact]
    public void Segments_follow_the_focused_track_not_the_programme()
    {
        var project = ThreeSegments();
        var graphics = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id;

        project.Overlays.Add(new CardItem
        {
            Id = Ids.NewItem(),
            Track = graphics,
            Composition = CardTemplates.LowerThird("Cody Hurst"),
            Start = new TimeAnchor(project.Spine[1].Id, 1),
            Length = 2,
        });

        var map = TimelineMap.Build(project);

        Assert.Equal(6, TrackProbe.SegmentStart(project, map, graphics, 7)!.Value, 3);
        Assert.Equal(8, TrackProbe.SegmentEnd(project, map, graphics, 7)!.Value, 3);
    }

    [Fact]
    public void A_track_with_nothing_on_it_has_no_segments()
    {
        var project = ThreeSegments();
        var music = project.Tracks.First(t => t.Kind == TrackKind.Audio).Id;
        var map = TimelineMap.Build(project);

        Assert.Empty(TrackProbe.Segments(project, map, music));
        Assert.Null(TrackProbe.SegmentStart(project, map, music, 7));
        Assert.Null(TrackProbe.SegmentEnd(project, map, music, 7));
    }

    [Fact]
    public void Disabled_segments_are_not_landing_spots()
    {
        var project = ThreeSegments();
        var graphics = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id;

        project.Overlays.Add(new TitleItem
        {
            Id = Ids.NewItem(),
            Track = graphics,
            Text = "hidden",
            Enabled = false,
            Start = new TimeAnchor(project.Spine[1].Id, 1),
            Length = 2,
        });

        var map = TimelineMap.Build(project);

        Assert.Empty(TrackProbe.Segments(project, map, graphics));
    }

    [Fact]
    public void Splitting_one_segment_gives_two()
    {
        // The naming rule, pinned: one split, two segments; two splits, three.
        var project = ThreeSegments();
        var track = project.ProgrammeTrack.Id;

        Assert.Equal(3, TrackProbe.Segments(project, TimelineMap.Build(project), track).Count);

        AccessibleVideoEditor.Core.Editing.EditOperations.SplitAt(project, 2);
        Assert.Equal(4, TrackProbe.Segments(project, TimelineMap.Build(project), track).Count);

        AccessibleVideoEditor.Core.Editing.EditOperations.SplitAt(project, 3.5);
        Assert.Equal(5, TrackProbe.Segments(project, TimelineMap.Build(project), track).Count);
    }
}

/// <summary>
/// Splitting on a track other than the programme. Splitting used to always cut
/// the spine whatever track was focused, so a split on the graphics track
/// appeared to do nothing and navigation then found no new boundaries.
/// </summary>
public class OverlaySplitTests
{
    private static Project WithTitle(out TrackId graphics)
    {
        var project = Project.CreateDefault("overlay split");
        project.Settings.SpanPadIn = 0;
        project.Settings.SpanPadOut = 0;
        project.Settings.JumpCutDuration = 0;
        project.Settings.SceneTransitionDuration = 0;

        var source = new Source { Id = Ids.NewSource(), Path = "a.mkv", Duration = 60 };
        project.Sources.Add(source);

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(), Source = source.Id, SourceIn = 0, SourceOut = 20, Text = "hello",
        });

        graphics = project.Tracks.First(t => t.Kind == TrackKind.Graphics).Id;

        project.Overlays.Add(new TitleItem
        {
            Id = Ids.NewItem(),
            Track = graphics,
            Text = "Cody Hurst",
            Start = new TimeAnchor(project.Spine[0].Id, 2),
            Length = 8,
        });

        return project;
    }

    [Fact]
    public void Splitting_a_title_gives_two_titles()
    {
        var project = WithTitle(out var graphics);

        var result = AccessibleVideoEditor.Core.Editing.EditOperations.SplitItemAt(project, graphics, 6);

        Assert.True(result.Changed);
        Assert.Equal(2, project.Overlays.Count);
    }

    [Fact]
    public void The_two_halves_cover_the_same_stretch_as_the_original()
    {
        // A split must not change what is on screen, only where the boundary is.
        var project = WithTitle(out var graphics);
        AccessibleVideoEditor.Core.Editing.EditOperations.SplitItemAt(project, graphics, 6);

        var map = TimelineMap.Build(project);
        var segments = TrackProbe.Segments(project, map, graphics);

        Assert.Equal(2, segments.Count);
        Assert.Equal(2, segments[0].Start, 2);
        Assert.Equal(6, segments[0].End, 2);
        Assert.Equal(6, segments[1].Start, 2);
        Assert.Equal(10, segments[1].End, 2);
    }

    [Fact]
    public void The_new_boundary_can_then_be_navigated_to()
    {
        // The actual complaint: splitting several times and then finding
        // nothing to move between.
        var project = WithTitle(out var graphics);

        AccessibleVideoEditor.Core.Editing.EditOperations.SplitItemAt(project, graphics, 4);
        AccessibleVideoEditor.Core.Editing.EditOperations.SplitItemAt(project, graphics, 6);
        AccessibleVideoEditor.Core.Editing.EditOperations.SplitItemAt(project, graphics, 8);

        var map = TimelineMap.Build(project);

        Assert.Equal(4, TrackProbe.Segments(project, map, graphics).Count);
        Assert.Equal(4, TrackProbe.AdjacentSegmentStart(project, map, graphics, 2, forward: true)!.Value, 2);
        Assert.Equal(6, TrackProbe.AdjacentSegmentStart(project, map, graphics, 4, forward: true)!.Value, 2);
    }

    [Fact]
    public void Splitting_at_an_edge_or_outside_the_segment_does_nothing()
    {
        var project = WithTitle(out var graphics);

        Assert.False(AccessibleVideoEditor.Core.Editing.EditOperations.SplitItemAt(project, graphics, 2).Changed);
        Assert.False(AccessibleVideoEditor.Core.Editing.EditOperations.SplitItemAt(project, graphics, 15).Changed);
        Assert.Single(project.Overlays);
    }

    [Fact]
    public void A_track_with_nothing_on_it_says_so_rather_than_failing()
    {
        var project = WithTitle(out _);
        var music = project.Tracks.First(t => t.Kind == TrackKind.Audio).Id;

        var result = AccessibleVideoEditor.Core.Editing.EditOperations.SplitItemAt(project, music, 5);

        Assert.False(result.Changed);
        Assert.Contains("nothing to split", result.Description);
    }

    [Fact]
    public void Splitting_b_roll_advances_the_second_half_into_its_source()
    {
        // Otherwise the second half would replay the same footage as the first.
        var project = WithTitle(out _);
        var broll = project.Tracks.First(t => t.Kind == TrackKind.Overlay).Id;

        project.Overlays.Add(new BrollItem
        {
            Id = Ids.NewItem(),
            Track = broll,
            Source = project.Sources[0].Id,
            SourceIn = 30,
            Start = new TimeAnchor(project.Spine[0].Id, 0),
            Length = 10,
        });

        AccessibleVideoEditor.Core.Editing.EditOperations.SplitItemAt(project, broll, 4);

        var halves = project.Overlays.OfType<BrollItem>().OrderBy(b => b.SourceIn).ToList();

        Assert.Equal(2, halves.Count);
        Assert.Equal(30, halves[0].SourceIn, 2);
        Assert.Equal(34, halves[1].SourceIn, 2);
    }
}
