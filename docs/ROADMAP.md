# Accessible Video Editor — roadmap

Everything agreed, in build order, with the reasoning kept. See `MANUAL.md` for
how features behave and `../ARCHITECTURE.md` for why the model is shaped as it
is.

---

## Phase 1 — The transcript editor **[built]**

Done. The transcript pane is a real text editor: structural verbs by identity,
caption editing with deferred commit, and line announcements with timecodes.

### 1.1 Structural editing

One line is one segment. The same edit verbs as the timeline, resolved against
the shared cursor:

Every structural key takes a modifier, because unmodified keys are typing and
plain `Delete` has to stay character deletion:

- `Ctrl+Shift+K` — delete this segment (VS Code's delete-line)
- `Ctrl+Shift+E` — disable; the line stays, marked `[cut]`, and can be restored
- `Alt+Up` / `Alt+Down` — **move this line earlier or later; the video reorders
  with it.** This is the feature that makes text better than a timeline: you
  restructure a video the way you restructure a paragraph.
- `Ctrl+Enter` — split the segment at the word the caret is on
- Range selection then any verb — **[built]**. `EditTarget.Resolve` gives a
  marked range priority over the segment under the cursor, so Delete, Lift and
  the rest act on the selection when there is one. `Ctrl+A` selects the segment
  and `Ctrl+Shift+A` the whole track, so the common ranges do not need marking
  by hand.

Segments are addressed **by identity, not by time**, so a cut line can still be
deleted, restored and moved despite having no programme time at all.

### 1.2 Caption editing

Typing edits **caption text only**, never the cut. Announced on the first
keystroke in a line, because it surprises people.

- Commits when the caret **leaves the line**, not per keystroke, so the buffer is
  never rebuilt underneath a half-typed word
- A caption identical to the transcript **clears the override**, so it keeps
  tracking the transcript instead of silently freezing
- `Ctrl+Shift+C` — caption on/off per segment (a title card should not carry
  the narration's caption)
- Non-speech captions — `[keyboard clacking]`, `[music]` over a clip
- Editing a generated `[card: …]` line is discarded, saying so out loud

### 1.3 Line announcements **[built]**

Moving between lines announces position, in and out times, duration, then the
text. Timecodes are spoken, never written into the text — inline timestamps
would make the transcript unreadable as prose.

### 1.4 Tag convention

Non-speech segments read as `[card: …]`, `[clip]`, `[hole: …]`, `[pause 0.7s]`,
`[silence]`. The brackets stay: they distinguish *words that will be spoken*
from *a thing that is not speech*, and they are what makes the `edit.md`
round-trip unambiguous. Speech is never bracketed.

---

## Phase 2 — Views and the status line **[built]**

- One view at a time: `Ctrl+1` timeline, `Ctrl+2` tracks, `Ctrl+3` transcript,
  `Ctrl+4` media bin, `Ctrl+5` stream, `Ctrl+6` images **[built]**. There is
  deliberately **no record view** - recording is per track, so it happens in the
  track editor and the timeline.
- Views announced by name, never by number **[built]**
- `F6` / `Shift+F6` cycles for when numbers are not to hand **[built]**
- **`Tab` returns to normal within-view focus movement** **[built]**
- **Status line always present**, outside the view stack: position, duration,
  step size, focused track **[built]**
- `F1` reads the commands valid in the current view **[built]**
- Toolbar under the menu bar for sighted collaborators **[planned]**

A toolbar may sit under the menu bar for sighted collaborators. It is never the
only route to anything, and there are no modal tools to select first.

---

## Phase 3 — Playback **[built]**

- `Space` play/pause, `J K L` shuttle, `Ctrl+Space` audition, `Shift+Space` loop
- Driven by libmpv over its `edl://` protocol — playback of the decision list,
  so edits are audible immediately with no encode
- **Segments announce themselves as they pass**, on boundary crossings only:
  *"b-roll"*, *"transition, wipe left"*, *"card, Cody Hurst"*. Never timecodes.
  This is how you confirm an edit worked without stopping to inspect it.
- Verbosity: off / boundaries only / everything

---

## Phase 4 — Recording and takes **[built]**

- `Ctrl+F5` chooses the track's input; `F5` arms it **[built]**
- Arming binds the device; the **signal check runs when recording starts**, not
  when a key is pressed — probing opens the camera, and that should never happen
  as a side effect
- `R` records to the armed track, `R` again stops
- **Takes**: recording into the same segment again gives take 2, not a second
  segment. `T` cycles, announcing *"take 2 of 3, 4.1 seconds"*. Borrowed from
  Reaper; no video editor does this well, and it is exactly right for
  talking-head work where you say a line four times.
- Recording into a hole splices the new take in at that point
- Drift log: framing and exposure problems captured live, so a take arrives
  carrying *"out of frame from 2:10 to 2:24"*

---

## Phase 4b — Monitoring **[built]**

- Audible VU meter on `Shift+F9`, per track
- Monitoring output selection, separate from any input
- Per-track input channel for multi-input interfaces

Deliberately **not** doing Reaper's full input matrix ("interface ch 1 / ch 2 /
ch 1+2"). A single stereo input with an optional left/right pick covers the real
cases without a routing model nobody asked for.

---

## Phase 5 — The card editor **[built]**

Cards are created, edited and described. A card on the programme track is a
full-screen segment; the same composition placed on the graphics track with a
transparent background composites over the picture instead.

- A card opens in its own editor: a list of layers, one per row
- `Enter` on a layer edits its text; `Numpad 1-9` places it; a second numpad
  press picks a sub-cell; `Alt+arrows` nudge 1%
- Add text layer, add image layer, reorder layers (`Alt+Up`/`Down`)
- Background: solid colour, image, video, or transparent
- Switch between stack and grid layout, announced
- `Ctrl+Shift+;` summarises the whole card **[built]** — background, layout, and
  every layer with where it lands
- **Collision warnings**: refuse or warn when a layer overlaps the detected
  face, crosses the caption band, or leaves title-safe

Inserting a card inserts a **new segment** with its own duration.

---

## Phase 6 — Media, assembly, and audio detach **[built, less subclips and compound segments]**

- `Ctrl+I` import; the import **reports what it got** — resolution, frame rate,
  duration, and how many audio tracks and what they are
- Open a source's transcript, select sentences, `,` insert / `.` overwrite
- **Detach audio**: split a video segment's audio onto its own audio track,
  becoming two linked segments. Editing one affects both until unlinked.
  Needed whenever you want to keep someone's voice while cutting away from
  their picture, or to duck one clip's audio independently.
  (Until then, `atrack=` already selects *which* audio track of a source plays —
  0 mix, 1 microphone, 2 system audio.)
- **Subclips** — mark a range of a source and name it, so "the good intro" is a
  thing you can insert **[built]**. `U` in the bin names the marked range,
  `Shift+U` lists them, Enter inserts at the cursor. A duplicate name is refused
  rather than numbered: "good intro" and "good intro 2" are indistinguishable in
  a list read aloud, which defeats the point of naming them.
- **Compound segments** — collapse ten segments into one named thing. Very
  useful without sight: it turns a region into a single object you can move.
  **[built]** as a *grouping* rather than as nesting — see below.

### Why groups rather than nested clips

Every other editor makes a compound a container: the segments go inside it and
you open it to reach them. That would mean programme time had to be computed
through a tree, and everything that maps time — navigation, the transcript,
overlay anchors, the render plan, the EDL — would have to learn about depth.

A `SegmentGroup` instead records that a run of segments belongs together. It
buys the thing people actually want from a compound, *one object I can move*,
without moving a single invariant. It also keeps something nesting takes away:
the members stay individually reachable, and `Collapsed` only decides whether
navigation stops on them. A group is one object when you are moving it and ten
segments when you are fixing one, with no converting between the two.

---

## Phase 7 — Fades and transitions **[built]**

- **Transitions** live on the boundary *between* segments **[built]**
- **Fades** are per-segment: fade in, fade out, audio and/or video. A fade from
  black at the top of a video is a fade; a dissolve between two shots is a
  transition. Both are needed and they are not the same thing.
- Crossfade on the audio side, independent of the picture transition
- Audition a transition or fade without leaving the cursor

---

## Phase 8 — Rendering **[built]**

The largest single remaining port: ~1,400 lines of filtergraph building in
`vid.py`.

- Segment renders, concat, xfade, libass overlays, `sidechaincompress` ducking,
  `loudnorm`
- Three tiers: live playback → warm draft (background, incremental) → master
- Content-hash segment cache **[built]** — one edit re-renders one segment, and
  reordering costs nothing
- Holes block the master render **[built]** — `FfmpegRenderEngine` refuses a
  master while any hole remains, and counts them
- Export presets: YouTube 1080p, shorts 9:16, audio only **[built]** — `Ctrl+F2`.
  Each one **says what it will cost before it runs**: a vertical export crops
  about 68 percent of the width of every frame, and that is otherwise something
  you find out by watching the result. A preset changes only the final encode, so
  exporting the same edit twice re-renders the segments once
- Progress announced periodically, not on every tick

---

## Phase 9 — Analysis and advice **[built]**

All of this is possible with filters already present in the installed ffmpeg.

- **Quality report** (`F4`): exposure, clipped highlights, crushed blacks, white
  balance cast, contrast, saturation, focus (`blurdetect`), audio LUFS, true
  peak, clipping, noise floor
- **Shot matching** — *"take 3 is 0.6 stops darker and 400 K warmer than takes 1
  and 2"*. Invisible without eyes, and the thing that makes a video look
  amateur.
- **Auto-correct** — the analysis already yields the `eq`/`colorbalance`
  parameters
- **Filler-word removal** — strip every "um" and "uh" in one command. A text
  operation for us, given word timings.
- **Silence removal** with a threshold: *"removed 14 gaps, 47 seconds"*
- **Pace report** — *"190 words per minute here, 130 elsewhere"*
- **Describe this frame** (`F8`) — render the still under the cursor, read back
  what is actually there. The Claude-backed differentiator.

---

## Phase 10 — Stills **[built]**

**Rule: stills work everywhere video works.** Not a photo mode bolted on, just
no place where the application assumes motion.

Carries over unchanged: placement grid, cards, colour analysis and correction,
quality report, describe-frame, transitions, export. `zoompan` gives Ken Burns
so a still does not look dead. A slideshow is a sequence of cards with image
backgrounds.

Does not apply: transcript, retiming.

Photo *editing* — crop, straighten, exposure, batch export — is a real scope
expansion but the same accessibility gap exists there and nothing addresses it.

---

## Phase 11 — The visual timeline **[built]**

Track headers on the left, drawn lanes on the right - the layout every editor
uses, so a sighted collaborator already knows how to read it.

- Time ruler with an interval chosen so labels never collide at any zoom
  **[built]**
- Segments as blocks scaled to duration, labelled with their own words
  **[built]**
- The cursor as a playhead; the segment under it takes a bright border
  **[built]**
- Waveforms on audio and programme lanes, extracted in the background and
  cached per source **[built]**
- A marked range drawn as a highlighted band across every lane **[built]**
- Transitions hatched across the join they actually cover; fades drawn as the
  wedge they are **[built]**
- Mute, hide and disable each look different, and none relies on colour alone
  **[built]**
- Zoom is the step size - `Ctrl+Up`/`Ctrl+Down` changes both at once, so what
  is on screen and what an arrow key moves by cannot disagree **[built]**
- The view follows the playhead, moving only when it reaches the edge
  **[built]**

**Order held.** The layout is computed in `AccessibleVideoEditor.Core.Timeline.TimelineLayout`
from the same model the speech comes from, and is covered by tests that run
without a window. The drawing itself takes no focus and answers no keys: the
header list beside it is still the thing you interact with, so the picture can
never start dictating the interaction.

### The visual design **[built]**

One palette in `AccessibleVideoEditor.Gtk.Theme`, used by both the CSS and the Cairo drawing,
so a lane and its header are the same colour by construction rather than by
coincidence. Dark, with text held above a 7:1 contrast ratio, rounded panes, a
visible focus ring on every focusable thing, and the status line given a
surface and monospaced digits so the timecode does not jitter as it counts.

---

## Phase 12 — Streaming **[built]**

Four areas, and **`Ctrl+`` `** goes round them: scenes, sources, preview, then
**one chat area per platform**. Merging the chats would mean a reply could land
on the wrong service, which is a mistake you cannot take back.

### Streaming to several services at once **[built]**

One encode, sent to every destination, using ffmpeg's `tee` muxer. Encoding
twice would cost twice the processor on a machine already running a camera, a
screen capture and a screen reader.

The consequence is that every service gets the same picture, so the settings
have to satisfy the **strictest** destination - `EncoderSettings.ForTargets`
works them out from the enabled targets and says which one set the limit,
because *"why is my YouTube stream only 6000 kbps"* is otherwise unanswerable.
`onfail=ignore` on each output means YouTube dropping does not end the Twitch
stream.

**A stream key is typed in and never read back.** Not in speech, not in the
status line, not in a log. It is a password that lets anyone broadcast as you,
and speech is often on a speaker in a room with other people in it.

### Scenes and sources **[built]**

- A **scene** is a named arrangement; a **source** is a camera, screen,
  microphone, image, video, looping music, card or text
- Sources are **held by reference**, so one lower third in five scenes is one
  object - renaming it renames it everywhere
- The same camera can be full frame in one scene and a corner inset in another,
  because placement and size live on the reference rather than the source
- **A digit cuts to that scene**, announced with what is now live rather than
  just the name - the whole risk of scene switching is cutting to something that
  is not showing what you think it is
- Hiding is not removing, exactly as on a track
- Order decides who is in front and there is no way to see that, so every move
  says where it landed
- **A song over a static picture** is a still and a track both looped, which is
  why music is added looping by default

Placement uses the same 3 by 3 language as the card editor, so *"camera, 25
percent, bottom right"* means the same thing live as it does in an edit.

### Chat **[built for Twitch]**

- **Twitch reads with no account at all** - an anonymous IRC connection - so
  chat works the first time the view is opened, with nothing to register
- Messages are **categorised**: being named, a first-time chatter, a question, a
  moderator - each with **its own earcon**, so you know something wants you
  without reading it
- **A busy chat is rate-limited.** Past six messages in four seconds the reading
  stops and a count takes over. A chat reader that cannot be out-talked is a
  chat reader you turn off - but the earcon still plays, so you know it happened
- The same person twice running is not named twice; the platform is named only
  when more than one is connected
- **Scrolling back stops new messages interrupting** and counts them instead
- **YouTube and Facebook say what they are waiting for** (an OAuth application
  each). A pane silent because it never connected is indistinguishable from a
  chat nobody is talking in, which is the worst failure available here

### Before going live **[built]**

`P` reads the preflight list - no scenes, no destination, nothing showing, no
audio, a destination enabled but not set up. It costs one key and no risk.
`Ctrl+Shift+L` goes live, and is the only command in this view that is not a
single letter, because an audience feels it the instant it happens.

### All three platforms **[built]**

Each does different things, and the differences are **stated rather than
smoothed over** - a moderation key that appears to work and does nothing is how
somebody stays in a chat you believe you removed them from. `Shift+C` reads what
is possible in the pane you are in, given what is actually configured.

| | Twitch | YouTube | Facebook |
|---|---|---|---|
| Read | anonymously, no account | API key + video id | page token + video id |
| Reply | token | sign-in | page token |
| Delete | Helix | sign-in | **hides**, not deletes |
| Time out | Helix | sign-in | **no timeout**; blocking is the nearest thing |
| Ban | Helix | sign-in | blocks from the page |
| Pin | **none**; announcement instead | none | none |

**Twitch moderation goes through Helix, not IRC.** Twitch removed `/ban` and
`/timeout` from IRC in 2023; anything still sending them appears to work and
does nothing.

### Moderation **[built]**

`D` delete, `T` time out, `B` ban, `Shift+P` pin, `Ctrl+Shift+P` announce.
Banning **asks first, with No focused** - the safe answer is the one you get by
pressing Enter without having listened. Nothing else in the application confirms.

### Music **[built]**

A playlist with `Space`, `Shift+Space`, `Shift+Left`/`Shift+Right`, `Shift+S` to
shuffle. What is playing **and what is next** are said together, because the
second question always follows the first.

It plays **locally** rather than through the encoder: changing a source inside a
running ffmpeg means restarting the encode, which every viewer sees. It reaches
the stream through a desktop-audio source, and starting the music **says so if
there is not one** - music you can hear and your viewers cannot is a mistake
that otherwise survives a whole broadcast.

### Stream health **[built]**

The encoder already knew whether frames were dropping; nobody was listening.
Its statistics are now parsed into earcons: **dropping**, **behind real time**,
and **recovered**, said once each rather than on every sample - anything that
fires continuously gets turned off within a minute and then protects nobody.
`H` asks at any time; `Shift+F9` puts the audible meter on the live mix, the
same key and the same meter as the track editor.

### Settings **[built]**

Stream keys and destinations are saved in **application settings**, not the
project - see [SETTINGS.md](SETTINGS.md). Keys live in a separate,
owner-readable-only secret file, so settings can be copied or pasted into a bug
report without handing over your broadcast.

### Still to do

- A settings **view**, so all of this is editable in the application rather than
  by editing JSON
- Twitch EventSub for follows and raids, which IRC does not carry reliably
- Saved stream setups, so scenes survive between sessions

---

## Phase 13 — Accessible image editing **[built]**

Its own view, `Ctrl+6`. Three panes and a report: what the picture **is**, what
has been **decided** about it, and what has been **drawn** on it - the order of
the questions you actually ask.

**Nothing is destructive.** A crop is a rectangle, a resize is a size, a
rotation is an angle and a brush stroke is a shape in a list; the file is not
touched until it is exported. That is not tidiness - it is what makes every
operation reversible and describable, which is the only way an edit you cannot
see is an edit you can trust.

### Knowing what you have **[built]**

Opening a picture measures it first and says what it found: size, the aspect as
a ratio people say out loud, orientation, **physical size at its resolution**,
how much of it is empty paper, and **how far it is rotated**. `F8` describes it,
using the same command and the same brief as reviewing a video frame.

### The scanner-bed case **[built]**

*A photo dropped on a scanner, sideways, with white all round it.*

Solved by **detection and a spoken report**, never a dragged marquee:

- The picture is found inside the bed, and **several pictures are found
  separately** - two photographs on one scanner is the normal case, and treating
  them as one content rectangle would crop to a box containing both plus the gap
- The report names what it found: how many, how big, which way up, how crooked,
  how much of the bed it fills, and **which side the empty paper is on**
- `Shift+F` straightens and crops in one; `Shift+S` splits several into one file
  each
- The background is **measured, not assumed** - some scanner lids are black on
  purpose - and dust on the glass is not mistaken for a photograph

Skew comes from a projection-profile estimate: for each candidate angle, how
sharply the rows line up. It needs no line detection and no assumption about
what is in the picture. **A picture with no measurable tilt reports zero** rather
than an arbitrary fraction of a degree - offering to straighten something that
is already straight is worse than saying nothing.

### Resizing **[built]**

Arrow keys, and **every press says the new size**. The shape is locked by
default; unlocking says so and every later step says how far the ratio has
drifted. Presets are named by what they are for - half, double, fit 1080, fit
4K - because "fit 1080" is a decision and "1920 by 1080" is arithmetic you have
to do first. Enlarging past the original warns that it will look softer, and the
print size and rough file size come with every change.

### Cropping **[built]**

Not a rectangle you drag; one you **name, then adjust**. Crop to the picture,
crop to a ratio anchored on a 3 by 3 cell, then `Shift`+arrows move **one edge
at a time** - each press saying the edge, how much is being cut, as a percentage,
and what is left.

### Brush, fill and paint **[built]**

Freehand painting is a gesture, and making a gesture accessible is the wrong
problem. It is replaced by a **language**:

    circle at centre, radius 20 percent, white
    rectangle at bottom left, 30 percent, red
    line from top left to bottom right, yellow
    gradient navy to black
    text "Chapter one" at centre, white

Every shape is a **listed layer** that reads back as the sentence that would
create it, and can be selected, described and removed. Each one reports **how
much of the picture it covered**, which is the part you cannot see. Colours are
**named before they are valued** - "a mid blue, #3d6fd6" - using a weighting that
does not confuse a dark blue with a dark green, and `K` reads the whole picture
as its colours: *"80 percent navy, 20 percent white"*.

Flood fill exists in the canvas and **reports how far it went**, because the
surprise in a fill is always whether it escaped through a gap.

### Undo **[built]**

Whole-document snapshots, as the project uses. An image document is a handful of
numbers and a short list of shapes, so a snapshot cannot drift out of step with
the model the way a hand-written inverse can.

**Undoing says what it undid and what the picture is now.** Without the second
half you know something moved but not where it landed, which is worse than not
having undo at all. `U` asks what would be undone without undoing it, and
`Ctrl+Z` means "take back the last thing I did *here*" - in the image editor it
is the picture, everywhere else it is the video.

A refused edit is not recorded: undoing something that did nothing, twice, is
how a history stops being trusted.

### Text at export **[built]**

Text is the one shape Core cannot draw - it has the arithmetic but no fonts - so
it is described and listed there and rendered by ffmpeg, which means real
hinting and kerning rather than something hand-plotted. It sits **above** the
painted shapes, which is what anyone means by putting a caption on a picture,
and its outline is chosen from its own brightness: light text gets a dark edge
and dark text a light one.

### The pointer you can hear **[built]**

`G` turns sweeping on and the arrow keys move a pointer instead of resizing.
Its position is a tone: **panned left to right, pitched high to low** - the
viewfinder's vocabulary, already learnt. Up is high, because every other mapping
is something you have to remember rather than something you already know.

Words are spoken only when the pointer **crosses into a new cell**: two numbers
on every press is unusable at speed and silence is unusable at all. `Enter`
reads what is under it, a digit jumps to a cell, plus and minus change the step
from thirds down to two-hundredths, and `F12` says the position in percentages
and in pixels - one is what you can picture, the other is what you type in.

### Colour correction **[built]**

Every grading tool is a curve or a wheel and both are pointing at a picture. The
controls underneath are not: they are **stops** of exposure and **kelvin** of
white balance, which photographers already say out loud. So `V` offers the
sentences - brighter, warmer, punchier, lift the shadows - each a nudge that can
be applied twice, and each said back in its own unit: *"exposure up a third of a
stop"*, *"warmer, 6100 kelvin"*.

`Shift+V` does the half that normally happens by looking: it **measures** the
picture - average brightness, how much of the range is used, what is crushed or
blown - and suggests the correction **in the same words the commands are
called**, so the advice can be acted on by pressing the thing it just named.

Exposure goes through gamma rather than brightness: adding brightness shifts the
whole picture and flattens it, while gamma lifts the middle and leaves black as
black, which is what "a third of a stop" actually means.

### Shared with the video editor **[built]**

Cody asked whether the overlay system could be reused here. It is, and the split
is deliberate:

- **Cards are shared.** `Shift+A` puts a card on a photograph and opens the same
  card editor the timeline uses. A lower third over a photograph is the same
  object as one over a clip - same placement language, same summary sentence,
  same renderer.
- **Shapes are not merged into cards.** The shape language is geometry and cards
  are titles and logos; folding either into the other would make both worse.
  They are complementary layers of one idea and already share `Placement`.
- **`I` sends the picture into the project**, saving it and putting it in the
  media bin, so a photograph that has just been straightened and cropped can go
  on the timeline without leaving the application or finding the file again.

### Levels **[built]**

A levels curve is a graph you drag - the least accessible control in any image
editor. But the graph is only a picture of five numbers, and the numbers have
names photographers already use. So `;` offers those: the **black point**, the
**white point**, and the **shadows**, **midtones** and **highlights** between
them, each nudged and read back as a number that means something.

**Auto levels is the one command that makes a curve worth having without a
graph.** It finds where the picture actually starts and stops - ignoring the
half percent at each end that is noise - pulls those to black and white, and
*says the numbers it chose*, so the automatic answer can be adjusted rather than
merely accepted. It warns when the stretch is big enough to band.

`'` reads the **histogram as five numbers** rather than two hundred and fifty
six, with the shape said as a sentence first: *"bunched in the shadows"*,
*"almost all midtones, so it will look flat"*. That is what the curve was drawn
on top of, and it is the part that tells you which way to move.

### Batch **[built]**

`B` does to a folder what you have just done to one picture. The design decision
is what "the same treatment" means:

- **The corrections travel** - colour, levels, size, the card.
- **The geometry is measured per picture.** A photograph lands somewhere
  different on the bed every time, so each one is found, straightened and
  cropped on its own terms. Auto levels likewise runs on each picture's own
  histogram rather than one setting for all.

That is the difference between a batch that saves an afternoon and one that
ruins a hundred files in a single keystroke.

It **says what it will do before it does it**, then confirms; it counts out loud
as it goes, because four minutes of silence is indistinguishable from a hang;
one unreadable file does not stop the other ninety-nine; and it **refuses to
overwrite the originals**.

### Per-channel levels **[built]**

The only thing that reaches a cast the temperature control cannot. Temperature
moves the whole picture along one axis, from orange to blue; a yellowed page, a
mixed light or a print that has faded unevenly is off in a direction that axis
does not pass through, and no amount of "warmer" will fix it.

- **`:` opens the per-channel levels** - automatic, balance-on-the-pointer, and
  a nudge for each channel.
- **`"` says which way the colour is pulling**, as a direction rather than as
  three numbers: *"a warm cast, 40 percent"*. A cast is invisible to a
  brightness histogram - a photograph that is too blue and one that is right
  have the same shape in grey - so colour is measured separately.
- **Auto colour levels** stretches each channel to its own range, which is what
  removes a cast. It rests on the grey-world assumption, so it says what it did
  and can be undone: it is wrong for a picture that really is mostly one colour.
- **`W` while sweeping balances on the pointer** - the eyedropper, done without
  pointing. Sweep to something that ought to be grey, and the correction that
  makes it neutral is worked out from there. It is the most reliable white
  balance there is, because it uses a fact about the scene rather than an
  assumption about the average, and it refuses a spot that is too dark, blown
  out, or already neutral.

This is where the sonified pointer earns its keep: it exists to answer "what is
over there", and it turns out to be the control a white balance needed.

### Phase 13 is complete.

---

## Phase 14 — Audio effects **[built]**

The largest audible gap. A talking-head recording off a room mic wants noise
reduction and levelling, and neither existed.

A mixer answers "what is happening" by eye — knobs and a spectrum. The
replacement is the one the image editor already proved for colour: **named
presets plus a spoken number**.

- Effects are named for **what they are for** — "room tone removal", "rumble
  filter", "levelling" — never for their filter. You choose the decision; the
  number is said after it and can be nudged, but you never need it to choose
- **One control per effect.** A compressor with four numbers is a compressor you
  have to be taught; the presets encode the shape that matters
- Every setting is stated in the unit an engineer says out loud — dB, hertz,
  "4 to 1", LUFS — not as a percentage of an invisible slider
- **Effects run in a fixed order regardless of the order they were added.**
  Filtering before compression, compression before loudness. A compressor
  pumping on rumble you were about to remove is a mistake you would need the
  theory to predict
- Ready-made chains — close mic, laptop mic, noisy room, voice polish, broadcast
- A chain **reads back as the sentence that would create it**, in the order it
  will actually run

### Track effects and segment effects are different **[built]**

Noise reduction is a property of the microphone and the room, so it belongs on
the **track** — setting it forty times is both tedious and a way to end up with
forty slightly different settings. A **segment's** own effects are for fixing one
bad take. Track effects run first, and which one you changed is always said.

### Measure, then advise in the words of the commands **[built]**

`Ctrl+F4`. The measurements already existed — loudness, true peak, noise floor —
and nothing read them back as a decision.

*"The noise floor is at -35 decibels, which is audible under speech. Suggested:
room tone removal at 12 dB, rumble filter below 88 hertz."*

The advice **names the thing you then press**, exactly as `Shift+V` does in the
image editor. A worse measurement suggests a harder setting, so the suggestion
arrives roughly right rather than at a default. Sound that measures fine is told
so rather than met with silence — a command that says nothing reads as one that
failed.

---

## Phase 15 — Volume over time **[built]**

What every other editor draws as a curve with draggable points, which is the
least accessible control in any of them.

A curve is a picture of a decision, and the decision has a name. So the model
stores **the shape** — duck, ramp, rise, ease in, ease out, steady — rather than
a list of coordinates. That makes it describable (*"volume dips to -18 decibels
over 4 seconds, then comes back"*), adjustable by keystroke, and impossible to
leave a stray point in that you cannot find.

Arbitrary point editing is **deliberately not offered**. Every use of it that
matters for talking-head video is one of these shapes.

`Ctrl+Alt+A`. Position and opacity over time use the same model and are not yet
exposed.

---

## Phase 16 — Multicam **[built]**

The recording side already worked. What was missing was knowing how far apart
two files start, and being able to switch without finding the same moment in
each one by hand.

### Synced by sound **[built]**

By **envelope cross-correlation**, not timecode: two consumer cameras have no
shared clock, and a clap is something you can do without seeing anything. It
runs on the peak data already cached for drawing waveforms, so it costs no extra
decoding, and it is robust to the two cameras having completely different
microphones — which correlating raw samples is not.

**Confidence is kept and spoken**, because a confident wrong answer is the
failure mode of audio sync. Two unrelated recordings still produce a best shift,
and reporting that as a sync is how a whole edit ends up a second out. Below a
threshold it says so and refuses; the angles that matched poorly are **named
individually**, because which camera is unreliable decides whether you can use
it and a count does not tell you that.

### Switching **[built]**

**A digit cuts to that angle** — deliberately the same gesture as a digit cutting
to a scene in the stream view, so it is learnt once.

A switch **splits and re-points**: the segment is cut at the cursor and the
second half plays a different camera. Because the offset is known, the cut is
frame-accurate by construction rather than by you finding the moment twice. The
words stay with the segment, because a transcript belongs to the take and not to
the camera.

Cutting to an unsynced angle is **refused rather than done wrongly** — an
unsynced cut looks like a working edit and is off by however far the files
differ. So is cutting to a camera that was not running at that moment.

---

## Candidate features — what other editors have that this does not

Recorded so the list exists rather than being rediscovered. Each notes how it
would be made usable without sight, because that is the part that decides
whether it is worth building.

| Feature | How it becomes accessible |
|---|---|
| **Chroma key** (green screen) | Automatic key, then **measurement**: what fraction of the frame keyed, where spill remains, whether edges are ragged. Plus `F8` to have the result described. You cannot see a key edge, but you can be told about it. |
| **Colour correction** | Apply by description - "warmer by 200 Kelvin", "lift the shadows" - never curves. The analysis already produces the numbers; this is the other half of it. |
| **Auto-reframe to vertical** | The face tracker already exists, so a 9:16 crop that keeps you centred is nearly free. Announces how much of the frame it had to lose. |
| **Noise reduction, EQ, compression** | **[built]** — see phase 14 |
| **Stabilisation** | One toggle; report how much it had to crop. |
| **Multicam angle switching** | **[built]** — see phase 16 |
| **Beat-synced cutting** | Detect the music's tempo and snap cuts to it. The only version of a "grid" that means anything in video. |
| **Speed ramps** | Named shapes - "ease into slow motion" - rather than keyframe curves. |
| **Proxy media** | Purely mechanical; no accessibility question. Matters once files are large. |
| **Burned-in captions** | A render option. The captions already exist. |
| **Project templates** | Intro and outro bookends, standard lower thirds, house colours - so a new video starts where the last one ended. |
| **Export queue** | Render several presets unattended, announcing each as it finishes. |
| **Version snapshots** | Save a named state and compare; "what changed since I last rendered". |

**The rule that decides all of them:** replace a continuous control with named
presets plus a numeric readout, and replace visual verification with measurement
plus description. Anything that only works by pointing at a picture is the wrong
shape and needs rethinking rather than adapting.

---

## Ongoing — things that are not a phase

- **Preferences window**: verbosity, step-size defaults, snapping, ripple mode,
  earcons on/off, device defaults, render presets, keymap remapping. Every
  setting reachable from the command palette too.
- **Keymap remapping** from the registry, with conflict detection already
  available as `CommandRegistry.Conflicts()` **[built]**
- **Undo history as a navigable list** — jump back to a point rather than
  pressing Ctrl+Z hopefully
- **Markers with types** — to-do, issue, chapter, note. Chapters export to a
  YouTube description.
- **Smart bins** — auto-collections by criteria, from Resolve
- **Beat grid** — snapping derived from the music track's tempo, for cutting to
  music. The only version of "grid" that earns its place; video has no meter,
  so a musical bar grid would be meaningless.
- **Workflows** — named macros over command IDs **[model built]**. `Workflow`
  and `WorkflowStep` exist and are tested; nothing records or runs one.
  `Ctrl+Alt+K` and `Ctrl+Alt+Shift+K` are held for them.
- **Nudge by a frame** (`Alt+Left`/`Alt+Right`) and **move a segment to the
  track above or below** (`Alt+Up`/`Alt+Down`) — designed, no verb in
  `EditOperations` yet. Moving a spine element to another track means turning it
  into an overlay item, which is a decision rather than a keystroke.

### Keys held for unbuilt commands

Reserved rather than reassigned, so the design survives and nothing quietly
takes the binding. A test pins them.

| Key | Waiting for |
|---|---|
| `Ctrl+F2` | Export presets |
| `Ctrl+Alt+K`, `Ctrl+Alt+Shift+K` | Run and record a workflow |
| `Alt+Left`, `Alt+Right` | Nudge one frame |

**None of these appear in `CommandRegistry`.** An entry there is a promise that
a key does something — it feeds `F1`, the command palette and the keymap — so a
command listed with no handler is a key that lies. They come back when they work.

---

## Deliberately not doing

| | Why |
|---|---|
| Modal tools (razor, slip, slide) | You cannot see which mode you are in. Every tool is a verb instead. |
| GL Transitions / frei0r | Needs an ffmpeg rebuild. 58 xfade transitions is already more than any sane video uses. |
| Musical bar/beat grid as a primary view | Video has no meter. Tempo-derived snapping only. |
| Bundling ffmpeg or Whisper | Personal build. Keeps the x264 GPL question off the table. |
| Cross-platform UI in one codebase | GTK does not map to UIA. Windows gets its own front end over the same Core — see [CLIENTS.md](CLIENTS.md). |
| Arbitrary keyframe points | A curve is a picture of a decision, and the decision has a name. Named shapes instead — phase 15. |
| Nested compound clips | Programme time would have to be computed through a tree. A grouping buys what people want from a compound without moving an invariant — phase 6. |
