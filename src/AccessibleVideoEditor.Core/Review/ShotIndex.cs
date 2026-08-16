using System.Text.Json;
using System.Text.Json.Serialization;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Review;

/// <summary>
/// What is on screen, at every point in a source, ready before you ask.
///
/// <b>Why this is not a call per cursor move.</b> Describing a frame takes
/// seconds. Moving the cursor takes milliseconds. A description fetched as you
/// navigate would arrive attached to a position you left several presses ago,
/// and a description of the wrong moment is worse than no description at all -
/// it is the one failure mode you cannot detect without sight. So the work is
/// done once, in the background, and navigation reads the answer out of a
/// dictionary.
///
/// <b>Why shots rather than frames.</b> Sampling every few seconds would
/// describe the same shot forty times, at forty times the cost, and give
/// slightly different words each time - so moving the cursor inside one
/// unchanging shot would make the description flicker, which reads as the
/// picture changing when it has not. A shot is the unit that actually has an
/// identity: the picture is the same thing from one cut to the next, so it gets
/// one description and every position inside it gives the same answer.
/// </summary>
public sealed record Shot(double At, double Until, string Label, string Detail)
{
    public bool Contains(double sourceTime) => sourceTime >= At && sourceTime < Until;

    /// <summary>
    /// The short form, for a cursor that is moving. Cards carry their text for
    /// the same reason: a label is the only identity a shot has, and "shot" on
    /// its own is useless in a video with thirty of them.
    /// </summary>
    public string Announce() => Label;
}

/// <summary>
/// The shots found in one source, and the lookup that answers "what is on
/// screen here".
/// </summary>
public sealed class ShotIndex
{
    [JsonInclude]
    public Dictionary<string, List<Shot>> BySource { get; private set; } = [];

    /// <summary>Shots are held in order, so the lookup is a walk rather than a scan.</summary>
    public void Set(SourceId source, IEnumerable<Shot> shots) =>
        BySource[source.ToString()] = [.. shots.OrderBy(shot => shot.At)];

    public bool Has(SourceId source) => BySource.ContainsKey(source.ToString());

    public IReadOnlyList<Shot> For(SourceId source) =>
        BySource.TryGetValue(source.ToString(), out var shots) ? shots : [];

    public Shot? At(SourceId source, double sourceTime)
    {
        var shots = For(source);

        // Walked backwards: the last shot that starts at or before this moment
        // is the one on screen, which stays true even if the durations were
        // rounded when they were written.
        for (var i = shots.Count - 1; i >= 0; i--)
        {
            if (sourceTime >= shots[i].At) return shots[i];
        }

        return shots.Count > 0 && sourceTime >= 0 ? shots[0] : null;
    }

    /// <summary>
    /// The next shot change after this moment, or null at the last shot.
    ///
    /// This is the navigation this whole feature buys: a cut inside a take is
    /// something a sighted editor sees at a glance and a blind editor has,
    /// until now, had no way to find at all.
    /// </summary>
    public Shot? Next(SourceId source, double sourceTime) =>
        For(source).FirstOrDefault(shot => shot.At > sourceTime + 0.001);

    public Shot? Previous(SourceId source, double sourceTime) =>
        For(source).LastOrDefault(shot => shot.At < sourceTime - 0.001);

    public int Count(SourceId source) => For(source).Count;

    // ---- the disk cache ----------------------------------------------------

    private static JsonSerializerOptions Json => new() { WriteIndented = false };

    public string Serialise() => JsonSerializer.Serialize(this, Json);

    public static ShotIndex Deserialise(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ShotIndex>(json, Json) ?? new ShotIndex();
        }
        catch (Exception)
        {
            // A cache that cannot be read is a cache that gets rebuilt. It is
            // never the reason the application fails to open a project.
            return new ShotIndex();
        }
    }
}

/// <summary>
/// Says the shot label when, and only when, the cursor crosses into a different
/// shot.
///
/// The same rule the playback announcer follows, and for the same reason: at
/// navigation speed the readout is a time and one word, because syllables are
/// latency. Four sentences on every arrow press would make the timeline
/// unusable; four sentences when the picture actually changes is the thing you
/// could not otherwise know.
/// </summary>
public sealed class ShotAnnouncer
{
    private string? _lastSource;
    private double _lastShotAt = double.NaN;

    /// <summary>Call when the project changes, so the next move announces rather than assumes.</summary>
    public void Reset()
    {
        _lastSource = null;
        _lastShotAt = double.NaN;
    }

    /// <summary>
    /// The label to speak, or null for silence - which is most moves, because
    /// most moves stay inside one shot.
    /// </summary>
    public string? Moved(ShotIndex index, SourceId? source, double sourceTime)
    {
        if (source is not { } id)
        {
            // Off the end of the programme, or over a card. Cleared rather than
            // held, so stepping back onto the same shot announces it again -
            // you have been somewhere else in between.
            Reset();
            return null;
        }

        if (index.At(id, sourceTime) is not { } shot)
        {
            Reset();
            return null;
        }

        var key = id.ToString();

        if (key == _lastSource && Math.Abs(shot.At - _lastShotAt) < 0.0001) return null;

        _lastSource = key;
        _lastShotAt = shot.At;

        return shot.Announce();
    }
}
