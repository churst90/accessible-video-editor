# Audit

Measured on the whole tree, not sampled. Numbers are from the source, not from
memory.

## 1. What is in the code but not reachable from the interface

**Nine commands remain unbuilt.** Every one of them announces what it will do
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

Everything else is reachable. Verified by two checks that now run over the tree:
every `win.` action has a handler, and every menu id exists in the command
registry. Both come back clean, and the second one found two View menu items
that had been silently missing since they were written.

## 2. Bugs and design issues

### Fixed during this audit

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

### Open, in the order they matter

1. **`MainWindow` is 4,855 lines.** It is the only file in the tree that is
   genuinely too big. It holds five views' key handlers, every command
   registration and every dialog. The seam is obvious - each view already has a
   `On…Key` method and a build method - and `StreamView`, `ImageView` and
   `TimelineCanvas` show the shape the rest should follow. Nothing is wrong with
   it; it is just where everything without a home ended up.

2. **Twelve `async void` methods**, all in the front end. Each is an entry point
   called from a key or a menu, which is the correct signature there - but an
   exception inside one takes the process down instead of being announced. The
   exposure is smaller than the count suggests, because every one of them awaits
   a method that already catches and returns a message rather than throwing. It
   is a latent risk rather than a live bug, and the fix is one helper that wraps
   the body and speaks the failure.

3. **The image editor's raster is decoded twice** - once grey for analysis, once
   in colour for the cast. The grey one is derivable from the colour one.

4. **`Refresh()` rebuilds every track row on every edit**, including during
   playback. It has not been a problem at the sizes tried, but it is O(tracks)
   work on a 100 ms timer.

5. **No project autosave.** Saving is explicit and reliable, and the window says
   so on the way out, but an eight-hour session still rests on remembering
   `Ctrl+S`.

## 3. Comments and cleanliness

**27,861 lines of source. 15 percent comments, 18 percent blank.**

That ratio is defensible for this codebase, because a large share of the
comments carry decisions that are not recoverable from the code - *why* silence
means framed, *why* ducking is off by default, *why* Twitch moderation cannot go
over IRC. Those are worth their space; they are the reasoning that would
otherwise be re-litigated.

Where it is too heavy: the newest files run 14 to 16 percent, and some of that
is restating what the next line says. `ImageIo` and `ImageEdits` are the
clearest examples.

Where it is too light: `MainWindow` at 10 percent and `StreamView` at 7 percent
are the two files where a reader most needs help, because they are the biggest.

**Grade: B+.** Consistent naming, no dead code, no `TODO`s, no swallowed
exceptions that hide a failure from the user, no `.Result` or `.Wait()`
anywhere, and 623 tests that assert on behaviour and spoken output rather than
on implementation. It is held back from an A by one oversized file and by
comment density that has drifted up in the newest work.

## 4. Is the editing complete?

The verbs that exist: split, heal, trim head and tail, roll, ripple delete,
lift, disable, mute, hide, retime, fades, transitions, takes, insert and
overwrite from the bin, detach and reattach audio, duration and Ken Burns for
stills, and cut, copy and paste across tracks. Every one is reachable, announced
and undoable.

**What a working editor still wants and this does not have:**

| Missing | Why it matters |
|---|---|
| **Markers** | The most-used feature in every NLE. Chapters, notes, "fix this" |
| **Adding titles, graphics and b-roll** | They render; they cannot be created |
| **Nested or compound clips** | Grouping a sequence and treating it as one |
| **Subclips** | Naming a range of a source and reusing it |
| **Multicam angle switching** | The recording side exists; the switching does not |
| **Audio effects** | EQ, compression, noise reduction. `afftdn` is installed and would help every laptop-mic recording |
| **Keyframes of any kind** | Volume over time, position over time |
| **Autosave** | See above |

The commands that do exist are coherent: one verb, one key, one announcement,
one undo entry. The keyboard scheme is documented with provenance for every
binding, and a test asserts no two commands claim the same key in a view where
both are live.
