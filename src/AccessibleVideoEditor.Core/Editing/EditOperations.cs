using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// The edit verbs. Three deletes, because a video editor needs three and
/// collapsing them is how people lose work:
/// </summary>
public static class EditOperations
{
    private const double Epsilon = 1e-4;

    /// <summary>
    /// Splits the element under <paramref name="programmeTime"/> in two. The
    /// first half keeps the original ID so anything anchored to it stays put;
    /// the second half gets a fresh one.
    /// </summary>
    public static EditResult SplitAt(Project project, double programmeTime)
    {
        var map = TimelineMap.Build(project);
        if (map.Locate(programmeTime) is not { } placed)
        {
            return EditResult.NoChange("nothing to split");
        }

        var offset = programmeTime - placed.ProgrammeStart;
        if (offset <= Epsilon || offset >= placed.Duration - Epsilon)
        {
            return EditResult.NoChange("already a boundary");
        }

        var index = project.Spine.IndexOf(placed.Element);
        var second = SplitElement(placed, offset);
        if (second is null)
        {
            return EditResult.NoChange($"cannot split a {placed.Element.GetType().Name}");
        }

        project.Spine.Insert(index + 1, second);

        // Anything anchored past the split point belongs to the new half.
        var warnings = new List<string>();
        foreach (var anchorRef in AnchorsOf(project))
        {
            var anchor = anchorRef.Get();
            if (anchor.Element != placed.Element.Id || anchor.Offset <= offset) continue;

            anchorRef.Set(new TimeAnchor(second.Id, anchor.Offset - offset));
            warnings.Add("an overlay moved to the second half");
        }

        return EditResult.Ok(
            $"split at {Timecode.FormatShort(programmeTime)}",
            warnings.Distinct().ToList());
    }

    /// <summary>Removes the range and closes the gap. Everything after shifts earlier.</summary>
    public static EditResult RippleDelete(Project project, TimeSelection selection)
    {
        if (selection.IsEmpty) return EditResult.NoChange("nothing selected");

        var (from, to) = (selection.From, selection.To);
        var removed = RemoveRange(project, from, to, out var warnings);

        if (removed <= 0) return EditResult.NoChange("nothing to delete");

        var remaining = TimelineMap.Build(project).Duration;
        return EditResult.Ok(
            $"deleted {Timecode.Speak(removed)}, {Timecode.Speak(remaining)} remaining",
            warnings);
    }

    /// <summary>
    /// Removes the range but leaves a silent gap of the same length, so nothing
    /// downstream moves. The gap is a <see cref="PauseElement"/> - a finished
    /// beat, as opposed to a hole, which is a to-do.
    /// </summary>
    public static EditResult Lift(Project project, TimeSelection selection)
    {
        if (selection.IsEmpty) return EditResult.NoChange("nothing selected");

        var (from, to) = (selection.From, selection.To);
        var length = selection.Length;

        var index = InsertionIndexFor(project, from);
        var removed = RemoveRange(project, from, to, out var warnings);
        if (removed <= 0) return EditResult.NoChange("nothing to lift");

        index = Math.Clamp(InsertionIndexFor(project, from), 0, project.Spine.Count);
        project.Spine.Insert(index, new PauseElement
        {
            Id = Ids.NewElement(),
            Length = length,
            TransitionIn = Transition.Cut,
        });

        return EditResult.Ok($"lifted {Timecode.Speak(removed)}, timing preserved", warnings);
    }

    /// <summary>The non-destructive cut. Toggles the element under the cursor.</summary>
    public static EditResult ToggleDisable(Project project, double programmeTime)
    {
        var map = TimelineMap.Build(project);
        if (map.Locate(programmeTime) is not { } placed)
        {
            return EditResult.NoChange("nothing under the cursor");
        }

        placed.Element.Enabled = !placed.Element.Enabled;

        return EditResult.Ok(placed.Element.Enabled
            ? $"restored {Timecode.Speak(placed.Duration)}"
            : $"cut {Timecode.Speak(placed.Duration)}");
    }

    /// <summary>
    /// Reserves blank space to fill in later. Blocks the master render until
    /// it is filled, so structure-first editing cannot ship a gap by accident.
    /// </summary>
    public static EditResult InsertHole(Project project, double programmeTime, double length, string note)
    {
        SplitAt(project, programmeTime);

        var index = Math.Clamp(InsertionIndexFor(project, programmeTime), 0, project.Spine.Count);
        project.Spine.Insert(index, new HoleElement
        {
            Id = Ids.NewElement(),
            Length = length,
            Note = note,
            TransitionIn = Transition.Cut,
        });

        return EditResult.Ok($"hole, {Timecode.Speak(length)}{(note.Length > 0 ? $", {note}" : string.Empty)}");
    }

    /// <summary>
    /// Undoes a split: joins the element at the cursor with the one before it,
    /// if they are the two halves of the same shot. Refuses out loud otherwise,
    /// naming the reason - two clips from different files are not a split that
    /// can be healed, they are an edit.
    /// </summary>
    public static EditResult Heal(Project project, double programmeTime)
    {
        var map = TimelineMap.Build(project);
        if (map.Locate(programmeTime) is not { } placed)
        {
            return EditResult.NoChange("nothing under the cursor");
        }

        var index = project.Spine.IndexOf(placed.Element);
        if (index <= 0) return EditResult.NoChange("no boundary before this");

        var previous = project.Spine[index - 1];

        if (!AreContiguous(previous, placed.Element))
        {
            return EditResult.NoChange("cannot heal - not two halves of one shot");
        }

        var healed = Timecode.Speak(placed.Duration);

        switch (previous, placed.Element)
        {
            case (SpanElement first, SpanElement second):
                first.SourceOut = second.SourceOut;
                first.Text = string.Join(" ", new[] { first.Text, second.Text }.Where(t => t.Length > 0));
                first.Words.AddRange(second.Words);
                break;

            case (ClipElement first, ClipElement second):
                first.SourceOut = second.SourceOut;
                break;

            default:
                return EditResult.NoChange("cannot heal these two");
        }

        // Anchors on the absorbed half keep their position by being offset from
        // the surviving half's start instead.
        var shift = placed.ProgrammeStart - (map.Find(previous.Id)?.ProgrammeStart ?? 0);
        var warnings = Reanchor(project, placed.Element.Id, previous.Id, shift);
        project.Spine.Remove(placed.Element);

        return EditResult.Ok($"healed, {healed} rejoined", warnings);
    }

    /// <summary>
    /// Moves the element's in-point to the cursor, shortening it. Downstream
    /// ripples, which is the behaviour that keeps a transcript-driven edit
    /// coherent - a roll edit, which moves a boundary without changing total
    /// length, is <see cref="Roll"/>.
    /// </summary>
    public static EditResult TrimHead(Project project, double programmeTime) =>
        Trim(project, programmeTime, head: true);

    /// <summary>Moves the element's out-point to the cursor, shortening it.</summary>
    public static EditResult TrimTail(Project project, double programmeTime) =>
        Trim(project, programmeTime, head: false);

    private static EditResult Trim(Project project, double programmeTime, bool head)
    {
        var map = TimelineMap.Build(project);
        if (map.Locate(programmeTime) is not { Media: not null } placed)
        {
            return EditResult.NoChange("nothing trimmable under the cursor");
        }

        var offset = programmeTime - placed.ProgrammeStart;
        if (offset <= Epsilon || offset >= placed.Duration - Epsilon)
        {
            return EditResult.NoChange("already at the edge");
        }

        var sourceTime = placed.SourceTimeAt(offset);
        var removed = head ? offset : placed.Duration - offset;

        switch (placed.Element)
        {
            case SpanElement span when head: span.SourceIn = sourceTime; break;
            case SpanElement span: span.SourceOut = sourceTime; break;
            case ClipElement clip when head: clip.SourceIn = sourceTime; break;
            case ClipElement clip: clip.SourceOut = sourceTime; break;
            default: return EditResult.NoChange("this element has no in and out points");
        }

        return EditResult.Ok($"trimmed {(head ? "head" : "tail")}, {Timecode.Speak(removed)} removed");
    }

    /// <summary>
    /// Moves the boundary at the cursor without changing total length: one side
    /// gains what the other loses. The classic roll edit.
    /// </summary>
    public static EditResult Roll(Project project, double boundaryTime, double delta)
    {
        var map = TimelineMap.Build(project);

        var after = map.Elements.FirstOrDefault(p => Math.Abs(p.ProgrammeStart - boundaryTime) < 0.05);
        if (after is null) return EditResult.NoChange("not on a boundary");

        var index = project.Spine.IndexOf(after.Element);
        if (index <= 0) return EditResult.NoChange("nothing before this boundary");

        var before = map.Find(project.Spine[index - 1].Id);
        if (before is null) return EditResult.NoChange("nothing before this boundary");

        if (!Extend(before.Element, delta, tail: true) || !Extend(after.Element, -delta, tail: false))
        {
            return EditResult.NoChange("these elements cannot be rolled");
        }

        return EditResult.Ok(
            $"rolled {Timecode.Speak(Math.Abs(delta))} {(delta > 0 ? "later" : "earlier")}");
    }

    /// <summary>
    /// Retimes the element under the cursor. 2.0 plays it twice as fast in half
    /// the time; the media it plays does not change, only how long it occupies.
    /// </summary>
    public static EditResult SetSpeed(Project project, double programmeTime, double speed)
    {
        if (speed <= 0) return EditResult.NoChange("speed must be greater than zero");

        var map = TimelineMap.Build(project);
        if (map.Locate(programmeTime) is not { Media: not null } placed)
        {
            return EditResult.NoChange("nothing retimeable under the cursor");
        }

        placed.Element.Speed = speed;

        var after = TimelineMap.Build(project).Find(placed.Element.Id);
        return EditResult.Ok(
            $"speed {speed:0.##} times, now {Timecode.Speak(after?.Duration ?? 0)}");
    }

    /// <summary>
    /// Cycles the drift given to a still. Refused on moving footage, where it
    /// would mean nothing.
    /// </summary>
    public static EditResult CycleKenBurns(Project project, double programmeTime)
    {
        var map = TimelineMap.Build(project);

        if (map.Locate(programmeTime) is not { } placed) return EditResult.NoChange("nothing under the cursor");

        var source = placed.Media is { } media ? project.SourceOf(media.Source) : null;

        if (source?.Kind != SourceKind.Image)
        {
            return EditResult.NoChange("this is not a still, so it has nothing to drift");
        }

        placed.Element.KenBurns = placed.Element.KenBurns switch
        {
            KenBurns.None => KenBurns.ZoomIn,
            KenBurns.ZoomIn => KenBurns.ZoomOut,
            KenBurns.ZoomOut => KenBurns.PanLeft,
            KenBurns.PanLeft => KenBurns.PanRight,
            _ => KenBurns.None,
        };

        return EditResult.Ok(placed.Element.KenBurns switch
        {
            KenBurns.None => "no movement",
            KenBurns.ZoomIn => "slow zoom in",
            KenBurns.ZoomOut => "slow zoom out",
            KenBurns.PanLeft => "slow pan left",
            _ => "slow pan right",
        });
    }

    /// <summary>
    /// Sets how long a segment stays on screen.
    ///
    /// Only meaningful where the length is arbitrary rather than given by the
    /// media: a still, a card, a hole, a pause. A photograph has no duration of
    /// its own, so it can be held for as long as you like - which is the whole
    /// difference between a still and a clip.
    /// </summary>
    public static EditResult SetDuration(Project project, double programmeTime, double seconds)
    {
        if (seconds <= 0) return EditResult.NoChange("a duration has to be more than nothing");

        var map = TimelineMap.Build(project);
        if (map.Locate(programmeTime) is not { } placed) return EditResult.NoChange("nothing under the cursor");

        switch (placed.Element)
        {
            case CardElement card:
                card.Length = seconds;
                break;

            case HoleElement hole:
                hole.Length = seconds;
                break;

            case PauseElement pause:
                pause.Length = seconds;
                break;

            case ClipElement clip when IsStill(project, clip.Source):
                // A still is held by moving its out point; there is no media
                // beyond the single frame to run out of.
                clip.SourceIn = 0;
                clip.SourceOut = seconds;
                break;

            default:
                return EditResult.NoChange(
                    "this segment's length comes from its media; trim it instead");
        }

        return EditResult.Ok($"{Timecode.Speak(seconds)} on screen");
    }

    /// <summary>Stretches or shrinks a still by a step, for nudging its length.</summary>
    public static EditResult AdjustDuration(Project project, double programmeTime, double by)
    {
        var map = TimelineMap.Build(project);
        if (map.Locate(programmeTime) is not { } placed) return EditResult.NoChange("nothing under the cursor");

        return SetDuration(project, programmeTime, Math.Max(0.1, placed.Duration + by));
    }

    private static bool IsStill(Project project, SourceId source) =>
        project.SourceOf(source)?.Kind == SourceKind.Image;

    /// <summary>Silences an element without removing its picture.</summary>
    public static EditResult ToggleMute(Project project, double programmeTime)
    {
        var map = TimelineMap.Build(project);
        if (map.Locate(programmeTime) is not { } placed)
        {
            return EditResult.NoChange("nothing under the cursor");
        }

        placed.Element.Muted = !placed.Element.Muted;
        return EditResult.Ok(placed.Element.Muted ? "item muted" : "item unmuted");
    }

    /// <summary>Sets or clears the transition entering the element at the cursor.</summary>
    /// <summary>
    /// Deletes a track and everything on it. The programme track is refused:
    /// it is the spine, and a project without one is not a project.
    /// </summary>
    public static EditResult RemoveTrack(Project project, TrackId id)
    {
        if (project.TrackOf(id) is not { } track) return EditResult.NoChange("no such track");

        if (track.Kind == TrackKind.Programme)
        {
            return EditResult.NoChange("the programme track cannot be deleted");
        }

        var items = project.Overlays.Count(i => i.Track == id);

        project.Overlays.RemoveAll(i => i.Track == id);
        project.Tracks.Remove(track);

        return EditResult.Ok(
            items == 0
                ? $"deleted {track.Name}"
                : $"deleted {track.Name} and {items} {(items == 1 ? "item" : "items")} on it");
    }

    public static EditResult SetTransition(Project project, double programmeTime, Transition? transition)
    {
        var map = TimelineMap.Build(project);

        var target = map.Elements
            .Where(p => p.ProgrammeStart <= programmeTime + 0.05)
            .LastOrDefault();

        if (target is null || project.Spine.IndexOf(target.Element) == 0)
        {
            return EditResult.NoChange("the first element has no incoming boundary");
        }

        target.Element.TransitionIn = transition;

        return EditResult.Ok(transition is null
            ? "transition cleared, back to the project default"
            : $"{transition.Describe()} entering {target.Element.Describe()}");
    }

    // ---- by identity, for the transcript -----------------------------------
    //
    // The transcript addresses segments by ID rather than by time, because a
    // cut line has no programme time at all and must still be deletable,
    // restorable and movable.

    /// <summary>Ripple-deletes a segment named by ID, wherever it is.</summary>
    public static EditResult DeleteSegment(Project project, ElementId id)
    {
        var element = project.Element(id);
        if (element is null) return EditResult.NoChange("no such segment");

        var map = TimelineMap.Build(project);
        var placed = map.Find(id);

        // A disabled segment occupies no programme time, so removing it cannot
        // shift anything - it just goes.
        if (placed is null)
        {
            var orphaned = Reanchor(project, id, null, 0);
            project.Spine.Remove(element);
            return EditResult.Ok("deleted a cut segment", orphaned);
        }

        var removed = RemoveRange(project, placed.ProgrammeStart, placed.ProgrammeEnd, out var warnings);

        return removed <= 0
            ? EditResult.NoChange("nothing to delete")
            : EditResult.Ok(
                $"deleted {Timecode.Speak(removed)}, " +
                $"{Timecode.Speak(TimelineMap.Build(project).Duration)} remaining",
                warnings);
    }

    /// <summary>The non-destructive cut, by ID. The line stays and can be restored.</summary>
    public static EditResult ToggleDisableSegment(Project project, ElementId id)
    {
        var element = project.Element(id);
        if (element is null) return EditResult.NoChange("no such segment");

        element.Enabled = !element.Enabled;

        var length = TimelineMap.Build(project).Find(id)?.Duration ?? element.Duration;

        return EditResult.Ok(element.Enabled
            ? $"restored, {Timecode.Speak(length)}"
            : $"cut, {Timecode.Speak(element.Duration)}");
    }

    /// <summary>
    /// Moves a segment earlier or later in the spine.
    ///
    /// This is the operation that makes editing as text worth doing: reordering
    /// a line reorders the video. Nothing needs re-anchoring, because overlays
    /// name the segment they ride on rather than a time - they move with it for
    /// free.
    /// </summary>
    public static EditResult MoveSegment(Project project, ElementId id, int delta)
    {
        var element = project.Element(id);
        if (element is null) return EditResult.NoChange("no such segment");

        var from = project.Spine.IndexOf(element);
        var to = from + delta;

        if (to < 0) return EditResult.NoChange("already first");
        if (to >= project.Spine.Count) return EditResult.NoChange("already last");

        project.Spine.RemoveAt(from);
        project.Spine.Insert(to, element);

        return EditResult.Ok($"moved {(delta < 0 ? "earlier" : "later")}, now {to + 1} of {project.Spine.Count}");
    }

    /// <summary>
    /// Changes what a segment's caption says. Never changes the cut - the rule
    /// the transcript pane announces the first time you type in it.
    /// </summary>
    public static EditResult SetCaption(Project project, ElementId id, string text)
    {
        var element = project.Element(id);
        if (element is null) return EditResult.NoChange("no such segment");

        var trimmed = text.Trim();

        // For speech, storing text identical to the transcript would just be
        // noise; clearing the override keeps them in step.
        if (element is SpanElement span && trimmed == span.Text.Trim())
        {
            element.Caption = null;
            return EditResult.NoChange("caption unchanged");
        }

        if (element.Caption == trimmed) return EditResult.NoChange("caption unchanged");

        element.Caption = trimmed.Length == 0 ? null : trimmed;
        return EditResult.Ok("caption updated");
    }

    public static EditResult ToggleCaption(Project project, ElementId id)
    {
        var element = project.Element(id);
        if (element is null) return EditResult.NoChange("no such segment");

        element.Captioned = !element.Captioned;
        return EditResult.Ok(element.Captioned ? "captioned" : "no caption");
    }

    // ---- takes -------------------------------------------------------------

    /// <summary>
    /// Cycles to the next or previous take of the segment under the cursor.
    /// The segment keeps its identity and its place; only the media changes, so
    /// auditioning takes never disturbs the edit around them.
    /// </summary>
    public static EditResult CycleTake(Project project, ElementId id, int direction)
    {
        var element = project.Element(id);
        if (element is null) return EditResult.NoChange("no such segment");

        if (element.Takes.Count == 0) return EditResult.NoChange("this segment has no takes");
        if (element.Takes.Count == 1) return EditResult.NoChange("only one take");

        var count = element.Takes.Count;
        element.TakeIndex = ((element.TakeIndex + direction) % count + count) % count;

        var take = element.Takes[element.TakeIndex];
        return EditResult.Ok(take.Describe(element.TakeIndex, count));
    }

    /// <summary>
    /// Adds a take and makes it active - a newly recorded take is the one you
    /// want to hear.
    /// </summary>
    public static EditResult AddTake(Project project, ElementId id, Take take)
    {
        var element = project.Element(id);
        if (element is null) return EditResult.NoChange("no such segment");

        // The first recorded take has to carry the segment's original media
        // with it, or choosing take 2 would leave no way back to what was there.
        if (element.Takes.Count == 0 && Original(element) is { } original)
        {
            element.Takes.Add(original);
        }

        element.Takes.Add(take);
        element.TakeIndex = element.Takes.Count - 1;

        return EditResult.Ok(take.Describe(element.TakeIndex, element.Takes.Count));
    }

    /// <summary>Removes the active take. The segment keeps whatever remains.</summary>
    public static EditResult DeleteTake(Project project, ElementId id)
    {
        var element = project.Element(id);
        if (element is null) return EditResult.NoChange("no such segment");

        if (element.Takes.Count == 0) return EditResult.NoChange("this segment has no takes");
        if (element.Takes.Count == 1) return EditResult.NoChange("cannot delete the only take");

        element.Takes.RemoveAt(Math.Clamp(element.TakeIndex, 0, element.Takes.Count - 1));
        element.TakeIndex = Math.Clamp(element.TakeIndex, 0, element.Takes.Count - 1);

        var take = element.Takes[element.TakeIndex];
        return EditResult.Ok($"take deleted. Now {take.Describe(element.TakeIndex, element.Takes.Count)}");
    }

    /// <summary>The segment's pre-take media, captured as take 1.</summary>
    private static Take? Original(SpineElement element) => element switch
    {
        SpanElement span => new Take
        {
            Id = Ids.NewTake(),
            Source = span.Source,
            SourceIn = span.SourceIn,
            SourceOut = span.SourceOut,
            Label = "original",
        },
        ClipElement clip => new Take
        {
            Id = Ids.NewTake(),
            Source = clip.Source,
            SourceIn = clip.SourceIn,
            SourceOut = clip.SourceOut,
            Label = "original",
        },
        _ => null,
    };

    /// <summary>
    /// Splits an overlay segment - a title, a card, b-roll - on a track other
    /// than the programme.
    /// </summary>
    public static EditResult SplitItemAt(Project project, TrackId trackId, double programmeTime)
    {
        var map = TimelineMap.Build(project);

        foreach (var item in project.ItemsOn(trackId).Where(i => i.Enabled).ToList())
        {
            var start = map.ResolveAnchor(item.Start);
            if (start is null) continue;

            var end = item.End is { } anchor ? map.ResolveAnchor(anchor) : start + (item.Length ?? 0);
            if (end is null) continue;

            if (programmeTime <= start + Epsilon || programmeTime >= end - Epsilon) continue;

            var second = Duplicate(item);
            if (second is null) return EditResult.NoChange("this segment cannot be split");

            // The first half ends at the cut; the second begins there and keeps
            // the original extent.
            var offsetIntoItem = programmeTime - start.Value;

            second.Start = map.ToAnchor(programmeTime) ?? item.Start;
            second.End = item.End;
            second.Length = item.End is null ? Math.Max(0, end.Value - programmeTime) : null;

            if (second is BrollItem secondBroll && item is BrollItem firstBroll)
            {
                secondBroll.SourceIn = firstBroll.SourceIn + offsetIntoItem;
            }

            item.End = null;
            item.Length = offsetIntoItem;

            project.Overlays.Add(second);

            return EditResult.Ok($"split at {Timecode.FormatShort(programmeTime)}");
        }

        return EditResult.NoChange("nothing to split on this track");
    }

    private static OverlayItem? Duplicate(OverlayItem item) => item switch
    {
        TitleItem title => new TitleItem
        {
            Id = Ids.NewItem(), Track = title.Track, Text = title.Text,
            Placement = title.Placement, Style = title.Style, Start = title.Start,
        },
        CardItem card => new CardItem
        {
            Id = Ids.NewItem(), Track = card.Track,
            Composition = card.Composition.Clone(), Start = card.Start,
        },
        GraphicItem graphic => new GraphicItem
        {
            Id = Ids.NewItem(), Track = graphic.Track, Source = graphic.Source,
            Placement = graphic.Placement, Scale = graphic.Scale, Opacity = graphic.Opacity,
            Start = graphic.Start,
        },
        BrollItem broll => new BrollItem
        {
            Id = Ids.NewItem(), Track = broll.Track, Source = broll.Source,
            SourceIn = broll.SourceIn, GainDb = broll.GainDb, Fit = broll.Fit,
            AudioTrack = broll.AudioTrack, Start = broll.Start,
        },
        AudioItem audio => new AudioItem
        {
            Id = Ids.NewItem(), Track = audio.Track, Source = audio.Source,
            SourceIn = audio.SourceIn, GainDb = audio.GainDb, LinkedTo = audio.LinkedTo,
            Start = audio.Start,
        },
        _ => null,
    };

    // ---- assembly and detaching --------------------------------------------

    /// <summary>
    /// Inserts a whole source at the cursor, rippling everything after it.
    /// Premiere's Insert.
    /// </summary>
    public static EditResult InsertSource(
        Project project,
        SourceId sourceId,
        double programmeTime,
        double? length = null)
    {
        var source = project.SourceOf(sourceId);
        if (source is null) return EditResult.NoChange("no such source");

        // A still has no duration of its own, so the project supplies one.
        var isStill = source.Kind == SourceKind.Image;
        var duration = length ?? (isStill ? project.Settings.StillDuration : source.Duration);

        if (duration <= 0) return EditResult.NoChange($"{Path.GetFileName(source.Path)} has no duration");

        InsertRange(project, sourceId, 0, duration, programmeTime, audioTrack: 0);

        return EditResult.Ok(
            $"inserted {Path.GetFileName(source.Path)}, {Timecode.Speak(duration)}"
            + (isStill ? " as a still" : string.Empty));
    }

    /// <summary>
    /// Puts one range of one source on the spine at the cursor, rippling. The
    /// single place a clip is added, so inserting a whole source, a subclip and
    /// a marked range all land identically - three code paths building the same
    /// element differently is how they drift apart.
    /// </summary>
    public static ElementId InsertRange(
        Project project,
        SourceId sourceId,
        double sourceIn,
        double sourceOut,
        double programmeTime,
        int audioTrack)
    {
        var isStill = project.SourceOf(sourceId)?.Kind == SourceKind.Image;

        SplitAt(project, programmeTime);

        var index = Math.Clamp(InsertionIndexFor(project, programmeTime), 0, project.Spine.Count);
        var id = Ids.NewElement();

        project.Spine.Insert(index, new ClipElement
        {
            Id = id,
            Source = sourceId,
            SourceIn = sourceIn,
            SourceOut = sourceOut,
            AudioTrack = audioTrack,
            TransitionIn = Transition.Cut,

            // A still that does not move reads as a frozen video.
            KenBurns = isStill && project.Settings.KenBurnsByDefault
                ? KenBurns.ZoomIn
                : KenBurns.None,
        });

        return id;
    }

    /// <summary>
    /// Replaces what is at the cursor for the length of the source, without
    /// changing anything downstream. Premiere's Overwrite.
    /// </summary>
    public static EditResult OverwriteSource(
        Project project,
        SourceId sourceId,
        double programmeTime,
        double? length = null)
    {
        var source = project.SourceOf(sourceId);
        if (source is null) return EditResult.NoChange("no such source");

        var duration = length ?? source.Duration;
        if (duration <= 0) return EditResult.NoChange($"{Path.GetFileName(source.Path)} has no duration");

        var map = TimelineMap.Build(project);
        var end = Math.Min(programmeTime + duration, map.Duration);

        // Clearing the range first is what makes this an overwrite rather than
        // an insert; the replacement is the same length, so nothing shifts.
        if (end > programmeTime)
        {
            RemoveRange(project, programmeTime, end, out _);
        }

        return InsertSource(project, sourceId, programmeTime, duration) is { Changed: true }
            ? EditResult.Ok($"overwrote {Timecode.Speak(duration)} with {Path.GetFileName(source.Path)}")
            : EditResult.NoChange("nothing to overwrite with");
    }

    /// <summary>
    /// Moves a segment's sound onto an audio track, leaving the picture where it
    /// is. Needed whenever you want to keep someone's voice while cutting away
    /// from their face.
    /// </summary>
    public static EditResult DetachAudio(Project project, ElementId id, TrackId audioTrack)
    {
        var element = project.Element(id);
        if (element is null) return EditResult.NoChange("no such segment");

        if (element.Muted) return EditResult.NoChange("this segment's audio is already detached or muted");

        var track = project.TrackOf(audioTrack);
        if (track is null || track.Media != TrackMedia.Audio)
        {
            return EditResult.NoChange("pick an audio track to detach onto");
        }

        var media = element switch
        {
            SpanElement span => (Source: span.Source, In: span.SourceIn),
            ClipElement clip => (Source: clip.Source, In: clip.SourceIn),
            _ => default,
        };

        if (media.Source.IsUnset) return EditResult.NoChange("this segment has no sound to detach");

        var placed = TimelineMap.Build(project).Find(id);
        if (placed is null) return EditResult.NoChange("this segment is not in the programme");

        project.Overlays.Add(new AudioItem
        {
            Id = Ids.NewItem(),
            Track = audioTrack,
            Source = media.Source,
            SourceIn = media.In,
            LinkedTo = id,
            Start = new TimeAnchor(id),
            Length = placed.Duration,
        });

        element.Muted = true;

        return EditResult.Ok(
            $"audio detached onto {track.Name}, {Timecode.Speak(placed.Duration)}. " +
            "The picture keeps its place");
    }

    /// <summary>Puts detached audio back and unmutes the segment it came from.</summary>
    public static EditResult ReattachAudio(Project project, ElementId id)
    {
        var element = project.Element(id);
        if (element is null) return EditResult.NoChange("no such segment");

        var detached = project.Overlays.OfType<AudioItem>().Where(a => a.LinkedTo == id).ToList();

        if (detached.Count == 0) return EditResult.NoChange("nothing was detached from this segment");

        foreach (var item in detached) project.Overlays.Remove(item);

        element.Muted = false;

        return EditResult.Ok("audio reattached");
    }

    // ---- internals ---------------------------------------------------------

    private static bool AreContiguous(SpineElement first, SpineElement second) =>
        (first, second) switch
        {
            (SpanElement a, SpanElement b) =>
                a.Source == b.Source && Math.Abs(a.SourceOut - b.SourceIn) < 0.05,
            (ClipElement a, ClipElement b) =>
                a.Source == b.Source && Math.Abs(a.SourceOut - b.SourceIn) < 0.05,
            _ => false,
        };

    private static bool Extend(SpineElement element, double delta, bool tail)
    {
        switch (element)
        {
            case SpanElement span when tail: span.SourceOut += delta; return true;
            case SpanElement span: span.SourceIn -= delta; return true;
            case ClipElement clip when tail: clip.SourceOut += delta; return true;
            case ClipElement clip: clip.SourceIn -= delta; return true;
            case HoleElement hole: hole.Length += delta; return true;
            case PauseElement pause: pause.Length += delta; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Moves anything anchored to <paramref name="from"/> onto
    /// <paramref name="to"/>. A null target means the anchor has nowhere to go
    /// and the owner is reported as orphaned rather than silently dropped.
    /// </summary>
    private static List<string> Reanchor(Project project, ElementId from, ElementId? to, double offsetShift)
    {
        var warnings = new List<string>();

        foreach (var anchorRef in AnchorsOf(project))
        {
            var anchor = anchorRef.Get();
            if (anchor.Element != from) continue;

            if (to is null)
            {
                warnings.Add($"{anchorRef.Owner} lost its anchor");
                continue;
            }

            anchorRef.Set(new TimeAnchor(to.Value, anchor.Offset + offsetShift));
            warnings.Add($"{anchorRef.Owner} re-anchored");
        }

        return warnings.Distinct().ToList();
    }

    /// <summary>
    /// Splits at both ends, drops what is between, then re-anchors every
    /// overlay and marker through the time remapping the delete implies. That
    /// re-anchoring is the part a visual editor gets for free by showing you
    /// what moved; here it has to be computed and announced.
    /// </summary>
    internal static double RemoveRange(Project project, double from, double to, out List<string> warnings)
    {
        SplitAt(project, from);
        SplitAt(project, to);

        var map = TimelineMap.Build(project);
        var anchors = CaptureAnchors(project, map);

        var doomed = map.Elements
            .Where(p => p.ProgrammeStart >= from - Epsilon && p.ProgrammeEnd <= to + Epsilon)
            .Select(p => p.Element)
            .ToList();

        if (doomed.Count == 0)
        {
            warnings = [];
            return 0;
        }

        var removed = doomed.Sum(e => map.Find(e.Id)?.Duration ?? 0);
        foreach (var element in doomed)
        {
            project.Spine.Remove(element);
        }

        // A time t before the cut stays; inside it collapses to the cut point;
        // after it shifts earlier by the removed length.
        var remapped = TimelineMap.Build(project);
        warnings = ReapplyAnchors(anchors, remapped, t =>
            t <= from ? t : t >= to ? t - removed : from);

        return removed;
    }

    private static SpineElement? SplitElement(PlacedElement placed, double offset) => placed.Element switch
    {
        // The split point goes through SourceTimeAt so retimed elements split
        // where the cursor actually is, not where it would be at 1x.
        SpanElement span when placed.Media is not null => new SpanElement
        {
            Id = Ids.NewElement(),
            Source = span.Source,
            SourceIn = Truncate(span, placed.SourceTimeAt(offset)),
            SourceOut = span.SourceOut,
            Speed = span.Speed,
            Text = SplitText(span, placed.SourceTimeAt(offset), tail: true),
            Words = span.Words.Where(w => w.Start >= placed.SourceTimeAt(offset)).ToList(),
            TransitionIn = Transition.Cut,
        }.Also(_ =>
        {
            var cut = placed.SourceTimeAt(offset);
            span.Words = span.Words.Where(w => w.Start < cut).ToList();
            span.Text = SplitText(span, cut, tail: false);
            span.SourceOut = Truncate(span, cut);
        }),

        ClipElement clip when placed.Media is not null => new ClipElement
        {
            Id = Ids.NewElement(),
            Source = clip.Source,
            SourceIn = placed.SourceTimeAt(offset),
            SourceOut = clip.SourceOut,
            Speed = clip.Speed,
            AudioTrack = clip.AudioTrack,
            GainDb = clip.GainDb,
            Fit = clip.Fit,
            TransitionIn = Transition.Cut,
        }.Also(_ => clip.SourceOut = placed.SourceTimeAt(offset)),

        HoleElement hole => new HoleElement
        {
            Id = Ids.NewElement(),
            Length = hole.Length - offset,
            Note = hole.Note,
            TransitionIn = Transition.Cut,
        }.Also(_ => hole.Length = offset),

        PauseElement pause => new PauseElement
        {
            Id = Ids.NewElement(),
            Length = pause.Length - offset,
            TransitionIn = Transition.Cut,
        }.Also(_ => pause.Length = offset),

        _ => null,
    };

    private static double Truncate(SpanElement span, double sourceTime) =>
        Math.Clamp(sourceTime, span.SourceIn, span.SourceOut);

    private static string SplitText(SpanElement span, double cut, bool tail)
    {
        if (span.Words.Count == 0) return tail ? string.Empty : span.Text;

        var words = span.Words.Where(w => tail ? w.Start >= cut : w.Start < cut).Select(w => w.Text);
        return string.Join(" ", words).Trim();
    }

    private static int InsertionIndexFor(Project project, double programmeTime)
    {
        var map = TimelineMap.Build(project);
        var next = map.Elements.FirstOrDefault(p => p.ProgrammeStart >= programmeTime - Epsilon);
        return next is null ? project.Spine.Count : project.Spine.IndexOf(next.Element);
    }

    private sealed record AnchorRef(Func<TimeAnchor> Get, Action<TimeAnchor> Set, string Owner);

    private static IEnumerable<AnchorRef> AnchorsOf(Project project)
    {
        foreach (var item in project.Overlays)
        {
            var captured = item;
            yield return new AnchorRef(() => captured.Start, a => captured.Start = a, captured.Describe());

            if (captured.End is not null)
            {
                yield return new AnchorRef(
                    () => captured.End!.Value, a => captured.End = a, captured.Describe());
            }
        }

        foreach (var marker in project.Markers)
        {
            var captured = marker;
            yield return new AnchorRef(() => captured.At, a => captured.At = a, captured.Describe());
        }
    }

    private static List<(AnchorRef Ref, double Time)> CaptureAnchors(Project project, TimelineMap map) =>
        AnchorsOf(project)
            .Select(r => (Ref: r, Time: map.ResolveAnchor(r.Get())))
            .Where(x => x.Time.HasValue)
            .Select(x => (x.Ref, x.Time!.Value))
            .ToList();

    private static List<string> ReapplyAnchors(
        List<(AnchorRef Ref, double Time)> captured,
        TimelineMap map,
        Func<double, double> remap)
    {
        var warnings = new List<string>();

        foreach (var (anchorRef, before) in captured)
        {
            var after = remap(before);
            if (map.ToAnchor(after) is { } anchor)
            {
                anchorRef.Set(anchor);
            }
            else
            {
                warnings.Add($"{anchorRef.Owner} lost its anchor");
            }
        }

        return warnings;
    }

    private static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
