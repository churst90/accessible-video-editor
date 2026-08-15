# Accessible Video Editor — architecture

A blind-first video editor. Grew out of the `video-edit` Claude skill
(`~/scripts/vid`), which stays alive as a client of the same core.

## The shape

Nothing in this design is novel except the interaction model, and the
architecture exists to protect that model:

    ┌──────────────┐   ┌──────────────┐   ┌──────────────────┐
    │ …Editor.Gtk  │   │ …Editor.Cli  │   │  Claude skill    │
    │ (GTK4/GirCore)│  │              │   │  (~/scripts/vid) │
    └──────┬───────┘   └──────┬───────┘   └────────┬─────────┘
           └──────────────────┼─────────────────────┘
                              ▼
    ┌──────────────────────────────────────────────────────────┐
    │ AccessibleVideoEditor.Core — model, time mapping, edits, undo, EDL I/O    │
    └──┬──────────┬──────────┬───────────┬──────────┬───────────┘
       ▼          ▼          ▼           ▼          ▼
    Engine     Playback    Audio      Speech     Vision
   (ffmpeg,     (libmpv)   (SDL2,   (IAnnouncer,  (camera, face
    whisper)               tones)   no synth)     detect, drift)

The GUI is a replaceable client: everything below `…Editor.Gtk` is
UI-agnostic, so a second front end is a leaf change rather than a rewrite.

**The application never synthesises speech.** `IAnnouncer` is an interface, and
the only implementation that speaks routes through `gtk_accessible_announce`
(GTK 4.14+), which hands the text to whatever screen reader is running. An
earlier speech-dispatcher announcer was deleted: spawning `spd-say` per
utterance was slow, and its "interrupt" killed a process that had already
exited.

## The document model

**One ordered spine plus anchored overlays.**

- The **spine** is an ordered list of elements — speech spans, clips, holes,
  pauses. Order *is* the edit. It defines programme time.
- Everything else — b-roll, titles, graphics, music, markers — stores a
  `TimeAnchor` (element ID + offset), never an absolute time.

That second rule is the one that makes ripple editing safe. Nothing downstream
holds a number an edit could invalidate, so overlays ride along for free. An
absolute-time model would break on every ripple, silently, and silence is the
failure mode this application can least afford.

### Why JSON is canonical and `edit.md` is an export

The text format has nowhere to put stable IDs, and stable IDs are load-bearing
for three separate things:

- **Undo** must say "element `e7f2` changed", not "line 14 changed" — line
  identity dies the moment a ripple shifts everything.
- **Overlay anchoring** needs to name an element that survives reordering.
- **Cache invalidation** keys on `(element, content)`, so reordering the video
  costs no re-render at all.

The cost is that `edit.md` stops being the source of truth. That is paid back
by making the export **round-trip**: `EdlReader.Read(text, existing)` matches
spans by source and timestamp and reuses the existing IDs. Hand-editing in
pluma and edits made by the Claude skill both reconcile cleanly. Without that,
the escape hatch would be a trap.

### Two clocks

- **Source time** — 11.360 seconds into `take1.mkv`.
- **Programme time** — where that lands in the finished video.

`TimelineMap` maps between them in both directions. This is the single
function the whole application leans on: F6 between the transcript and the
scrubber with the cursor intact *is* this map, applied one way or the other.

Span padding is folded into the map rather than applied at render time, so
programme time, source time and split points all agree. Otherwise a split
lands a frame or two away from where it was requested.

**A cut moment has no programme time.** `FromSource` returns null, and the UI
must announce "cut, not in programme" rather than snapping somewhere
plausible. Silent snapping is disorienting in a way it never is on screen.

## Interaction model

### The cursor belongs to the document

Not to a pane. F6 does not move the cursor to a matching timestamp — the
cursor never moved, the lens changed. Panes render `DocumentCursor`; none of
them owns it.

### The transcript pane is a text editor, and there is no syntax to check

The transcript pane edits **words**, not markup. You never type
`!broll from=0:12` — structure is changed by commands, not by typing
directives. So there is nothing to get syntactically wrong, and therefore no
syntax checker and no error messages to decipher.

That is the whole argument for the text editor over a list of spans: you get
familiar typing, selection and word navigation, and you pay nothing for it,
because the only thing free-form text can affect is caption wording. Structure
stays under command control where it can be validated up front and announced.

Syntax exists only in `edit.md`, which is the export and escape hatch — and
that gets validated on import, where a bad line can be reported once rather
than while you are typing.

### What is under the cursor

Every cursor move announces whether the focused track has content there:
`"0:12.4, blank"` or `"0:12.4, title, Cody Hurst"`. One word for the kind,
because at navigation speed every syllable is latency. `TrackProbe` computes
it; verbosity decides how much of it is spoken.

This replaces the glance that tells a sighted editor a track is empty here and
busy there. Moving Up and Down through tracks at a fixed time reads out the
vertical slice of the edit.

### Navigation is structural

| Key | Action |
|---|---|
| `Up` / `Down` | Focus track above / below |
| `Left` / `Right` | Move by the current granularity, announcing |
| `Ctrl+Up` / `Ctrl+Down` | Change granularity |
| `Ctrl+;` | Full position readout |

Granularity runs `frame → tenth → second → word → element → boundary →
marker`. This is the same idea as Orca's navigation levels, not a
pixel-denominated slider.

Left/Right announces **only what changed** — full detail on every press makes
the timeline too slow to move through. Detail is on demand.

**Audio scrub** plays a blip of the real audio wherever the cursor lands. At
word granularity you hear the word. Reading timestamps aloud is not how anyone
finds a cut point.

### Three deletes

| Action | Key | Effect |
|---|---|---|
| Ripple delete | `Delete` | Removes the range, closes the gap |
| Lift | `Shift+Delete` | Replaces the range with silence of equal length |
| Disable | `Ctrl+D` | Non-destructive, stays in the document |

Collapsing these is how people lose work. Lift is the one that gets forgotten
and then desperately needed, the moment music or a synced demo is involved.

### Split before selection

`S` splits at the cursor; `Shift+S` splits every track. `[` and `]` set a time
selection as a secondary tool.

Splitting is primary because **a split leaves a boundary you can navigate back
to** and a selection leaves nothing. When you cannot see the screen, persistent
objects beat invisible ranges every time.

In the transcript pane a text selection *is* a time selection — same object,
two renderings.

### Holes

`!hole dur=5 "explain the order panel"` reserves space to fill later. It shows
in the To-Do pane and **blocks the master render** as a lint error, so
structure-first editing cannot ship a gap by accident.

### Cards: one concept for title screens and lower thirds

A **card** is a composed screen — a background plus text and image layers. The
same composition serves two roles depending on where it sits:

- On the **programme track** (`CardElement`) it is a full screen and narration
  stops for it: a title card, a section break, an end screen.
- On the **graphics track** (`CardItem`), with a transparent background, it
  composites over the video below: a lower third is exactly this.

One concept, one editor, two placements — so "how do I make a title screen" and
"how do I add a lower third" have the same answer.

**Layout defaults to a stack**, not a grid. Placing a heading, a subheading and
a logo on a grid individually is fiddly; stacking them and letting the layout
space them is one decision instead of three. Grid placement is there when you
deliberately want something off-centre.

**Templates matter more here than in a visual editor.** Sighted users nudge
things until they look right; that feedback loop does not exist without sight,
so the fast path has to be a composition that is already correct — legible
sizes, title-safe margins, sensible hierarchy — which you only have to fill in
with words.

### The four panes

    Media bin  →  Tracks  →  Timeline  →  Transcript

The order is the direction work flows: media comes in, becomes tracks, becomes
a timeline, reads out as a transcript. `Tab` moves rightwards, `Shift+Tab` back.

Splitting **Tracks** (headers: name, arm, mute, solo, lock) from **Timeline**
(content lanes) is what makes `Delete` unambiguous — in the Tracks pane the
focused thing is a track, so Delete deletes a track; in the Timeline pane it
deletes content. It also lets plain letters be reused safely: `M` is mute on a
track header and marker on the timeline, and those panes never overlap.

### The drawn timeline

`AccessibleVideoEditor.Core.Timeline.TimelineLayout` turns the project, the map and the cursor
into blocks, ruler ticks, a playhead and a selection band - **plain numbers, in
Core, tested without a window.** `AccessibleVideoEditor.Gtk.TimelineCanvas` does nothing but
paint them.

That direction is the whole point. The accessible model was built first and the
picture is derived from it, so the drawing can never become the source of truth
about what the timeline contains; the canvas takes no focus, answers no keys,
and reports itself to the accessibility tree as `presentation`. The header list
beside it is still the thing you interact with.

Two consequences worth keeping:

- **Zoom is the step size.** `TimelineZoom` derives pixels-per-second from the
  same `Granularity` the arrow keys use, so a sighted zoom and a blind step size
  cannot drift apart.
- **Lane geometry comes from the real header rows.** The front end passes
  `LaneSlot`s measured from GTK rather than computed from the CSS, because a
  lane that does not keep step with its own header is a picture that contradicts
  the speech.

`AccessibleVideoEditor.Gtk.Theme` holds one palette used by both the CSS and the Cairo drawing,
so a lane and its header are the same colour by construction.

Pane order, names and **empty states** live in Core, not the UI, so they are one
definition and can be tested. An empty pane that says nothing is
indistinguishable from a broken one — and with no project open, every pane says
"no project loaded" rather than "empty timeline", which would imply a project
exists.

### Placement is a numpad grid

3×3, not 4×4:

- An even grid has **no centre cell** — the most common placement becomes
  unaddressable.
- 3×3 *is* the rule-of-thirds grid video composition already uses.
- The numpad maps 1:1.

Precision comes from a second keystroke selecting a sub-cell (81 positions),
then 1% arrow nudges. **The anchor is derived from the cell** — a graphic at
cell 7 anchors top-left so it grows inward — which is what stops corner
placements drifting off-canvas when text length changes.

## Preview: three fidelity tiers

1. **Live** — mpv plays the decision list via `edl://`. Instant, no encode.
   Real editors do not render to preview and neither does this. What makes
   the transcript editor feel alive.
2. **Draft** — background re-render of dirty segments at 540p. Needed because
   transitions, titles and ducking cannot be faked in playback.
3. **Master** — 1080p plus `captions.srt`, on demand.

Segment renders are content-hash cached, so changing one line re-renders one
segment.

## Speech is one channel, and the application does not own it

`IAnnouncer` is an interface with one speaking implementation, and it routes
**through the screen reader** — `gtk_accessible_announce`, with a priority per
message. The original design had the application speak for itself through
speech-dispatcher, on the reasoning that fast dynamic feedback needs to
interrupt itself and prioritise in ways an announcement channel does not offer.

That was tried and reversed. Speaking directly meant two voices talking over
each other, and the "interrupt" killed a process that had already exited. What
the reasoning actually pointed at was that **fast feedback should not be speech
at all**: earcons, the sonified viewfinder and the audible meter carry it now,
they are faster than any voice, and they do not compete with Orca for the
channel. Words are kept for the things that need words.

`Progress`-priority messages never queue. While moving fast, the newest
position is the only one worth hearing.

## The viewfinder

`ViewfinderSonifier.Evaluate` is a pure function from framing error to tone, so
it is testable and tunable without a camera in the loop. Three perceptually
orthogonal channels:

- **Pan** — horizontal offset, double-encoded into pitch because pan is
  unreliable on laptop speakers.
- **Pitch** — vertical offset from the target eyeline.
- **Beep tempo** — distance, parking-sensor style.

Framing targets the **eyeline at the upper third**, not the centre. Centring a
face vertically is bad framing and a tool that trains you into it is worse than
no tool.

**On target, the tone stops.** A tone playing through the whole take is
unusable; a "locked" chime followed by silence is what lets you start talking.

`DriftMonitor` logs framing and exposure problems during a take, so a recording
arrives carrying "out of frame from 2:10 to 2:24" instead of that being
discovered at review.

## Arm checks

Arming a track means three things at once: it is the record target, it binds
the capture device, and **it runs the signal check** — non-silent microphone,
non-black frames, disk space. Recording an hour into a dead device is the
classic disaster and it is entirely preventable.

## Decisions taken, and what they cost

| Decision | Cost accepted |
|---|---|
| Personal-use build, Linux first | No installers or bundling; device access stays behind an interface so cross-platform is a leaf change later |
| Keep the torch Whisper venv | Not portable, but ~20× realtime on the 5070 and zero packaging work |
| Shell out to system ffmpeg | Keeps the x264 GPL question off the table until there is something to distribute |
| `double` seconds, not rational frame time | Simpler and matches ffmpeg and Whisper; can drift at boundaries on non-integer frame rates |
| Snapshot undo, not inverse commands | More memory per step; cannot drift out of sync with the model |
| mpv in its own window | No embedded video surface in v1; removes the fiddliest toolkit problem and costs the primary user nothing |

## The toolkit, and how it was settled

**Avalonia was tried and failed.** The plan was Avalonia by default, with GTK as
the fallback if its Linux AT-SPI bridge proved too weak — the bridge being the
least mature of its three backends, and Orca the primary target. A four-track
scrubber was built as a spike and run under Orca. Orca could not see the window
at all: Insert+T reported "could not find current location" while the AT-SPI
stack was independently verified healthy. That is not a gap to work around, so
the Avalonia head was deleted.

**GTK4 through GirCore is the UI**, and Orca reads it correctly. The cost is the
one the original analysis named, and it has been accepted rather than solved:

> GTK does not map to UIA on Windows or to NSAccessibility on macOS, so a GTK
> build is excellent on Linux and unusable with NVDA, JAWS or VoiceOver.

So this is **Linux-first**, and Windows or macOS means a second front end over
the same Core rather than a recompile. That is a bounded piece of work — Core,
Engine, Audio, Speech, Vision and Playback are all UI-agnostic and the whole
interaction model lives in `CommandRegistry` — but it is a real one, and no such
front end exists today.

### Speech routing

There is one route: `gtk_accessible_announce`. An earlier design had a
`SpeechRoute` setting (`ScreenReader` / `Direct` / `Auto`) so rapid feedback
could bypass the screen reader through speech-dispatcher. It was removed with
the speech-dispatcher announcer and **the setting no longer exists**. Earcons
and the sonified meters carry the rapid feedback instead, which is better: a
tone is faster than any speech route and does not compete with Orca for the
channel.

## Risks

**The scrubber's feel is unproven.** The granularity model and announcement
design have no prior art to copy, and they decide whether this is a joy or
unusable. Prototype the audio-scrub feel early and expect to throw the first
version away.

## Milestones

| | Milestone | Why here |
|---|---|---|
| M1 | Core, EDL round-trip, time mapping, mpv EDL playback | Headless and testable before any UI |
| M2 | Panes, F6, cursor, granularity, audio scrub, announcements | **First point the timeline is navigable by ear** |
| M3 | Transcript pane, three deletes, holes, live preview | Now it is an editor |
| M4 | Viewfinder, arm checks, recording, drift log | **Independently useful before M5/M6 exist** |
| M5 | Overlays, numpad placement, collision warnings, describe-frame | The Claude-backed differentiator |
| M6 | Master render, captions, publishing | Mostly porting existing `vid.py` logic |
