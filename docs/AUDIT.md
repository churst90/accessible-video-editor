# Audit

Measured on the whole tree, not sampled. Numbers are from the source, not from
memory.

## 1. What is in the code but not reachable from the interface

**Was: nine commands. Now: none.** All nine were built - markers, transcript
search, the command palette, the to-do list, "read me the edit", reloading a
hand-edited `edit.md`, and creating titles, graphics and b-roll.

The last three were the most misleading: b-roll, titles and graphics rendered
correctly and could be described, but could not be created. A project made in
this application can now contain one.

Two checks run over the tree and both come back clean: every `win.` action has a
handler, and every menu id exists in the command registry.

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

### Open, in the order they matter

1. **`MainWindow` is 4,390 lines**, still the largest file. It is the only file in the tree that is
   genuinely too big. It holds five views' key handlers, every command
   registration and every dialog. The seam is obvious - each view already has a
   `On…Key` method and a build method - and `StreamView`, `ImageView` and
   `TimelineCanvas` show the shape the rest should follow. Nothing is wrong with
   it; it is just where everything without a home ended up.

2. **`StreamView` at 1,048 lines** is heading the same way as the window did.

3. **No keyframes anywhere.** Volume over time and position over time are the
   two that a finished edit eventually wants.

## 3. Comments and cleanliness

**28,341 lines of source. 14 percent comments.** Was 15 percent across 27,861
lines before this pass, with 91 doc blocks of nine lines or more; there are now
59, and the rule applied throughout is the same: **a comment keeps the reason
and drops the restatement.** Where a summary elaborated on its own first
paragraph, the elaboration went.

That ratio is defensible for this codebase, because a large share of the
comments carry decisions that are not recoverable from the code - *why* silence
means framed, *why* ducking is off by default, *why* Twitch moderation cannot go
over IRC. Those are worth their space; they are the reasoning that would
otherwise be re-litigated.

**Grade: A-.** Consistent naming, no dead code, no `TODO`s, no swallowed
exceptions that hide a failure from the user, no `.Result` or `.Wait()`
anywhere, and 623 tests that assert on behaviour and spoken output rather than
on implementation. It is held back from an A by two files that are still
larger than they should be.

## 4. Is the editing complete?

The verbs that exist: split, heal, trim head and tail, roll, ripple delete,
lift, disable, mute, hide, retime, fades, transitions, takes, insert and
overwrite from the bin, detach and reattach audio, duration and Ken Burns for
stills, and cut, copy and paste across tracks. Every one is reachable, announced
and undoable.

**What a working editor still wants and this does not have:**

| Missing | Why it matters |
|---|---|
| **Nested or compound clips** | Grouping a sequence and treating it as one |
| **Subclips** | Naming a range of a source and reusing it |
| **Multicam angle switching** | The recording side exists; the switching does not |
| **Audio effects** | EQ, compression, noise reduction. `afftdn` is installed and would help every laptop-mic recording |
| **Keyframes of any kind** | Volume over time, position over time |

The commands that do exist are coherent: one verb, one key, one announcement,
one undo entry. The keyboard scheme is documented with provenance for every
binding, and a test asserts no two commands claim the same key in a view where
both are live.
