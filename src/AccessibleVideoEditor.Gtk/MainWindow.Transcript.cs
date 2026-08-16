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
/// The transcript as a text editor.
///
/// Typing edits caption text only; the structure changes through commands,
/// which is why there is no syntax here to get wrong and no syntax checker.
/// Timecodes are spoken on caret movement rather than written into the
/// buffer, so the transcript still reads as prose.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Transcript verbs. Every one takes a modifier: unmodified keys are
    /// typing, and plain Delete has to stay character deletion or the pane
    /// stops being a text editor.
    /// </summary>
    private bool OnTranscriptKey(Gtk_.EventControllerKey.KeyPressedSignalArgs args)
    {
        var control = args.State.HasFlag(Gdk.ModifierType.ControlMask);
        var shift = args.State.HasFlag(Gdk.ModifierType.ShiftMask);
        var alt = args.State.HasFlag(Gdk.ModifierType.AltMask);

        var element = CaretSegment();

        switch (args.Keyval)
        {
            case Gdk.Constants.KEY_K or Gdk.Constants.KEY_k when control && shift:
                if (element is null) return true;
                CommitCaption();
                ApplyToTranscript("delete segment", p => EditOperations.DeleteSegment(p, element.Value));
                return true;

            case Gdk.Constants.KEY_E or Gdk.Constants.KEY_e when control && shift:
                if (element is null) return true;
                CommitCaption();
                ApplyToTranscript("cut", p => EditOperations.ToggleDisableSegment(p, element.Value));
                return true;

            case Gdk.Constants.KEY_Up when alt:
            case Gdk.Constants.KEY_Down when alt:
                if (element is null) return true;
                CommitCaption();
                ApplyToTranscript("move",
                    p => EditOperations.MoveSegment(p, element.Value,
                        args.Keyval == Gdk.Constants.KEY_Up ? -1 : 1));
                return true;

            case Gdk.Constants.KEY_Return or Gdk.Constants.KEY_KP_Enter when control:
            {
                CommitCaption();
                var at = _transcriptDocument.LocationAt(_transcript.GetBuffer().CursorPosition).ProgrammeTime;

                if (at is null)
                {
                    Announce("cannot split here, this line is not in the programme", urgent: true);
                    return true;
                }

                ApplyToTranscript("split", p => EditOperations.SplitAt(p, at.Value));
                return true;
            }

            case Gdk.Constants.KEY_C or Gdk.Constants.KEY_c when control && shift:
                if (element is null) return true;
                ApplyToTranscript("caption", p => EditOperations.ToggleCaption(p, element.Value));
                return true;

            case Gdk.Constants.KEY_semicolon when control:
                Announce(_transcriptDocument.AnnounceLine(_transcript.GetBuffer().CursorPosition), urgent: true);
                return true;

            default:
                // The first ordinary keystroke in a line says what typing does,
                // because it surprises people who expect it to change the cut.
                if (!_captionRuleAnnounced && !control && !alt && args.Keyval > 0x20 && args.Keyval < 0xFF00)
                {
                    _captionRuleAnnounced = true;
                    Announce("editing caption text, not the cut", urgent: true);
                }

                return false;
        }
    }

    private ElementId? CaretSegment()
    {
        var index = _transcriptDocument.LineAt(_transcript.GetBuffer().CursorPosition);
        return index < 0 ? null : _transcriptDocument.Segments[index].Element;
    }

    /// <summary>
    /// Applies a structural edit and rebuilds the buffer, keeping the caret on
    /// a sensible line rather than dumping it at the top.
    /// </summary>
    private void ApplyToTranscript(string label, Func<Project, EditResult> operation)
    {
        var line = _transcriptDocument.LineAt(_transcript.GetBuffer().CursorPosition);

        var result = _session.Apply(label, (project, _) => operation(project));

        _suppressTranscriptCommit = true;
        Refresh();
        _suppressTranscriptCommit = false;

        PlaceCaretOnLine(Math.Min(line, _transcriptDocument.Segments.Count - 1));
        _lastAnnouncedLine = -1;

        Announce(result.Announce(), urgent: true);
    }

    private void PlaceCaretOnLine(int line)
    {
        if (line < 0 || line >= _transcriptDocument.Segments.Count) return;

        var buffer = _transcript.GetBuffer();
        buffer.GetIterAtOffset(out var iter, _transcriptDocument.Segments[line].CharStart);
        buffer.PlaceCursor(iter);
        _editingLine = line;
    }

    /// <summary>
    /// Writes the line the caret is leaving back as that segment's caption.
    /// Deferred rather than per-keystroke, so the buffer is never rebuilt
    /// underneath a half-typed word.
    /// </summary>
    private void CommitCaption()
    {
        if (!_transcriptDirty || _suppressTranscriptCommit || _editingLine < 0) return;

        _transcriptDirty = false;

        if (_editingLine >= _transcriptDocument.Segments.Count) return;

        var segment = _transcriptDocument.Segments[_editingLine];
        var text = CurrentLineText(_editingLine);

        // Bracketed lines are not speech; their text is generated, so an edit
        // to one is discarded rather than becoming a nonsense caption.
        if (segment.Span is null)
        {
            _suppressTranscriptCommit = true;
            Refresh();
            _suppressTranscriptCommit = false;
            Announce("that line is not editable text", urgent: true);
            return;
        }

        var result = _session.Apply("caption", (project, _) =>
            EditOperations.SetCaption(project, segment.Element, text));

        if (result.Changed) Announce(result.Announce(), urgent: false);
    }

    private string CurrentLineText(int line)
    {
        var buffer = _transcript.GetBuffer();
        var lines = (buffer.Text ?? string.Empty).Split('\n');
        return line >= 0 && line < lines.Length ? lines[line] : string.Empty;
    }

    private void AnnounceTranscriptLine()
    {
        var offset = _transcript.GetBuffer().CursorPosition;
        var line = _transcriptDocument.LineAt(offset);

        if (line == _lastAnnouncedLine) return;

        CommitCaption();

        _lastAnnouncedLine = line;
        _editingLine = line;
        Announce(_transcriptDocument.AnnounceLine(offset), urgent: false);
    }
    private void SyncTranscriptToCursor()
    {
        var buffer = _transcript.GetBuffer();
        var offset = Math.Clamp(
            _transcriptDocument.OffsetAt(_cursor.ProgrammeTime),
            0,
            Math.Max(0, (buffer.Text?.Length ?? 1) - 1));

        buffer.GetIterAtOffset(out var iter, offset);
        buffer.PlaceCursor(iter);
    }
}
