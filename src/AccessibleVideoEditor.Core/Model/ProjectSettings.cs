namespace AccessibleVideoEditor.Core.Model;

public sealed class ProjectSettings
{
    public int CanvasWidth { get; set; } = 1920;
    public int CanvasHeight { get; set; } = 1080;
    public double Fps { get; set; } = 30;

    /// <summary>Spans are padded outward so cuts do not clip the start or tail of a word.</summary>
    public double SpanPadIn { get; set; } = 0.08;

    public double SpanPadOut { get; set; } = 0.20;

    /// <summary>
    /// Adjacent speech from the same shot gets a short dissolve - long enough
    /// to hide a jump cut, short enough to read as a cut.
    /// </summary>
    public double JumpCutDuration { get; set; } = 0.12;

    /// <summary>Everything else gets a scene transition.</summary>
    public double SceneTransitionDuration { get; set; } = 0.40;

    public RippleMode RippleMode { get; set; } = RippleMode.AllTracks;

    /// <summary>
    /// How much the announcer says on each cursor move. Terse is the default
    /// because full detail on every Left/Right press makes the timeline too
    /// slow to move through; detail is available on demand instead.
    /// </summary>
    public Verbosity Verbosity { get; set; } = Verbosity.Terse;

    /// <summary>Play a blip of source audio when the cursor moves. The core of scrubbing by ear.</summary>
    public bool AudioScrub { get; set; } = true;

    public double AudioScrubLength { get; set; } = 0.18;

    public bool Earcons { get; set; } = true;

    /// <summary>
    /// Where playback and scrub are heard. Null uses the system default.
    ///
    /// Separate from any capture device: you can perfectly well record from an
    /// interface while monitoring on headphones, and assuming they are the same
    /// is how people end up listening to the wrong thing.
    /// </summary>
    public string? MonitorOutputId { get; set; }

    public string? MonitorOutputName { get; set; }

    /// <summary>
    /// How long a still is on screen when inserted. An image has no duration of
    /// its own, so one has to be chosen - and four seconds is about how long a
    /// photograph holds attention before it starts to feel static.
    /// </summary>
    public double StillDuration { get; set; } = 4;

    /// <summary>
    /// Off. A slideshow, or a two-second flash of a photograph, does not want a
    /// slow drift over it - and a still that moves when you did not ask reads
    /// as a mistake. It is an effect you add, on Ctrl+Shift+B.
    /// </summary>
    public bool KenBurnsByDefault { get; set; }

    /// <summary>Snap selections and the cursor to boundaries, word starts and markers.</summary>
    public bool Snap { get; set; } = true;

    public double TargetLoudnessLufs { get; set; } = -14;
    public double TargetTruePeakDb { get; set; } = -1.5;
}

public enum Verbosity
{
    /// <summary>Position only, and only what changed.</summary>
    Terse,

    /// <summary>Position plus the element under the cursor.</summary>
    Normal,

    /// <summary>Everything: element, track, transition, overlays in force.</summary>
    Verbose,
}
