using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Timeline;
using AccessibleVideoEditor.Engine;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// The things that turn a pile of footage into a library you can work from:
/// subclips, groups and camera angles - plus the sound and export commands that
/// go with them.
///
/// All of it follows the same rule. Every one of these commands acts on
/// something you cannot see, so every one says what it did to what, by name,
/// including when it refused.
/// </summary>
public sealed partial class MainWindow
{
    private void RegisterLibraryActions()
    {
        // ---- subclips -----------------------------------------------------

        Action("subclipCreate", MakeSubclip);
        Action("subclipList", ShowSubclips);

        // ---- groups -------------------------------------------------------

        Action("group", MakeGroup);
        Action("groupList", ShowGroups);

        // ---- angles -------------------------------------------------------

        Action("multicamCreate", MakeMulticam);
        Action("multicamSync", SyncMulticam);
        Action("multicamSwitch", ChooseAngle);

        // ---- sound --------------------------------------------------------

        Action("audioEffects", EditEffects);
        Action("audioAdvise", AdviseOnSound);
        Action("audioAutomation", EditAutomation);

        // ---- output -------------------------------------------------------

        Action("renderPresets", ChooseExportPreset);
    }

    // ---- subclips ---------------------------------------------------------

    /// <summary>
    /// Names the marked range of the source being browsed. The range comes from
    /// the same in and out marks the timeline uses, so there is one way to mark
    /// a range in the application rather than one per view.
    /// </summary>
    private void MakeSubclip()
    {
        var index = _mediaList.GetSelectedRow()?.GetIndex() ?? -1;

        if (index < 0 || index >= Project.Sources.Count)
        {
            Announce("select a source in the media bin first", urgent: true);
            return;
        }

        if (_cursor.Selection is not { IsEmpty: false } selection)
        {
            Announce("mark a range first with I and O", urgent: true);
            return;
        }

        var source = Project.Sources[index].Id;

        Prompt("Name this subclip", string.Empty, "Make", name =>
            Apply("subclip", p => SubclipOperations.Create(
                p, source, selection.From, selection.To, name)));
    }

    private void ShowSubclips()
    {
        if (Project.Subclips.Count == 0)
        {
            Announce("no subclips yet. Mark a range and press U", urgent: true);
            return;
        }

        var subclips = Project.Subclips.ToList();

        ChooseFromList(
            "Subclips",
            subclips.Select(s => s.Describe()).ToList(),
            index => Apply("insert subclip",
                p => SubclipOperations.Insert(p, subclips[index].Id, _cursor.ProgrammeTime)));
    }

    // ---- groups -----------------------------------------------------------

    private void MakeGroup()
    {
        if (_cursor.Selection is not { IsEmpty: false } selection)
        {
            Announce("mark a range first, or press Ctrl+A for this segment", urgent: true);
            return;
        }

        Prompt("Name this group", string.Empty, "Group", name =>
            Apply("group", p => GroupOperations.Group(p, selection, name)));
    }

    /// <summary>
    /// One list, four verbs. A group is rare enough that giving each verb its
    /// own key would spend four bindings on something you do twice a video.
    /// </summary>
    private void ShowGroups()
    {
        if (Project.Groups.Count == 0)
        {
            Announce("no groups yet. Mark a range and press Ctrl+Shift+G", urgent: true);
            return;
        }

        var groups = Project.Groups.ToList();
        var map = _session.Map;

        var lines = groups.Select(g =>
        {
            var members = Project.Spine.Where(e => g.Members.Contains(e.Id)).ToList();
            return g.Describe(members.Count, members.Sum(m => m.Duration));
        }).ToList();

        ChooseFromList("Groups", lines, index =>
        {
            var group = groups[index];

            ChooseFromList(
                group.Name,
                ["Go to it", "Collapse or expand", "Rename", "Ungroup", "Delete it and its segments"],
                verb =>
                {
                    switch (verb)
                    {
                        case 0:
                            if (GroupOperations.RangeOf(Project, map, group.Id) is { } range)
                            {
                                _cursor.MoveTo(range.From);
                                Refresh();
                                Announce(FocusedStatus(), urgent: true);
                            }

                            break;

                        case 1:
                            Apply("group", p => GroupOperations.ToggleCollapsed(p, group.Id));
                            break;

                        case 2:
                            Prompt("New name", group.Name, "Rename", name =>
                                Apply("group", p => GroupOperations.Rename(p, group.Id, name)));
                            break;

                        case 3:
                            Apply("ungroup", p => GroupOperations.Ungroup(p, group.Id));
                            break;

                        // The only destructive one here, so it is the only one
                        // that asks. Everything else can be pressed again.
                        default:
                            ConfirmThen(
                                $"Delete {group.Name} and its {group.Members.Count} segments?",
                                () => Apply("delete group", p => GroupOperations.Delete(p, group.Id)));
                            break;
                    }
                });
        });
    }

    // ---- camera angles ----------------------------------------------------

    private void MakeMulticam()
    {
        var cameras = Project.Sources.Where(s => s.Kind == SourceKind.Video).ToList();

        if (cameras.Count < 2)
        {
            Announce("import at least two videos first", urgent: true);
            return;
        }

        Prompt("Name this multicam group", "interview", "Make", name =>
            Apply("multicam", p => MulticamOperations.Create(
                p, name, cameras.Select(c => c.Id).ToList())));
    }

    /// <summary>
    /// Lines the angles up by sound. The waveforms are already cached for
    /// drawing, so this costs no extra decoding - and the first angle is the
    /// reference, because something has to be.
    /// </summary>
    private void SyncMulticam()
    {
        if (Project.Multicams.Count == 0)
        {
            Announce("no multicam groups yet. Press M to make one", urgent: true);
            return;
        }

        var groups = Project.Multicams.ToList();

        ChooseFromList("Sync which group", groups.Select(g => g.Describe()).ToList(), index =>
            _ = SyncAnglesAsync(groups[index]));
    }

    private async Task SyncAnglesAsync(MulticamGroup group)
    {
        if (_waveforms is not { } extractor)
        {
            Announce("save the project first; syncing needs somewhere to cache the sound", urgent: true);
            return;
        }

        Announce($"syncing {group.Angles.Count} angles by sound", urgent: true);

        var sources = group.Angles
            .Select(a => Project.SourceOf(a.Source))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        Dictionary<SourceId, WaveformData> waveforms;

        try
        {
            waveforms = [];

            foreach (var source in sources)
            {
                if (await extractor.LoadAsync(source).ConfigureAwait(true) is { } data)
                {
                    waveforms[source.Id] = data;
                }
            }
        }
        catch (Exception exception)
        {
            Announce($"could not read the sound. {exception.Message}", urgent: true);
            return;
        }

        if (waveforms.Count < 2)
        {
            Announce("could not read the sound of at least two angles", urgent: true);
            return;
        }

        // The first angle is the reference, because something has to be, and
        // saying so is better than choosing silently.
        var referenceSource = group.Angles[0].Source;

        if (!waveforms.TryGetValue(referenceSource, out var reference))
        {
            Announce($"could not read {group.Angles[0].Name}, which is the reference", urgent: true);
            return;
        }

        var results = group.Angles.ToDictionary(
            a => a.Source,
            a => a.Source == referenceSource
                ? new SyncResult(0, 1, "the reference")
                : waveforms.TryGetValue(a.Source, out var wave)
                    ? MulticamSync.Align(reference, wave)
                    : new SyncResult(0, 0, "no sound to read"));

        Apply("sync angles", p => MulticamOperations.ApplySync(p, group.Id, results));
    }

    /// <summary>
    /// The discoverable route to the digits. Reading the angles out is also the
    /// only way to learn which number is which without cutting to find out.
    /// </summary>
    private void ChooseAngle()
    {
        if (Project.Multicams.Count == 0)
        {
            Announce("no multicam groups in this project", urgent: true);
            return;
        }

        var group = Project.Multicams[0];

        ChooseFromList(
            $"Cut to an angle in {group.Name}",
            group.Angles.Select((a, i) => $"{i + 1}: {a.Describe()}").ToList(),
            index => SwitchAngle(index + 1));
    }

    /// <summary>
    /// A digit cuts to that angle - deliberately the same idea as a digit
    /// cutting to a scene while streaming, so the gesture is learnt once.
    /// </summary>
    private void SwitchAngle(int number)
    {
        if (Project.Multicams.Count == 0)
        {
            Announce("no multicam groups in this project", urgent: true);
            return;
        }

        var group = Project.Multicams[0];

        Apply("cut to angle",
            p => MulticamOperations.SwitchTo(p, group.Id, number - 1, _cursor.ProgrammeTime));
    }

    // ---- sound ------------------------------------------------------------

    /// <summary>
    /// Track effects or segment effects - asked first, because which one you are
    /// changing is the thing that is otherwise impossible to tell afterwards.
    /// </summary>
    private void EditEffects()
    {
        var track = _cursor.FocusedTrack is { } id ? Project.TrackOf(id) : Project.ProgrammeTrack;
        if (track is null) return;

        var element = _session.Map.Locate(_cursor.ProgrammeTime)?.Element;

        var where = element is null
            ? new[] { $"On the whole {track.Name} track" }
            : [$"On the whole {track.Name} track", "On this segment only"];

        ChooseFromList("Treat the sound", where, scope =>
        {
            var onTrack = scope == 0 || element is null;
            var chain = onTrack ? track.Effects : element!.Effects;
            var what = onTrack ? track.Name : "this segment";

            ChooseFromList(
                $"{what}: {AudioChains.Describe(chain)}",
                AudioChains.Presets.Select(p => $"{p.Name} - {p.Purpose}")
                           .Append("Clear them all")
                           .ToList(),
                choice =>
                {
                    if (choice >= AudioChains.Presets.Count)
                    {
                        chain.Clear();
                        _dirty = true;
                        Announce($"cleared the effects on {what}", urgent: true);
                        return;
                    }

                    var preset = AudioChains.Presets[choice];
                    chain.Clear();
                    chain.AddRange(AudioChains.Build(preset.Name)!);
                    _dirty = true;

                    Announce($"{what}: {AudioChains.Describe(chain)}", urgent: true);
                });
        });
    }

    /// <summary>
    /// Measures the sound under the cursor and suggests the effects by name, so
    /// the advice can be acted on by pressing the thing it just named.
    /// </summary>
    private void AdviseOnSound() => _ = AdviseOnSoundAsync();

    private async Task AdviseOnSoundAsync()
    {
        if (_session.Map.Locate(_cursor.ProgrammeTime) is not { Media: { } media } placed)
        {
            Announce("nothing recorded under the cursor to measure", urgent: true);
            return;
        }

        if (Project.SourceOf(media.Source) is not { } source)
        {
            Announce("that segment's source is missing", urgent: true);
            return;
        }

        Announce("measuring the sound", urgent: true);

        var path = System.IO.Path.IsPathRooted(source.Path) || Project.RootPath is null
            ? source.Path
            : System.IO.Path.Combine(Project.RootPath, source.Path);

        QualityReport report;

        try
        {
            report = await new QualityAnalyser()
                .AnalyseAsync(path, media.In, Math.Max(1, Math.Min(10, placed.Duration)))
                .ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Announce($"could not measure it. {exception.Message}", urgent: true);
            return;
        }

        var advice = AudioAdvice.ForReport(report.Loudness, report.PeakDb, null);

        if (!advice.HasAdvice)
        {
            Announce(advice.Announce(), urgent: true);
            return;
        }

        ChooseFromList(
            advice.Announce(),
            ["Apply them to this segment", "Apply them to the track", "Leave it"],
            choice =>
            {
                if (choice > 1) return;

                var chain = choice == 0
                    ? placed.Element.Effects
                    : Project.ProgrammeTrack.Effects;

                chain.Clear();
                chain.AddRange(advice.Suggested);
                _dirty = true;

                Announce($"applied: {AudioChains.Describe(chain)}", urgent: true);
            });
    }

    /// <summary>
    /// Volume over time, as a named shape. The numbers are offered as choices
    /// rather than typed, because "how many decibels should music duck by" is a
    /// question with three sensible answers and no reason to make you supply one.
    /// </summary>
    private void EditAutomation()
    {
        // An overlay is the only thing with somewhere to move to - a spine
        // element is the whole frame - so what is under the cursor decides
        // which question you are being asked, and the title says which.
        if (OverlayUnderCursor() is { } item)
        {
            EditOverlayAutomation(item);
            return;
        }

        if (_session.Map.Locate(_cursor.ProgrammeTime)?.Element is not { } element)
        {
            Announce("nothing under the cursor to automate", urgent: true);
            return;
        }

        var existing = element.Automation.FirstOrDefault(a => a.Target == AutomationTarget.Volume);

        var options = new List<string>
        {
            "Duck - down and back, for music under a voice",
            "Fade the volume up over this segment",
            "Fade the volume down over this segment",
            "Ease in to full",
            "Hold quieter for the whole segment",
        };

        if (existing is not null) options.Add($"Remove it - currently {existing.Describe()}");

        ChooseFromList(
            existing is null ? "Volume over time" : $"Volume over time: {existing.Describe()}",
            options,
            choice =>
            {
                element.Automation.RemoveAll(a => a.Target == AutomationTarget.Volume);

                if (choice >= 5)
                {
                    _dirty = true;
                    Refresh();
                    Announce("removed the volume shape", urgent: true);
                    return;
                }

                var duration = _session.Map.Find(element.Id)?.Duration ?? 4;

                var shape = choice switch
                {
                    0 => Automation.Duck(0, -18, Math.Min(duration, 4)),

                    1 => new Automation
                    {
                        Target = AutomationTarget.Volume,
                        Shape = AutomationShape.Ramp, From = -40, To = 0,
                    },

                    2 => new Automation
                    {
                        Target = AutomationTarget.Volume,
                        Shape = AutomationShape.Ramp, From = 0, To = -40,
                    },

                    3 => new Automation
                    {
                        Target = AutomationTarget.Volume,
                        Shape = AutomationShape.EaseIn, From = -40, To = 0,
                    },

                    _ => new Automation
                    {
                        Target = AutomationTarget.Volume,
                        Shape = AutomationShape.Steady, From = -12, To = -12,
                    },
                };

                element.Automation.Add(shape);
                _dirty = true;
                Refresh();

                Announce(shape.Describe(), urgent: true);
            });
    }

    /// <summary>
    /// The overlay item under the cursor on the focused track, if the focused
    /// track carries overlays at all. The programme track never does.
    /// </summary>
    private OverlayItem? OverlayUnderCursor()
    {
        if (_cursor.FocusedTrack is not { } trackId) return null;
        if (Project.TrackOf(trackId) is not { Kind: not TrackKind.Programme }) return null;

        var map = _session.Map;

        return Project.ItemsOn(trackId).FirstOrDefault(item =>
        {
            if (map.ResolveAnchor(item.Start) is not { } start) return false;

            var end = item.End is { } anchor
                ? map.ResolveAnchor(anchor)
                : start + (item.Length ?? 0);

            return end is not null
                   && _cursor.ProgrammeTime >= start
                   && _cursor.ProgrammeTime < end;
        });
    }

    /// <summary>
    /// Position and opacity over the life of an overlay - a lower third that
    /// slides in from the left, a graphic that fades up and holds.
    ///
    /// Offered as movements rather than as axes and numbers. "Slide in from the
    /// left" is a decision; "horizontal position, ramp, 0 to 50 percent over 0.6
    /// seconds" is the same thing said in a way you would have to translate.
    /// </summary>
    private void EditOverlayAutomation(OverlayItem item)
    {
        var existing = item.Automation.Count == 0
            ? null
            : string.Join(", ", item.Automation.Select(a => a.Describe()));

        var options = new List<string>
        {
            "Fade it up as it appears",
            "Fade it out as it goes",
            "Slide in from the left",
            "Slide in from the right",
            "Rise up into place",
            "Hold it half transparent",
        };

        if (existing is not null) options.Add($"Remove it all - currently {existing}");

        ChooseFromList(
            existing is null
                ? $"Movement for {item.Describe()}"
                : $"{item.Describe()}: {existing}",
            options,
            choice =>
            {
                var length = Math.Max(0.3, Math.Min(item.Length ?? 1, 0.6));

                if (choice >= 6)
                {
                    item.Automation.Clear();
                    _dirty = true;
                    Refresh();
                    Announce($"cleared the movement on {item.Describe()}", urgent: true);
                    return;
                }

                // One shape per axis: a second on the same axis would be two
                // people moving the same thing, and the result would depend on
                // order rather than on intent.
                var (target, shape) = Movement(choice, item, length);

                item.Automation.RemoveAll(a => a.Target == target);
                item.Automation.Add(shape);

                _dirty = true;
                Refresh();

                Announce(shape.Describe(), urgent: true);
            });
    }

    private static (AutomationTarget Target, Automation Shape) Movement(
        int choice,
        OverlayItem item,
        double length)
    {
        var (x, y) = item is TitleItem title
            ? title.Placement.Resolve()
            : (0.5, 0.5);

        return choice switch
        {
            0 => (AutomationTarget.Opacity, new Automation
            {
                Target = AutomationTarget.Opacity,
                Shape = AutomationShape.Ramp, From = 0, To = 100, Length = length,
            }),

            1 => (AutomationTarget.Opacity, new Automation
            {
                Target = AutomationTarget.Opacity,
                Shape = AutomationShape.Ramp, From = 100, To = 0,
                Length = length,
                Delay = Math.Max(0, (item.Length ?? length * 3) - length),
            }),

            // Off the edge of the frame to where it belongs, so the slide ends
            // exactly where the placement said it would sit.
            2 => (AutomationTarget.PositionX, new Automation
            {
                Target = AutomationTarget.PositionX,
                Shape = AutomationShape.EaseOut, From = -20, To = x * 100, Length = length,
            }),

            3 => (AutomationTarget.PositionX, new Automation
            {
                Target = AutomationTarget.PositionX,
                Shape = AutomationShape.EaseOut, From = 120, To = x * 100, Length = length,
            }),

            4 => (AutomationTarget.PositionY, new Automation
            {
                Target = AutomationTarget.PositionY,
                Shape = AutomationShape.EaseOut, From = y * 100 + 12, To = y * 100, Length = length,
            }),

            _ => (AutomationTarget.Opacity, new Automation
            {
                Target = AutomationTarget.Opacity,
                Shape = AutomationShape.Steady, From = 50, To = 50,
            }),
        };
    }

    // ---- output -----------------------------------------------------------

    /// <summary>
    /// Each preset says what it will do to the picture <b>before</b> it runs.
    /// A vertical export throws away most of the width of every frame, and that
    /// is otherwise something you discover by watching the result.
    /// </summary>
    private void ChooseExportPreset()
    {
        var settings = Project.Settings;

        var options = ExportPreset.BuiltIn
            .Select(p => $"{p.Describe()}. {p.DescribeCost(settings.CanvasWidth, settings.CanvasHeight)}")
            .ToList();

        ChooseFromList("Export preset", options, index =>
            _ = RenderWithPresetAsync(ExportPreset.BuiltIn[index]));
    }

    private async Task RenderWithPresetAsync(ExportPreset preset)
    {
        if (Project.RootPath is null)
        {
            Announce("save the project first; a render needs somewhere to put its files", urgent: true);
            return;
        }

        if (_rendering)
        {
            Announce("a render is already running", urgent: true);
            return;
        }

        _rendering = true;
        Announce($"rendering {preset.Name}", urgent: true);

        var lastSpoken = -1;

        // Every ten percent, as the other renders do. An export that talks
        // constantly is one you cannot work through.
        var progress = new Progress<RenderProgress>(report =>
        {
            var decile = (int)(report.Fraction * 10);
            if (decile == lastSpoken || decile == 0) return;

            lastSpoken = decile;
            Announce($"{decile * 10} percent", urgent: false);
        });

        try
        {
            var output = await new FfmpegRenderEngine()
                .RenderAsync(Project, RenderQuality.Master, progress, default, preset)
                .ConfigureAwait(true);

            Announce(
                $"{preset.Name} done, {Timecode.Speak(output.Duration)}, "
                + System.IO.Path.GetFileName(output.Path),
                urgent: true);
        }
        catch (Exception exception)
        {
            Announce($"{preset.Name} failed. {exception.Message}", urgent: true);
        }
        finally
        {
            _rendering = false;
        }
    }
}
