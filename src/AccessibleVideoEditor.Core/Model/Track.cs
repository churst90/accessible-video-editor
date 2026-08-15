namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// Tracks are the vertical axis of the scrubber: Up/Down moves focus between
/// them, Left/Right moves along the focused one.
/// </summary>
public sealed class Track
{
    public required TrackId Id { get; init; }

    /// <summary>Freely renameable. Spoken first when focus moves to this track.</summary>
    public required string Name { get; set; }

    /// <summary>What the track does in the edit.</summary>
    public required TrackKind Kind { get; init; }

    /// <summary>
    /// What kind of media it carries. Separate from <see cref="Kind"/> because
    /// the role and the medium are different questions, and the medium is what
    /// decides whether a paste is legal.
    /// </summary>
    public TrackMedia Media { get; set; } = TrackMedia.Video;

    /// <summary>Top-to-bottom position in the scrubber.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Armed means three things at once, because they are one intent: new
    /// recordings land here, <see cref="CaptureDeviceId"/> is the device that
    /// feeds it, and arming runs the signal check (non-silent mic, non-black
    /// frames, disk space). You cannot arm a dead device without being told.
    /// </summary>
    public bool Armed { get; set; }

    /// <summary>
    /// The device this track records from. A property of the track, not of a
    /// recording session - which is what makes a separate record view
    /// unnecessary.
    /// </summary>
    public string? CaptureDeviceId { get; set; }

    /// <summary>The human name, kept so the UI can say it without re-enumerating.</summary>
    public string? CaptureDeviceName { get; set; }

    /// <summary>
    /// Which channel of a multi-input interface to record. A two-input
    /// interface presents as one stereo source, so recording it whole would put
    /// a microphone on the left and silence on the right - which sounds like a
    /// broken take and is not obvious without looking at a meter.
    /// </summary>
    public InputChannel Channel { get; set; } = InputChannel.All;

    public bool Muted { get; set; }
    public bool Soloed { get; set; }

    /// <summary>Locked tracks are excluded from ripple, even in RippleAll mode.</summary>
    public bool Locked { get; set; }

    public double GainDb { get; set; }

    public bool IsAudible(bool anyTrackSoloed) => !Muted && (!anyTrackSoloed || Soloed);

    /// <summary>
    /// What kind of input this track can record from. The track's medium
    /// decides: a video track offers cameras, an audio track microphones, and
    /// an image track records nothing at all.
    /// </summary>
    public TrackInput AcceptsInput => Media switch
    {
        TrackMedia.Video or TrackMedia.Mixed => TrackInput.Camera,
        TrackMedia.Audio => TrackInput.Microphone,
        _ => TrackInput.None,
    };

    /// <summary>
    /// Spoken when focus lands on the track. Name and medium always; state
    /// flags only when they are on, so a plain track announces in three words.
    /// </summary>
    public string Describe()
    {
        var flags = new List<string>();
        if (Armed) flags.Add("armed");
        if (Muted) flags.Add("muted");
        if (Soloed) flags.Add("soloed");
        if (Locked) flags.Add("locked");

        var medium = Media switch
        {
            TrackMedia.Video => "video",
            TrackMedia.Audio => "audio",
            TrackMedia.Image => "image",
            _ => "mixed",
        };

        // The bound device is named only when the track is armed - it is
        // relevant when you are about to record and noise the rest of the time.
        if (Armed && CaptureDeviceName is { Length: > 0 } device) flags.Add($"input {device}");

        return flags.Count == 0
            ? $"{Name}, {medium} track"
            : $"{Name}, {medium} track, {string.Join(", ", flags)}";
    }
}

/// <summary>
/// Which channel of a capture device to record. Relevant for audio interfaces
/// with more than one input on a single device.
/// </summary>
public enum InputChannel
{
    /// <summary>Everything the device offers, as it comes.</summary>
    All,

    /// <summary>Left, or input 1.</summary>
    Left,

    /// <summary>Right, or input 2.</summary>
    Right,
}

/// <summary>What a track can record from, derived from its medium.</summary>
public enum TrackInput
{
    None,
    Camera,
    Microphone,
}

public enum TrackMedia
{
    Video,
    Audio,
    Image,

    /// <summary>Picture and sound together, as the programme track carries.</summary>
    Mixed,
}

public enum TrackKind
{
    /// <summary>The spine. Exactly one per project; driven by the transcript.</summary>
    Programme,

    /// <summary>B-roll and cutaway picture over the programme's audio.</summary>
    Overlay,

    /// <summary>Titles and graphics, placed by <see cref="Placement"/>.</summary>
    Graphics,

    /// <summary>Music beds and additional audio.</summary>
    Audio,
}

/// <summary>
/// Whether an edit on one track shifts the others. Default is
/// <see cref="RippleMode.AllTracks"/> because transcript-driven editing assumes
/// sync - but the mode is always announced when it changes, since a silent
/// ripple mode is how an edit gets destroyed.
/// </summary>
public enum RippleMode
{
    Off,
    FocusedTrack,
    AllTracks,
}
