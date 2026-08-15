# A Windows and Mac client

**GTK stays the Linux client.** It works, Orca reads it correctly, and nothing
here proposes replacing it. This is about a *second* head, for Windows and
possibly macOS, over the same Core.

Nothing has been built yet. This is the decision written down before the work,
because the last time a toolkit was chosen the choice was wrong and three weeks
of front end went in the bin.

---

## What is actually being ported

Very little, which is the point.

| Project | Ports as-is |
|---|---|
| `Core` | Yes. Pure C#, no UI, no platform calls |
| `Engine` | Yes, once `ffmpeg` and `ffprobe` are found rather than assumed on `PATH` |
| `Audio` | Yes. SDL2 exists on all three platforms |
| `Playback` | Yes. libmpv exists on all three |
| `Speech` | The interface ports; the one implementation does not |
| `Vision` | Capture is behind an interface; the X11 grab and `/dev/video0` are not |
| `Gtk` | No. This is the whole job |

So the work is **one front end plus two device backends**. The interaction model
itself is already portable: it lives in `CommandRegistry`, not in the widgets.

## The one thing that decides it

Not the language, not the layout system, not how modern the framework is.

**Can a custom-drawn timeline expose itself to a screen reader, and can the
application push an announcement with a priority?**

Everything this editor does rests on those two. The timeline is drawn by Cairo
today and read through a `GtkListBox` beside it; announcements go through
`gtk_accessible_announce`, which takes a priority so an urgent message can
overtake a chatty one. A toolkit that cannot do both is not a candidate,
however pleasant the rest of it is.

---

## The options

### .NET MAUI

One codebase, both platforms, C#, shares Core directly. The obvious answer, and
the weakest one on the question that matters.

- **Windows** is WinUI 3, which maps to UIA. That part is sound.
- **macOS** is Mac Catalyst - UIKit pretending to be a Mac app, bridged to
  NSAccessibility. It is MAUI's least-exercised target, and custom controls
  there are the least-exercised part of it.
- Accessibility is `SemanticProperties` plus `SemanticScreenReader.Announce`.
  That is **one method with no priority and no interrupt**, which is a genuine
  regression from what GTK already gives.
- A custom-drawn timeline means dropping through MAUI to a WinUI
  `AutomationPeer` on one platform and to Catalyst on the other - so the "one
  codebase" saving mostly evaporates exactly where the difficulty is.

**Verdict: no.** It optimises for the part that is easy here and is weakest at
the part that is hard.

### WPF on Windows, native on macOS

Two front ends, each best-in-class.

- **WPF has the most mature UIA implementation there is.** `AutomationPeer` is
  a well-trodden path, NVDA and JAWS handle custom WPF controls correctly, and
  `AutomationPeer.RaiseNotificationEvent` is a real equivalent of
  `gtk_accessible_announce` - it carries a priority and a processing hint.
  It runs on .NET 10 and would consume Core, Engine, Audio and Playback
  unchanged.
- **macOS** would be a native AppKit front end over the JSON-RPC-over-stdio
  seam the architecture already anticipated, or over a thin C# shim.
  NSAccessibility with a real `NSAccessibilityElement` is the only way to get
  VoiceOver to read a custom timeline properly.

**Verdict: the best result, and the most work.** Two heads rather than one.

### Avalonia, Windows only

Worth naming because it was tried and because the failure was specific: it was
the **Linux AT-SPI bridge** that Orca could not see. Avalonia's UIA backend on
Windows is its strongest, and the drawing model is close to what already exists.

**Verdict: a credible Windows-only option, and a reason for caution.** We have
first-hand evidence that Avalonia's accessibility quality varies sharply by
platform, which is exactly the property that makes it risky to extend to macOS
on faith.

### A web front end

ARIA is the best-documented accessibility API in existence, live regions work in
every screen reader, and one build covers both platforms.

It fails on the other half of this application. The viewfinder tone, the audible
VU meter and the audio scrub are **low-latency continuous audio**, and Web Audio
in an Electron shell is the wrong tool for a tone that has to track your head
movement without lag. Those are not decoration here; they are how framing and
levels are perceived at all.

**Verdict: no**, and interesting that the reason is audio rather than
accessibility.

---

## The recommendation

**Windows first, with WPF, starting with a spike.**

1. Windows is where NVDA and JAWS users are, so it is the larger audience.
2. WPF's UIA support is the closest match to what GTK gives today, including a
   real announcement channel with a priority.
3. It is C#, so everything below the UI comes free.
4. macOS is deferred rather than guessed at. Doing it natively later is more
   work than a shared framework and is the only route that gets VoiceOver
   reading a custom timeline properly.

### The spike, before any of it

Exactly the shape of the one that killed Avalonia, because that lesson is
already paid for:

- A four-track scrubber as a custom-drawn control with an `AutomationPeer`
- Announcements through `RaiseNotificationEvent`, at two priorities
- Arrow-key navigation with the real announcement strings from `TrackProbe`

Then run it under NVDA **and** JAWS. If both read it, build the head. If they do
not, the answer is a native Win32/UIA control or WinUI, and better to find that
out in a day than after five views.

**Nothing else gets built until the spike is run.** That is the rule this
project learnt the expensive way.

### What else has to be solved

Not accessibility, but real:

- **ffmpeg and Whisper are assumed to be on `PATH`.** That is a Linux
  personal-build decision. Windows and macOS need either a bundled binary - which
  reopens the x264 GPL question the project deliberately closed - or a settings
  path with a clear failure message when it is wrong.
- **Capture is X11 and `/dev/video0`.** Windows needs DirectShow or Media
  Foundation; macOS needs AVFoundation. Both sit behind the existing interface,
  which is why that interface exists.
- **`Fonts.cs` looks in Linux font directories.**
