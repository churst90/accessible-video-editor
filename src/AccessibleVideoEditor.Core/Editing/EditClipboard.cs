using System.Text.Json;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Serialization;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// Copy, cut and paste, including between tracks.
///
/// Pasting is checked against the target track's medium and refused out loud
/// rather than silently coerced. On a visual timeline a wrong paste is obvious
/// the moment you see it; here the only feedback is what the application says,
/// so an illegal paste has to be a spoken refusal, not a surprise.
///
/// Everything is deep-copied through JSON on the way in, with fresh IDs minted
/// on the way out, so pasting twice gives two independent items rather than two
/// references to one.
/// </summary>
public sealed class EditClipboard
{
    public ClipboardContents? Contents { get; private set; }

    public bool IsEmpty => Contents is null;

    public EditResult Copy(Project project, TimelineMap map, TrackId trackId, TimeSelection selection)
    {
        if (selection.IsEmpty) return EditResult.NoChange("nothing selected");

        var track = project.TrackOf(trackId);
        if (track is null) return EditResult.NoChange("no track focused");

        if (track.Kind == TrackKind.Programme)
        {
            var elements = map.Elements
                .Where(p => p.ProgrammeStart < selection.To && p.ProgrammeEnd > selection.From)
                .Select(p => p.Element)
                .ToList();

            if (elements.Count == 0) return EditResult.NoChange("nothing to copy");

            Contents = new ClipboardContents(
                Clone(elements),
                [],
                track.Media,
                selection.Length);

            return EditResult.Ok($"copied {elements.Count} element{(elements.Count == 1 ? "" : "s")}, " +
                                 $"{Timecode.Speak(selection.Length)}");
        }

        var items = project.ItemsOn(trackId)
            .Where(i => Overlaps(map, i, selection))
            .ToList();

        if (items.Count == 0) return EditResult.NoChange("nothing to copy");

        Contents = new ClipboardContents([], Clone(items), track.Media, selection.Length);

        return EditResult.Ok($"copied {items.Count} item{(items.Count == 1 ? "" : "s")}");
    }

    public EditResult Cut(Project project, TimelineMap map, TrackId trackId, TimeSelection selection)
    {
        var copied = Copy(project, map, trackId, selection);
        if (!copied.Changed) return copied;

        var removed = EditOperations.RippleDelete(project, selection);
        return removed.Changed
            ? EditResult.Ok($"cut {Timecode.Speak(selection.Length)}", removed.Warnings)
            : copied;
    }

    /// <summary>
    /// Inserts at the cursor. Spine elements ripple everything after them;
    /// overlay items are anchored to whatever element the cursor is inside.
    /// </summary>
    public EditResult Paste(Project project, TrackId trackId, double programmeTime)
    {
        if (Contents is null) return EditResult.NoChange("clipboard is empty");

        var track = project.TrackOf(trackId);
        if (track is null) return EditResult.NoChange("no track focused");

        if (track.Locked) return EditResult.NoChange($"{track.Name} is locked");

        if (!IsCompatible(Contents.Media, track.Media))
        {
            return EditResult.NoChange(
                $"cannot paste {Describe(Contents.Media)} onto {track.Name}, " +
                $"which is {Describe(track.Media)}");
        }

        if (Contents.Elements.Count > 0)
        {
            if (track.Kind != TrackKind.Programme)
            {
                return EditResult.NoChange($"{track.Name} does not hold programme elements");
            }

            EditOperations.SplitAt(project, programmeTime);

            var map = TimelineMap.Build(project);
            var next = map.Elements.FirstOrDefault(p => p.ProgrammeStart >= programmeTime - 1e-4);
            var index = next is null ? project.Spine.Count : project.Spine.IndexOf(next.Element);

            var pasted = Clone(Contents.Elements);
            project.Spine.InsertRange(index, pasted);

            return EditResult.Ok(
                $"pasted {pasted.Count} element{(pasted.Count == 1 ? "" : "s")} into {track.Name}");
        }

        var timeline = TimelineMap.Build(project);
        if (timeline.ToAnchor(programmeTime) is not { } anchor)
        {
            return EditResult.NoChange("nowhere to anchor the paste");
        }

        var items = Clone(Contents.Items);
        foreach (var item in items)
        {
            item.Track = trackId;
            item.Start = anchor;

            // Re-express the extent as a length; the source anchors named
            // elements that may not exist at the destination.
            item.End = null;
            item.Length ??= Contents.Length;
        }

        project.Overlays.AddRange(items);

        return EditResult.Ok($"pasted {items.Count} item{(items.Count == 1 ? "" : "s")} into {track.Name}");
    }

    public void Clear() => Contents = null;

    private static bool Overlaps(TimelineMap map, OverlayItem item, TimeSelection selection)
    {
        var start = map.ResolveAnchor(item.Start);
        if (start is null) return false;

        var end = item.End is { } anchor ? map.ResolveAnchor(anchor) : start + (item.Length ?? 0);
        return end is not null && start < selection.To && end > selection.From;
    }

    /// <summary>Mixed accepts anything; otherwise the media must match.</summary>
    private static bool IsCompatible(TrackMedia source, TrackMedia target) =>
        target == TrackMedia.Mixed || source == TrackMedia.Mixed || source == target;

    private static string Describe(TrackMedia media) => media switch
    {
        TrackMedia.Video => "video",
        TrackMedia.Audio => "audio",
        TrackMedia.Image => "an image",
        _ => "mixed media",
    };

    private static List<T> Clone<T>(IReadOnlyCollection<T> source)
    {
        var json = JsonSerializer.Serialize(source, ProjectJson.Options);
        var copy = JsonSerializer.Deserialize<List<T>>(json, ProjectJson.Options) ?? [];

        // Fresh IDs, or a second paste would collide with the first.
        foreach (var element in copy)
        {
            switch (element)
            {
                case SpineElement spine:
                    spine.Id = Ids.NewElement();
                    break;

                case OverlayItem item:
                    item.Id = Ids.NewItem();
                    break;
            }
        }

        return copy;
    }
}

public sealed record ClipboardContents(
    IReadOnlyList<SpineElement> Elements,
    IReadOnlyList<OverlayItem> Items,
    TrackMedia Media,
    double Length)
{
    public string Describe() =>
        Elements.Count > 0
            ? $"{Elements.Count} element{(Elements.Count == 1 ? "" : "s")}, {Timecode.Speak(Length)}"
            : $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";
}
