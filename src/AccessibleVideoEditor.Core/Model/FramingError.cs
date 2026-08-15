namespace AccessibleVideoEditor.Core.Model;

/// <summary>
/// Normalised framing measurements, 0..1 across the frame. Lives in Core rather
/// than in Audio or Vision because both sides need it: Vision produces it from
/// tracked faces, Audio turns it into a tone.
/// </summary>
public readonly record struct FramingError(
    bool FaceVisible,
    double CentreX,
    double EyelineY,
    double FaceWidth,
    bool CroppedTop = false,
    bool CroppedBottom = false,
    double RollDegrees = 0)
{
    public static FramingError NoFace => new(false, 0.5, 0.5, 0);
}
