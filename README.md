# Accessible Video Editor

An accessible video editor. Edit by transcript, navigate by ear, frame the shot
by tone.

Built for a blind editor as the primary user rather than as an accessibility
layer over a visual timeline — which is why the timeline model, the navigation
model and the viewfinder all look different from a conventional NLE.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the design and the reasoning behind
it.

## Status

**Version 0.17.0.** The minor number is the highest roadmap phase built, so it
says how far through the plan this is rather than being a number that goes up.
All seventeen phases are built; 843 tests.

It is deliberately **not 1.0**, and the deciding reason is not a missing
feature: no whole video has been cut and published with it yet, and that is the
only test that finds what nobody thought to assert.
[docs/ROADMAP.md](docs/ROADMAP.md) lists the rest of the criteria.

Usable end to end: import, cut, card, render — and stream.

**Built:** the document model and programme/source time mapping; three deletes,
split, heal, trim, retime, holes, cards and the clipboard; the `edit.md` round
trip; five views with a persistent status line; the transcript editor; playback
and audio scrub; recording, takes and the audible VU meter; the card editor;
fades, transitions and rendering; frame description and quality analysis;
stills and Ken Burns; the drawn timeline with waveforms; the streamer view with
scenes, sources, multi-destination streaming and chat on Twitch, YouTube and
Facebook; the image editor, including scanner-bed detection and drawing by
description; subclips and segment groups; audio effects with measure-then-advise;
volume over time as named shapes; multicam sync and switching; export presets
that say what they will crop before they run; the preferences window; and shot
descriptions, so the footage itself says what is on screen.

**Every command in the registry is wired**, and a test keeps it that way by
reading the interface sources and checking that every menu item reaches a
handler. Commands that are designed but unbuilt are *not* listed in the
registry - an entry there feeds `F1`, the palette and the keymap, so one with no
handler is a key that lies. They live in
[docs/ROADMAP.md](docs/ROADMAP.md) with their keys reserved.

What a finished editor still wants is listed in [docs/AUDIT.md](docs/AUDIT.md),
along with an honest grade of the codebase.

A **Windows or Mac client** would be a second front end over the same Core - GTK
stays the Linux one. The options and the recommendation are in
[docs/CLIENTS.md](docs/CLIENTS.md). **The deciding spike is written** -
`spikes/AccessibleVideoEditor.WpfSpike`, a drawn four-track scrubber with an
`AutomationPeer` tree and an announcer with real priorities - and it has never
been run. Nothing else gets built until NVDA and JAWS have read it.

See [docs/MANUAL.md](docs/MANUAL.md) for how it works,
[docs/AUDIT.md](docs/AUDIT.md) for an honest state of the codebase,
[docs/SETTINGS.md](docs/SETTINGS.md) for what lives where, and
[docs/ROADMAP.md](docs/ROADMAP.md) for what is coming.

## Build and run

Requires .NET 10 SDK, and GTK 4.14 or newer for `gtk_accessible_announce`.

    dotnet build
    dotnet test
    dotnet run --project src/AccessibleVideoEditor.Gtk

## Accessibility

GTK4 via GirCore, confirmed working with Orca. The application never synthesises
speech: announcements go through `gtk_accessible_announce`, so Orca speaks them
in its own voice, at its own rate, with its own interrupt behaviour.

Every view is a native `GtkListBox` or `GtkTextView`, so the accessibility tree
is real rather than constructed.

Avalonia was tried first and abandoned — Orca could not see the window at all,
not even its title, while the AT-SPI stack was verified healthy. See
ARCHITECTURE.md for why GTK rather than Avalonia or Qt, and what it costs.

Keys:

| Key | Action | |
|---|---|---|
| `Ctrl+1`–`Ctrl+6` | Go to a view | timeline, tracks, transcript, media, stream, images |
| `Ctrl+,` | Preferences | speech, saving, new-project defaults, devices, tools |
| `F6` / `Shift+F6` | Next / previous view | |
| `Tab` / `Shift+Tab` | Next / previous edit point on any track | Reaper's transient key |
| `Up` / `Down` | Move between rows — tracks | native list navigation |
| `Left` / `Right` | Move along the timeline by the current step | |
| `Ctrl+Left` / `Ctrl+Right` | Previous / next segment start on this track | |
| `Shift+,` / `Shift+.` | Start / end of the current segment | |
| `-` / `=` | Zoom, which is the same control as step size | also `Ctrl+Up`/`Down` |
| `Home` / `End` | Start / end of the programme | |
| `S` | Split at cursor | Reaper |
| `Delete` / `Shift+Delete` | Ripple delete / lift | inverted from Premiere, see ARCHITECTURE |
| `Shift+E` | Enable or disable segment | Premiere |
| `Ctrl+J` | Heal a split | |
| `Alt+[` / `Alt+]` | Trim head / tail to cursor | |
| `Ctrl+Z` | Undo | |
| `F12` | Where am I | also `Ctrl+;` |
| `F1` / `F2` / `F5` | Help / render / arm track | Shift and Ctrl stack each key |
| `F8` / `Ctrl+F8` | Describe this frame / describe every shot in this take | |
| `Ctrl+Shift+Left/Right` | Previous / next shot change | a cut you cannot otherwise find |
| `Ctrl+Shift+R` | Revert to the last save | autosave writes beside the project, not over it |
| `Ctrl+T` | New track | |
| `Ctrl+C` / `Ctrl+X` / `Ctrl+V` | Copy, cut, paste — including between tracks | |
| `Applications` / `Shift+F10` | Context menu, varies by view | |

In the **track editor**, plain letters are track controls — `M` mute, `S` solo,
`L` lock, `N` rename, `Delete` remove the track. Nothing there is a text field,
so single keys are safe.

In the **timeline**, `Ctrl+A` selects the segment under the cursor and
`Ctrl+Shift+A` the whole track; a marked range wins over the segment for every
verb, and which one is being acted on is always spoken first. `N` toggles
snapping and `Ctrl+Alt+R` cycles ripple mode — both say what they now *do*, not
just what they are called, because a mode you cannot see has to be announced.
`Ctrl+Shift+G` groups a run of segments under a name, `Ctrl+Alt+E` treats the
sound, `Ctrl+Alt+A` shapes change over time — volume on a segment, movement on an
overlay — and **a digit cuts to that camera angle**, the same gesture as a digit
cutting to a scene while streaming.

In the **media bin**, `U` names the marked range of a source as a subclip and
`M` makes a multicam group from your cameras.

In the **transcript editor** every structural key takes a modifier, because
unmodified keys are typing: `Ctrl+Shift+K` delete a segment, `Ctrl+Shift+E` cut
or restore, `Alt+Up`/`Alt+Down` reorder, `Ctrl+Enter` split.

Every default binding records **where it came from** — Premiere, Reaper, a
universal NLE convention, or invented. `AccessibleVideoEditor.Cli keys` prints them, and
`CommandRegistry.Invented` lists the ones with no precedent.

## CLI

The CLI is a client of the same core the GUI uses. It exists so the engine
stays testable headless, and so the `video-edit` Claude skill keeps a
command-line surface to drive.

    dotnet src/AccessibleVideoEditor.Cli/bin/Debug/net10.0/AccessibleVideoEditor.Cli.dll new <dir> [name]
    dotnet src/AccessibleVideoEditor.Cli/bin/Debug/net10.0/AccessibleVideoEditor.Cli.dll info <dir>
    dotnet src/AccessibleVideoEditor.Cli/bin/Debug/net10.0/AccessibleVideoEditor.Cli.dll export <dir>
    dotnet src/AccessibleVideoEditor.Cli/bin/Debug/net10.0/AccessibleVideoEditor.Cli.dll import <dir>
    dotnet src/AccessibleVideoEditor.Cli/bin/Debug/net10.0/AccessibleVideoEditor.Cli.dll keys

`import` reconciles a hand-edited `edit.md` back into `project.json`, keeping
element IDs intact — so editing in pluma stays a working escape hatch.

## Runtime dependencies

Present on the development machine; the code shells out to them rather than
bundling anything.

| | Used for |
|---|---|
| `ffmpeg` / `ffprobe` | Probing and rendering. CPU x264 — this build has no NVENC. |
| `libmpv.so.2` | Preview playback via the `edl://` protocol |
| SDL2 | Low-latency audio for earcons, scrub and the viewfinder tone |
| GTK 4.14+ | UI and `gtk_accessible_announce` for speech |
| `~/voice/venv` | Whisper `large-v3-turbo`, word-level transcripts |

## Layout

    src/AccessibleVideoEditor.Core       document model, time mapping, edits, undo, edit.md I/O,
                         timeline layout, streaming model, settings,
                         image analysis, drawing and the shape language
    src/AccessibleVideoEditor.Engine     ffmpeg and Whisper drivers, render cache, validation,
                         scene compositing, encoder, chat clients
    src/AccessibleVideoEditor.Playback   libmpv preview, EDL construction, music player
    src/AccessibleVideoEditor.Audio      audio output, earcons, viewfinder sonification
    src/AccessibleVideoEditor.Speech     the IAnnouncer contract and earcon vocabulary
    src/AccessibleVideoEditor.Vision     capture devices, face detection, drift monitoring
    src/AccessibleVideoEditor.Gtk        GTK4 user interface
    src/AccessibleVideoEditor.Cli        headless client
    tests/               Core tests
