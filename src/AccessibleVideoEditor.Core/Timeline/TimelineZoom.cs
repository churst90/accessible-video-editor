namespace AccessibleVideoEditor.Core.Timeline;

/// <summary>
/// Zoom and step size are the same control.
///
/// A sighted editor zooms in to work on a detail; a blind editor makes the step
/// size finer to do the same job. Driving both from <see cref="Granularity"/>
/// means the two never disagree - what you can see and what an arrow key moves
/// by are always the same scale, so two people can work on one timeline without
/// talking past each other.
/// </summary>
public static class TimelineZoom
{
    public static double PixelsPerSecondFor(Granularity granularity) => granularity switch
    {
        Granularity.Frame => 480,
        Granularity.Tenth => 220,
        Granularity.Second => 90,
        Granularity.Word => 42,
        Granularity.Element => 18,
        Granularity.Boundary => 9,
        Granularity.Marker => 4,
        _ => 42,
    };

    /// <summary>
    /// The zoom that shows a whole programme at once, used when a project is
    /// first opened so the first thing on screen is the shape of the edit.
    /// </summary>
    public static double FitToWidth(double duration, double width) =>
        duration <= 0 || width <= 0 ? PixelsPerSecondFor(Granularity.Word) : width / duration;
}
