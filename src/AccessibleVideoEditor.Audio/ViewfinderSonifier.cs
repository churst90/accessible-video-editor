using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Audio;

/// <summary>
/// Turns a framing error into a tone. The mapping is a pure function so it can
/// be tested and tuned without a camera in the loop.
///
/// Three channels, chosen because they are perceptually orthogonal - you can
/// read all three at once:
/// <list type="bullet">
/// <item><b>Pan</b> - horizontal offset. Double-encoded into pitch bend, because
/// pan is unreliable on laptop speakers.</item>
/// <item><b>Pitch</b> - vertical offset from the target eyeline.</item>
/// <item><b>Beep tempo</b> - distance. Parking-sensor logic.</item>
/// </list>
///
/// On target the tone stops. Silence-on-target is not a detail: a tone that
/// plays for the whole take is unusable, and a "locked" chime followed by quiet
/// is what lets you actually start talking.
/// </summary>
public static class ViewfinderSonifier
{
    /// <summary>
    /// Framing is judged against the eyeline sitting a third of the way down the
    /// frame, not against centre. Centring a face vertically is bad framing, and
    /// a tool that trains you into it is worse than no tool.
    /// </summary>
    public const double TargetEyelineY = 1.0 / 3.0;

    public const double TargetFaceWidth = 0.28;

    /// <summary>Inside this, you are framed. Wide enough that breathing does not re-trigger it.</summary>
    public const double DeadZone = 0.04;

    public static SonificationState Evaluate(FramingError error)
    {
        if (!error.FaceVisible)
        {
            return new SonificationState(
                Locked: false,
                Silent: false,
                Pan: 0,
                PitchHz: 110,
                BeepsPerSecond: 1.5,
                Guidance: "no face detected");
        }

        var horizontal = error.CentreX - 0.5;
        var vertical = error.EyelineY - TargetEyelineY;
        var size = error.FaceWidth - TargetFaceWidth;

        var onTarget = Math.Abs(horizontal) < DeadZone
                       && Math.Abs(vertical) < DeadZone
                       && Math.Abs(size) < TargetFaceWidth * 0.35;

        if (onTarget)
        {
            return new SonificationState(
                Locked: true,
                Silent: true,
                Pan: 0,
                PitchHz: 440,
                BeepsPerSecond: 0,
                Guidance: "framed");
        }

        // Pan follows the face, so the tone moves the way you need to move.
        var pan = Math.Clamp(horizontal * 2.5, -1, 1);

        // One octave either side of A4 across the usable vertical range.
        var pitch = 440 * Math.Pow(2, Math.Clamp(-vertical * 3.0, -1, 1));

        // Too close speeds up, too far slows down, like a reversing sensor.
        var tempo = size > 0
            ? 2 + Math.Clamp(size / TargetFaceWidth, 0, 1) * 8
            : 2 - Math.Clamp(-size / TargetFaceWidth, 0, 1) * 1.4;

        return new SonificationState(
            Locked: false,
            Silent: false,
            Pan: pan,
            PitchHz: pitch,
            BeepsPerSecond: Math.Max(0.5, tempo),
            Guidance: Describe(horizontal, vertical, size, error));
    }

    /// <summary>
    /// The alternative to tones, for people who would rather be told. Also what
    /// the drift log records during a take.
    /// </summary>
    private static string Describe(double horizontal, double vertical, double size, FramingError error)
    {
        if (error.CroppedTop) return "you are cropped at the top";
        if (error.CroppedBottom) return "you are cropped at the bottom";

        var parts = new List<string>();

        if (Math.Abs(horizontal) >= DeadZone)
        {
            parts.Add(horizontal > 0 ? "move left" : "move right");
        }

        if (Math.Abs(vertical) >= DeadZone)
        {
            parts.Add(vertical > 0 ? "raise the camera" : "lower the camera");
        }

        if (Math.Abs(size) >= TargetFaceWidth * 0.35)
        {
            parts.Add(size > 0 ? "move back" : "move closer");
        }

        return parts.Count == 0 ? "close" : string.Join(", ", parts);
    }
}

/// <summary>What the synth should be doing right now.</summary>
public readonly record struct SonificationState(
    bool Locked,
    bool Silent,
    double Pan,
    double PitchHz,
    double BeepsPerSecond,
    string Guidance);
