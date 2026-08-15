using AccessibleVideoEditor.Audio;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// The audible VU meter. A visual meter works by being glanceable; the
/// equivalent by ear is a tick whose pitch tracks the level, with words
/// reserved for crossing into a new zone.
/// </summary>
public class LevelSonifierTests
{
    [Theory]
    [InlineData(-80, LevelZone.Silent)]
    [InlineData(-51, LevelZone.Silent)]
    [InlineData(-40, LevelZone.Green)]
    [InlineData(-19, LevelZone.Green)]
    [InlineData(-12, LevelZone.Yellow)]
    [InlineData(-3, LevelZone.Red)]
    [InlineData(-0.2, LevelZone.Clipping)]
    public void Zones_follow_the_thresholds_a_meter_would_use(double db, LevelZone expected)
    {
        Assert.Equal(expected, LevelSonifier.ZoneOf(db));
    }

    [Fact]
    public void Louder_is_higher_pitched_across_the_whole_range()
    {
        // The core of the idea: pitch is the reading, so it has to rise
        // monotonically or it conveys nothing.
        var levels = new[] { -60.0, -45, -30, -18, -12, -6, -3, 0 };
        var pitches = levels.Select(LevelSonifier.PitchFor).ToList();

        for (var i = 1; i < pitches.Count; i++)
        {
            Assert.True(pitches[i] > pitches[i - 1], $"{levels[i]} dB was not higher than {levels[i - 1]} dB");
        }
    }

    [Fact]
    public void The_pitch_range_is_wide_enough_to_hear_but_not_shrill()
    {
        // Comfortable for a long take: roughly two and a half octaves, ending
        // well below the range that becomes fatiguing.
        Assert.InRange(LevelSonifier.PitchFor(-60), 150, 250);
        Assert.InRange(LevelSonifier.PitchFor(0), 900, 1200);
    }

    [Fact]
    public void A_few_decibels_makes_an_audible_difference_in_pitch()
    {
        var quiet = LevelSonifier.PitchFor(-24);
        var slightlyLouder = LevelSonifier.PitchFor(-18);

        Assert.True(slightlyLouder / quiet > 1.1);
    }

    [Fact]
    public void Clipping_speeds_the_ticks_up_because_it_is_an_alarm_not_a_reading()
    {
        Assert.Equal(8, LevelSonifier.TicksPerSecond(-20));
        Assert.Equal(8, LevelSonifier.TicksPerSecond(-8));
        Assert.True(LevelSonifier.TicksPerSecond(-0.1) > 8);
    }

    [Fact]
    public void Silence_ticks_slowly_rather_than_going_completely_quiet()
    {
        // Total silence from the meter is indistinguishable from the meter
        // being switched off.
        Assert.True(LevelSonifier.TicksPerSecond(-80) > 0);
    }

    // ---- what gets spoken -------------------------------------------------

    [Fact]
    public void The_first_reading_announces_its_zone()
    {
        var monitor = new LevelMonitor();

        Assert.Equal("green", monitor.Observe(-30, 0));
    }

    [Fact]
    public void Speech_flickering_across_a_boundary_does_not_chatter()
    {
        // The spam case. Speech crosses -18 dB dozens of times a minute; a zone
        // has to settle before it is worth reporting, or this reads out
        // "yellow, green, yellow, green" continuously.
        var monitor = new LevelMonitor();
        monitor.Observe(-30, 0);

        var spoken = new List<string>();

        for (var i = 1; i < 60; i++)
        {
            var db = i % 2 == 0 ? -14.0 : -22.0;
            if (monitor.Observe(db, i * 0.12) is { } said) spoken.Add(said);
        }

        Assert.Empty(spoken);
    }

    [Fact]
    public void A_settled_move_into_yellow_is_announced()
    {
        // Yellow is worth hearing - it is the "getting hot" warning - as long
        // as it has actually settled there.
        var monitor = new LevelMonitor();
        monitor.Observe(-30, 0);

        monitor.Observe(-12, 2.0);
        Assert.Equal("yellow", monitor.Observe(-12, 2.3));
    }

    [Fact]
    public void Settling_back_into_green_is_announced_too()
    {
        var monitor = new LevelMonitor();
        monitor.Observe(-30, 0);
        monitor.Observe(-12, 2.0);
        monitor.Observe(-12, 2.3);

        monitor.Observe(-30, 4.0);
        Assert.Equal("green", monitor.Observe(-30, 4.3));
    }

    [Fact]
    public void A_peak_is_reported_even_when_the_level_returns_at_once()
    {
        // The failure this replaces: the peak meter showed -3 dB red while red
        // was never once announced.
        var monitor = new LevelMonitor();
        monitor.Observe(-30, 0);

        Assert.Equal("red", monitor.Observe(-3, 1.2));

        // And coming back from it is reported as a recovery.
        monitor.Observe(-30, 1.32);
        Assert.Equal("back to green", monitor.Observe(-30, 2.0));
    }

    [Fact]
    public void A_momentary_peak_into_the_red_is_announced_immediately()
    {
        // Red is a peak event. Requiring it to settle first means it is never
        // reported at all - speech flickers in and out of the red rather than
        // sitting there, which is exactly what a meter exists to catch.
        var monitor = new LevelMonitor();
        monitor.Observe(-30, 0);

        Assert.Equal("red", monitor.Observe(-4, 2.0));
    }

    [Fact]
    public void Clipping_is_announced_the_moment_it_happens()
    {
        var monitor = new LevelMonitor();
        monitor.Observe(-30, 0);

        Assert.Equal("clipping", monitor.Observe(-0.1, 2.0));
    }

    [Fact]
    public void Recovering_from_a_problem_is_announced_as_coming_back()
    {
        var monitor = new LevelMonitor();
        monitor.Observe(-30, 0);

        // Red announces at once - it is a peak, not a state that settles.
        Assert.Equal("red", monitor.Observe(-4, 2.0));

        // Recovery does have to persist: a single quiet sample mid-sentence is
        // not a recovery.
        Assert.Null(monitor.Observe(-30, 2.1));
        Assert.Equal("back to green", monitor.Observe(-30, 2.9));
    }

    [Fact]
    public void A_pause_between_sentences_is_not_reported_as_silence()
    {
        // Silence has to last far longer than a breath before it is a fault.
        var monitor = new LevelMonitor();
        monitor.Observe(-30, 0);

        Assert.Null(monitor.Observe(-80, 2.0));
        Assert.Null(monitor.Observe(-80, 2.8));
        Assert.Null(monitor.Observe(-30, 3.0));
    }

    [Fact]
    public void Sustained_silence_is_reported()
    {
        var monitor = new LevelMonitor();
        monitor.Observe(-30, 0);

        Assert.Null(monitor.Observe(-80, 1.0));
        Assert.Equal("silent", monitor.Observe(-80, 3.0));
    }

    [Fact]
    public void Announcements_are_never_closer_together_than_the_minimum_gap()
    {
        var monitor = new LevelMonitor();
        monitor.Observe(-30, 0);

        Assert.Equal("red", monitor.Observe(-3, 1.5));

        // Clipping straight after would otherwise talk over the last word.
        Assert.Null(monitor.Observe(-0.1, 1.6));
    }



    [Fact]
    public void The_peak_is_remembered_for_the_summary()
    {
        // The peak survives the dip afterwards - that is what tells you a take
        // nearly clipped even though it sounds fine now.
        var monitor = new LevelMonitor();
        monitor.Observe(-40, 0);
        monitor.Observe(-4, 1);
        monitor.Observe(-30, 2);

        Assert.Equal(-4, monitor.PeakDb, 1);
        Assert.Contains("peak -4", monitor.Summarise());
        Assert.Contains("red", monitor.Summarise());
    }

    [Fact]
    public void A_monitor_that_saw_nothing_says_so_rather_than_reporting_a_number()
    {
        Assert.Contains("no signal at all", new LevelMonitor().Summarise());
    }

    [Fact]
    public void Resetting_makes_the_next_reading_announce_again()
    {
        var monitor = new LevelMonitor();
        monitor.Observe(-30, 0);

        monitor.Reset();
        Assert.Equal("green", monitor.Observe(-29, 5));
    }
}

/// <summary>
/// Turning raw samples into a level. RMS rather than peak, because it
/// corresponds to perceived loudness - which is what a meter is for.
/// </summary>
public class LevelReaderTests
{
    private static byte[] Tone(double amplitude, int samples = 4800)
    {
        var pcm = new byte[samples * 2];

        for (var i = 0; i < samples; i++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * 440 * i / 48000.0) * amplitude * short.MaxValue);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        return pcm;
    }

    [Fact]
    public void Digital_silence_reads_as_effectively_nothing()
    {
        Assert.True(AccessibleVideoEditor.Engine.LevelReader.RootMeanSquareDb(new byte[960]) <= -99);
    }

    [Fact]
    public void A_full_scale_tone_reads_near_the_top_of_the_scale()
    {
        // A sine at full amplitude is about -3 dBFS RMS, not 0 - that is the
        // difference between peak and RMS, and getting it wrong would make
        // every reading look three decibels hotter than it is.
        var db = AccessibleVideoEditor.Engine.LevelReader.RootMeanSquareDb(Tone(1.0));

        Assert.InRange(db, -4, -2);
    }

    [Fact]
    public void Halving_the_amplitude_drops_the_reading_by_about_six_decibels()
    {
        var loud = AccessibleVideoEditor.Engine.LevelReader.RootMeanSquareDb(Tone(1.0));
        var quiet = AccessibleVideoEditor.Engine.LevelReader.RootMeanSquareDb(Tone(0.5));

        Assert.InRange(loud - quiet, 5.5, 6.5);
    }

    [Fact]
    public void Ordinary_speech_amplitude_lands_in_the_green_zone()
    {
        var db = AccessibleVideoEditor.Engine.LevelReader.RootMeanSquareDb(Tone(0.05));

        Assert.Equal(LevelZone.Green, LevelSonifier.ZoneOf(db));
    }

    [Fact]
    public void A_buffer_too_short_to_measure_reports_nothing_rather_than_zero()
    {
        Assert.True(double.IsNegativeInfinity(AccessibleVideoEditor.Engine.LevelReader.RootMeanSquareDb([0])));
    }
}
