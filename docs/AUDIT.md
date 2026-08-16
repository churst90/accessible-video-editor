# Audit

Measured on the whole tree, not sampled. Numbers are from the source, not from
memory.

## 1. What is in the code but not reachable from the interface

**Was: nine commands. Then: none, wrongly. Now: none, checked.**

The nine were built - markers, transcript search, the command palette, the
to-do list, "read me the edit", reloading a hand-edited `edit.md`, and creating
titles, graphics and b-roll. The last three were the most misleading: b-roll,
titles and graphics rendered correctly and could be described, but could not be
created.

**This section then claimed "two checks run over the tree" and they did not
exist.** Nothing in the test suite read a source file. The conclusion happened to
be right for menus and happened to be wrong for keys, and a further **nine
commands were documented in `CommandRegistry` with no handler anywhere**:

| Command | Key | Outcome |
|---|---|---|
| `edit.snap` | `N` | **Wired.** `ProjectSettings.Snap` existed with no way to change it |
| `edit.rippleMode` | `Ctrl+Alt+R` | **Wired.** Same - the setting existed, unreachable |
| `select.segment` | `Ctrl+A` | **Wired** |
| `select.track` | `Ctrl+Shift+A` | **Wired** |
| `workflow.run`, `workflow.record` | `Ctrl+Alt+K`, `Ctrl+Alt+Shift+K` | **Removed** - model built, feature not |
| `edit.nudgeBack`, `edit.nudgeForward` | `Alt+Left/Right` | **Removed** - no verb in `EditOperations` |
| `edit.moveTrackUp`, `edit.moveTrackDown` | `Alt+Up/Down` | **Removed** - same |
| `render.presets` | `Ctrl+F2` | **Removed** - and see the bug below |

Also `review.describe` and `review.describeEdit` were two registry entries for
one feature, so `F1` read it out twice and the palette offered it on two rows.
Collapsed into one, with `Ctrl+Alt+D` as its alternate.

The rule applied throughout: **a registry entry is a promise that a key does
something.** It feeds `F1`, the command palette and the keymap, so an entry with
no handler is a key that lies - and a key that does nothing reads as the
application having missed the press, not as the feature being absent. Commands
that are designed but unbuilt now live in `ROADMAP.md` with their keys reserved,
and a test pins those keys so nothing quietly takes one.

The checks now exist, in `tests/WiringTests.cs`, and read the GTK sources as
text: every menu action reaches a handler, every `Run` target exists, every menu
label is a real registry command, no two commands share a title, and the
reserved keys stay unclaimed.

### Historical - nine commands were unbuilt Every one of them announces what it will do
and roughly when, rather than doing nothing:

| Command | State |
|---|---|
| `marker` | Not in the model at all. Markers are a real gap - chapters, notes and "come back to this" all want them |
| `find` | Transcript search. The transcript is a document; searching it is expected and missing |
| `palette` | Command palette. `CommandRegistry` already holds every command with its keys, so this is a list over data that exists |
| `issues` | The to-do list. `HoleElement` and the quality analysis already produce the findings; nothing gathers them |
| `describeEdit` | "Read me the edit". The pieces exist - `TranscriptDocument`, `TrackProbe`, `Timecode` |
| `reloadEdl` | Re-reading a hand-edited `edit.md`. `EdlReader` exists and is tested; only the reconcile step is missing |
| `title`, `graphic`, `broll` | Overlay items. The model, the anchoring and the renderer all exist; there is no way to add one |

The last three are the most misleading: **b-roll, titles and graphics render
correctly and can be described, but cannot be created from the interface.** A
project made in this application cannot yet contain one.

Both menu checks come back clean, and the menu-id one found two View items that
had been silently missing since they were written.

## 2. Bugs and design issues

### Fixed in the reconciliation pass

- **Any modified `R` started a recording.** The window-level handler matched
  `R` in the Tracks and Timeline views without checking modifiers, so `Ctrl+R`,
  `Alt+R` and `Ctrl+Alt+R` all began or ended a take - opening the camera, with
  nothing to see that it had happened. Bare `R` only, now.
- **`Ctrl+F2` started a master render.** It was documented as export presets,
  which did not exist; the `F2` case did not exclude Control, so the key fell
  through to the full render. A long job nobody asked for, and no way to tell it
  had begun. Export presets have since been built and the key now opens them.
- **The manual documented `F5` as "render draft".** `F5` arms a track, which
  opens the camera. Worth more than a typo.

### Fixed during the earlier audit

- **Two View menu items never appeared.** They referenced `pane.next` and
  `pane.previous`, which do not exist; the menu builder skips unknown ids
  silently.
- **`transition.set` was documented on `T`, which is takes.** It had no handler,
  so the conflict was invisible until the handler was written.
- **`Shift+;` and `Shift+'` send a colon and a double quote**, not the unshifted
  key with a modifier, so two guarded cases were unreachable.
- **`colorlevels:rimin=…`** was missing its first equals and was rejected by
  ffmpeg. The string test passed anyway, because a malformed filter still
  contains every substring you would think to check.

### Fixed since

- **`MainWindow` was 4,855 lines.** Split into two partials at the obvious seam;
  the window is now 4,390 lines and the project, marker and overlay commands
  have their own file. Still the largest file in the tree.
- **Twelve `async void` methods** now guard their bodies through one helper per
  view, so a failure is announced rather than taking the process down.
- **The image editor decoded its raster twice**, grey and colour. The grey one
  is now derived from the colour one.
- **No autosave.** A project that already has a home is now saved quietly every
  three minutes. It never prompts: a project with nowhere to go is left alone,
  and an autosave you have to answer is one you start dismissing.

### Corrected

**`Refresh()` on a timer was overstated.** The 100 ms tick only runs while
playback is going, and only sets label text on rows that already exist. There is
no per-tick rebuild.

### Fixed in the 0.17.0 pass

- **Autosave overwrote the project.** The one real data-safety bug in the tree.
  `Autosave()` wrote straight to `project.json` and cleared the dirty flag, so
  there was no way back to the last deliberate save and - despite the roadmap
  listing crash-to-recovery as "untested" - **no recovered file to open at all.**
  The item could not have been exercised even in principle. It now writes to
  `project.autosave.json` beside the project; the explicit save is the file of
  record and clears the recovery; `Ctrl+Shift+R` reverts; opening a folder with
  a newer autosave beside it offers it and says how much newer.

- **A failing autosave said nothing.** The `catch` swallowed and left the
  project dirty, so a disk-full condition was inaudible until you pressed
  Ctrl+S. Said once now, and not again until one succeeds.

- **`AppSettings.Defaults` and `AppSettings.Devices` were read by nothing.**
  Written and saved, and no behaviour followed - the settings version of a key
  that does nothing when pressed, and the same class of fault as the nine
  registry commands this file was written about. Both now arrive somewhere:
  defaults stamp a new project, devices supply a track armed with no input of
  its own.

- **Two copies of verbosity and earcons.** `Behaviour` and `Defaults` each held
  one; two copies of a setting disagree eventually, and the one the announcer
  reads wins silently. `Behaviour` is now the single source and
  `Preferences.ApplyDefaults` enforces it.

- **`MANUAL.md` said `Ctrl+5` shows a record view.** There is no record view -
  `Workspace.cs`, `Track.cs` and `CommandRegistry.cs` all say so deliberately -
  and `Ctrl+5` is the streamer view. Written when it was true and never
  re-checked, one pass after this file was written about exactly that.
  `WiringTests` pins the *sources*; nothing pins the prose.

### Open, in the order they matter

1. **No video has been cut and published with this yet.** Now the largest open
   item by some distance, and not a code one: everything here is tested, and
   none of it has survived an actual edit from import to upload. That is the
   test that finds what nobody thought to assert, and it is the deciding
   criterion for 1.0.

2. **The Windows spike has never been run.** It is written and it compiles;
   whether NVDA and JAWS can read a WPF `AutomationPeer` tree over a drawn
   canvas is unknown, and it is the single fact that decides whether there is a
   Windows client at all. A day on Windows answers it.

3. **Nothing pins the manual.** `tests/WiringTests.cs` reads the GTK sources and
   catches a menu item that reaches nothing, but the prose in `MANUAL.md` and
   `ROADMAP.md` can still claim a key that does something else - and did, again,
   this pass. A test that extracts the key tables from the manual and checks
   them against `CommandRegistry` would close it permanently. It is the highest
   value test not yet written.

4. **`MainWindow` is 1,174 lines**, down from 4,452. `MainWindow.Keys.cs` at
   1,030 and `StreamView` at 1,083 are now the largest, and both are coherent -
   one is key dispatch and the other is one view. Nothing in the tree is over
   1,200 lines. This is no longer a finding, only a number worth watching.

5. **The whole of the "missing" list below has since been built** - subclips,
   compound segments, export presets, audio effects, change over time and
   multicam. Nothing on it remains.

## 3. Comments and cleanliness

**31,300 lines of source. 15 percent comments.** The rule applied throughout is
**a comment keeps the reason and drops the restatement**: where a summary
elaborated on its own first paragraph, the elaboration went, and doc blocks of
nine lines or more came down from 91 to 59.

That ratio is defensible for this codebase, because a large share of the
comments carry decisions that are not recoverable from the code - *why* silence
means framed, *why* ducking is off by default, *why* Twitch moderation cannot go
over IRC. Those are worth their space; they are the reasoning that would
otherwise be re-litigated.

**Grade: A-.** Consistent naming, no dead code, no `TODO`s, no `.Result` or
`.Wait()` anywhere, and 843 tests that assert on behaviour and spoken output
rather than on implementation. The two oversized files are gone - `MainWindow`
is 1,174 lines and nothing exceeds 1,200.

It stays at A- rather than moving up, and the reason has changed. It is no
longer file size. It is that **two separate passes have now found settings and
prose that were written, saved, and read by nothing** - nine registry commands
last time, `AppSettings.Defaults` and a manual key table this time. The code is
good; what is missing is a check that catches the class of fault, and until the
manual is pinned the way the menus are, a third pass will find a third one.

**The lesson of this pass is about the documentation, not the code.** Every
claim in `ROADMAP.md` and `MANUAL.md` was written when it was true and never
re-checked, so the manual told a new reader that importing media was unbuilt
while nine registry commands the manual never mentioned did nothing at all.
Prose drifts silently; that is what `tests/WiringTests.cs` is for, and why the
metrics in this file are now taken from the tree rather than from memory.

## 4. Is the editing complete?

The verbs that exist: split, heal, trim head and tail, roll, ripple delete,
lift, disable, mute, hide, retime, fades, transitions, takes, insert and
overwrite from the bin, detach and reattach audio, duration and Ken Burns for
stills, and cut, copy and paste across tracks. Every one is reachable, announced
and undoable.

**What a working editor wanted and this did not have.** All but the last line
has since been built:

| Was missing | Now |
|---|---|
| **Nested or compound clips** | `SegmentGroup` - a grouping rather than nesting, so programme time is untouched and the members stay reachable |
| **Subclips** | `U` in the bin names the marked range; a reference, not a copy |
| **Multicam angle switching** | Synced by envelope cross-correlation over the cached waveforms; a digit cuts to an angle |
| **Audio effects** | Named presets per track and per segment, with measure-then-advise on `Ctrl+F4` |
| **Keyframes of any kind** | Named shapes rather than points: volume on a segment, position and opacity on an overlay |

The commands that do exist are coherent: one verb, one key, one announcement,
one undo entry. The keyboard scheme is documented with provenance for every
binding, and a test asserts no two commands claim the same key in a view where
both are live.
