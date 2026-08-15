# Accessible Video Editor

An accessible video editor. Edit by transcript, navigate by ear, frame the shot
by tone.

Built for a blind editor as the primary user rather than as an accessibility
layer over a visual timeline — which is why the timeline model, the navigation
model and the viewfinder all look different from a conventional NLE.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the design and the reasoning behind
it.

## Status

Usable end to end: import, cut, card, render — and stream.

**Built:** the document model and programme/source time mapping; three deletes,
split, heal, trim, retime, holes, cards and the clipboard; the `edit.md` round
trip; five views with a persistent status line; the transcript editor; playback
and audio scrub; recording, takes and the audible VU meter; the card editor;
fades, transitions and rendering; frame description and quality analysis;
stills and Ken Burns; the drawn timeline with waveforms; the streamer view with
scenes, sources, multi-destination streaming and chat on Twitch, YouTube and
Facebook; and the image editor, including scanner-bed detection and drawing by
description.

**Not built:** a settings view, project save and open, markers, and the command
palette. Every unwired command says what it will do and roughly when, rather
than doing nothing.

See [docs/MANUAL.md](docs/MANUAL.md) for how it works,
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
| `Ctrl+1`–`Ctrl+6` | Go to a view | timeline, tracks, transcript, media, record, stream |
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
| `Ctrl+T` | New track | |
| `Ctrl+C` / `Ctrl+X` / `Ctrl+V` | Copy, cut, paste — including between tracks | |
| `Applications` / `Shift+F10` | Context menu, varies by view | |

In the **track editor**, plain letters are track controls — `M` mute, `S` solo,
`L` lock, `N` rename, `Delete` remove the track. Nothing there is a text field,
so single keys are safe.

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
    src/AccessibleVideoEditor.Speech     announcer and platform speech backends
    src/AccessibleVideoEditor.Vision     capture devices, face detection, drift monitoring
    src/AccessibleVideoEditor.Gtk        GTK4 user interface
    src/AccessibleVideoEditor.Cli        headless client
    tests/               Core tests
