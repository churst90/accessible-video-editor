namespace AccessibleVideoEditor.Audio;

/// <summary>
/// An audible VU meter.
///
/// A meter works by being glanceable: you see green, yellow or red without
/// reading anything. The equivalent by ear is a <b>tick whose pitch tracks the
/// level</b> - quiet is low, loud is high - so the shape of your delivery is
/// audible continuously, without a word being spoken.
///
/// Words are reserved for the thing a meter conveys by colour: crossing into a
/// new zone. "Green", "yellow", "red" are spoken only on the crossing, never
/// repeated, because a meter that talks constantly is one you turn off.
///
/// The mapping is a pure function so it can be tested and tuned without a
/// microphone in the loop.
/// </summary>
public static class LevelSonifier
{
    /// <summary>Below this there is nothing to report; the input is silent.</summary>
    public const double SilenceDb = -50;

    /// <summary>Comfortable speech sits below this.</summary>
    public const double YellowDb = -18;

    /// <summary>Hot. Still usable, but heading for trouble.</summary>
    public const double RedDb = -6;

    /// <summary>Effectively clipping.</summary>
    public const double ClipDb = -0.5;

    /// <summary>
    /// How far back a level must fall before the zone is announced again.
    /// Without it a level sitting exactly on a boundary chatters between two
    /// zones and says both names over and over.
    /// </summary>
    public const double Hysteresis = 2.0;

    public static LevelZone ZoneOf(double db) => db switch
    {
        >= ClipDb => LevelZone.Clipping,
        >= RedDb => LevelZone.Red,
        >= YellowDb => LevelZone.Yellow,
        >= SilenceDb => LevelZone.Green,
        _ => LevelZone.Silent,
    };

    /// <summary>
    /// The tick to play for this level. Pitch rises with loudness across a bit
    /// over two octaves, which is wide enough to hear a change of a few decibels
    /// and narrow enough to stay comfortable for a long take.
    /// </summary>
    public static double PitchFor(double db)
    {
        var clamped = Math.Clamp(db, -60, 0);
        var fraction = (clamped + 60) / 60.0;

        // Exponential in frequency so equal decibel steps sound like equal
        // musical steps.
        return 180 * Math.Pow(2, fraction * 2.6);
    }

    /// <summary>
    /// Ticks per second. Constant while the level is usable - pitch carries the
    /// information - but it doubles once clipping, because at that point you
    /// want an alarm rather than a reading.
    /// </summary>
    public static double TicksPerSecond(double db) =>
        ZoneOf(db) switch
        {
            LevelZone.Silent => 2,
            LevelZone.Clipping => 16,
            _ => 8,
        };

    public static string Name(LevelZone zone) => zone switch
    {
        LevelZone.Silent => "silent",
        LevelZone.Green => "green",
        LevelZone.Yellow => "yellow",
        LevelZone.Red => "red",
        LevelZone.Clipping => "clipping",
        _ => "unknown",
    };
}

public enum LevelZone
{
    Silent,
    Green,
    Yellow,
    Red,
    Clipping,
}

/// <summary>
/// Tracks the meter over time and decides what is worth saying.
///
/// Every zone is announced, but only once it has <b>settled</b>. Speech crosses
/// -18 dB dozens of times a minute, so reporting each crossing the instant it
/// happens reads out "yellow, green, yellow, green" continuously and is
/// unusable. Requiring a zone to hold for a moment, and never speaking twice
/// inside a second, keeps every zone informative without the chatter.
///
/// Silence needs longer still - a breath between sentences is not a fault.
/// </summary>
public sealed class LevelMonitor
{
    private LevelZone _announced = LevelZone.Silent;
    private bool _started;
    private double _zoneSince;
    private LevelZone _current = LevelZone.Silent;
    private double _lastSpokeAt = double.NegativeInfinity;

    /// <summary>
    /// How long green or yellow must hold before it is worth saying. Short, so
    /// the meter stays responsive; long enough that speech flickering across
    /// -18 dB does not chatter.
    /// </summary>
    public double SustainSeconds { get; init; } = 0.2;

    /// <summary>Silence has to last far longer - a pause between sentences is not a fault.</summary>
    public double SilenceSustainSeconds { get; init; } = 1.5;

    /// <summary>The usual minimum between announcements.</summary>
    public double MinimumGapSeconds { get; init; } = 0.6;

    /// <summary>
    /// Red and clipping get a shorter gap and no sustain at all, because they
    /// are <b>peak events</b>: a moment of overload is transient by nature, and
    /// waiting for it to settle means never reporting the thing a meter exists
    /// to catch.
    /// </summary>
    public double PeakGapSeconds { get; init; } = 0.35;

    public double PeakDb { get; private set; } = double.NegativeInfinity;

    public LevelZone Zone { get; private set; } = LevelZone.Silent;

    public void Reset()
    {
        _announced = LevelZone.Silent;
        _started = false;
        _zoneSince = 0;
        _lastSpokeAt = double.NegativeInfinity;
        _current = LevelZone.Silent;
        Zone = LevelZone.Silent;
        PeakDb = double.NegativeInfinity;
    }

    /// <summary>
    /// Feeds one measurement in, with the time it was taken. Returns something
    /// to say only when it is worth saying - which, during ordinary speech, is
    /// almost never.
    /// </summary>
    public string? Observe(double db, double atSeconds)
    {
        PeakDb = Math.Max(PeakDb, db);
        Zone = LevelSonifier.ZoneOf(db);

        if (Zone != _current)
        {
            _current = Zone;
            _zoneSince = atSeconds;
        }

        if (!_started)
        {
            _started = true;
            _announced = Zone;
            _lastSpokeAt = atSeconds;
            return LevelSonifier.Name(Zone);
        }

        if (Zone == _announced) return null;

        var isPeak = Zone is LevelZone.Red or LevelZone.Clipping;

        if (atSeconds - _lastSpokeAt < (isPeak ? PeakGapSeconds : MinimumGapSeconds)) return null;

        // Green, yellow and silence have to hold before they are worth
        // reporting. Red and clipping do not: they are peaks, and a peak that
        // had to persist to be announced would never be announced at all.
        var needed = Zone switch
        {
            LevelZone.Red or LevelZone.Clipping => 0,
            LevelZone.Silent => SilenceSustainSeconds,
            _ => SustainSeconds,
        };

        if (atSeconds - _zoneSince < needed) return null;

        var recovering = _announced is LevelZone.Red or LevelZone.Clipping or LevelZone.Silent
                         && Zone is LevelZone.Green or LevelZone.Yellow;

        _announced = Zone;
        _lastSpokeAt = atSeconds;

        // "Back to green" rather than a bare "green", so a recovery is
        // distinguishable from drifting there in the ordinary course of things.
        return recovering
            ? $"back to {LevelSonifier.Name(Zone)}"
            : LevelSonifier.Name(Zone);
    }

    /// <summary>The spoken summary when monitoring stops.</summary>
    public string Summarise() =>
        double.IsNegativeInfinity(PeakDb)
            ? "no signal at all"
            : $"peak {PeakDb:0} decibels, {LevelSonifier.Name(LevelSonifier.ZoneOf(PeakDb))}";
}
