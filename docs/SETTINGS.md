# Settings

There are three places a setting can live, and which one it belongs in is
decided by a single question:

> If you copied this project to another computer, should the setting go with it?

- **Yes** → it is a **project setting**. It describes the video.
- **No** → it is an **application setting**. It describes you and this machine.
- **Never** → it is a **secret**. It describes nothing; it is a password.

A canvas size travels with the project. Your microphone does not. Your stream
key must not.

---

## Where they are

| | File | Contains |
|---|---|---|
| Project | inside the project | everything that describes the video |
| Application | `~/.config/video/settings.json` | you, this machine, and what new projects start from |
| Secrets | `~/.config/video/secrets.json` | stream keys and access tokens, owner-readable only |

`XDG_CONFIG_HOME` is honoured if it is set.

**`Ctrl+,` edits the application settings** without opening the file. Everything
except the secrets: those are set in the streamer view and are never read back
anywhere, which a settings window that could show them would undo.

A project folder also holds **`project.autosave.json`** — the quiet save, beside
the project rather than over it, so `project.json` stays the state you last
chose. It is deleted on every explicit save, and offered on open when it is
newer. See the manual's section 11.

### Why secrets are a separate file

Three practical reasons, not one theoretical one:

1. It can be given **owner-only permissions**. The settings file does not need
   them and would lose them the first time anything rewrote it.
2. Settings can be **copied, shared, or pasted into a bug report**. A stream key
   lets anyone broadcast as you. Those two facts cannot share a file.
3. Backing up your configuration and backing up your credentials are different
   decisions. One file forces them to be the same decision.

**Nothing in the secret store is ever spoken, shown in the status line, or
written to a log.** Only *whether* something is set — `Shift+K` in the streamer
view reads back *"saved: twitch stream key, youtube api key"* and never a value.

---

## Application settings **[built]**

### Who you are

| Setting | Why it is here |
|---|---|
| `DisplayName` | So your name can be picked out of chat. Not an account name — people rarely get called by their login |

### What a new project starts from

`Defaults` is a whole `ProjectSettings`. It is the exception that proves the
rule: these *are* project settings, kept here so that "I always work at 30
frames per second" is said once rather than on every project.

Canvas size, frame rate, span padding, jump-cut length, still duration, Ken
Burns, loudness targets — all of it.

### How the application behaves

| Setting | Default |
|---|---|
| `Verbosity` | terse |
| `Earcons` | on |
| `FollowPlayback` | on |
| `ChatSpeaking` | mentions, first-timers, questions, events |
| `SpeakEveryChatMessage` | off |
| `ChatBurst` / `ChatBurstWindow` | 6 messages in 4 seconds |
| `OutputDirectory` | where renders go when nothing else is said |

The chat burst numbers are adjustable because **how much speech is too much is
genuinely personal**, and a rate limit somebody else chose is one they will turn
off.

### Devices

Camera, microphone, monitoring output, and the input channel on an interface.
All of this describes the hardware in front of you, not the video you are
making — the Focusrite case is exactly why it is per-machine.

### Streaming

Destinations (**without their keys**), the Twitch channel, the YouTube and
Facebook live video ids, whether chat connects when the view opens, and the
broadcast size and frame rate.

The live video ids are remembered but expected to be replaced — they change for
every broadcast, and the prompt is pre-filled with the last one rather than
pretending it is permanent.

### Where the tools are

`ffmpeg`, `ffprobe`, `claude`, the Whisper virtual environment, and the cache
directory. Machine-specific by definition.

---

## Secrets **[built]**

| Name | What it unlocks |
|---|---|
| `stream-key.Twitch` / `.YouTube` / `.Facebook` / `.Custom` | Broadcasting to that service |
| `twitch.token` | Sending in Twitch chat, and moderating |
| `twitch.clientId` | Required alongside the token by Twitch's API |
| `youtube.apiKey` | **Reading** a public YouTube live chat |
| `youtube.oauth` | Posting, deleting and banning on YouTube |
| `facebook.token` | Reading and moderating Facebook live comments |

Note the split on YouTube: an **API key alone gets you a working chat pane**,
which is far less to set up than the OAuth application that posting needs. The
two are kept separate so the easy half is available immediately.

---

## Failure behaviour

- A **missing** settings file is normal. Defaults, no complaint.
- A **broken** settings file gives defaults and says so. Refusing to start over
  a stray comma is a worse outcome than starting fresh.
- An **unreadable** secrets file means no secrets, not a failure to start —
  everything that needs one already says what it is missing.

---

## Still to sketch

A settings **view**, so all of the above is editable inside the application
rather than by editing JSON. The model is built and every value already has a
sensible default; what is missing is the pane. It belongs with the image editor
work, because both need the same thing: a list of properties that reads well and
edits one field at a time.
