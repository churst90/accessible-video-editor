using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Images;

/// <summary>
/// A pointer you can hear.
///
/// Naming a point is exact and slow; sweeping is fast and vague, and both are
/// wanted. This is the sweeping half - the pointer moves with the arrow keys
/// and its position is carried by a tone: <b>panned left to right, pitched high
/// to low</b>. That is the viewfinder's vocabulary, already learnt, and it
/// means "where am I" is answered continuously rather than on request.
///
/// Up is high. It has to be, because every other mapping is something you have
/// to remember rather than something you already know.
/// </summary>
public sealed class ImagePointer
{
    /// <summary>Position across the picture, 0 to 1 from the left.</summary>
    public double X { get; private set; } = 0.5;

    /// <summary>Position down the picture, 0 to 1 from the top.</summary>
    public double Y { get; private set; } = 0.5;

    /// <summary>
    /// How far one press moves, as a fraction of the picture. A third is the
    /// coarse sweep - it lands on cell centres - and a hundredth is the fine
    /// one for finding an edge.
    /// </summary>
    public double Step { get; private set; } = 1.0 / 3;

    public static readonly double[] Steps = [1.0 / 3, 0.1, 0.02, 0.005];

    public void MoveTo(double x, double y)
    {
        X = Math.Clamp(x, 0, 1);
        Y = Math.Clamp(y, 0, 1);
    }

    /// <summary>Jumps to the middle of a cell, so the two ways of pointing agree.</summary>
    public void MoveTo(Placement placement)
    {
        var (x, y) = placement.Resolve();

        MoveTo(x, y);
    }

    public bool Move(double dx, double dy)
    {
        var x = Math.Clamp(X + dx * Step, 0, 1);
        var y = Math.Clamp(Y + dy * Step, 0, 1);

        var moved = Math.Abs(x - X) > 1e-9 || Math.Abs(y - Y) > 1e-9;

        X = x;
        Y = y;

        return moved;
    }

    public string Coarser()
    {
        var index = Array.IndexOf(Steps, Step);
        Step = Steps[Math.Max(0, index - 1)];

        return DescribeStep();
    }

    public string Finer()
    {
        var index = Array.IndexOf(Steps, Step);
        Step = Steps[Math.Min(Steps.Length - 1, index + 1)];

        return DescribeStep();
    }

    public string DescribeStep() =>
        Step switch
        {
            > 0.3 => "stepping by thirds",
            > 0.05 => "stepping by tenths",
            > 0.01 => "stepping by fiftieths",
            _ => "stepping by two hundredths",
        };

    public Placement Placement
    {
        get
        {
            var column = X < 1.0 / 3 ? 0 : X > 2.0 / 3 ? 2 : 1;
            var row = Y < 1.0 / 3 ? 2 : Y > 2.0 / 3 ? 0 : 1;

            return new Placement(row * 3 + column + 1);
        }
    }

    public (int X, int Y) PixelIn(int width, int height) =>
        ((int)Math.Round(X * (width - 1)), (int)Math.Round(Y * (height - 1)));

    /// <summary>
    /// The tone. Pan is the horizontal position directly; pitch runs from a low
    /// note at the bottom to a high one at the top, over a range wide enough to
    /// hear a small move and narrow enough not to become shrill.
    /// </summary>
    public PointerTone Tone => new(
        Pan: X * 2 - 1,
        PitchHz: LowPitch * Math.Pow(HighPitch / LowPitch, 1 - Y));

    public const double LowPitch = 220;
    public const double HighPitch = 1760;

    /// <summary>
    /// Spoken when the pointer stops, or on demand. Percentages as well as
    /// pixels, because "40 percent across" is what you can picture and "1200"
    /// is what you need to type in.
    /// </summary>
    public string Describe(int width, int height)
    {
        var (px, py) = PixelIn(width, height);

        return $"{Math.Round(X * 100)} percent across, {Math.Round(Y * 100)} percent down, "
               + $"{px} by {py}, {ShapeLanguage.CellName(Placement)}";
    }

    /// <summary>
    /// The short form, for while you are moving. Only the cell, and only when
    /// it changes - saying two numbers on every press is unusable at speed.
    /// </summary>
    public string? CrossedInto(Placement previous) =>
        previous.Cell == Placement.Cell ? null : ShapeLanguage.CellName(Placement);
}

/// <summary>Where the pointer is, as something to hear rather than read.</summary>
public readonly record struct PointerTone(double Pan, double PitchHz);
