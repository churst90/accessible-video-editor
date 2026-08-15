using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// Making, keeping and using named ranges of a source.
///
/// The operations are deliberately thin - a subclip is a reference, so nothing
/// here touches media. What the code is actually for is the <b>refusals</b>:
/// every way of ending up with a subclip you cannot use is caught and named,
/// because a bad one is invisible until you insert it and hear the wrong thing.
/// </summary>
public static class SubclipOperations
{
    /// <summary>
    /// The shortest range worth naming. Below this you have almost certainly
    /// marked one point twice rather than a range, and a subclip you cannot hear
    /// is worse than no subclip.
    /// </summary>
    public const double MinimumDuration = 0.1;

    public static EditResult Create(
        Project project,
        SourceId sourceId,
        double sourceIn,
        double sourceOut,
        string name,
        int audioTrack = 0)
    {
        var source = project.SourceOf(sourceId);
        if (source is null) return EditResult.NoChange("no such source");

        name = name.Trim();
        if (name.Length == 0) return EditResult.NoChange("a subclip needs a name");

        var from = Math.Max(0, Math.Min(sourceIn, sourceOut));
        var to = Math.Max(sourceIn, sourceOut);

        // Clamped rather than refused: marking past the end of a take is normal
        // when you are marking by ear and the take runs out.
        if (source.Duration > 0) to = Math.Min(to, source.Duration);

        if (to - from < MinimumDuration)
        {
            return EditResult.NoChange(
                $"that range is {Timecode.Speak(Math.Max(0, to - from))}, too short to name");
        }

        if (project.Subclips.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            // Refused rather than made unique with a number. Two subclips called
            // "good intro" and "good intro 2" are indistinguishable in a list
            // read aloud, which defeats the point of naming them.
            return EditResult.NoChange($"there is already a subclip called {name}");
        }

        project.Subclips.Add(new Subclip
        {
            Id = Ids.NewSubclip(),
            Source = sourceId,
            Name = name,
            In = from,
            Out = to,
            AudioTrack = audioTrack,
        });

        return EditResult.Ok($"subclip {name}, {Timecode.Speak(to - from)}");
    }

    public static EditResult Rename(Project project, SubclipId id, string name)
    {
        var subclip = project.SubclipOf(id);
        if (subclip is null) return EditResult.NoChange("no such subclip");

        name = name.Trim();
        if (name.Length == 0) return EditResult.NoChange("a subclip needs a name");

        if (project.Subclips.Any(s => s.Id != id
                                      && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return EditResult.NoChange($"there is already a subclip called {name}");
        }

        var was = subclip.Name;
        subclip.Name = name;

        return EditResult.Ok($"renamed {was} to {name}");
    }

    public static EditResult Remove(Project project, SubclipId id)
    {
        var subclip = project.SubclipOf(id);
        if (subclip is null) return EditResult.NoChange("no such subclip");

        project.Subclips.Remove(subclip);

        // Says what removing it did *not* do. A subclip is a reference, so
        // deleting one after using it looks destructive and is not - and being
        // told so is the difference between trusting the command and avoiding it.
        return EditResult.Ok(
            $"removed the subclip {subclip.Name}. What you already put on the timeline is still there");
    }

    /// <summary>Adjusts an existing subclip's range without renaming it.</summary>
    public static EditResult Retrim(Project project, SubclipId id, double sourceIn, double sourceOut)
    {
        var subclip = project.SubclipOf(id);
        if (subclip is null) return EditResult.NoChange("no such subclip");

        var from = Math.Max(0, Math.Min(sourceIn, sourceOut));
        var to = Math.Max(sourceIn, sourceOut);

        if (project.SourceOf(subclip.Source) is { Duration: > 0 } source)
        {
            to = Math.Min(to, source.Duration);
        }

        if (to - from < MinimumDuration)
        {
            return EditResult.NoChange($"that range is too short to keep {subclip.Name}");
        }

        var was = subclip.Duration;
        subclip.In = from;
        subclip.Out = to;

        return EditResult.Ok(
            $"{subclip.Name} is now {Timecode.Speak(subclip.Duration)}, was {Timecode.Speak(was)}");
    }

    public static EditResult Insert(Project project, SubclipId id, double programmeTime)
    {
        var subclip = project.SubclipOf(id);
        if (subclip is null) return EditResult.NoChange("no such subclip");
        if (project.SourceOf(subclip.Source) is null) return EditResult.NoChange($"{subclip.Name} has no source");

        EditOperations.InsertRange(
            project, subclip.Source, subclip.In, subclip.Out, programmeTime, subclip.AudioTrack);

        return EditResult.Ok($"inserted {subclip.Name}, {Timecode.Speak(subclip.Duration)}");
    }

    public static EditResult Overwrite(Project project, SubclipId id, double programmeTime)
    {
        var subclip = project.SubclipOf(id);
        if (subclip is null) return EditResult.NoChange("no such subclip");
        if (project.SourceOf(subclip.Source) is null) return EditResult.NoChange($"{subclip.Name} has no source");

        var map = TimelineMap.Build(project);
        var end = Math.Min(programmeTime + subclip.Duration, map.Duration);

        if (end > programmeTime)
        {
            EditOperations.RemoveRange(project, programmeTime, end, out _);
        }

        EditOperations.InsertRange(
            project, subclip.Source, subclip.In, subclip.Out, programmeTime, subclip.AudioTrack);

        return EditResult.Ok($"overwrote {Timecode.Speak(subclip.Duration)} with {subclip.Name}");
    }

    /// <summary>
    /// The whole list, read out. Grouped by source, because "which take was that
    /// from" is the question that follows a name you half remember.
    /// </summary>
    public static string Describe(Project project)
    {
        if (project.Subclips.Count == 0) return "no subclips yet";

        var lines = project.Subclips
            .GroupBy(s => s.Source)
            .Select(group =>
            {
                var source = project.SourceOf(group.Key);
                var where = source is null ? "a missing source" : Path.GetFileName(source.Path);

                return $"from {where}: " + string.Join("; ", group.Select(s => s.Describe()));
            });

        return $"{project.Subclips.Count} subclip{(project.Subclips.Count == 1 ? string.Empty : "s")}. "
               + string.Join(". ", lines);
    }
}
