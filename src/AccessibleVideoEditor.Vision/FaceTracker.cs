using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Vision;

/// <summary>
/// Turns per-frame detections into a stable framing signal.
///
/// Tracking rather than raw detection matters more here than it would in a
/// visual viewfinder. A detector that jitters by a few pixels is invisible on
/// screen but audible as a warbling tone, and a single dropped frame would
/// otherwise announce "no face detected" while you are sitting still. So:
/// exponential smoothing on the position, and a grace period before declaring
/// the face lost.
/// </summary>
public sealed class FaceTracker
{
    private double _centreX;
    private double _eyelineY;
    private double _width;
    private double _roll;

    private bool _acquired;
    private double _lastSeen = double.NegativeInfinity;

    /// <summary>
    /// Smoothing factor, 0..1. Higher follows faster and jitters more. 0.35
    /// tracks a head turn without making the tone unsteady while you hold still.
    /// </summary>
    public double Smoothing { get; init; } = 0.35;

    /// <summary>How long a detection may drop out before the face counts as lost.</summary>
    public double LostGrace { get; init; } = 0.5;

    public bool HasFace => _acquired;

    public FramingError Track(IReadOnlyList<FaceObservation> faces, double timestamp)
    {
        var face = Best(faces);

        if (face is null)
        {
            // A single dropped frame is not a lost face.
            if (_acquired && timestamp - _lastSeen <= LostGrace)
            {
                return Current(cropped: false, croppedBottom: false);
            }

            _acquired = false;
            return FramingError.NoFace;
        }

        _lastSeen = timestamp;

        if (!_acquired)
        {
            // Snap on acquisition; smoothing in from a stale position would
            // sweep the tone across the stereo field for no reason.
            _centreX = face.CentreX;
            _eyelineY = face.EyelineY;
            _width = face.Width;
            _roll = face.RollDegrees;
            _acquired = true;
        }
        else
        {
            _centreX = Smooth(_centreX, face.CentreX);
            _eyelineY = Smooth(_eyelineY, face.EyelineY);
            _width = Smooth(_width, face.Width);
            _roll = Smooth(_roll, face.RollDegrees);
        }

        return Current(
            cropped: face.Y < 0.01,
            croppedBottom: face.Y + face.Height > 0.99);
    }

    public void Reset()
    {
        _acquired = false;
        _lastSeen = double.NegativeInfinity;
    }

    private double Smooth(double current, double target) =>
        current + (target - current) * Smoothing;

    private FramingError Current(bool cropped, bool croppedBottom) => new(
        FaceVisible: true,
        CentreX: _centreX,
        EyelineY: _eyelineY,
        FaceWidth: _width,
        CroppedTop: cropped,
        CroppedBottom: croppedBottom,
        RollDegrees: _roll);

    /// <summary>
    /// The largest confident face. In a talking-head setup the subject is the
    /// nearest person; picking by confidence alone would swap to someone
    /// walking past in the background.
    /// </summary>
    private static FaceObservation? Best(IReadOnlyList<FaceObservation> faces) =>
        faces.Where(f => f.Confidence >= 0.5)
             .OrderByDescending(f => f.Width * f.Height)
             .FirstOrDefault();
}
