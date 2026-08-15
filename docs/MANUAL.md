# Accessible Video Editor — manual

An accessible video editor. Edit by transcript, navigate by ear, frame the shot
by tone.

> **Status markers.** Sections marked **[built]** work today. **[planned]** is
> designed and agreed but not yet written. Nothing here is aspirational filler —
> if it is not marked built, it does not work yet.

---

## 1. Concepts

You need six ideas. Everything else follows from them.

### Segment

**A segment is one piece of content on one track.** Split one segment and you
get two; two splits give three. It is the thing you select, delete, trim,
retime, mute and move.

A segment might be a sentence of speech, a clip, a title card, an image, a
music bed, or a deliberate gap.

### Track, and what a track means

Tracks stack. **Higher tracks composite over lower ones** — automatically, the
same as every editor. You never mute a track to reveal the one below it.

| Track | Role |
|---|---|
| **Programme** | The spine. The story, in order. Track 1, the master. |
| **B-roll** | Picture that replaces the programme's picture while its audio keeps playing |
| **Graphics** | Titles, cards, images, logos — composited on top |
| **Music** | Beds and additional audio |

**This is what makes a clip "b-roll" rather than a cut** — not a property of the
clip, but the track it sits on. Put footage on the B-roll track and its picture
takes over while you keep talking. Put the same footage on the Programme track
and it becomes a cut: its own picture *and* its own sound take over, and your
narration stops.

So: *b-roll is a placement, not a kind of file.*

### The programme track is a sequence, not a ruler

This is the one place Accessible Video Editor differs from Premiere. The Programme track is an
**ordered list**, so deleting a segment closes the gap and everything after it
moves earlier. Everything on other tracks is anchored to a programme segment, so
it rides along.

That is what makes transcript editing possible: reorder the sentences, reorder
the video.

### Mute, hide, disable — three different things **[built]**

Collapsing these would make the announcement ambiguous, and ambiguity is
unrecoverable when you cannot look.

| Action | Effect | You hear |
|---|---|---|
| **Mute** | Audio silenced, picture stays | "audio muted" |
| **Hide** | Picture removed, audio still plays | "picture hidden" |
| **Disable** | Gone from the programme entirely, but still in the document and restorable | "cut" |

So if you import a clip with audio and mute it, **only its audio goes**. The
picture is still on screen. If you wanted the picture gone too, that is Disable.

### Cards **[built]**

A card is a composed screen: a background plus text and image layers. Where it
sits decides what it is.

- On the **Programme track** → a full screen. Narration stops. Title card,
  section break, end screen.
- On the **Graphics track**, with a transparent background → it composites over
  the video. **A lower third is exactly this.**

One concept, one editor, two placements.

Inserting a card inserts a **new segment** with its own duration, like any other
segment.

### Holes **[built]**

`!hole` reserves blank space with a note — "explain the order panel here". It
appears in the to-do list and **blocks the master render** until filled, so a
structure-first edit can never ship a gap by accident.

---

## 2. Views **[built]**

Only one view is on screen at a time.

Views are ordered by how often you are in them, not by how data flows — the
timeline is where the work happens, so it is view 1.

| Key | View | What it is for |
|---|---|---|
| `Ctrl+1` | **Timeline editor** | The cut. Move along it, split, trim, delete, add transitions. |
| `Ctrl+2` | **Track editor** | Track headers: name, arm, mute, solo, lock. Add and remove tracks. |
| `Ctrl+3` | **Transcript editor** | The same edit as text. Delete sentences, reorder them, fix captions. |
| `Ctrl+4` | **Media bin** | Everything imported. Browse a source, mark the bits you want, insert them. |
| `Ctrl+5` | **Streamer view** | Live output. **[planned, far out]** |

There is deliberately **no record view**. Recording is per track, so it happens
in the track editor and the timeline — leaving the view you are editing in to
record into a hole is exactly when you least want to move.

**Views are announced by name, never by number** — "view 3" tells you nothing
about where you are; "transcript editor" tells you everything.

`F6` / `Shift+F6` cycles views when you would rather not remember numbers.
Switching announces the view and what is in it: *"timeline editor. 6 segments,
28 seconds, 1 hole outstanding."*

**A status line is always present** regardless of view: position, total
duration, step size, and the focused track. It sits outside the view stack, so
it is the one thing that can never be a view away.

`Tab` is **not** a view switcher — an earlier build made it one and that fought
every other application. In the timeline editor `Tab` moves to the **next edit
point on any track**: the video equivalent of Reaper's move-to-next-transient,
and the fastest way to ask "what happens next anywhere in this project?".
`Shift+Tab` goes back.

### Function keys

One domain per key, stacked, so an unfamiliar binding is guessable:

| Key | Plain | Shift | Ctrl |
|---|---|---|---|
| `F1` | What can I do here | Read the whole keymap | |
| `F2` | Render master | Render draft | Export presets |
| `F3` | Find in transcript | Find previous | |
| `F4` | Quality of this segment | Quality across the project | |
| `F5` | Arm or disarm this track | Start or stop recording | Choose capture device |
| `F6` | Next view | Previous view | |
| `F7` | To-do list | | |
| `F8` | Describe this frame | Read me the edit | |
| `F9` | Accessible viewfinder | | |
| `F10` | | Context menu | |
| `F12` | Where am I | | |

`F2` is the render key by request. Premiere's `Ctrl+M` for export still works as
an alternate, so that muscle memory is not thrown away. Renaming a track — which
`F2` would normally do — is `N` in the track editor, where nothing is a text
field.

---

## 3. The Media view **[partly built]**

Everything imported into the project, with what `ffprobe` found: resolution,
frame rate, duration, and **how many audio tracks and what they are**.

`record-screen.sh` writes three audio tracks — 0 mix, 1 microphone, 2 system
audio — and they are listed by name, never as a bare number.

What you can do here:

- `Ctrl+I` — import video, audio or images **[planned]**
- `Enter` — open a source's transcript in the Transcript view **[planned]**
- `,` — **insert** the marked range at the cursor, rippling **[planned]**
- `.` — **overwrite** the marked range at the cursor **[planned]**
- `F4` — picture and sound quality report **[planned]**
- Applications key — context menu

**Importing several videos and cutting them together** is the normal workflow:
import each one, transcribe it, open its transcript, select the sentences you
want, and insert. You never need the clipboard for this.

---

## 4. The Tracks view **[built]**

Track headers. One row per track, announcing name, medium and any state that is
on: *"B-roll, video track, armed, muted"*.

Plain letters are safe here because nothing in this view is a text field.

| Key | Action |
|---|---|
| `M` | Mute or unmute |
| `S` | Solo or unsolo |
| `L` | Lock or unlock — locked tracks are excluded from ripple |
| `B` | Arm or disarm |
| `N` | Rename this track **[built]** |
| `Ctrl+T` | New track **[built]** |
| `F5` | Arm or disarm **[built]** |
| `Shift+F5` or `R` | Start or stop recording **[planned]** |
| `Delete` | Delete this track, confirming first **[planned]** |

**Arm means three things at once**, because they are one intent: this track is
the record target, it names the camera or microphone that feeds it, and arming
runs the signal check — non-silent microphone, non-black frames, disk space. You
cannot arm a dead device without being told.

`Delete` is unambiguous here because the focused thing *is* a track. In the
Timeline view the focused thing is content, so there it deletes content.

---

## 5. The Timeline view **[built]**

Track headers down the left, the drawn timeline to the right — the layout every
editor uses, so anyone looking over your shoulder already knows how to read it.

One header row per track, each announcing what is under the cursor on that
track: *"Graphics — 0:12.4, card 'Cody Hurst'"* or *"B-roll — 0:12.4, blank"*.

Moving **Up and Down** between rows at a fixed time reads out the vertical slice
of the edit — the thing a sighted editor gets from a glance.

### What is drawn **[built]**

The picture beside the headers is exactly that: a picture. It takes no focus,
answers no keys, and is computed from the same model the speech comes from, so
it can never tell you something different from what you just heard.

- Segments as blocks, scaled to their length and labelled with their own words
- A ruler across the top, at an interval that keeps its labels readable at any
  zoom
- The cursor as a red playhead; the segment under it takes a bright border
- Waveforms on audio and programme lanes, drawn as soon as they are extracted
- A marked range as a highlighted band across every lane
- Transitions hatched across the join they cover; fades as the wedge they are
- Muted, hidden and disabled segments each look different — and never by colour
  alone

**Zoom is the step size.** `Ctrl+Up` and `Ctrl+Down` change both at once, so
what is on screen and what an arrow key moves by can never disagree. The view
follows the playhead, moving on only when it reaches the edge.

### Moving

| Key | Action |
|---|---|
| `Up` / `Down` | Change track |
| `Left` / `Right` | Move by the current step size |
| `Ctrl+Left` / `Ctrl+Right` | Previous / next **segment start** on this track |
| `Shift+,` / `Shift+.` | Start / end of the **current segment**; press again to walk |
| `-` / `=` | Zoom out / in — the same control as step size |
| `Home` / `End` | Start / end of the programme |
| `F12` | Where am I — full readout |
| `Tab` / `Shift+Tab` | Next / previous edit point on **any** track |

**Zoom and step size are one control.** Zooming out makes each arrow press cover
more time, which is exactly what zooming out means when you cannot see pixels.
The ladder, coarse to fine:

`marker → boundary → segment → word → second → tenth → frame`

### Editing

| Key | Action |
|---|---|
| `S` | Split at cursor |
| `Shift+S` | Split every track at cursor |
| `Ctrl+J` | Heal a split — rejoin two halves of one shot |
| `Delete` | Ripple delete — remove and close the gap |
| `Shift+Delete` | Lift — remove but leave silence of the same length |
| `Shift+E` | Disable or restore — non-destructive |
| `Ctrl+Shift+M` | Mute the segment's audio |
| `Alt+[` / `Alt+]` | Trim head / tail to the cursor |
| `T` | Transition at this boundary **[planned]** |
| `M` | Add a marker **[planned]** |
| `Ctrl+C` / `Ctrl+X` / `Ctrl+V` | Copy, cut, paste — **including between tracks** |
| `Ctrl+Z` | Undo |

**Pasting between tracks** is checked against the target track's medium and
refused out loud: *"cannot paste video onto Music, which is audio."* On a visual
timeline a wrong paste is obvious the moment you see it; here silence would be a
trap.

**As the cursor crosses into a new segment its text is announced** — moving
within a sentence stays terse, arriving at a new one tells you what it says.

**Delete resolves its target explicitly and says what it did.** In order: a
marked selection wins; otherwise the segment under the cursor on the focused
track; otherwise it says *"nothing under the cursor"* rather than guessing.

**Ripple delete is on plain `Delete`.** Premiere has this the other way round —
there, `Delete` lifts and `Shift+Delete` ripples. Inverted here because a
transcript-driven edit ripples by default.

### Transitions

A transition belongs to the **boundary entering a segment, on the Programme
track**. It occupies the head of the segment it leads into, so landing inside
one announces *"0:04.5, transition, wipeleft, 1 second"* — which is how you
confirm it actually got inserted.

58 transitions are available. Stick to fade, the four wipes and fadeblack; the
other fifty read as amateur.

---

## 6. The Transcript view **[built]**

The same edit, as text. **This is a second way of doing the edit, not a view of
it.**

One line is one segment. Speech segments show their words; everything else shows
what it is: `[card: …]`, `[clip]`, `[hole: …]`, `[pause 0.7s]`. Disabled
segments stay visible, marked `[cut]`, so you can find them and restore them.

**Every structural key here takes a modifier**, because unmodified keys are
typing and plain `Delete` has to stay character deletion — otherwise this stops
being a text editor.

| Key | Action |
|---|---|
| `Ctrl+Shift+K` | Delete this segment — VS Code's delete-line |
| `Ctrl+Shift+E` | Cut or restore — the line stays, marked `[cut]` |
| `Alt+Up` / `Alt+Down` | Move this line earlier / later — **the video reorders with it** |
| `Ctrl+Enter` | Split the segment at the word the caret is on |
| `Ctrl+Shift+C` | Caption on or off for this segment |
| `Ctrl+;` | Read this line's position, times and text |
| Typing | Edits **caption text only**, never the cut |

That last rule is announced the first time you type in a line, because it
surprises people. There is no syntax here and therefore no syntax checker —
structure is changed by commands, not by typing.

**Caption edits commit when the caret leaves the line**, not on every keystroke,
so the pane is never rebuilt underneath a half-typed word. Typing a caption that
matches the transcript exactly clears the override, so the caption keeps
tracking the transcript rather than silently freezing.

Editing a bracketed line — `[card: …]`, `[clip]` — is discarded and says *"that
line is not editable text"*. Those lines are generated, not typed.

**A cut line can still be deleted, restored and moved**, even though it has no
programme time at all. That is why the transcript addresses segments by identity
rather than by time.

**The cursor is shared with the Timeline.** Switch to this view and the caret is
already on the word that was under the timeline cursor; switch back and the
timeline goes to wherever the caret was. **[built]**

A `[cut]` line has no programme time, so landing on one says *"cut, not in the
programme"* rather than snapping somewhere plausible.

---

## 7. Recording and takes **[built]**

### Track type

A track is video, audio, image or mixed. **The type decides what the track can
record from**, so it is asked for when the track is made — `Ctrl+T` offers the
choice — and changed afterwards with `Ctrl+Shift+Y` or from the track's context
menu.

**The input is a property of the track**, and the track's type decides what
inputs it can even have:

| Track medium | Records from |
|---|---|
| Video or mixed | Cameras |
| Audio | Microphones and system-audio sources |
| Image | Nothing — it cannot be armed |

| Key | Chooses |
|---|---|
| `Ctrl+F5` | This track's **input** — camera or microphone |
| `Ctrl+Alt+F5` | This track's **input channel** — both, left only, right only |
| `Ctrl+Shift+F5` | The **monitoring output** — where playback is heard |

`F5` arms the track, and an armed track announces its input: *"Camera, video
track, armed, input Laptop Webcam Module"*.

**Output is separate from input, deliberately.** Recording from an interface
while monitoring on headphones is normal, and assuming they are the same is how
people end up listening to the wrong thing. The monitoring output is a project
setting, not a track one — there is one pair of ears.

**Input channel matters for interfaces.** A two-input interface like a Focusrite
presents as a single stereo source, so recording it whole puts the microphone on
one side and silence on the other. That sounds like a broken take and there is
no meter to notice it on, so the channel is a per-track choice.

### The accessible VU meter — `Shift+F9`

A visual meter works by being glanceable: you see green, yellow or red without
reading anything. The equivalent by ear is a **tick whose pitch tracks the
level** — quiet is low, loud is high — played continuously, so the shape of your
delivery is audible without a word being spoken.

**Words are reserved for crossing into a new zone.** "Green", "yellow", "red",
"clipping" — spoken on the crossing and never repeated. A meter that talks
constantly is one you turn off.

| Zone | Level |
|---|---|
| Silent | below -50 dB |
| Green | -50 to -18 dB — comfortable speech |
| Yellow | -18 to -6 dB — hot but usable |
| Red | -6 to -0.5 dB |
| Clipping | above -0.5 dB — the tick rate doubles, because now it is an alarm |

A level resting exactly on a boundary would otherwise read out both zone names
over and over, so a reading has to fall 2 dB clear before the lower zone is
announced again.

Switching monitoring off reports the peak: *"monitoring off, peak -4 decibels,
red"* — which tells you a take nearly clipped even though it sounded fine.

Like the viewfinder, this is a **mode**: it opens the microphone, so it runs only
while you have asked for it.

### Multiple cameras

**Every armed track records at once**, each to its own file. Arm two video
tracks with different cameras, press record once, and both angles start
together.

Every device is checked *before* any recording starts — discovering the second
camera is dead after the first has been rolling for a minute would waste the
take. A multi-angle recording puts all its files in the media bin rather than
making them takes of one another: a second angle is separate footage, not
another attempt at the same line.

The **viewfinder is a mode, not a view** (`F9`). Framing a shot is not editing —
you are pointing a camera at yourself and the tones need the whole audio
channel — so it is something you enter and leave, not a place you can Tab into
and get stuck.

Arm a track, then:

| Key | Action |
|---|---|
| `Ctrl+F5` | Choose this track's input |
| `F5` | Arm or disarm the focused track |
| `Shift+F5` or `R` | Start recording; again to stop |
| `T` / `Shift+T` | Cycle takes forward / back |

### What happens when you press record

1. **The signal check runs** — a one second capture, measured. This is the only
   moment a device is opened, and it is why arming does not turn on your camera.
   - A silent microphone **refuses to record**: *"Arctis Nova Pro is silent, -91
     decibels. Check it is not muted."* An hour of footage from a muted
     microphone is unrecoverable and entirely preventable.
   - Clipping **warns but records** — too loud is recoverable and the take may
     still be the one you want.
   - A black picture refuses: *"check the lens cover"*.
2. **A spoken countdown** — three, two, one. Starting to talk the instant a key
   goes down gives you a take that begins mid-breath.
3. **Recording**, to `~/.local/share/videoedit/recordings`. Press `R` again to
   stop; ffmpeg is asked to finish cleanly rather than killed, so the file is
   always playable.
4. **The result becomes a take** on the segment at the cursor, and that take
   becomes the active one.

**Device listing never opens a device.** Camera names come from sysfs and
microphone names from `pactl`, so browsing what is available cannot switch on a
webcam light. Only recording touches hardware, and only when you ask for it.

`Ctrl+5` shows the record view: cameras, microphones and system-audio sources
with the names you would recognise — "Arctis Nova Pro Wireless Mono", not
"alsa_input.pci-0000_c1_00.6.HiFi__Mic1__source".

`AccessibleVideoEditor.Cli devices` prints the same list headlessly.

**Takes** come from the DAW world and no video editor does them well. Record
into the same segment again and you get take 2 rather than a second segment, so
the structure of the video does not change while you are still getting the words
right. `T` cycles forward, `Shift+T` back; each announces *"take 2 of 3, 4.1
seconds"*, plus any capture issues that take carries.

The segment's original media automatically becomes take 1, so choosing take 2
never discards what was there. Everything anchored to the segment — overlays,
markers, the edit around it — stays attached, because only the media changes.

Nothing is thrown away by choosing. The rejected takes stay in the document.

This is the right model for talking-head video, where you say the same sentence
four times and want the best one.

**Recording into a hole** is the same flow: put the cursor on a hole, arm, `R`.
The new take splices in at exactly that point.

---

## 8. Playback **[built]**

| Key | Action |
|---|---|
| `Space` | Play / pause |
| `J` `K` `L` | Shuttle back / stop / forward |
| `Ctrl+Space` | Audition — plays 1.5 seconds either side of the cursor |
| `Shift+Space` | Loop the selection |

**During playback, segments announce themselves as they pass** — the sentence
text, *"transition, wipeleft, 1 second"*, *"b-roll"*, *"card, Cody Hurst"* — on
boundary crossings only, never timecodes. You can hear time passing; what you
cannot hear is whether the wipe you inserted is really there.

Verbosity: off, boundaries only (default), or everything (which also announces
when an overlay ends).

Playback is **audio only** for now. There is no embedded video surface, and
letting mpv open a window of its own steals keyboard focus mid-edit. Video
preview arrives with the visual timeline.

Cards, holes and pauses have nothing to preview, so playback skips them and says
*"skipping to 0:03, nothing to preview before it"*.

Playback plays the **decision list** through mpv's `edl://` protocol — a list of
files with in and out points, played as one stream. Nothing is encoded, so an
edit is audible the moment it is made. The cursor follows playback, and playback
stops at the end of the programme rather than sitting past it.

### Audio scrub **[built]**

Moving the cursor plays a fraction of a second of the real audio at that point.
At word granularity you hear the word.

This is the feature that makes a timeline navigable by ear: a timestamp tells
you *where* you are, the audio tells you *what is there*. It runs on a separate
audio-only player, so scrubbing never disturbs where playback is parked, and
each blip cancels the last — holding an arrow key down does not queue a backlog.

Turn it off with `AudioScrub` in project settings.

### If media is missing

Playback reports it — *"cannot play: take1.mkv not found on disk"* — rather than
producing silence, which is indistinguishable from a broken player.

A demo file with a tone that steps every eight seconds is generated at
`~/.local/share/videoedit/demo/take1.mkv`, so scrubbing sounds different in
different places while there is no real footage loaded.

---

## 9. Placing images and text **[built in Core]**

Think of the screen as a numpad:

```
7 8 9      top-left     top      top-right
4 5 6      left         centre   right
1 2 3      bottom-left  bottom   bottom-right
```

Press a number to place. **Press a second number for a sub-cell** — `9` then `3`
puts a logo in the very bottom-right corner of the top-right cell. That is 81
positions from two keystrokes. Arrow keys then nudge in 1% steps.

The **anchor is derived from the cell**, so a graphic at 9 grows inward and
cannot drift off-canvas when its content changes size.

For a card with several elements, **stack layout** is the default and usually
what you want: layers flow top to bottom with automatic spacing, like a slide.
Grid placement is for when you deliberately want something off-centre.

Templates: title card, section break, quote, lower third, end screen.

---

## 10. Rendering **[planned]**

| Key | Action |
|---|---|
| `F5` | Draft — 540p, fast, for checking |
| `Ctrl+M` | Export media — 1080p plus `captions.srt` |

Three fidelity tiers:

1. **Live** — playback of the decision list. Instant, no encode. What makes
   editing feel alive.
2. **Draft** — background re-render of only the changed segments.
3. **Master** — on demand.

Segments are content-hash cached, so changing one line re-renders one segment,
and reordering the video costs nothing.

Unfilled holes block the master render.

---

## 10b. The Streamer view **[built]**

Four areas. **`Ctrl+`` `** goes round them and **`Ctrl+Shift+`` `** goes back:
**scenes**, **sources**, **preview**, then **one chat area per platform**.

Chat is kept per platform on purpose — a reply typed into the wrong pane goes to
the wrong audience, and that cannot be taken back.

### Keys

| Key | Action |
|---|---|
| `` Ctrl+` `` | Next area (Shift for previous) |
| `1`–`9` | Cut to that scene, announced with what is now live |
| `N` / `Shift+N` | New scene / build the starter setup |
| `F2` | Rename the scene |
| `A` | Add a source — camera, screen, microphone, image, video, looping music |
| `V` / `M` | Show or hide / mute a source in this scene |
| `[` `]` | Move a source back or forward, announced with where it landed |
| `Delete` | Remove the scene or the source, depending on the area |
| `C` / `Y` / `F` | Connect Twitch / YouTube / Facebook chat |
| `Shift+C` | What can I actually do in this chat |
| `R` | Reply in the chat you are in |
| `Ctrl+Home` | Back to following chat live |
| `D` / `T` / `B` | Delete a message / time out / ban (ban asks first) |
| `Shift+P` / `Ctrl+Shift+P` | Pin / announce |
| `Space` / `Shift+Space` | Play the playlist / stop the music |
| `Shift+Left` `Shift+Right` | Previous / next track |
| `Shift+S` / `Shift+A` | Shuffle / add music |
| `H` / `Shift+F9` | How is the stream doing / meter the live mix |
| `K` / `Shift+K` | Set a stream key / what keys are saved |
| `P` | Read the preflight list |
| `Ctrl+Shift+L` | Go live, or stop |

Single letters, which nothing else in the application does. While you are live
you are also talking, and a chord is a chord you will fumble. They are safe
because the only text entry here is the reply box, and typing in it is checked
for before any key is read as a command.

### Streaming to more than one service

One encode goes to every destination at once. That means **every service gets
the same picture**, so the settings satisfy the strictest of them — and the app
says which one that is, because otherwise "why is my YouTube stream only 6000
kbps" has no answer. If one service drops, the others carry on.

**Your stream key is never read back.** Not in speech, not in the status line,
not in a log.

### Chat

Twitch reads with no account at all. Messages are sorted into the ones that want
you — being named, a first-time chatter, a question, a moderator — and each gets
its own earcon, so you know something needs you without reading it.

A busy chat stops being read line by line and becomes a count; the earcon still
plays. Scrolling back stops new messages interrupting and counts them instead,
and `Ctrl+Home` returns you to live.

YouTube and Facebook say what they are waiting for rather than sitting silent —
YouTube needs an API key and the live video's id to read, Facebook a page token
and its video id.

### Moderation

The platforms genuinely differ, so `Shift+C` reads what is possible where you
are, given what is configured. Facebook **hides** comments rather than deleting
them and **blocks** rather than timing out; no platform lets an outside
application pin a message, and Twitch's announcement is offered as the nearest
thing. Twitch moderation goes through its API rather than chat commands, which
stopped working in 2023.

Banning asks first, and **No has the focus** — it is the answer you get by
pressing Enter without having listened.

### Music

`Space` starts the playlist and says what is playing *and* what is next. It
plays on this machine, like any other application, and reaches your stream
through a desktop-audio source in the scene — if there is not one, starting the
music tells you your viewers will not hear it.

### Knowing it is all right

Dropped frames, an encoder falling behind, and recovery each get an earcon, said
once rather than continuously. `H` asks at any time, and `Shift+F9` puts the
audible meter on the live mix — the same key and the same meter as the track
editor.

### Your keys

Stream keys and tokens are saved in your **application settings**, not in the
project, and in a separate owner-only file — see the settings document. `K` sets
one; `Shift+K` reads back *which* are saved and never what they are.

---

## 10c. The Image editor **[built]**

`Ctrl+6`. Three panes and a report: **the picture** (what it is), **what has
been decided** (everything you have done to it), and **drawn on top** (the
shapes you have added).

**Nothing here touches the file until you save.** A crop is a rectangle, a
resize is a size, a rotation is an angle, a brush stroke is a shape in a list —
so all of it is reversible and all of it can be read back.

`Ctrl+Z` undoes, and says **what it undid and what the picture is now** — the
second half being the part you would otherwise have to go and check. `U` asks
what would be undone without undoing it. In this view `Ctrl+Z` means the
picture; everywhere else it still means the video.

### Keys

| Key | Action |
|---|---|
| `O` / `E` | Open a picture / save it |
| `F8` | What does it look like |
| Arrows | Resize, saying the new size each press (`Ctrl` for a bigger step) |
| `L` / `S` | Lock or unlock the shape / size presets |
| `C` / `Shift+C` | Crop to the picture / crop to a shape |
| `Shift`+arrows | Move one crop edge |
| `Shift+R` | Back to the whole picture |
| `T` / `[` `]` | Straighten / turn a quarter |
| `Shift+F` | Fix the scan — straighten and crop in one |
| `Shift+S` | Split several pictures into one file each |
| `Shift+D` / `Delete` | Draw something / remove a shape |
| `K` / `P` | What colours are on it / what colour is that point |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |
| `U` | What would undo do |
| `G` | Sweep the picture with a pointer you can hear |
| `V` / `Shift+V` | Correct the colour / what is wrong with it |
| `Shift+A` | Put a card on it |
| `;` / `'` | Levels / read the histogram |
| `:` / `"` | Levels per channel / which way is the colour pulling |
| `B` | Do all of this to a whole folder |
| `I` | Send it to the project |

### The scanner-bed case

Put a photo on a scanner and it lands sideways, crooked, with white all round
it. Open the scan and it says what it found:

> *one picture found, 2400 by 1600, landscape, rotated 2.4 degrees clockwise,
> filling 38 percent of the scan, with 15 percent empty on the left*

`Shift+F` straightens it and crops to it in one. If there were several
photographs on the bed it says so, and `Shift+S` writes each one out
separately — two photos on one scanner is the normal case, not the exotic one.

None of this is a rectangle you drag. It is measured, reported, and fixed by a
key.

### Resizing

Every arrow press says the new size. The shape is locked by default; unlock it
with `L` and every later press says how far the ratio has drifted. `S` offers
presets named by what they are for — half, double, fit 1080, fit 4K — because
"fit 1080" is a decision and "1920 by 1080" is arithmetic you have to do first.

Enlarging past the original warns that it will look softer, and every change
tells you the print size at its dpi and roughly what the file will weigh.

### Cropping

You do not drag a crop; you **name one and then adjust it**. `C` crops to the
picture, `Shift+C` crops to a shape anchored on a cell — *"square, anchored top
centre"* is one instruction. Then `Shift`+arrows move one edge at a time, each
press saying which edge, how much is being cut and what is left.

### Drawing

Painting is a gesture, so it is replaced by something you can say:

    circle at centre, radius 20 percent, white
    rectangle at bottom left, 30 percent, red
    line from top left to bottom right, yellow
    gradient navy to black
    text "Chapter one" at centre, white

Every shape becomes a **layer in a list** that reads back as the sentence that
made it, and each one says how much of the picture it covered. Text is drawn at
export with a real font, above the other shapes, with an outline chosen from its
own brightness so it stays readable on anything. `K` reads the
whole thing as colours — *"80 percent navy, 20 percent white"* — and `P` reads
the colour at a point, by coordinates or by cell, **named before it is valued**.

### Sweeping

`G` turns on a pointer you can hear. The arrows move it instead of resizing, and
its position is a tone — **panned to where it is, pitched to how far up**. Up is
high, which is the one mapping you do not have to learn.

It only speaks when you cross into a new cell; `Enter` reads what is under it, a
digit jumps to a cell, plus and minus change the step, `F12` says exactly where
it is, and `Escape` leaves.

### Colour

`V` offers corrections by name — brighter, warmer, punchier, lift the shadows —
each a nudge you can apply twice, said back in the units photographers use:
*"exposure up a third of a stop"*, *"warmer, 6100 kelvin"*.

`Shift+V` measures the picture and tells you what is wrong with it, **using the
same words the corrections are called**, so you can act on the advice by
pressing the thing it just named.

### Levels

A levels curve is a graph you drag. But the graph is only a picture of five
numbers, so `;` gives you the numbers: black point, white point, and the
shadows, midtones and highlights between them.

**Auto levels** is the one worth knowing. It finds where the picture really
starts and stops, pulls those to black and white, and tells you the numbers it
picked — so you can adjust its answer rather than just accept it. It warns you
when the stretch is hard enough to show banding.

`'` reads the histogram as five numbers with the shape said first — *"bunched in
the shadows"*, *"almost all midtones, so it will look flat"*.

### Colour casts

Temperature makes a picture warmer or cooler, which is one axis. A yellowed
page or a mixed light is off in a direction that axis does not pass through, and
no amount of "warmer" fixes it — so `:` gives you the channels.

`"` says which way the colour is pulling: *"a warm cast, 40 percent"*. **Auto
colour levels** stretches each channel to its own range, which removes the cast.

Best of all: turn on the pointer with `G`, sweep to something that ought to be
grey — a wall, a shirt, the paper a photograph is printed on — and press `W`.
That is the eyedropper white balance, done without pointing at anything, and it
is the most reliable correction there is because it uses a fact about the scene
rather than an assumption about the average.

### A whole folder at once

`B` does to a folder what you have just done to the picture on screen. **The
corrections travel; the geometry does not** — each picture is found,
straightened and cropped on its own terms, because a photograph lands somewhere
different on the scanner every time.

It tells you how many pictures and what each will get, then asks before running.
It counts out loud as it goes, skips anything it cannot read and tells you which,
and will not overwrite your originals.

### Shared with the video

`Shift+A` puts a **card** on the picture and opens the same card editor the
timeline uses — a lower third over a photograph is the same object as one over a
clip. `I` saves the picture and puts it in the **media bin**, so something you
have just straightened can go on the timeline without leaving the application.

---

## 10d. The viewfinder **[built]**

`F9` on a video track with a camera chosen. It says *"opening the camera"*
before it does, every time.

**Silence means you are framed.** That is the whole design: you stop moving when
the sound stops, rather than interpreting anything.

Until then you hear a tone **panned** to where your face is, **pitched** to how
far up it is, and **ticking faster** as you get too close. Words only when the
guidance changes — "move left", "raise the camera", "move back".

`F8` is the talking viewfinder: what is actually in shot, rather than where you
are in it. `Escape` closes it and turns the camera off.

The detection is by skin colour and shape, tested across the range of skin
tones. It finds a face; it does not identify one.

---

## 11. Saving your work **[built]**

| Key | Action |
|---|---|
| `Ctrl+N` / `Ctrl+O` | New project / open one |
| `Ctrl+S` / `Ctrl+Shift+S` | Save / save somewhere else |
| `Ctrl+Shift+O` | Recent projects |

A project is a folder with a `project.json` in it. Saving writes to a temporary
file and moves it, so a crash mid-save cannot leave you with half a project.

Every edit marks the project unsaved. Opening another one or starting a new one
asks first, and **No has the focus** — it is the answer you get by pressing
Enter without having listened.

---

## 12. Files

    project.json   the project — canonical
    edit.md        a text export that reads back in
    media/         imported footage
    work/          transcripts, segment cache
    out/           master and captions.srt

`edit.md` is a **live second face of the document**, not a backup. It is written
on every save, and reading it back in keeps segment identities intact — so
hand-editing it in pluma, or having Claude edit it, both work.

---

## 13. Keyboard scheme

The bindings follow a rule, so unfamiliar ones are guessable:

| Shape | Meaning |
|---|---|
| **Plain letter** | A verb at the cursor — split, take, record. Only where nothing is a text field. |
| **`Ctrl` + letter** | Document or project scope — new, open, save, new track |
| **`Shift` + letter** | The bigger or opposite variant — `Shift+S` splits all tracks |
| **`Ctrl` + arrows** | Navigate by structure — segment to segment |
| **`Alt` + arrows** | Adjust the thing under the cursor — nudge, trim |
| **`Shift` + punctuation** | Jump to an edge |
| **`Ctrl` + digit** | Switch view |
| **Function keys** | Views, reports and help |

Every binding records **where it came from** — Premiere, Reaper, a universal
editor convention, or invented here. Roughly two thirds are borrowed; the rest
are ours and open to change. `AccessibleVideoEditor.Cli keys` prints the lot.

**There are no modal tools.** Premiere and Resolve make you pick a razor tool
before you can cut. That is a trap without sight — you cannot see which mode you
are in, and the same key does different things depending on a state you cannot
check. Here every tool is a verb: press the key, the action happens at the
cursor. A toolbar may exist for sighted collaborators, but it is never the only
route to anything.
