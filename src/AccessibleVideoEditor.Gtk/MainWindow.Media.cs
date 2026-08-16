using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Commands;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Images;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Serialization;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Streaming;
using AccessibleVideoEditor.Core.Timeline;
using AccessibleVideoEditor.Audio;
using AccessibleVideoEditor.Engine;
using AccessibleVideoEditor.Playback;
using AccessibleVideoEditor.Vision;
using AccessibleVideoEditor.Speech;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// Getting material in, and getting it out again: import, assembly from
/// the bin, the quality report, rendering and describing a frame.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// The audible VU meter. A visual meter is glanceable; the equivalent by ear
    /// is a tick whose pitch rises with the level, with the zone name spoken
    /// only as it changes - a meter that talks constantly is one you turn off.
    /// </summary>
    private void AssembleFromBin(bool overwrite)
    {
        var index = _mediaList.GetSelectedRow()?.GetIndex() ?? -1;

        if (index < 0 || index >= Project.Sources.Count)
        {
            Announce("select a source in the media bin first, Control 4", urgent: true);
            return;
        }

        var source = Project.Sources[index];

        var result = _session.Apply(overwrite ? "overwrite" : "insert", (project, _) => overwrite
            ? EditOperations.OverwriteSource(project, source.Id, _cursor.ProgrammeTime)
            : EditOperations.InsertSource(project, source.Id, _cursor.ProgrammeTime));

        Refresh();
        Announce(result.Announce(), urgent: true);
    }

    /// <summary>
    /// Moves a segment's sound onto an audio track, or puts it back. Needed
    /// whenever you want to keep someone's voice while cutting away from their
    /// picture.
    /// </summary>
    private void AnalyseQuality(bool wholeProject)
    {
        var sources = new List<(Source Source, double At)>();

        if (wholeProject)
        {
            sources.AddRange(Project.Sources
                .Where(s => s.Kind != SourceKind.Image)
                .Select(s => (s, Math.Min(1, s.Duration / 2))));
        }
        else if (_session.Map.ToSource(_cursor.ProgrammeTime) is { } point
                 && Project.SourceOf(point.Source) is { } source)
        {
            sources.Add((source, point.Time));
        }

        if (sources.Count == 0)
        {
            Announce(wholeProject ? "no media to measure" : "nothing measurable under the cursor",
                urgent: true);
            return;
        }

        Announce($"measuring {sources.Count} source{(sources.Count == 1 ? "" : "s")}", urgent: true);

        _ = Task.Run(async () =>
        {
            var analyser = new QualityAnalyser();
            var reports = new List<QualityReport>();

            foreach (var (source, at) in sources)
            {
                var path = System.IO.Path.IsPathRooted(source.Path) || Project.RootPath is null
                    ? source.Path
                    : System.IO.Path.Combine(Project.RootPath, source.Path);

                if (!System.IO.File.Exists(path)) continue;

                try
                {
                    reports.Add(await analyser.AnalyseAsync(path, at).ConfigureAwait(false));
                }
                catch (Exception)
                {
                    // A source that cannot be measured is reported as missing
                    // rather than taking the whole pass down.
                }
            }

            var message = reports.Count == 0
                ? "nothing could be measured; the files may be missing"
                : wholeProject && reports.Count > 1
                    ? QualityAnalyser.CompareShots(reports)
                    : reports[0].Announce();

            GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
            {
                Announce(message, urgent: true);
                return false;
            });
        });
    }

    private void Render(RenderQuality quality)
    {
        if (_rendering)
        {
            Announce("a render is already running", urgent: true);
            return;
        }

        if (Project.RootPath is null)
        {
            Announce("save the project first; a render needs somewhere to put its files", urgent: true);
            return;
        }

        _rendering = true;
        Announce(quality == RenderQuality.Draft ? "rendering draft" : "rendering master", urgent: true);

        var lastSpoken = -1;

        var progress = new Progress<RenderProgress>(report =>
        {
            // Every ten percent, not every tick: a render that talks constantly
            // is one you cannot work through.
            var decile = (int)(report.Fraction * 10);
            if (decile == lastSpoken || decile == 0) return;

            lastSpoken = decile;
            Announce($"{decile * 10} percent", urgent: false);
        });

        _ = Task.Run(async () =>
        {
            string message;

            try
            {
                var output = await new FfmpegRenderEngine()
                    .RenderAsync(Project, quality, progress).ConfigureAwait(false);

                message = $"rendered {Timecode.Speak(output.Duration)} to "
                          + $"{System.IO.Path.GetFileName(output.Path)}"
                          + (quality == RenderQuality.Master ? ", with captions" : string.Empty);
            }
            catch (Exception exception)
            {
                message = $"render failed. {exception.Message}";
            }

            GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
            {
                _rendering = false;
                Announce(message, urgent: true);
                return false;
            });
        });
    }

    private void DetachAudio()
    {
        if (_session.Map.Locate(_cursor.ProgrammeTime)?.Element.Id is not { } id)
        {
            Announce("nothing under the cursor", urgent: true);
            return;
        }

        if (Project.Element(id) is { Muted: true })
        {
            var reattached = _session.Apply("reattach audio", (project, _) =>
                EditOperations.ReattachAudio(project, id));

            Refresh();
            Announce(reattached.Announce(), urgent: true);
            return;
        }

        var audioTracks = Project.InOrder.Where(t => t.Media == TrackMedia.Audio).ToList();

        if (audioTracks.Count == 0)
        {
            Announce("no audio track to detach onto. Control T makes one", urgent: true);
            return;
        }

        if (audioTracks.Count == 1)
        {
            Apply("detach audio", p => EditOperations.DetachAudio(p, id, audioTracks[0].Id));
            return;
        }

        ChooseFromList(
            "Detach onto which track",
            audioTracks.Select(t => t.Name).ToList(),
            choice => Apply("detach audio", p => EditOperations.DetachAudio(p, id, audioTracks[choice].Id)));
    }

    private void ImportMedia()
    {
        var dialog = Gtk_.FileChooserNative.New(
            "Import media", _window, Gtk_.FileChooserAction.Open, "Import", "Cancel");

        var filter = Gtk_.FileFilter.New();
        filter.Name = "Video, audio and images";

        foreach (var extension in MediaImporter.SupportedExtensions)
        {
            filter.AddPattern($"*{extension}");
            filter.AddPattern($"*{extension.ToUpperInvariant()}");
        }

        dialog.AddFilter(filter);

        dialog.OnResponse += (chooser, args) =>
        {
            if (args.ResponseId != (int)Gtk_.ResponseType.Accept) return;

            var path = dialog.GetFile()?.GetPath();
            if (path is null) return;

            ImportAsync(path).ConfigureAwait(true);
        };

        // Keep it alive until the response arrives; a native dialog collected
        // early simply never answers.
        _openDialog = dialog;
        dialog.Show();
    }

    private async Task ImportAsync(string path)
    {
        Announce($"importing {System.IO.Path.GetFileName(path)}", urgent: true);

        var result = await new MediaImporter().ImportAsync(Project, path).ConfigureAwait(true);

        RebuildMediaRows();
        Refresh();

        Announce(result.Succeeded
            ? $"imported. {result.Summary}"
            : $"could not import. {result.Summary}", urgent: true);
    }

    private void RebuildMediaRows()
    {
        while (_mediaList.GetRowAtIndex(0) is { } row) _mediaList.Remove(row);

        if (Project.Sources.Count == 0)
        {
            _mediaList.Append(Row("Media bin empty. Control I to import."));
            return;
        }

        foreach (var source in Project.Sources)
        {
            _mediaList.Append(Row(MediaImporter.Describe(source)));
        }
    }

    /// <summary>
    /// How long a still, card, hole or pause is held. A photograph has no
    /// duration of its own, so it can stay up for as long as you like.
    /// </summary>
    private void SetDuration()
    {
        ChooseFromList(
            "How long on screen",
            ["2 seconds", "3 seconds", "4 seconds", "6 seconds", "10 seconds",
             "One second longer", "One second shorter"],
            index =>
            {
                var result = _session.Apply("duration", (project, _) => index switch
                {
                    0 => EditOperations.SetDuration(project, _cursor.ProgrammeTime, 2),
                    1 => EditOperations.SetDuration(project, _cursor.ProgrammeTime, 3),
                    2 => EditOperations.SetDuration(project, _cursor.ProgrammeTime, 4),
                    3 => EditOperations.SetDuration(project, _cursor.ProgrammeTime, 6),
                    4 => EditOperations.SetDuration(project, _cursor.ProgrammeTime, 10),
                    5 => EditOperations.AdjustDuration(project, _cursor.ProgrammeTime, 1),
                    _ => EditOperations.AdjustDuration(project, _cursor.ProgrammeTime, -1),
                });

                Refresh();
                Announce(result.Announce(), urgent: true);
            });
    }

    /// <summary>
    /// Reads back what is actually in the frame under the cursor - the part of
    /// editing that genuinely needs eyes, done by something that has them.
    /// </summary>
    private void DescribeFrame()
    {
        if (_session.Map.ToSource(_cursor.ProgrammeTime) is not { } point
            || Project.SourceOf(point.Source) is not { } source)
        {
            Announce("nothing to describe under the cursor", urgent: true);
            return;
        }

        var describer = new FrameDescriber();

        if (!describer.IsAvailable)
        {
            Announce("the claude command is not installed, so frames cannot be described", urgent: true);
            return;
        }

        var path = System.IO.Path.IsPathRooted(source.Path) || Project.RootPath is null
            ? source.Path
            : System.IO.Path.Combine(Project.RootPath, source.Path);

        if (!System.IO.File.Exists(path))
        {
            Announce($"{System.IO.Path.GetFileName(source.Path)} is not on disk", urgent: true);
            return;
        }

        Announce("looking at the frame", urgent: true);

        var at = point.Time;

        _ = Task.Run(async () =>
        {
            var frame = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"videoedit-frame-{Guid.NewGuid():N}.jpg");

            string message;

            try
            {
                message = await describer.ExtractFrameAsync(path, at, frame).ConfigureAwait(false) is null
                    ? "could not take a frame from that point"
                    : await describer.DescribeAsync(frame).ConfigureAwait(false);
            }
            finally
            {
                if (System.IO.File.Exists(frame)) System.IO.File.Delete(frame);
            }

            GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
            {
                Announce(message, urgent: true);
                return false;
            });
        });
    }
}
