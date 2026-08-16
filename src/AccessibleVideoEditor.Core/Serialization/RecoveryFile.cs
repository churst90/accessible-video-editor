namespace AccessibleVideoEditor.Core.Serialization;

/// <summary>
/// Where a quiet save goes, which is deliberately <b>not</b> over the top of
/// the project.
///
/// Autosave used to write straight to <c>project.json</c>, and that has two
/// consequences that only show up on the day they matter:
///
/// <list type="number">
/// <item><b>There was no way back to the last deliberate save.</b> Try an idea
/// for ten minutes, decide against it, close without saving - and it had
/// already been written three minutes ago. "Discard changes?" could only ever
/// offer to discard the last few.</item>
/// <item><b>There was nothing to recover.</b> The roadmap listed
/// "crash-to-recovery" as untested, but there was no recovered file to open:
/// the autosave <i>was</i> the project. The item could not have been exercised
/// even in principle.</item>
/// </list>
///
/// So the explicit save stays the file of record, and the quiet one goes beside
/// it. Opening a project that has a newer autosave beside it is then a real
/// question with two real answers, which is what recovery is.
/// </summary>
public static class RecoveryFile
{
    public const string FileName = "project.autosave.json";

    public static string PathFor(string projectDirectory) =>
        Path.Combine(projectDirectory, FileName);

    public static string ProjectPathFor(string projectDirectory) =>
        Path.Combine(projectDirectory, ProjectJson.FileName);

    /// <summary>
    /// Whether there is work in the autosave that is not in the project, and
    /// how much newer it is.
    /// </summary>
    public static RecoveryStatus Check(string projectDirectory)
    {
        var recovery = PathFor(projectDirectory);
        var project = ProjectPathFor(projectDirectory);

        if (!File.Exists(recovery)) return RecoveryStatus.None;

        var recoveryTime = File.GetLastWriteTimeUtc(recovery);

        // No project file at all but an autosave beside it: the crash happened
        // before the first explicit save. The work is still worth offering.
        if (!File.Exists(project)) return new RecoveryStatus(true, recoveryTime, null);

        var projectTime = File.GetLastWriteTimeUtc(project);

        // A second of slack. Two files written moments apart during one
        // ordinary save are not a recovery, and offering one would teach you to
        // dismiss the question without listening to it.
        return recoveryTime > projectTime.AddSeconds(1)
            ? new RecoveryStatus(true, recoveryTime, projectTime)
            : RecoveryStatus.None;
    }

    /// <summary>
    /// Removes the autosave, because the work is now in the file of record.
    ///
    /// Called on every explicit save. A recovery offer that survives the save
    /// that made it redundant is worse than none: it would offer to replace
    /// good work with older work, in a dialog that sounds helpful.
    /// </summary>
    public static void Clear(string projectDirectory)
    {
        try
        {
            var path = PathFor(projectDirectory);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // A recovery file that will not delete is stale rather than
            // dangerous - the check compares timestamps, and the explicit save
            // has just made the project newer.
        }
    }
}

/// <summary>
/// Whether there is something to recover, said as a sentence rather than as
/// two timestamps to compare in your head.
/// </summary>
public sealed record RecoveryStatus(bool Available, DateTime RecoveredAtUtc, DateTime? SavedAtUtc)
{
    public static RecoveryStatus None { get; } = new(false, default, null);

    /// <summary>
    /// How far ahead the autosave is. This is the number that decides whether
    /// you want it, so it is the number that gets said.
    /// </summary>
    public TimeSpan Ahead =>
        SavedAtUtc is { } saved ? RecoveredAtUtc - saved : TimeSpan.Zero;

    public string Describe()
    {
        if (!Available) return "no recovered work";

        if (SavedAtUtc is null)
        {
            return "there is recovered work here from a session that was never saved";
        }

        return $"there is recovered work here, {Spoken(Ahead)} newer than the saved project";
    }

    /// <summary>The question, with the consequence of each answer stated.</summary>
    public string Question() =>
        SavedAtUtc is null
            ? "Open the recovered work? It was never saved, so the alternative is nothing at all."
            : $"Open the recovered work, {Spoken(Ahead)} newer than the last save? "
              + "Choosing no opens the saved project and leaves the recovery alone.";

    private static string Spoken(TimeSpan span)
    {
        if (span.TotalSeconds < 90) return $"{Math.Max(1, (int)span.TotalSeconds)} seconds";
        if (span.TotalMinutes < 90) return $"{(int)span.TotalMinutes} minutes";
        if (span.TotalHours < 48) return $"{(int)span.TotalHours} hours";

        return $"{(int)span.TotalDays} days";
    }
}
