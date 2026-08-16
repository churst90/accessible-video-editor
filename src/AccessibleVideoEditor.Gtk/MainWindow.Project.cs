using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Commands;
using AccessibleVideoEditor.Core.Edl;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Images;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Serialization;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Streaming;
using AccessibleVideoEditor.Core.Timeline;
using AccessibleVideoEditor.Engine;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// Files, markers, overlays and the whole-project questions. Split out because
/// the window was the only file in the tree that had grown too big to read.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Markers, overlays, search and the two whole-project questions. The model
    /// had all of this; none of it had a key.
    /// </summary>
    private void RegisterReviewActions()
    {
        Action("marker", () => Prompt("Marker label", string.Empty, "Add", label =>
            Apply("marker", p => OverlayOperations.AddMarker(p, _cursor.ProgrammeTime, label))));

        Action("removeMarker", () =>
            Apply("remove marker", p => OverlayOperations.RemoveMarker(p, _cursor.ProgrammeTime)));

        Action("markerList", ShowMarkers);

        Action("title", () => Prompt("Title text", string.Empty, "Add", text =>
            Apply("title", p => OverlayOperations.AddTitle(p, _cursor.ProgrammeTime, text))));

        Action("graphic", () => ChooseSource(
            "Graphic",
            SourceKind.Image,
            source => Apply("graphic", p =>
                OverlayOperations.AddGraphic(p, _cursor.ProgrammeTime, source))));

        Action("broll", () => ChooseSource(
            "B-roll",
            SourceKind.Video,
            source => Prompt("How long, in seconds", "4", "Add", length =>
            {
                if (!double.TryParse(length, out var seconds) || seconds <= 0)
                {
                    Announce("say a number of seconds", urgent: true);
                    return;
                }

                Apply("b-roll", p =>
                    OverlayOperations.AddBroll(p, _cursor.ProgrammeTime, source, 0, seconds));
            })));

        Action("issues", ShowIssues);
        Action("describeEdit", () => Announce(ProjectReview.Describe(Project), urgent: true));
        Action("find", FindInTranscript);
        Action("palette", ShowPalette);
        Action("reloadEdl", ReloadEdl);
    }

    /// <summary>
    /// A list of the sources of one kind. Overlays need a file, and typing a
    /// path when the media bin already knows them is work for its own sake.
    /// </summary>
    private void ChooseSource(string what, SourceKind kind, System.Action<SourceId> chosen)
    {
        var sources = Project.Sources.Where(s => s.Kind == kind).ToList();

        if (sources.Count == 0)
        {
            Announce($"import {what.ToLowerInvariant()} first with Control I", urgent: true);
            return;
        }

        _sourcePicked = chosen;

        var menu = Gio.Menu.New();

        foreach (var source in sources)
        {
            menu.Append(System.IO.Path.GetFileName(source.Path), $"win.pickSource::{source.Id.Value}");
        }

        PopUp(menu, $"{sources.Count} to choose from");
    }

    private System.Action<SourceId>? _sourcePicked;

    private void ShowMarkers()
    {
        var markers = OverlayOperations.MarkersInOrder(Project);

        if (markers.Count == 0)
        {
            Announce("no markers yet. M adds one", urgent: true);
            return;
        }

        ShowList(
            "Markers",
            markers.Select(m => ($"{Timecode.FormatShort(m.At)}, {m.Marker.Describe()}", m.At)).ToList(),
            $"{markers.Count} markers");
    }

    private void ShowIssues()
    {
        var issues = ProjectReview.Issues(Project);

        if (issues.Count == 0)
        {
            Announce("nothing outstanding", urgent: true);
            return;
        }

        ShowList(
            "To do",
            issues.Select(i => (i.Describe(), i.At)).ToList(),
            ProjectReview.DescribeIssues(Project));
    }

    private void FindInTranscript() =>
        Prompt("Find in the transcript", string.Empty, "Find", phrase =>
        {
            var found = ProjectReview.Find(Project, phrase);

            if (found.Count == 0)
            {
                Announce($"{phrase} is not in the transcript", urgent: true);
                return;
            }

            ShowList(
                $"Found {phrase}",
                found.Select(f => ($"{Timecode.FormatShort(f.At)}, {f.Text}", f.At)).ToList(),
                $"{found.Count} {(found.Count == 1 ? "result" : "results")}");
        });

    /// <summary>
    /// Anything with a time attached, as a list you can arrow through and press
    /// Enter on to go there. Markers, issues and search results are the same
    /// shape, so they are the same window.
    /// </summary>
    private void ShowList(string title, IReadOnlyList<(string Text, double At)> entries, string announce)
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = title;
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(640, 520);

        var list = Gtk_.ListBox.New();
        list.SelectionMode = Gtk_.SelectionMode.Single;

        foreach (var (text, _) in entries) list.Append(Row(text));

        void GoThere()
        {
            var index = list.GetSelectedRow()?.GetIndex() ?? -1;

            if (index < 0 || index >= entries.Count) return;

            dialog.Close();

            _cursor.MoveTo(entries[index].At);
            Refresh();
            Announce(entries[index].Text, urgent: true);
        }

        list.OnRowActivated += (_, _) => GoThere();

        var keys = Gtk_.EventControllerKey.New();
        keys.OnKeyPressed += (_, args) =>
        {
            switch (args.Keyval)
            {
                case Gdk.Constants.KEY_Escape: dialog.Close(); return true;
                case Gdk.Constants.KEY_Return or Gdk.Constants.KEY_KP_Enter: GoThere(); return true;
                default: return false;
            }
        };

        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(list);
        scroller.Vexpand = true;
        scroller.AddCssClass("pane");

        dialog.AddController(keys);
        dialog.SetChild(scroller);
        dialog.Present();

        list.GrabFocus();
        Announce($"{announce}. Enter goes there, Escape closes", urgent: true);
    }

    /// <summary>
    /// Every command by name, filtered as you type. The registry already holds
    /// all of them with their keys, so this is a list over data that exists.
    /// </summary>
    private void ShowPalette()
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = "Commands";
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(680, 560);

        var entry = Gtk_.Entry.New();
        entry.PlaceholderText = "Type to filter";

        var list = Gtk_.ListBox.New();
        list.SelectionMode = Gtk_.SelectionMode.Single;

        var all = CommandRegistry.All.OrderBy(c => c.Title).ToList();
        var shown = new List<CommandDefinition>();

        void Fill(string filter)
        {
            while (list.GetRowAtIndex(0) is { } row) list.Remove(row);

            shown.Clear();

            foreach (var command in all.Where(c =>
                         filter.Length == 0
                         || c.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                shown.Add(command);
                list.Append(Row($"{command.Title}: {command.Keys}"));
            }

            if (shown.Count == 0) list.Append(Row("nothing matches"));
        }

        Fill(string.Empty);

        entry.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "text") Fill(entry.GetText());
        };

        var keys = Gtk_.EventControllerKey.New();
        keys.OnKeyPressed += (_, args) =>
        {
            switch (args.Keyval)
            {
                case Gdk.Constants.KEY_Escape:
                    dialog.Close();
                    return true;

                case Gdk.Constants.KEY_Down:
                    list.GrabFocus();
                    return true;

                case Gdk.Constants.KEY_Return or Gdk.Constants.KEY_KP_Enter:
                    var index = list.GetSelectedRow()?.GetIndex() ?? 0;

                    if (index < 0 || index >= shown.Count) return true;

                    var chosen = shown[index];
                    dialog.Close();
                    Announce($"{chosen.Title}, {chosen.Keys}", urgent: true);

                    return true;

                default:
                    return false;
            }
        };

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 12; box.MarginBottom = 12; box.MarginStart = 12; box.MarginEnd = 12;
        box.Append(entry);

        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(list);
        scroller.Vexpand = true;
        scroller.AddCssClass("pane");
        box.Append(scroller);

        dialog.AddController(keys);
        dialog.SetChild(box);
        dialog.Present();

        entry.GrabFocus();
        Announce($"{all.Count} commands. Type to filter, down arrow for the list", urgent: true);
    }

    /// <summary>
    /// Re-reads a hand-edited edit.md. Element ids survive the round trip, so
    /// overlays anchored to a segment stay attached to it.
    /// </summary>
    private void ReloadEdl()
    {
        if (Project.RootPath is not { Length: > 0 } root)
        {
            Announce("save the project first; there is nowhere to read from", urgent: true);
            return;
        }

        var path = System.IO.Path.Combine(root, "edit.md");

        if (!File.Exists(path))
        {
            Announce("there is no edit dot m d beside this project", urgent: true);
            return;
        }

        WithUnsavedWork(() =>
        {
            try
            {
                var before = _session.Map.Duration;
                var reloaded = EdlReader.Read(File.ReadAllText(path), Project);

                LoadInto(reloaded);

                _dirty = true;

                Announce(
                    $"reloaded, {reloaded.Spine.Count} segments, "
                    + $"{Timecode.Speak(_session.Map.Duration)}, was {Timecode.Speak(before)}",
                    urgent: true);
            }
            catch (Exception exception)
            {
                Announce($"could not read it: {exception.Message}", urgent: true);
            }
        });
    }

    /// <summary>
    /// New, open, save. Everything here guards unsaved work, because losing an
    /// afternoon's edit is the one failure no amount of announcing makes up for.
    /// </summary>
    private void RegisterFileActions()
    {
        Action("new", NewProject);
        Action("open", OpenProject);
        Action("save", () => _ = SaveProject(saveAs: false));
        Action("saveAs", () => _ = SaveProject(saveAs: true));
        Action("revert", RevertToSaved);
        Action("recent", OpenRecent);
    }

    private void NewProject() => WithUnsavedWork(() =>
        Prompt("Name for the new project", "Untitled", "Create", name =>
        {
            var project = Project.CreateDefault(name);

            // The preferences window's "what a new project starts from" section
            // arrives here. Without this the defaults were stored, saved and
            // read by nothing at all.
            Preferences.ApplyDefaults(_settings, project);

            LoadInto(project);

            _dirty = true;
            Announce(
                $"{name}, {project.Settings.CanvasWidth} by {project.Settings.CanvasHeight}. "
                + "Nothing is on disk until you save it with Control S",
                urgent: true);
        }));

    private void OpenProject() => WithUnsavedWork(() =>
        Prompt("Project folder", LastFolder(), "Open", path =>
        {
            var recovery = RecoveryFile.Check(path);

            if (!File.Exists(System.IO.Path.Combine(path, "project.json")) && !recovery.Available)
            {
                Announce("there is no project in that folder", urgent: true);
                return;
            }

            // The recovery question is asked before the project is opened, not
            // after, so answering it is a choice rather than an undo.
            if (recovery.Available)
            {
                ConfirmThen(
                    recovery.Question(),
                    () => _ = OpenFrom(path, RecoveryFile.PathFor(path), recovered: true),
                    otherwise: () => _ = OpenFrom(path, RecoveryFile.ProjectPathFor(path), recovered: false));

                return;
            }

            _ = OpenFrom(path, RecoveryFile.ProjectPathFor(path), recovered: false);
        }));

    private async Task OpenFrom(string folder, string file, bool recovered)
    {
        try
        {
            var project = await ProjectJson.LoadFromAsync(file, folder).ConfigureAwait(true);

            LoadInto(project);
            Remember(folder);

            // Recovered work is *not* on disk in the project yet, so it is
            // dirty by definition. Marking it clean would mean the one thing
            // you must not do here: let it be closed again without a prompt.
            _dirty = recovered;

            Announce(
                $"{(recovered ? "recovered work. " : string.Empty)}{project.Name}. "
                + $"{project.Spine.Count} segments, {Timecode.Speak(_session.Map.Duration)}"
                + (recovered ? ". Control S to keep it" : string.Empty),
                urgent: true);
        }
        catch (Exception exception)
        {
            Announce($"could not open it: {exception.Message}", urgent: true);
        }
    }

    /// <summary>
    /// Saves in place when the project has a home, and asks where when it does
    /// not. Silent success is deliberate elsewhere in this application; not
    /// here - a save you did not hear is a save you will not trust.
    /// </summary>
    private async Task SaveProject(bool saveAs)
    {
        if (!saveAs && Project.RootPath is { Length: > 0 } existing)
        {
            await WriteTo(existing).ConfigureAwait(true);
            return;
        }

        Prompt(
            "Folder to save the project in",
            Project.RootPath ?? System.IO.Path.Combine(LastFolder(), Sanitise(Project.Name)),
            "Save",
            path => _ = WriteTo(path));
    }

    /// <summary>
    /// Saves a project that already has a home, quietly, <b>beside</b> the
    /// project rather than over it. It never prompts: a project with nowhere to
    /// go is left alone rather than interrupting you to ask where, and an
    /// autosave you have to answer is one you start dismissing.
    ///
    /// It deliberately does not clear <c>_dirty</c>. The work is safe from a
    /// crash but it is not in the file of record, and those are different
    /// things - saying otherwise is what let an afternoon's abandoned
    /// experiment overwrite the last deliberate save.
    /// </summary>
    private async Task Autosave()
    {
        if (!_dirty || Project.RootPath is not { Length: > 0 } path) return;

        try
        {
            await ProjectJson.SaveToAsync(Project, RecoveryFile.PathFor(path)).ConfigureAwait(true);

            _autosaveFailed = false;
        }
        catch (Exception exception)
        {
            // Said once, then not again until one succeeds. A quiet save that
            // has been failing for an hour is worth interrupting for exactly
            // once; every three minutes is how a warning gets ignored.
            if (_autosaveFailed) return;

            _autosaveFailed = true;

            Announce($"autosave is failing: {exception.Message}", urgent: true);
        }
    }

    /// <summary>
    /// Throws away everything since the last explicit save.
    ///
    /// The counterpart of autosave no longer overwriting the project: there is
    /// now a saved state to go back to, so going back to it is a thing you can
    /// ask for. It confirms first, and says what it is about to cost.
    /// </summary>
    private void RevertToSaved()
    {
        if (Project.RootPath is not { Length: > 0 } path
            || !File.Exists(RecoveryFile.ProjectPathFor(path)))
        {
            Announce("this project has never been saved, so there is nothing to go back to", urgent: true);
            return;
        }

        if (!_dirty)
        {
            // The specific thing rather than reverting to what is already
            // loaded, which would look like it had worked and changed nothing.
            Announce("nothing has changed since the last save", urgent: true);
            return;
        }

        ConfirmThen(
            $"Throw away every change to {Project.Name} since the last save?",
            () =>
            {
                RecoveryFile.Clear(path);
                _ = OpenFrom(path, RecoveryFile.ProjectPathFor(path), recovered: false);
            });
    }

    private async Task WriteTo(string path)
    {
        try
        {
            await ProjectJson.SaveAsync(Project, path).ConfigureAwait(true);

            // The work is now in the file of record, so the autosave beside it
            // is stale. Left there, it would offer at the next open to replace
            // this save with something older, in a dialog that sounds helpful.
            RecoveryFile.Clear(path);

            Remember(path);

            _dirty = false;
            _window.Title = $"{Project.Name} - {AboutInfo.Name}";

            Announce($"saved to {System.IO.Path.GetFileName(path)}", urgent: true);
        }
        catch (Exception exception)
        {
            Announce($"could not save: {exception.Message}", urgent: true);
        }
    }

    private void OpenRecent()
    {
        var recent = _settings.Recent.Where(Directory.Exists).ToList();

        if (recent.Count == 0)
        {
            Announce("nothing recent yet", urgent: true);
            return;
        }

        var menu = Gio.Menu.New();

        foreach (var path in recent)
        {
            menu.Append(System.IO.Path.GetFileName(path), $"win.openRecent::{path}");
        }

        PopUp(menu, $"{recent.Count} recent");
    }

    /// <summary>
    /// Replaces everything the window is showing. The session is rebuilt rather
    /// than mutated so undo cannot reach back into a project you have closed.
    /// </summary>
    private void LoadInto(Project project)
    {
        _session = new EditSession(project);
        _cursor.FocusedTrack = project.ProgrammeTrack.Id;
        _cursor.MoveTo(0);
        _cursor.ClearSelection();
        _viewStart = 0;

        _window.Title = $"{project.Name} - {AboutInfo.Name}";

        RebuildTrackRows();
        RebuildMediaRows();
        Refresh();
    }

    /// <summary>
    /// Asks before throwing work away, and only when there is work to throw.
    /// No is focused, because it is the answer you get by pressing Enter
    /// without having listened.
    /// </summary>
    private void WithUnsavedWork(System.Action then)
    {
        if (!_dirty)
        {
            then();
            return;
        }

        ConfirmThen($"{Project.Name} has unsaved changes. Discard them?", then);
    }

    private void Remember(string path)
    {
        _settings.Recent.Remove(path);
        _settings.Recent.Insert(0, path);

        while (_settings.Recent.Count > 10) _settings.Recent.RemoveAt(_settings.Recent.Count - 1);

        _settings.Save();
    }

    private string LastFolder() =>
        _settings.Recent.FirstOrDefault() is { Length: > 0 } recent
            ? System.IO.Path.GetDirectoryName(recent) ?? recent
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string Sanitise(string name) =>
        string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));

    /// <summary>
    /// Transitions, track levels, and the handful of commands that were in the
    /// core with no way to reach them.
    /// </summary>
    private void RegisterTransitionActions()
    {
        Action("setTransition", ChooseTransition);
        Action("auditionTransition", AuditionTransition);
        Action("transitionSound", ChooseTransitionSound);
        Action("saveTransition", SaveCustomTransition);
        Action("customTransition", ChooseCustomTransition);

        Action("trackVolume", SetTrackVolume);

        Action("speed", SetSpeed);
        Action("insertHole", InsertHole);
        Action("verbosity", CycleVerbosity);

        Action("removeTrack", () =>
        {
            if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track) return;

            ConfirmThen(
                $"Delete {track.Name} and everything on it?",
                () => Apply("delete track", p => EditOperations.RemoveTrack(p, track.Id)));
        });

        ParameterisedAction("pickTransition", name => ApplyTransition(name));
        ParameterisedAction("pickTransitionLength", name =>
        {
            if (double.TryParse(name, out var seconds)) SetTransitionLength(seconds);
        });
        ParameterisedAction("pickCustomTransition", name => UseCustomTransition(name));

        ParameterisedAction("pickSource", id =>
        {
            if (id.Length == 0 || _sourcePicked is not { } chosen) return;

            _sourcePicked = null;
            chosen(new SourceId(id));
        });

        ParameterisedAction("openRecent", path =>
        {
            if (path.Length == 0) return;

            WithUnsavedWork(async () =>
            {
                try
                {
                    LoadInto(await ProjectJson.LoadAsync(path).ConfigureAwait(true));
                    Remember(path);

                    _dirty = false;
                    Announce($"{Project.Name}", urgent: true);
                }
                catch (Exception exception)
                {
                    Announce($"could not open it: {exception.Message}", urgent: true);
                }
            });
        });
    }

    /// <summary>
    /// The transition entering the segment under the cursor. Type first,
    /// because that is the decision; the length follows and has its own menu.
    /// </summary>
    private void ChooseTransition()
    {
        var menu = Gio.Menu.New();

        var common = Gio.Menu.New();
        foreach (var (name, _) in TransitionLibrary.Common)
        {
            common.Append(TitleCase(name), $"win.pickTransition::{name}");
        }

        menu.AppendSection(null, common);

        var more = Gio.Menu.New();
        foreach (var name in TransitionLibrary.More)
        {
            more.Append(name, $"win.pickTransition::{name}");
        }

        menu.AppendSubmenu("More", more);

        var lengths = Gio.Menu.New();
        foreach (var seconds in TransitionLibrary.Lengths)
        {
            lengths.Append(
                TransitionLibrary.DescribeLength(seconds),
                $"win.pickTransitionLength::{seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        menu.AppendSubmenu("Length", lengths);

        var extras = Gio.Menu.New();
        extras.Append("Sound On This Transition", "win.transitionSound");
        extras.Append("Audition It", "win.auditionTransition");
        extras.Append("Save As My Own", "win.saveTransition");
        extras.Append("Use One Of Mine", "win.customTransition");
        menu.AppendSection(null, extras);

        PopUp(menu, $"transition menu. {DescribeTransitionHere()}");
    }

    private Transition? TransitionHere()
    {
        var placed = _session.Map.Locate(_cursor.ProgrammeTime);

        return placed?.Element.TransitionIn;
    }

    private string DescribeTransitionHere() =>
        TransitionHere() is { } transition
            ? $"currently {transition.Describe()}"
            : "currently the project default";

    private void ApplyTransition(string name)
    {
        if (name.Length == 0) return;

        var existing = TransitionHere();

        var match = TransitionLibrary.Common.FirstOrDefault(t => t.Name == name);

        var transition = existing?.Copy() ?? new Transition();

        if (match.Name is not null)
        {
            transition.Type = match.Type;
            transition.CustomType = null;
            transition.Expression = null;
        }
        else
        {
            transition.Type = TransitionType.Custom;
            transition.CustomType = name;
            transition.Expression = null;
        }

        if (transition.Type == TransitionType.Cut) transition.Duration = 0;
        else if (transition.Duration <= 0) transition.Duration = 0.4;

        Apply("set transition", p => EditOperations.SetTransition(p, _cursor.ProgrammeTime, transition));
    }

    private void SetTransitionLength(double seconds)
    {
        var transition = TransitionHere()?.Copy() ?? new Transition();

        transition.Duration = seconds;

        if (transition.Type == TransitionType.Cut && seconds > 0) transition.Type = TransitionType.Fade;

        Apply(
            "transition length",
            p => EditOperations.SetTransition(p, _cursor.ProgrammeTime, transition));

        Announce(TransitionLibrary.DescribeLength(seconds), urgent: true);
    }

    /// <summary>
    /// A sound under the cut. It belongs to the boundary, so moving the cut
    /// moves the sound with it.
    /// </summary>
    private void ChooseTransitionSound() =>
        Prompt("Sound file for this transition", string.Empty, "Use", path =>
        {
            var transition = TransitionHere()?.Copy() ?? new Transition();

            transition.SoundPath = path;

            Apply(
                "transition sound",
                p => EditOperations.SetTransition(p, _cursor.ProgrammeTime, transition));

            Announce(
                "the programme is not ducked under it; set the track levels yourself with Shift+G",
                urgent: true);
        });

    /// <summary>Plays across the boundary, so the transition can be heard rather than imagined.</summary>
    private void AuditionTransition()
    {
        if (TransitionHere() is not { } transition)
        {
            Announce("there is no transition here", urgent: true);
            return;
        }

        var length = Math.Max(0.4, transition.Duration);
        var from = Math.Max(0, _cursor.ProgrammeTime - length);

        if (!_player.IsAvailable || !EnsureLoaded()) return;

        Announce($"auditioning {transition.Describe()}", urgent: true);

        _ = _player.PlayRangeAsync(from, Math.Min(_session.Map.Duration, _cursor.ProgrammeTime + length));
    }

    private void SaveCustomTransition() =>
        Prompt("Name for this transition", string.Empty, "Save", name =>
        {
            var here = TransitionHere();

            Prompt(
                "xfade name, or an expression",
                here?.Expression ?? here?.FfmpegName ?? "fade",
                "Save",
                definition =>
                {
                    var custom = new CustomTransition
                    {
                        Name = name,
                        Definition = definition,
                        IsExpression = definition.Contains('(') || definition.Contains("PROGRESS"),
                        Duration = here?.Duration ?? 0.4,
                        SoundPath = here?.SoundPath,
                    };

                    Project.CustomTransitions.RemoveAll(t => t.Name == name);
                    Project.CustomTransitions.Add(custom);

                    Announce($"saved {custom.Describe()}", urgent: true);
                });
        });

    private void ChooseCustomTransition()
    {
        if (Project.CustomTransitions.Count == 0)
        {
            Announce("you have not saved any yet; set one up and choose save as my own", urgent: true);
            return;
        }

        var menu = Gio.Menu.New();

        foreach (var custom in Project.CustomTransitions)
        {
            menu.Append(custom.Name, $"win.pickCustomTransition::{custom.Name}");
        }

        PopUp(menu, $"{Project.CustomTransitions.Count} of your own");
    }

    private void UseCustomTransition(string name)
    {
        if (Project.CustomTransitions.FirstOrDefault(t => t.Name == name) is not { } custom) return;

        Apply(
            "custom transition",
            p => EditOperations.SetTransition(p, _cursor.ProgrammeTime, custom.ToTransition()));
    }

    /// <summary>
    /// The level of one track. There is no automatic ducking anywhere in this
    /// application, so this is how a music bed is put under a voice - by
    /// deciding it.
    /// </summary>
    private void SetTrackVolume()
    {
        if (Project.TrackOf(_cursor.FocusedTrack ?? default) is not { } track) return;

        Prompt(
            $"{track.Name} level in dB",
            track.GainDb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            "Set",
            text =>
            {
                if (!double.TryParse(text, out var db))
                {
                    Announce("say a number of decibels, like minus 12", urgent: true);
                    return;
                }

                track.GainDb = Math.Clamp(db, -60, 12);

                RebuildTrackRows();
                Announce($"{track.Name} at {track.GainDb:0.#} dB", urgent: true);
            });
    }

    private void SetSpeed() =>
        Prompt("Speed, where 1 is normal", "1", "Set", text =>
        {
            if (!double.TryParse(text, out var speed) || speed <= 0)
            {
                Announce("say a number, like 0.5 for half speed", urgent: true);
                return;
            }

            Apply("speed", p => EditOperations.SetSpeed(p, _cursor.ProgrammeTime, speed));
        });

    private void InsertHole() =>
        Prompt("What is missing here", string.Empty, "Insert", note =>
            Apply("insert hole", p => EditOperations.InsertHole(p, _cursor.ProgrammeTime, 2, note)));

    private void CycleVerbosity()
    {
        Project.Settings.Verbosity = Project.Settings.Verbosity switch
        {
            Verbosity.Terse => Verbosity.Normal,
            Verbosity.Normal => Verbosity.Verbose,
            _ => Verbosity.Terse,
        };

        _settings.Behaviour.Verbosity = Project.Settings.Verbosity;
        _settings.Save();

        Refresh();
        Announce($"{Project.Settings.Verbosity.ToString().ToLowerInvariant()} speech", urgent: true);
    }
}
