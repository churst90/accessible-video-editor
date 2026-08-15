using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// Cutting between camera angles.
///
/// The whole interaction is one key per angle, at the cursor, announced. That is
/// not a simplification of what sighted editors do - it is what they do, with
/// the multicam viewer removed. The viewer answers "what does angle 2 look like
/// right now", which is a question <c>F8</c> already answers better here by
/// describing the frame.
///
/// Switching <b>splits and re-points</b>: the segment under the cursor is cut at
/// that moment and the second half plays a different angle. Because every angle
/// is the same moment in time, the cut is frame-accurate by construction rather
/// than by you finding it twice.
/// </summary>
public static class MulticamOperations
{
    public static EditResult Create(Project project, string name, IReadOnlyList<SourceId> sources)
    {
        name = name.Trim();
        if (name.Length == 0) return EditResult.NoChange("a multicam group needs a name");
        if (sources.Count < 2) return EditResult.NoChange("a multicam group needs at least two angles");

        if (project.Multicams.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return EditResult.NoChange($"there is already a multicam group called {name}");
        }

        var angles = new List<CameraAngle>();

        foreach (var sourceId in sources)
        {
            var source = project.SourceOf(sourceId);
            if (source is null) return EditResult.NoChange("one of those sources is not in the project");

            angles.Add(new CameraAngle
            {
                Source = sourceId,
                Name = Path.GetFileNameWithoutExtension(source.Path),
            });
        }

        project.Multicams.Add(new MulticamGroup
        {
            Id = Ids.NewGroup(),
            Name = name,
            Angles = angles,
        });

        // Says the next step out loud. A multicam group that has not been synced
        // will cut to the right camera at the wrong moment, and nothing about
        // that is audible.
        return EditResult.Ok(
            $"multicam {name}, {angles.Count} angles. Sync them by sound before cutting");
    }

    public static EditResult RenameAngle(Project project, GroupId id, int index, string name)
    {
        var group = project.MulticamOf(id);
        if (group is null) return EditResult.NoChange("no such multicam group");
        if (index < 0 || index >= group.Angles.Count) return EditResult.NoChange("no such angle");

        name = name.Trim();
        if (name.Length == 0) return EditResult.NoChange("an angle needs a name");

        var was = group.Angles[index].Name;
        group.Angles[index].Name = name;

        return EditResult.Ok($"renamed {was} to {name}");
    }

    /// <summary>
    /// Records the offsets a sync produced. The measuring happens in the engine,
    /// which has the waveforms; the decision about what counts as trustworthy is
    /// here, where it can be tested.
    /// </summary>
    public static EditResult ApplySync(
        Project project,
        GroupId id,
        IReadOnlyDictionary<SourceId, SyncResult> results)
    {
        var group = project.MulticamOf(id);
        if (group is null) return EditResult.NoChange("no such multicam group");

        var synced = 0;
        var doubtful = new List<string>();

        foreach (var angle in group.Angles)
        {
            if (!results.TryGetValue(angle.Source, out var result)) continue;

            angle.Offset = result.Offset;
            angle.SyncConfidence = result.Confidence;

            if (result.Trustworthy) synced++;
            else doubtful.Add(angle.Name);
        }

        if (synced == 0)
        {
            return EditResult.NoChange(
                "none of the angles matched by sound. Check they are the same take, "
                + "or line them up by hand");
        }

        var message = $"synced {synced} of {group.Angles.Count} angles";

        // Named individually rather than counted. Which camera is unreliable
        // decides whether you can use it, and a count does not tell you that.
        if (doubtful.Count > 0)
        {
            message += $". {string.Join(" and ", doubtful)} did not match well - check by ear";
        }

        return EditResult.Ok(message);
    }

    /// <summary>
    /// Cuts to an angle at the cursor. The heart of the feature, and one key.
    /// </summary>
    public static EditResult SwitchTo(Project project, GroupId id, int angleIndex, double programmeTime)
    {
        var group = project.MulticamOf(id);
        if (group is null) return EditResult.NoChange("no such multicam group");

        if (angleIndex < 0 || angleIndex >= group.Angles.Count)
        {
            return EditResult.NoChange($"there is no angle {angleIndex + 1} in {group.Name}");
        }

        var angle = group.Angles[angleIndex];

        if (!angle.Synced)
        {
            // Refused rather than cut at the wrong moment. An unsynced cut looks
            // like a working edit and is off by however far the files differ.
            return EditResult.NoChange($"{angle.Name} is not synced yet, so a cut would land in the wrong place");
        }

        var map = TimelineMap.Build(project);

        if (map.Locate(programmeTime) is not { Media: { } media } placed)
        {
            return EditResult.NoChange("nothing under the cursor to switch");
        }

        if (placed.Element is not (SpanElement or ClipElement))
        {
            return EditResult.NoChange("only a recorded segment has angles");
        }

        // Where this moment sits in the reference recording, then in the angle
        // being cut to. The offset is the whole reason this is frame-accurate.
        var atSource = placed.SourceTimeAt(programmeTime - placed.ProgrammeStart);
        var inAngle = atSource - angle.Offset;

        if (inAngle < 0)
        {
            return EditResult.NoChange($"{angle.Name} had not started recording at that moment");
        }

        if (project.SourceOf(angle.Source) is { Duration: > 0 } source && inAngle >= source.Duration)
        {
            return EditResult.NoChange($"{angle.Name} had stopped recording by that moment");
        }

        var remaining = placed.ProgrammeEnd - programmeTime;
        if (remaining <= 0.001) return EditResult.NoChange("no room to cut here");

        // Split, then re-point the second half. Splitting first means the cut is
        // a real, navigable boundary rather than invisible state - the same rule
        // that makes split the primary edit idiom everywhere else.
        EditOperations.SplitAt(project, programmeTime);

        var after = TimelineMap.Build(project).Locate(programmeTime + 0.001)?.Element;
        if (after is null) return EditResult.NoChange("the split did not take");

        switch (after)
        {
            // The words and their timings belong to the take, not to the
            // camera, so re-pointing changes the picture and leaves the
            // transcript exactly as it was.
            case SpanElement span:
            {
                var length = span.SourceOut - span.SourceIn;
                span.Source = angle.Source;
                span.SourceIn = inAngle;
                span.SourceOut = inAngle + length;
                break;
            }

            case ClipElement clip:
            {
                var length = clip.SourceOut - clip.SourceIn;
                clip.Source = angle.Source;
                clip.SourceIn = inAngle;
                clip.SourceOut = inAngle + length;
                break;
            }

            default:
                return EditResult.NoChange("that segment cannot take an angle");
        }

        _ = media;

        group.ActiveAngle = angleIndex;

        return EditResult.Ok($"cut to {angle.Name}, {Timecode.Speak(remaining)} remaining");
    }

    public static string Describe(Project project)
    {
        if (project.Multicams.Count == 0) return "no multicam groups";

        return string.Join(". ", project.Multicams.Select(m =>
            $"{m.Describe()}: {string.Join(", ", m.Angles.Select(a => a.Describe()))}"));
    }
}
