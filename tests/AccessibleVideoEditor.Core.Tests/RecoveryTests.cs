using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Serialization;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Crash to recovery, which the roadmap listed as untested and which could not
/// have been tested: autosave wrote over <c>project.json</c>, so there was no
/// recovered file to open. These exercise the thing that now exists.
/// </summary>
public class RecoveryTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"ave-recovery-{Guid.NewGuid():N}");

    public RecoveryTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void WriteProject(string name, DateTime writtenUtc)
    {
        var path = RecoveryFile.ProjectPathFor(_folder);
        File.WriteAllText(path, ProjectJson.Serialise(Project.CreateDefault(name)));
        File.SetLastWriteTimeUtc(path, writtenUtc);
    }

    private void WriteAutosave(string name, DateTime writtenUtc)
    {
        var path = RecoveryFile.PathFor(_folder);
        File.WriteAllText(path, ProjectJson.Serialise(Project.CreateDefault(name)));
        File.SetLastWriteTimeUtc(path, writtenUtc);
    }

    [Fact]
    public void A_project_with_no_autosave_beside_it_has_nothing_to_recover()
    {
        WriteProject("Saved", DateTime.UtcNow);

        Assert.False(RecoveryFile.Check(_folder).Available);
    }

    [Fact]
    public void An_autosave_newer_than_the_project_is_offered()
    {
        var now = DateTime.UtcNow;

        WriteProject("Saved", now.AddMinutes(-20));
        WriteAutosave("Crashed", now);

        var status = RecoveryFile.Check(_folder);

        Assert.True(status.Available);
        Assert.Equal(20, (int)status.Ahead.TotalMinutes);
        Assert.Contains("20 minutes newer", status.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_autosave_older_than_the_project_is_not_offered()
    {
        // The ordinary case after a save: the autosave is stale, and offering
        // it would be offering to replace good work with older work.
        var now = DateTime.UtcNow;

        WriteAutosave("Older", now.AddMinutes(-5));
        WriteProject("Saved", now);

        Assert.False(RecoveryFile.Check(_folder).Available);
    }

    [Fact]
    public void Two_files_written_moments_apart_are_not_a_recovery()
    {
        // One ordinary save touches both. A second of slack, or the question
        // gets asked every single time and stops being listened to.
        var now = DateTime.UtcNow;

        WriteProject("Saved", now);
        WriteAutosave("Saved", now.AddMilliseconds(300));

        Assert.False(RecoveryFile.Check(_folder).Available);
    }

    [Fact]
    public void An_autosave_with_no_project_beside_it_is_still_offered()
    {
        // Crashed before the first explicit save. The work is all there is.
        WriteAutosave("Never saved", DateTime.UtcNow);

        var status = RecoveryFile.Check(_folder);

        Assert.True(status.Available);
        Assert.Null(status.SavedAtUtc);
        Assert.Contains("never saved", status.Describe(), StringComparison.Ordinal);
        Assert.Contains("nothing at all", status.Question(), StringComparison.Ordinal);
    }

    [Fact]
    public void Saving_clears_the_recovery_so_it_cannot_be_offered_again()
    {
        WriteProject("Saved", DateTime.UtcNow.AddMinutes(-20));
        WriteAutosave("Crashed", DateTime.UtcNow);

        Assert.True(RecoveryFile.Check(_folder).Available);

        RecoveryFile.Clear(_folder);

        Assert.False(File.Exists(RecoveryFile.PathFor(_folder)));
        Assert.False(RecoveryFile.Check(_folder).Available);
    }

    [Fact]
    public void Clearing_a_recovery_that_is_not_there_is_not_an_error()
    {
        RecoveryFile.Clear(_folder);
        RecoveryFile.Clear(_folder);
    }

    // ---- the whole round trip ----------------------------------------------

    [Fact]
    public async Task An_autosave_does_not_touch_the_saved_project()
    {
        // The bug this whole change exists to fix: an abandoned experiment used
        // to overwrite the last deliberate save three minutes later.
        var saved = Project.CreateDefault("Deliberate");
        await ProjectJson.SaveAsync(saved, _folder);

        var experiment = Project.CreateDefault("Experiment");
        await ProjectJson.SaveToAsync(experiment, RecoveryFile.PathFor(_folder));

        var onDisk = await ProjectJson.LoadAsync(_folder);

        Assert.Equal("Deliberate", onDisk.Name);
    }

    [Fact]
    public async Task A_recovered_project_reads_back_with_the_folder_as_its_home()
    {
        // RootPath has to be the project's folder, not the file the work
        // happened to be read out of, or the next save writes the autosave.
        var work = Project.CreateDefault("Crashed");
        await ProjectJson.SaveToAsync(work, RecoveryFile.PathFor(_folder));

        var recovered = await ProjectJson.LoadFromAsync(RecoveryFile.PathFor(_folder), _folder);

        Assert.Equal("Crashed", recovered.Name);
        Assert.Equal(_folder, recovered.RootPath);
    }

    [Fact]
    public async Task Saving_to_a_named_file_does_not_move_the_project()
    {
        var project = Project.CreateDefault("Anywhere");
        await ProjectJson.SaveAsync(project, _folder);

        var elsewhere = Path.Combine(_folder, "somewhere-else.json");
        await ProjectJson.SaveToAsync(project, elsewhere);

        Assert.Equal(_folder, project.RootPath);
    }

    [Fact]
    public async Task A_half_written_autosave_cannot_be_left_behind()
    {
        // Written to a temp file and moved, so a crash mid-write leaves the
        // previous autosave rather than a truncated one.
        var project = Project.CreateDefault("Atomic");
        await ProjectJson.SaveToAsync(project, RecoveryFile.PathFor(_folder));

        Assert.False(File.Exists(RecoveryFile.PathFor(_folder) + ".tmp"));

        var reread = await ProjectJson.LoadFromAsync(RecoveryFile.PathFor(_folder), _folder);
        Assert.Equal("Atomic", reread.Name);
    }
}
