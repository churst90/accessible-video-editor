using AccessibleVideoEditor.Audio;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// The synthesiser behind earcons and the level tick. Sample-based and pure, so
/// it can be checked without an audio device.
/// </summary>
public class ToneBankTests
{
    private static float[] Render(ToneBank bank, int frames)
    {
        var buffer = new float[frames * 2];
        bank.Fill(buffer);
        return buffer;
    }

    [Fact]
    public void Silence_is_produced_when_nothing_is_playing()
    {
        var bank = new ToneBank();

        Assert.All(Render(bank, 256), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void A_played_tone_actually_produces_signal()
    {
        var bank = new ToneBank();
        bank.Play(440, 0.05);

        Assert.Contains(Render(bank, 512), sample => Math.Abs(sample) > 0.01f);
    }

    [Fact]
    public void A_tone_stops_by_itself_and_frees_its_voice()
    {
        // Voices that never retire would accumulate until the mix is mush.
        var bank = new ToneBank { SampleRate = 48000 };
        bank.Play(440, 0.005);

        Assert.Equal(1, bank.ActiveVoices);

        Render(bank, 1024);

        Assert.Equal(0, bank.ActiveVoices);
    }

    [Fact]
    public void Output_never_exceeds_full_scale_even_with_many_voices()
    {
        // Clipping the earcon bus would be audible as a crackle over speech.
        var bank = new ToneBank();

        for (var i = 0; i < 12; i++) bank.Play(300 + i * 100, 0.1);

        Assert.All(Render(bank, 512), sample => Assert.InRange(sample, -1f, 1f));
    }

    [Fact]
    public void A_backlog_of_overlapping_blips_is_capped()
    {
        var bank = new ToneBank();

        for (var i = 0; i < 40; i++) bank.Play(440, 1.0);

        Assert.True(bank.ActiveVoices <= 7);
    }

    [Fact]
    public void The_envelope_starts_and_ends_quietly()
    {
        // Without a ramp, every blip begins and ends with a click of its own -
        // which is exactly the sound the earcons are trying to be.
        var bank = new ToneBank { Gain = 1.0 };
        bank.Play(440, 0.05, amplitude: 1.0);

        var buffer = Render(bank, 2400);

        Assert.True(Math.Abs(buffer[0]) < 0.05f);
        Assert.True(Math.Abs(buffer[^2]) < 0.05f);
    }

    [Fact]
    public void Panning_left_puts_more_signal_in_the_left_channel()
    {
        var bank = new ToneBank { Gain = 1.0 };
        bank.Play(440, 0.05, amplitude: 1.0, pan: -1);

        var buffer = Render(bank, 512);

        var left = 0.0;
        var right = 0.0;

        for (var i = 0; i < buffer.Length; i += 2)
        {
            left += Math.Abs(buffer[i]);
            right += Math.Abs(buffer[i + 1]);
        }

        Assert.True(left > right * 4);
    }

    [Fact]
    public void Panning_does_not_change_how_loud_the_tone_is_overall()
    {
        // Equal-power panning: moving a tone across the field must not make it
        // seem to get quieter in the middle.
        var centre = Energy(0);
        var left = Energy(-1);

        Assert.InRange(left / centre, 0.8, 1.25);

        static double Energy(double pan)
        {
            var bank = new ToneBank { Gain = 1.0 };
            bank.Play(440, 0.05, amplitude: 1.0, pan: pan);

            var buffer = new float[1024];
            bank.Fill(buffer);

            return buffer.Sum(s => (double)s * s);
        }
    }

    [Fact]
    public void Silencing_drops_everything_immediately()
    {
        var bank = new ToneBank();
        bank.Play(440, 1.0);
        bank.Silence();

        Assert.Equal(0, bank.ActiveVoices);
    }

    [Fact]
    public void Every_earcon_has_a_distinct_pitch()
    {
        // A family of similar beeps is worse than none - they only work if they
        // are told apart instantly.
        var pitches = Enum.GetValues<AccessibleVideoEditor.Speech.Earcon>()
            .Select(e => Earcons.Voice(e).Frequency)
            .ToList();

        Assert.Equal(pitches.Count, pitches.Distinct().Count());
    }

    [Fact]
    public void Every_earcon_is_short_enough_not_to_delay_the_next_one()
    {
        Assert.All(
            Enum.GetValues<AccessibleVideoEditor.Speech.Earcon>(),
            e => Assert.InRange(Earcons.Voice(e).Seconds, 0.01, 0.2));
    }
}
