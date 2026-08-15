using AccessibleVideoEditor.Core;

namespace AccessibleVideoEditor.Audio;

/// <summary>
/// Low-latency output for earcons, the viewfinder tone and audio scrub.
///
/// Implemented over SDL2 by P/Invoke - the library is already present on all
/// three targets and needs no NuGet native runtime package, which matters on a
/// source distribution. The hard constraint is latency: viewfinder feedback
/// has to land under about 100 ms or you cannot close the loop with your own
/// body movement, so the synth runs in the audio callback rather than being
/// scheduled from the UI thread.
/// </summary>
public interface IAudioOutput : IDisposable
{
    int SampleRate { get; }

    void Start();
    void Stop();

    /// <summary>Installs the generator the callback pulls from. Null silences it.</summary>
    void SetGenerator(IToneGenerator? generator);

    /// <summary>Fire-and-forget one-shot, mixed above the generator.</summary>
    void PlayEarcon(AccessibleVideoEditor.Speech.Earcon earcon);
}

public interface IToneGenerator
{
    /// <summary>Fills an interleaved stereo buffer. Called on the audio thread; must not allocate.</summary>
    void Fill(Span<float> stereoBuffer);
}

/// <summary>
/// Plays a short blip of source audio wherever the cursor lands. This is what
/// makes the scrubber usable - reading timestamps aloud is not how anyone finds
/// a cut point. At word granularity you hear the word.
/// </summary>
public interface IScrubPlayer
{
    /// <summary>Length is <see cref="Core.Model.ProjectSettings.AudioScrubLength"/>.</summary>
    Task ScrubAsync(SourceId source, double sourceTime, double length, CancellationToken cancellationToken = default);
}
