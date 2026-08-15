using AccessibleVideoEditor.Core.Commands;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;

namespace AccessibleVideoEditor.Core.Tests;

public class WorkspaceTests
{
    [Fact]
    public void Views_are_ordered_by_how_often_you_are_in_them()
    {
        // The timeline is where the work happens, so it is view 1. Data-flow
        // order would have put the media bin first, which is not where anyone
        // spends their day.
        // No record view: recording is per track, so it lives in the track
        // editor and the timeline rather than somewhere you have to travel to.
        // Images is appended rather than slotted next to the media bin, which
        // would be the tidier order: renumbering a view somebody has already
        // learned costs more than the tidiness is worth.
        Assert.Equal(
            [Pane.Timeline, Pane.Tracks, Pane.Transcript, Pane.MediaBin, Pane.Stream, Pane.Images],
            Workspace.Panes);
    }

    [Fact]
    public void Each_view_has_the_number_that_selects_it()
    {
        Assert.Equal(1, Workspace.Number(Pane.Timeline));
        Assert.Equal(2, Workspace.Number(Pane.Tracks));
        Assert.Equal(3, Workspace.Number(Pane.Transcript));
        Assert.Equal(5, Workspace.Number(Pane.Stream));

        Assert.Equal(Pane.Transcript, Workspace.ByNumber(3));
        Assert.Null(Workspace.ByNumber(0));
        Assert.Null(Workspace.ByNumber(9));
    }

    [Fact]
    public void Views_are_named_never_numbered_when_announced()
    {
        // "View 3" tells you nothing about where you are.
        Assert.Equal("timeline editor", Workspace.Name(Pane.Timeline));
        Assert.Equal("track editor", Workspace.Name(Pane.Tracks));
        Assert.Equal("transcript editor", Workspace.Name(Pane.Transcript));
        Assert.Equal("media bin", Workspace.Name(Pane.MediaBin));
        Assert.Equal("streamer view", Workspace.Name(Pane.Stream));
    }

    [Fact]
    public void F6_cycles_forwards_and_wraps()
    {
        var workspace = new Workspace();
        workspace.FocusOn(Workspace.Panes[^1]);

        Assert.Equal(Pane.Timeline, workspace.Next());
    }

    [Fact]
    public void Shift_F6_cycles_backwards_and_wraps()
    {
        var workspace = new Workspace();
        workspace.FocusOn(Pane.Timeline);

        Assert.Equal(Workspace.Panes[^1], workspace.Previous());
    }

    // ---- the status line -------------------------------------------------

    [Fact]
    public void The_status_line_carries_position_duration_step_and_track()
    {
        // This is the one thing that must never be a view away, so it is
        // assembled in Core rather than by whichever view happens to be up.
        var line = Workspace.StatusLine(12.4, 90, "word", "B-roll");

        Assert.Contains("00:00:12.400", line);
        Assert.Contains("00:01:30.000", line);
        Assert.Contains("step: word", line);
        Assert.Contains("track: B-roll", line);
    }

    [Fact]
    public void The_status_line_says_none_rather_than_going_blank_with_no_track()
    {
        Assert.Contains("track: none", Workspace.StatusLine(0, 0, "second", null));
    }

    // ---- empty states ----------------------------------------------------

    [Fact]
    public void With_no_project_open_every_view_says_so()
    {
        // "Empty timeline" would imply a project exists. It does not.
        foreach (var pane in Workspace.Panes)
        {
            var state = Workspace.EmptyState(pane, null);

            Assert.NotNull(state);
            Assert.Contains("no project loaded", state);
        }
    }

    [Fact]
    public void A_new_project_reports_each_view_empty_in_its_own_words()
    {
        var project = Project.CreateDefault("fresh");

        Assert.Contains("media bin empty", Workspace.EmptyState(Pane.MediaBin, project));
        Assert.Contains("timeline empty", Workspace.EmptyState(Pane.Timeline, project));
        Assert.Contains("transcript empty", Workspace.EmptyState(Pane.Transcript, project));

        // A default project does have tracks, so that view is not empty.
        Assert.Null(Workspace.EmptyState(Pane.Tracks, project));
    }

    [Fact]
    public void Empty_states_say_what_to_do_next_not_just_that_it_is_empty()
    {
        var project = Project.CreateDefault("fresh");

        Assert.Contains("Control I", Workspace.EmptyState(Pane.MediaBin, project));
        Assert.Contains("Control I", Workspace.EmptyState(Pane.Timeline, project));
    }

    [Fact]
    public void The_unbuilt_view_admits_it_rather_than_looking_broken()
    {
        var project = Project.CreateDefault("fresh");

        Assert.Contains("not built yet", Workspace.EmptyState(Pane.Stream, project));
    }

    [Theory]
    [InlineData(TrackMedia.Video, TrackInput.Camera)]
    [InlineData(TrackMedia.Mixed, TrackInput.Camera)]
    [InlineData(TrackMedia.Audio, TrackInput.Microphone)]
    [InlineData(TrackMedia.Image, TrackInput.None)]
    public void A_tracks_medium_decides_what_it_can_record_from(TrackMedia media, TrackInput expected)
    {
        // This is what removed the need for a separate record view: the input
        // is a property of the track, so choosing it belongs with the track.
        var track = new Track
        {
            Id = Ids.NewTrack(),
            Name = "test",
            Kind = TrackKind.Overlay,
            Media = media,
        };

        Assert.Equal(expected, track.AcceptsInput);
    }

    [Fact]
    public void An_armed_track_names_its_input_and_an_idle_one_does_not()
    {
        var track = new Track
        {
            Id = Ids.NewTrack(),
            Name = "Camera",
            Kind = TrackKind.Programme,
            Media = TrackMedia.Video,
            CaptureDeviceName = "Laptop Webcam Module",
        };

        Assert.DoesNotContain("Laptop Webcam", track.Describe());

        track.Armed = true;
        Assert.Contains("input Laptop Webcam Module", track.Describe());
    }

    // ---- summaries -------------------------------------------------------

    [Fact]
    public void A_view_with_content_summarises_it_rather_than_reporting_empty()
    {
        var project = WithOneSegment();

        var announced = Workspace.Announce(Pane.Timeline, project);

        Assert.Contains("timeline", announced);
        Assert.Contains("1 segment", announced);
        Assert.DoesNotContain("empty", announced);
    }

    [Fact]
    public void Outstanding_holes_interrupt_the_timeline_summary()
    {
        // Everything else can wait; a hole means the video cannot be exported.
        var project = WithOneSegment();
        project.Spine.Add(new HoleElement
        {
            Id = Ids.NewElement(),
            Length = 5,
            Note = "explain the order panel",
        });

        Assert.Contains("1 hole outstanding", Workspace.Announce(Pane.Timeline, project));
    }

    [Fact]
    public void Summaries_pluralise_correctly()
    {
        var project = WithOneSegment();
        Assert.Contains("1 segment,", Workspace.Announce(Pane.Timeline, project));

        project.Sources.Add(new Source { Id = Ids.NewSource(), Path = "b.mkv" });
        Assert.Contains("2 sources", Workspace.Announce(Pane.MediaBin, project));
    }

    [Fact]
    public void Each_view_maps_to_the_command_context_F1_should_list()
    {
        Assert.Equal(CommandContext.Timeline, Workspace.ContextOf(Pane.Timeline));
        Assert.Equal(CommandContext.Tracks, Workspace.ContextOf(Pane.Tracks));

        var trackCommands = CommandRegistry.InContext(Workspace.ContextOf(Pane.Tracks)).ToList();

        Assert.Contains(trackCommands, c => c.Id == "track.remove");
        Assert.DoesNotContain(trackCommands, c => c.Id == "edit.split");
    }

    private static Project WithOneSegment()
    {
        var project = Project.CreateDefault("with content");
        var source = new Source { Id = Ids.NewSource(), Path = "take1.mkv", Duration = 60 };
        project.Sources.Add(source);

        project.Spine.Add(new SpanElement
        {
            Id = Ids.NewElement(),
            Source = source.Id,
            SourceIn = 0,
            SourceOut = 5,
            Text = "hello",
        });

        return project;
    }
}
