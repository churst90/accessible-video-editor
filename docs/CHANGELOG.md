# Changes

Newest first. Dates are when the work landed.











## The viewfinder, project save and open, and the rename

### Renamed to Accessible Video Editor

Everything: eight project directories, every namespace, the solution, the window
title, the documentation. `ViDeo` appears nowhere in the code, projects or
solution.

### The viewfinder

- **Face detection, not recognition.** It says "there is a face there", never
  "that is Cody". The test is on **chrominance rather than red-green-blue**,
  because the usual RGB skin rules are tuned on pale skin and quietly fail on
  everyone else. Asserted across five skin tones from very pale to very dark.
- Shape rules out what colour cannot: a wooden door sits inside the skin band,
  so anything much wider than it is tall is refused. The larger of two faces
  wins, so somebody walking past behind you does not become the subject.
- **One long-lived ffmpeg at 160 by 120, twelve frames a second**, read frame by
  frame - not stills grabbed per tick. Opening a camera takes the best part of a
  second, so stills would answer a second after you moved and you would correct
  against stale information.
- **A tone panned to where you are, pitched to how far up, ticking faster as you
  get too close - and silence when you are framed.** Silence is the target, so
  you stop moving when the sound stops rather than interpreting anything.
- The words are held back unless the guidance changes or six seconds pass. A
  viewfinder that says "move left" four times a second is one you turn off.
- **`F8` is the talking viewfinder**: what is actually in shot, which is a
  different question from where you are in it.
- The camera opens only on an explicit key, says so before it does, and closes
  on Escape from anywhere.

### Project save and open

- `Ctrl+S`, `Ctrl+Shift+S`, `Ctrl+O`, `Ctrl+N`, and a recent list.
- Every edit marks the project unsaved; opening or starting a new one asks
  before discarding, with **No focused**.
- The round trip is tested on a project that has actually been edited: mute,
  hide and disable come back as three different things, a transition keeps its
  sound and its length, track levels survive, custom transitions survive, and
  **element ids are stable so anchored overlays still point where they did**.

### About

`Ctrl+F1`. Version, credits, and donations - Cash App `$churst90`, with named
slots for Bitcoin, Ethereum and Monero that say they are not set yet rather than
omitting them silently. The text is selectable, so an address can be copied
rather than transcribed by ear.

### Also

- The whole keymap on `Shift+F1`, grouped.
- Transitions: type, length, sound on the boundary, audition, and your own saved
  by name. **No automatic ducking anywhere** - `Shift+G` sets any track's level.
- Six commands that were in the core with no way to reach them are now wired:
  set and audition transition, speed, insert hole, delete track, verbosity.
- **`docs/AUDIT.md`**: what is in the code but not in the interface, the open
  bugs and design issues, a comment-density measurement and a grade, and what a
  working editor still wants.

## Per-channel levels, and Phase 13 is finished

- **`:` opens levels per channel** — the only thing that reaches a cast the
  temperature control cannot. Temperature moves the picture along one axis;
  a yellowed page is off in a direction that axis does not pass through.
- **`"` says which way the colour is pulling** as a direction rather than three
  numbers. A cast is invisible to a brightness histogram, so colour is measured
  separately on its own small copy.
- **Auto colour levels** stretches each channel to its own range. It rests on
  the grey-world assumption, so it says what it did and is undoable — it is
  wrong for a picture that really is mostly one colour.
- **`W` while sweeping balances on the pointer.** The eyedropper, without
  pointing: sweep to something that ought to be grey and the correction is
  worked out from there. It refuses a spot that is too dark, blown out, or
  already neutral. This is where the sonified pointer earns its keep — it was
  built to answer "what is over there" and turns out to be the control a white
  balance needed.

### Fixed

- The per-channel filter was emitting `colorlevels:rimin=…` with the first
  equals missing, which ffmpeg rejects outright. The string test passed anyway
  because a malformed filter still contains every substring you would think to
  check; the render test caught it. That test now asserts the whole prefix.
- `Shift+;` and `Shift+'` send a colon and a double quote rather than the
  unshifted key with a modifier, so the guarded cases were unreachable. Both
  keyvals are now named.

## Levels, and a folder at a time

- **Levels without a graph.** A levels curve is a picture of five numbers, so
  `;` offers the numbers: black point, white point, shadows, midtones,
  highlights — each nudged and read back in units that mean something.
- **Auto levels** finds where the picture actually starts and stops, ignoring
  the half percent at each end that is noise, and **says the numbers it chose**
  so its answer can be adjusted rather than merely accepted. It warns when the
  stretch is big enough to band, and refuses a picture that is all one tone
  rather than stretching it into nonsense.
- **`'` reads the histogram as five numbers** with the shape said as a sentence
  first — "bunched in the shadows", "almost all midtones, so it will look flat".
  That is what the curve was drawn on top of.
- **`B` runs the whole thing over a folder.** The corrections travel; the
  geometry is measured per picture, because a photograph lands somewhere
  different on the bed every time — carrying one crop rectangle across a hundred
  files is how a batch ruins them all in one keystroke.
- The batch says what it will do, confirms, counts out loud as it goes, survives
  a file it cannot read, and **refuses to overwrite the originals**.
- 26 more tests. The batch ones use real scans with the photograph in a
  different place in each, and check that both come out the size of the
  photograph rather than the size of the bed.

## The pointer you can hear, colour correction, and one card model

- **`G` sweeps the picture with a pointer you can hear** — panned to where it
  is, pitched to how far up. Up is high, which is the one mapping nobody has to
  learn. It speaks only when it crosses into a new cell, because two numbers per
  press is unusable at speed and silence is unusable at all.
- **Colour correction by name**: brighter, warmer, punchier, lift the shadows —
  each a nudge, each said back in stops and kelvin rather than as a slider
  position.
- **`Shift+V` measures the picture and suggests the fix in the same words the
  commands are called**, so the advice can be acted on by pressing the thing it
  just named. This is the half of grading that normally happens by looking.
- Exposure goes through **gamma rather than brightness** — adding brightness
  shifts the whole picture and flattens it; gamma lifts the middle and leaves
  black as black, which is what "a third of a stop" means.
- **Cards are shared with the video editor.** `Shift+A` puts one on a photograph
  and opens the same card editor the timeline uses. Shapes were deliberately
  *not* merged into cards: the shape language is geometry, cards are titles and
  logos, and folding either into the other would make both worse.
- **`I` sends the picture into the project**, so a scan that has just been
  straightened can go on the timeline without leaving the application.
- Colour changes are undoable like everything else.
- 24 more tests, six of which render and read the pixels back: a filter string
  that looks right and is ignored by ffmpeg is indistinguishable from one that
  works, right up until it matters.

## Undo for pictures, and text that actually renders

- **Undo in the image editor**, on whole-document snapshots. Undoing says what
  it undid *and what the picture is now* — without the second half you know
  something moved but not where it landed. `U` asks what would be undone without
  undoing it, and a refused edit is not recorded.
- **`Ctrl+Z` now means "take back the last thing I did here"** — the picture in
  the image editor, the video everywhere else. One key, right target.
- **Text shapes are drawn at export.** Core has the arithmetic but no fonts, so
  text is described and listed there and rendered by ffmpeg — real hinting, and
  it sits above the painted shapes. Its outline is chosen from its own
  brightness: light text gets a dark edge, dark text a light one.
- The font finder is now shared between the video renderer and the image editor
  rather than existing twice — one of two copies drifting would mean text
  disappearing from renders and nowhere else.
- 20 more tests, including two that render text to a real file and read the
  pixels back: a split like "Core describes it, ffmpeg draws it" is exactly the
  kind that works in principle and silently produces nothing.

## Phase 13 — the image editor

- **Its own view, `Ctrl+6`.** Appended rather than slotted beside the media bin,
  which would be the tidier order: renumbering a view that has already been
  learned costs more than the tidiness is worth.
- **Nothing is destructive.** Crop, resize, rotation and every drawn shape are
  decisions held in a document; the file is untouched until export.
- **Opening measures first.** Size, ratio, orientation, print size at its dpi,
  how much empty paper there is and which side it is on, and how far the picture
  is rotated.
- **The scanner-bed case works end to end**: the photograph is found inside the
  bed, several photographs are found separately, `Shift+F` straightens and crops
  in one and `Shift+S` splits them into a file each. The background is measured
  rather than assumed, and dust is not mistaken for a photograph.
- **Resizing announces the new size on every press**, with the shape locked by
  default, presets named by what they are for, and a warning before enlarging
  past the original.
- **Cropping is named, then nudged** — crop to the picture, to a ratio anchored
  on a cell, then one edge at a time with the amount cut said each press.
- **Drawing is a language, not a gesture**: `circle at centre, radius 20 percent,
  white`. Every shape is a listed layer that reads back as the sentence that
  would create it and reports how much of the picture it covered.
- **Colours are named before they are valued**, with a weighting that does not
  confuse a dark blue with a dark green.
- A **PNG writer** in Core, about forty lines, so drawing needs no image library
  in a video editor's dependencies.

### Verified rather than assumed

Six end-to-end tests build real scans with ffmpeg and run the whole path: a
photo on a bed found in the original's coordinates, a 4-degree tilt measured, a
crop-and-resize whose output is probed back, a drawn shape confirmed by reading
the exported file, and two photographs split into two files.

### Fixed while building it

- The skew estimator returned an arbitrary angle for a picture with no
  measurable tilt. It now returns zero unless the best angle genuinely beats
  straight — offering to straighten something already straight is worse than
  saying nothing.
- The rotation filter negated its angle, so straightening would have tilted a
  scan further the wrong way. ffmpeg rotates clockwise for a positive angle,
  which is the same convention the document uses.
- A shape read back with the full placement description rather than the cell
  name, so it did not match the sentence that would create it.

## Phase 12 finished — all three platforms, and application settings

### Settings

- **Application settings and a secret store**, at `~/.config/video/`. Stream
  keys were in the project; they are now in settings, and their *values* are in
  a separate owner-readable-only file — settings can be copied or pasted into a
  bug report, and a stream key cannot. Documented in `docs/SETTINGS.md`, with
  the rule for deciding where any setting belongs.
- Nothing in the secret store is ever spoken. `Shift+K` reads back which keys
  are saved and never what they are, and a test asserts it.
- Also holds: what a new project starts from, verbosity and earcons, the chat
  rate limit, preferred devices, and where the tools are.

### All three chat platforms

- **YouTube**, over the Data API. An API key alone gets a working chat pane —
  far less to set up than the OAuth application posting needs, so the two are
  separate. Obeys the polling interval YouTube asks for.
- **Facebook**, over the Graph API, polling live comments and dropping
  duplicates.
- **Twitch moderation over Helix, not IRC** — Twitch removed `/ban` and
  `/timeout` from IRC in 2023, so anything still sending them appears to work
  and does nothing.
- **Capabilities are modelled explicitly.** Facebook hides rather than deletes
  and blocks rather than timing out; no platform allows pinning from outside its
  own app, and Twitch's announcement is offered instead. `Shift+C` reads what is
  possible where you are.
- Banning confirms first, with **No focused**.

### Music and health

- **Playlists**: play, stop, next, previous, shuffle, three repeat modes. What
  is playing and what is next are announced together. Plays locally rather than
  through the encoder, because changing a source mid-encode is a hitch every
  viewer sees — and starting it warns when nothing in the scene is capturing
  desktop audio.
- **Stream health from the encoder's own statistics**: dropping frames, falling
  behind real time, and recovery, each said once with an earcon rather than on
  every sample. `H` asks; `Shift+F9` meters the live mix with the same meter as
  the track editor.

### Fixed

- Two **View menu items were silently missing** — they referenced command ids
  (`pane.next`, `pane.previous`) that do not exist, and the menu builder skips
  unknown ids. Found by extending the wiring audit to check menu ids against the
  command registry, not just handlers.
- Removed the placeholder YouTube and Facebook chat stubs now that both are
  real.

## Phase 12 — streaming

- **The streamer view is built**: scenes, sources, preview and one chat area per
  platform, with **`Ctrl+`` `** cycling between them and `Ctrl+Shift+`` `
  going back.
- **Several services at once, one encode.** ffmpeg's `tee` muxer sends the same
  encoded stream to every destination; `EncoderSettings.ForTargets` derives the
  settings from the strictest one and says which service set the limit.
  `onfail=ignore` keeps Twitch alive when YouTube drops.
- **A stream key is never read back** — not in speech, not in the status line,
  not in a log. Covered by a test that asserts the key appears in nothing that
  gets spoken.
- **Scenes hold sources by reference**, so the same camera is full frame in one
  scene and a corner inset in another without being two cameras. Placement uses
  the card editor's 3 by 3 language.
- **A digit cuts to that scene**, announced with what is now live and warning
  when the scene has nothing showing or no audio.
- **Music loops by default** — a song over a static picture was the case asked
  for, and a bed that stops when the track ends is not what anyone meant.
- **Twitch chat reads with no account**, over anonymous IRC. Messages are
  categorised — named, first-timer, question, moderator — each with its own
  earcon, and a busy chat is rate-limited to a count rather than being read
  line by line. Scrolling back stops new messages interrupting.
- **YouTube and Facebook say what they are waiting for** rather than sitting
  silent, which would look exactly like a chat nobody is talking in.
- `P` reads the preflight list; `Ctrl+Shift+L` goes live and is the only command
  in the view that is not a single letter.
- 57 new tests. One of them found a real bug: `HasAudio` claimed screen captures
  carry sound, which would have built a filtergraph referencing a stream that
  does not exist — failing at the moment of going live rather than at setup.
- Two new earcons for on air and off air, shortened to fit the existing rule
  that no earcon runs longer than a fifth of a second.

### Roadmap

- **Phase 13, accessible image editing**, written up in full: knowing what you
  have before touching it, the scanner-bed case (a 4 by 6 photo dropped sideways
  with white around it, found and straightened and cropped by report rather than
  by dragging), resizing that announces dimensions as they change, cropping by
  name then by nudge, and replacing freehand painting with a shape language
  where every shape is a listed, describable layer.

## Phase 11 — the visual timeline

- **The timeline is drawn.** Track headers on the left, lanes on the right,
  ruler across the top, playhead through all of it. Segments are blocks scaled
  to their duration and labelled with their own words.
- **Waveforms** on audio and programme lanes. Extracted at 8 kHz in the
  background, cached per source keyed by path, size and modification time, and
  never waited on - a block draws solid until its peaks arrive.
- **The marked range is visible**, as a band across every lane with solid
  edges so a very short selection is still findable.
- **Transitions** are hatched across the join they actually cover, and **fades**
  are drawn as the wedge they are rather than as a marker at the edge.
- **Mute, hide and disable look like three different things**, and each is
  paired with something other than colour.
- **Zoom is the step size.** `TimelineZoom` derives pixels-per-second from the
  same `Granularity` the arrow keys use, so the picture and the keyboard can
  never drift apart.
- The layout lives in `AccessibleVideoEditor.Core.Timeline.TimelineLayout` and is tested
  without a window - 30 new tests covering geometry, clipping, the ruler
  ladder, the playhead, the selection band, and following the playhead.
- The drawing **takes no focus and answers no keys**; its accessible role is
  `presentation`. The header list is still the thing you move through, and it
  still reads exactly what it read before.

### The look of the application

- **One palette**, in `AccessibleVideoEditor.Gtk.Theme`, consumed by both the CSS and the Cairo
  drawing so a lane and its header cannot end up different colours.
- Dark surfaces, rounded panes, text above a 7:1 contrast ratio, and a visible
  focus ring on everything focusable - which is how a sighted collaborator
  finds the row the speech is talking about.
- The status line has a surface of its own and monospaced digits, so the
  timecode does not jitter as it counts.
- Lane heights are **measured from a real header row** rather than assumed from
  the CSS, so a lane and its header line up whatever the theme or font size.

## Roadmap reordered

- The **visual timeline is now Phase 11** and streaming is Phase 12. The
  timeline is incremental and useful the day part of it lands; streaming is not
  usable until most of it exists.
- Streaming requirements written down properly: **chat accessibility as the
  centrepiece** (one unified YouTube/Twitch/Facebook feed, arrow through history
  while new messages arrive, filters with their own earcons, moderate from the
  keyboard), OBS-style scenes and sources with key-driven switching, playlists,
  and live monitoring through the existing audible VU meter.
- **Candidate features** table added: chroma key, colour correction, auto-reframe,
  audio repair, stabilisation, multicam switching, beat-synced cutting, speed
  ramps, proxies, burned-in captions, templates, export queue, snapshots — each
  with the note on what makes it usable without sight.

## 2026-08-13

### Playback fixes
- **Programme time and playback time are now translated, not assumed equal.**
  An EDL can only contain real media, but a programme also contains cards,
  holes and pauses. The card at the head of the demo occupied three seconds of
  programme and none of playback, so every seek was three seconds out - which
  is why Home then Space did not play.
- **`loadfile` is asynchronous**: a seek issued straight after it did nothing at
  all, silently. The player now waits for the file to open before seeking.
- Starting inside a card skips to the next real media and says so, rather than
  appearing to start somewhere else for no reason.
- **No more mpv window.** Both players are audio-only; with no embedded video
  surface, enabling video just made mpv open a window that stole keyboard focus
  mid-edit. Video preview belongs with the visual timeline.

### The accessible VU meter
- `Shift+F9` monitors input levels: a tick whose **pitch rises with the level**,
  with the zone name spoken only as it changes.
- Zones follow a real meter's thresholds; clipping doubles the tick rate because
  at that point it is an alarm rather than a reading.
- 2 dB of hysteresis, or a level sitting on a threshold reads out both zone
  names endlessly.
- Stopping reports the peak, which is what tells you a take nearly clipped even
  though it sounded fine.
- Levels are read from `parec` with RMS computed in-process rather than parsed
  from ffmpeg's log — a meter that lags behind your voice is worse than none,
  because you correct for a level you are no longer at.

### Inputs, outputs and multi-camera
- **Monitoring output is selectable** (`Ctrl+Shift+F5`) and separate from any
  input — recording from an interface while listening on headphones is normal.
  Applied to both playback and scrub so they are never heard in different places.
- **Per-track input channel** (`Ctrl+Alt+F5`): both, left only, right only. A
  two-input interface presents as one stereo source, so recording it whole puts
  the microphone on one side and silence on the other.
- **Every armed track records at once**, each to its own file, for multi-camera
  shoots. All devices are checked before any recording starts.
- Multi-angle recordings go to the media bin rather than becoming takes of each
  other — a second angle is separate footage, not another attempt at the line.
- Device listing now reports channel counts and enumerates outputs.

### Recording
- **The recording flow is built.** `R` or `Shift+F5`: signal check, spoken
  countdown, record, stop cleanly, and the result becomes a take on the segment
  at the cursor.
- The **signal check opens the device** — the only place that happens. A silent
  microphone refuses to record and says why; clipping warns but proceeds,
  because too loud is recoverable and silence is not.
- ffmpeg is asked to finish with `q` rather than killed, so a take is never left
  as an unplayable file.
- **Insert menu**: track, segment, card, lower third, graphic, b-roll, hole,
  marker, import.
- **Track type is chosen when a track is made** (`Ctrl+T`) and changeable after
  (`Ctrl+Shift+Y`). It decides what the track can record from, so guessing it
  would be guessing the most consequential thing about the track.

### Recording reorganised
- **The record view is gone.** Recording is per track, so it belongs in the
  track editor and the timeline; a separate view split one workflow across two
  places, and you would have had to leave the timeline to record into a hole.
- **A track's medium decides its inputs**: video tracks offer cameras, audio
  tracks microphones, image tracks nothing and cannot be armed.
- `Ctrl+F5` chooses the input for the focused track; it is stored on the track.
  An armed track announces its input.
- **The viewfinder is a mode** (`F9`), not a view — framing a shot is not
  editing, so you enter it and leave rather than being able to Tab into it.
- Views are now five: timeline, tracks, transcript, media bin, stream.

### Takes and devices
- **Takes**: recording into a segment again gives take 2, not a second segment.
  `T` cycles forward, `Shift+T` back. The original media becomes take 1
  automatically, so choosing a take never discards what was there, and anything
  anchored to the segment stays attached because only the media changes.
- Capture issues travel with the take that has them — a take that drifted out of
  frame is exactly the one you cannot hear when auditioning.
- **Device listing that never opens a device**: cameras from sysfs, microphones
  from `pactl`. Browsing cannot switch on a webcam light.
- Device names come from PipeWire descriptions rather than node names — "Arctis
  Nova Pro Wireless Mono" rather than
  "alsa_input.pci-0000_c1_00.6.HiFi__Mic1__source".
- Record view (`Ctrl+5`) lists what is available. `AccessibleVideoEditor.Cli devices` does the
  same headlessly.
- `F5` arms a track and reports what it found. **The signal probe is deliberately
  not run on arming** — it would open the camera, and that should not happen
  because a key was pressed.

### Playback and audio scrub
- Playback through libmpv's `edl://` protocol: the decision list is played
  directly, so nothing is encoded and an edit is audible immediately.
- `Space` play/pause, `J`/`K`/`L` shuttle, `Ctrl+Space` audition 1.5 seconds
  either side of the cursor.
- **Segments announce themselves as they pass** — sentence text, transitions,
  b-roll, cards — on boundary crossings only, never timecodes.
- **Audio scrub**: moving the cursor plays a fraction of a second of the real
  audio there. Separate audio-only player, so scrubbing never disturbs where
  playback is parked; each blip cancels the last.
- Missing media is reported rather than producing silence.
- A demo file with a tone stepping every eight seconds is generated at
  `~/.local/share/videoedit/demo/take1.mkv` so scrubbing is audible before any
  real footage exists.
- Forced `LC_NUMERIC=C` for mpv: it parses numbers with the C library, so a
  comma-decimal locale would turn a seek to 12.5 into a seek to 12.

### One command table
- Menu items, context menus and key handlers now all invoke through a single
  command table. They were three parallel systems, which is how **Rename**
  announced "not wired" from the context menu while `N` opened the dialog.
  Add track, mute, solo and lock had the same split.
- Unbuilt commands say what they will do and roughly when — *"the viewfinder is
  not built yet, phase 4"* — rather than a bare "not wired".
- `F3`, `F4`, `F7`, `F8`, `F9` are now handled; they were declared in the
  registry but no key reached them, so they did nothing at all.
- `Ctrl+;` works in every view, not only the timeline, as the reliable alternate
  to `F12` (which some desktops grab).

### Views
- Six views, one on screen at a time: `Ctrl+1` timeline editor, `Ctrl+2` track
  editor, `Ctrl+3` transcript editor, `Ctrl+4` media bin, `Ctrl+5` record view,
  `Ctrl+6` streamer view. `F6` cycles.
- **Views are announced by name, never by number.** "View 3" says nothing about
  where you are.
- Order is by how often you are in a view, not by how data flows — the timeline
  is where the work happens, so it is view 1.
- Record view added as a real slot (viewfinder, device selection, levels), so it
  is not retrofitted later.
- Status line lives outside the view stack: position, duration, step size,
  focused track. Structurally impossible for it to be a view away.
- Placeholder views are top-aligned. They were vertically centred, which read as
  a rendering fault.

### Keyboard
- Function keys restructured, one domain per key, stacked with Shift and Ctrl:
  `F1` help, `F2` render, `F3` find, `F4` quality, `F5` arm/record/device,
  `F6` views, `F7` to-do, `F8` describe, `F9` viewfinder, `F12` where am I.
- `Tab` is no longer a view switcher. In the timeline it moves to the **next
  edit point on any track** — the video equivalent of Reaper's
  move-to-next-transient.
- `Ctrl+T` adds a track. `N` renames one in the track editor (`F2` would be the
  convention, but `F2` is the render key here).
- Premiere's `Ctrl+M` export survives as an alternate to `F2`.

### Editing
- `Ctrl+C` / `Ctrl+X` / `Ctrl+V` wired, including between tracks. A paste that
  does not match the target track's medium is refused out loud.
- Crossing into a new segment announces its text; moving within one stays terse.
- `F5` arms and disarms the focused track.

### Transcript editor
- Now a real text editor. `Ctrl+Shift+K` delete a segment, `Ctrl+Shift+E` cut or
  restore, `Alt+Up`/`Alt+Down` reorder (the video reorders with it),
  `Ctrl+Enter` split at the caret, typing edits caption text only.
- Segments are addressed **by identity, not by time**, so a cut line can still be
  deleted, restored and moved despite having no programme time.
- Caption edits commit when the caret leaves the line, not per keystroke. A
  caption identical to the transcript clears the override so it keeps tracking.
- Line announcements carry position, in and out times and duration.

### Cards
- A card is a background plus text and image layers. On the programme track it
  is a full screen; on the graphics track with a transparent background it is a
  lower third. One concept, two placements.
- Five templates: title, section break, quote, lower third, end screen.
- `Ctrl+Shift+;` summarises a card — background, layout, and every layer with
  where it lands, in thirds ("upper left", "lower right").

### Fixed
- Transcript first line was blank: cards had no case in the transcript builder.
- Card text was missing from the cursor readout; cards and titles now carry
  their text, because "card" is useless in a video with six of them.
- `Ctrl+Left`/`Right` on an empty track claimed you were at the first or last
  segment. It now says "no segments on this track".
- Context menu was parented inside a scrolled window, which could position it
  off-screen where autohide dismissed it instantly.
- `Shift+,`/`Shift+.` interleaved segment starts and ends, so one press felt
  like two. Starts and ends are now separate movements.

## 2026-08-12

- Project started. C# on .NET 10.
- **Avalonia tried and abandoned**: Orca could not see the window at all, not
  even its title, while the AT-SPI stack was verified healthy.
- **GTK4 via GirCore confirmed**: full accessibility tree, and
  `gtk_accessible_announce` hands speech to Orca directly.
- Speech-dispatcher announcer deleted. Spawning `spd-say` per utterance was slow
  and its "interrupt" killed a process that had already exited — but the deeper
  problem was the design: an application should not duplicate the screen reader.
- Core model, timeline mapping, edit operations, `edit.md` round trip, keymap
  registry with recorded provenance.
