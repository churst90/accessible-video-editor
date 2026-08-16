# The Windows scrubber spike

**This is not the Windows client. It is the one question that decides whether
there will be one.**

The last time a toolkit was chosen without asking it first, Orca could not see
an Avalonia window at all - not even its title - and three weeks of front end
went in the bin. `docs/CLIENTS.md` records the rule that came out of that:
*spike first, always.*

---

## The question

> Can a custom-drawn timeline expose itself to a screen reader, and can the
> application push an announcement **with a priority**?

Everything else - the language, the layout system, how modern the framework
is - is noise. GTK gives both today. MAUI cannot give the second one at all,
which is why it was rejected on paper. WPF claims to give both, and this is
where that claim gets tested.

## What is here

A four-track scrubber, drawn rather than assembled out of widgets, in about 500
lines.

| File | What it is for |
|---|---|
| `UiaAnnouncer.cs` | The announcement channel, over `RaiseNotificationEvent`. Three priorities |
| `Controls/TimelineCanvas.cs` | The drawn control, its keys, and `OnRender` |
| `Controls/TimelineCanvasPeer.cs` | **The load-bearing file.** The peer tree that makes pixels readable |
| `MainWindow.xaml(.cs)` | The host, and a status line that is also a live region |

It references **`Core` and nothing else**. Every rectangle comes from
`TimelineLayout.Build`, every spoken string from `TrackProbe.Announce`, the
stepping from `TimelineNavigator`, and the four tracks from `DemoProject`. None
of it was rewritten for Windows - which is itself a finding, because the claim
that the interaction model is portable rests on exactly that.

## Running it

Windows, .NET 10 SDK. It is **deliberately not in `AccessibleVideoEditor.slnx`**,
because WPF does not build on Linux and the Linux client must keep building on
the machine it is developed on.

    cd spikes\AccessibleVideoEditor.WpfSpike
    dotnet run

Then start NVDA. Then do it again with JAWS.

**It compiles, and it has never been run.** Those are different things and the
difference is the whole point of a spike. It was written and compiled on Linux -
`dotnet build -p:EnableWindowsTargeting=true` builds a WPF project against the
reference assemblies without a Windows machine, cleanly, no warnings - so the
peer tree and the announcer are known to be valid C# against the real WPF API.
Whether NVDA can *read* any of it is exactly what nobody knows yet.

## What to check, in order

Each of these is a separate way the thing can fail, and they fail differently.

**1. Does the window exist at all?**
Press `Insert+T` for the title. This is the check Avalonia failed. If the title
does not read, stop - nothing after it matters.

**2. Is the timeline in the tree?**
Navigate to it. It should read as a **list** called "Timeline". Then arrow
through its items with the screen reader's own object navigation: four rows,
"Programme", "B-roll", "Graphics", "Music", each followed by what is under the
cursor.

**3. Do the announcements arrive?**
Press an arrow. You should hear the position spoken - the same sentence the
Linux client speaks, because it is the same method producing it.

**4. Do the priorities work?** *This is the one MAUI cannot do.*
- Hold `Right` down. You should hear the position you have **arrived at**, not
  a queue of every position you passed through. That is `MostRecent` working.
- Now hold `Right` and press `E` while it is still talking. `E` raises an
  urgent message. It should **interrupt**, not wait its turn.

If 4 fails but 1 to 3 pass, WPF is usable and the announcer needs different
processing hints - a fixable problem. If 2 fails, the peer tree is not reaching
the reader, and the answer is a native Win32/UIA control or WinUI instead.

**5. Does the selected track read as selected?**
Press `Up` and `Down`. The focused row implements `ISelectionItemProvider`, so
the reader should say which row is current rather than just reading a name.

**6. Does it read the same in JAWS?**
Not optional. NVDA and JAWS disagree about custom controls often enough that one
of them passing proves nothing about the other.

## Recording the result

Write what happened at the top of `docs/CLIENTS.md`, including the versions of
NVDA and JAWS. A spike whose result lives only in someone's memory has to be run
again.

- **Both read it** → build the head, in the order `CLIENTS.md` gives: key
  router, then timeline, tracks, transcript, bin, stream, image.
- **Neither reads it** → the answer is native Win32/UIA or WinUI, and this cost
  a day rather than a month.
- **One reads it** → find out which part differs before writing anything else.

## What this spike deliberately does not do

No ffmpeg, no capture, no audio, no editing, no menus, no persistence. Those are
all known quantities - they are C# over Core and they will port. Anything added
here that is not the peer tree or the announcer is time spent not answering the
question.
