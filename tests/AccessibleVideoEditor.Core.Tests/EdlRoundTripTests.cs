using AccessibleVideoEditor.Core.Edl;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Serialization;

namespace AccessibleVideoEditor.Core.Tests;

public class EdlRoundTripTests
{
    private const string Sample = """
        # ATT v1.4 walkthrough

        !music bed.mp3 gain=-22 duck=9

        ## source: take1.mkv

        !title "Cody Hurst" style=lower-third dur=4 cell=2
        [00:00:02.100 -> 00:00:05.400] Hey everybody, welcome back.
        x [00:00:05.900 -> 00:00:12.100] Today I want to show you... sorry.
        [00:00:12.400 -> 00:00:19.200] Today I want to walk through version 1.4.
        !hole dur=5 "explain the order panel here"
        !pause dur=0.7
        """;

    [Fact]
    public void Reads_spans_directives_and_the_disabled_prefix()
    {
        var project = EdlReader.Read(Sample);

        Assert.Equal("ATT v1.4 walkthrough", project.Name);
        Assert.Equal(3, project.Spine.OfType<SpanElement>().Count());
        Assert.Single(project.Spine.OfType<HoleElement>());
        Assert.Single(project.Spine.OfType<PauseElement>());
        Assert.Single(project.Overlays.OfType<MusicItem>());

        var disabled = project.Spine.OfType<SpanElement>().Single(s => !s.Enabled);
        Assert.Contains("sorry", disabled.Text);
    }

    [Fact]
    public void Title_attaches_to_the_span_that_follows_it()
    {
        var project = EdlReader.Read(Sample);

        var title = project.Overlays.OfType<TitleItem>().Single();
        var firstSpan = project.Spine.OfType<SpanElement>().First();

        Assert.Equal(firstSpan.Id, title.Start.Element);
        Assert.Equal("Cody Hurst", title.Text);
        Assert.Equal(2, title.Placement.Cell);
    }

    [Fact]
    public void Export_then_import_preserves_the_cut()
    {
        var original = EdlReader.Read(Sample);
        var reimported = EdlReader.Read(EdlWriter.Write(original), original);

        Assert.Equal(original.Spine.Count, reimported.Spine.Count);
        Assert.Equal(
            original.Spine.Select(e => e.Enabled),
            reimported.Spine.Select(e => e.Enabled));
    }

    [Fact]
    public void Reconciling_a_hand_edit_keeps_element_ids()
    {
        // This is the requirement that makes edit.md a live second face of the
        // document rather than a one-way export. Without it, editing in pluma
        // would orphan every overlay, marker and undo entry.
        var original = EdlReader.Read(Sample);
        var originalIds = original.Spine.OfType<SpanElement>().Select(s => s.Id).ToList();

        var handEdited = EdlWriter.Write(original).Replace("welcome back", "welcome back everyone");
        var reconciled = EdlReader.Read(handEdited, original);

        Assert.Equal(originalIds, reconciled.Spine.OfType<SpanElement>().Select(s => s.Id).ToList());
        Assert.Contains("welcome back everyone", reconciled.Spine.OfType<SpanElement>().First().Text);
    }

    [Fact]
    public void Project_json_survives_a_serialisation_round_trip()
    {
        var original = EdlReader.Read(Sample);
        var restored = ProjectJson.Deserialise(ProjectJson.Serialise(original));

        Assert.Equal(original.Spine.Count, restored.Spine.Count);
        Assert.Equal(original.Spine[0].Id, restored.Spine[0].Id);
        Assert.IsType<HoleElement>(restored.Spine.First(e => e is HoleElement));
    }
}
