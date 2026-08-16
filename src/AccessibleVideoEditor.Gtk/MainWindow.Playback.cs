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
/// Playing, shuttling, auditioning and scrubbing.
///
/// Playback is of the decision list rather than of a render, so an edit is
/// audible the moment it is made. Segments announce themselves on boundary
/// crossings only - never timecodes - because that is how you confirm a cut
/// worked without stopping to inspect it.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// While playing, the cursor follows the player and segments announce
    /// themselves as they pass. Stopped, this does nothing at all.
    /// </summary>
    private bool OnPlaybackTick()
    {
        if (!_player.IsPlaying) return true;

        var position = _player.Position;

        if (position > 0)
        {
            _followingPlayback = true;
            _cursor.MoveTo(position, CursorMoveCause.Playback);
            _followingPlayback = false;

            UpdateStatusLine();
            RefreshLanes();

            if (_playbackAnnouncer.Tick(Project, _session.Map, position) is { } said)
            {
                Announce(said, urgent: false);
            }
        }

        // Stop at the end rather than sitting paused past it.
        if (_player.ReachedEnd || position >= _session.Map.Duration - 0.05)
        {
            _player.Pause();
            Announce("end of programme", urgent: true);
        }

        return true;
    }

    private void TogglePlay()
    {
        if (!_player.IsAvailable)
        {
            Announce("playback is unavailable: libmpv could not be loaded", urgent: true);
            return;
        }

        if (_player.IsPlaying)
        {
            _player.Pause();
            Announce($"paused at {Timecode.FormatShort(_cursor.ProgrammeTime)}", urgent: true);
            return;
        }

        if (!EnsureLoaded()) return;

        _playbackAnnouncer.Reset();

        var started = _player.Play(_cursor.ProgrammeTime);

        if (started is null)
        {
            Announce("nothing to play from here", urgent: true);
            return;
        }

        // Cards, holes and pauses have nothing to play, so preview skips them.
        // Say so rather than appearing to start somewhere else for no reason.
        Announce(started.Value > _cursor.ProgrammeTime + 0.05
            ? $"skipping to {Timecode.FormatShort(started.Value)}, nothing to preview before it"
            : "playing", urgent: true);
    }

    /// <summary>
    /// Points the player at the current cut. Reports missing media rather than
    /// producing silence, which is indistinguishable from a broken player.
    /// </summary>
    private bool EnsureLoaded()
    {
        var missing = Project.Sources
            .Where(source => !System.IO.File.Exists(ResolvePath(source.Path)))
            .Select(source => System.IO.Path.GetFileName(source.Path))
            .ToList();

        if (missing.Count > 0)
        {
            Announce($"cannot play: {string.Join(", ", missing)} not found on disk", urgent: true);
            return false;
        }

        _player.SetOutput(Project.Settings.MonitorOutputId);
        _player.Load(MpvEdl.Build(Project, _session.Map));
        return true;
    }

    private string ResolvePath(string path) =>
        System.IO.Path.IsPathRooted(path) || Project.RootPath is null
            ? path
            : System.IO.Path.Combine(Project.RootPath, path);

    private void Shuttle(double rate)
    {
        if (!_player.IsAvailable || !EnsureLoaded()) return;

        if (rate == 0)
        {
            _player.Pause();
            Announce("stopped", urgent: true);
            return;
        }

        _playbackAnnouncer.Reset();
        _player.SetRate(rate);
        Announce(rate == 1 ? "playing" : $"{rate:0.##} times speed", urgent: true);
    }

    private void Audition()
    {
        if (!_player.IsAvailable || !EnsureLoaded()) return;

        var from = Math.Max(0, _cursor.ProgrammeTime - 1.5);
        var to = Math.Min(_session.Map.Duration, _cursor.ProgrammeTime + 1.5);

        Announce("auditioning", urgent: true);
        _ = _player.PlayRangeAsync(from, to);
    }

    /// <summary>
    /// A blip of the real audio wherever the cursor lands. This is what makes
    /// the timeline navigable by ear - a timestamp tells you where you are, the
    /// audio tells you what is there.
    /// </summary>
    private void ScrubHere()
    {
        // A click at every segment boundary, so holding an arrow key down lets
        // you hear the shape of the edit going past.
        if (Project.Settings.Earcons
            && _cursor.FocusedTrack is { } focused
            && TrackProbe.Segments(Project, _session.Map, focused)
                .Any(seg => Math.Abs(seg.Start - _cursor.ProgrammeTime) < 0.02))
        {
            _announcer.Earcon(Earcon.Boundary);
        }

        if (!Project.Settings.AudioScrub || _player.IsPlaying || _followingPlayback) return;
        if (!_player.IsAvailable) return;

        if (Project.Sources.Any(source => !System.IO.File.Exists(ResolvePath(source.Path)))) return;

        _player.Load(MpvEdl.Build(Project, _session.Map));
        _player.Scrub(_cursor.ProgrammeTime, Project.Settings.AudioScrubLength);
    }
}
