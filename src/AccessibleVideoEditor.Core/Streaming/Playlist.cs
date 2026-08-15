namespace AccessibleVideoEditor.Core.Streaming;

/// <summary>
/// Music while you stream, with the track announced.
///
/// Deliberately not a media player. It is a list, a position, and rules about
/// what comes next - because the only questions that matter live are "what is
/// playing", "what is next" and "get me out of this one", and all three have to
/// be answerable in one key while you are talking about something else.
/// </summary>
public sealed class Playlist
{
    private readonly List<PlaylistTrack> _tracks = [];
    private readonly List<int> _order = [];
    private int _position = -1;

    public string Name { get; set; } = "Playlist";

    public IReadOnlyList<PlaylistTrack> Tracks => _tracks;

    public bool Shuffle { get; private set; }

    public RepeatMode Repeat { get; set; } = RepeatMode.All;

    public bool Playing { get; private set; }

    public PlaylistTrack? Current =>
        _position >= 0 && _position < _order.Count ? _tracks[_order[_position]] : null;

    public PlaylistTrack? NextUp
    {
        get
        {
            if (_order.Count == 0) return null;

            var next = _position + 1;

            if (next >= _order.Count)
            {
                return Repeat == RepeatMode.All ? _tracks[_order[0]] : null;
            }

            return _tracks[_order[next]];
        }
    }

    public void Add(PlaylistTrack track)
    {
        _tracks.Add(track);
        Reorder(keepCurrent: true);
    }

    public string AddRange(IEnumerable<PlaylistTrack> tracks)
    {
        var before = _tracks.Count;
        _tracks.AddRange(tracks);
        Reorder(keepCurrent: true);

        var added = _tracks.Count - before;

        return $"{added} {(added == 1 ? "track" : "tracks")} added, {_tracks.Count} in {Name}";
    }

    public string Remove(int index)
    {
        if (index < 0 || index >= _tracks.Count) return "no such track";

        var name = _tracks[index].Name;
        var wasCurrent = Current is { } current && current == _tracks[index];

        _tracks.RemoveAt(index);
        Reorder(keepCurrent: !wasCurrent);

        return wasCurrent ? $"{name} removed; it was playing" : $"{name} removed";
    }

    /// <summary>
    /// Shuffling is announced with what it did to the current track, because
    /// the surprising part is whether what is playing keeps playing. It does.
    /// </summary>
    public string ToggleShuffle()
    {
        Shuffle = !Shuffle;
        Reorder(keepCurrent: true);

        return Shuffle
            ? $"shuffled, {Current?.Name ?? "nothing"} still playing"
            : "playing in order";
    }

    public string CycleRepeat()
    {
        Repeat = Repeat switch
        {
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.None,
            _ => RepeatMode.All,
        };

        return Repeat switch
        {
            RepeatMode.All => "repeating the whole playlist",
            RepeatMode.One => "repeating this track",
            _ => "stopping at the end",
        };
    }

    public string Start(int index = 0)
    {
        if (_tracks.Count == 0) return "the playlist is empty";

        var slot = _order.IndexOf(Math.Clamp(index, 0, _tracks.Count - 1));

        _position = slot >= 0 ? slot : 0;
        Playing = true;

        return Announce();
    }

    /// <summary>
    /// Called when a track ends as well as by the skip key, so "what happens at
    /// the end" and "what happens when I press next" cannot drift apart.
    /// </summary>
    public string Next(bool userAsked = true)
    {
        if (_tracks.Count == 0) return "the playlist is empty";

        // Repeat-one repeats when a track ends and is ignored when you ask for
        // the next one - pressing next and hearing the same song again is not
        // what anybody means by it.
        if (Repeat == RepeatMode.One && !userAsked) return Announce();

        if (_position + 1 >= _order.Count)
        {
            if (Repeat == RepeatMode.None)
            {
                Playing = false;
                return $"end of {Name}";
            }

            _position = 0;

            if (Shuffle) Reorder(keepCurrent: false);
        }
        else
        {
            _position++;
        }

        Playing = true;

        return Announce();
    }

    public string Previous()
    {
        if (_tracks.Count == 0) return "the playlist is empty";

        _position = _position <= 0 ? _order.Count - 1 : _position - 1;
        Playing = true;

        return Announce();
    }

    public string Stop()
    {
        if (!Playing) return "the music is already stopped";

        Playing = false;

        return "music stopped";
    }

    /// <summary>
    /// What is playing and what is next. Both, because the second question
    /// always follows the first and asking twice while live is one key too
    /// many.
    /// </summary>
    public string Announce()
    {
        if (Current is not { } track) return $"{Name}, nothing playing";

        var next = NextUp is { } upcoming ? $". next, {upcoming.Name}" : ". last track";

        return $"{track.Name}{(track.Artist.Length > 0 ? $", {track.Artist}" : string.Empty)}"
               + $", {Position} of {_tracks.Count}{next}";
    }

    public int Position => _position + 1;

    public string Describe() =>
        _tracks.Count == 0
            ? $"{Name}, empty"
            : $"{Name}, {_tracks.Count} tracks, "
              + $"{(Playing ? Current?.Name ?? "playing" : "stopped")}"
              + $"{(Shuffle ? ", shuffled" : string.Empty)}";

    /// <summary>
    /// Rebuilds the play order. Shuffling is a permutation held beside the
    /// list rather than a reordering of it, so the playlist you built is still
    /// the playlist you see and turning shuffle off puts everything back.
    /// </summary>
    private void Reorder(bool keepCurrent)
    {
        var current = keepCurrent ? Current : null;

        _order.Clear();
        _order.AddRange(Enumerable.Range(0, _tracks.Count));

        if (Shuffle)
        {
            for (var i = _order.Count - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
        }

        if (current is null)
        {
            _position = _tracks.Count == 0 ? -1 : Math.Clamp(_position, 0, _order.Count - 1);
            return;
        }

        var index = _tracks.IndexOf(current);
        _position = index < 0 ? 0 : _order.IndexOf(index);
    }
}

public sealed record PlaylistTrack(string Path, string Name, string Artist = "", double Duration = 0);

public enum RepeatMode
{
    None,
    One,
    All,
}
