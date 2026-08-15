using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Engine;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Audio effects as named presets with a spoken number, and advice phrased as
/// the commands that would act on it.
/// </summary>
public class AudioEffectTests
{
    [Fact]
    public void Every_effect_is_named_for_what_it_does_not_for_its_filter()
    {
        // "room tone removal" is a decision; "afftdn" is an implementation you
        // would have to be taught.
        Assert.Equal("room tone removal", AudioEffect.Of(AudioEffectKind.NoiseReduction).Name);
        Assert.Equal("levelling", AudioEffect.Of(AudioEffectKind.Compress).Name);
        Assert.Equal("rumble filter", AudioEffect.Of(AudioEffectKind.HighPass).Name);
    }

    [Fact]
    public void Every_effect_states_its_setting_in_a_unit_people_say_out_loud()
    {
        Assert.Contains("dB", AudioEffect.Of(AudioEffectKind.NoiseReduction).DescribeAmount());
        Assert.Contains("hertz", AudioEffect.Of(AudioEffectKind.HighPass).DescribeAmount());
        Assert.Contains("to 1", AudioEffect.Of(AudioEffectKind.Compress).DescribeAmount());
        Assert.Contains("LUFS", AudioEffect.Of(AudioEffectKind.Normalise).DescribeAmount());
    }

    [Fact]
    public void Every_effect_explains_what_it_is_for()
    {
        Assert.All(Enum.GetValues<AudioEffectKind>(), kind =>
            Assert.False(string.IsNullOrWhiteSpace(AudioEffect.Of(kind).Purpose)));
    }

    [Fact]
    public void The_whole_amount_range_is_useful_at_both_ends()
    {
        // An effect whose bottom half does nothing audible is one you cannot set
        // by ear.
        var quiet = AudioEffect.Of(AudioEffectKind.NoiseReduction, 0);
        var hard = AudioEffect.Of(AudioEffectKind.NoiseReduction, 1);

        Assert.True(quiet.NoiseReductionDb >= 6);
        Assert.True(hard.NoiseReductionDb <= 24);
        Assert.True(hard.NoiseReductionDb - quiet.NoiseReductionDb > 10);
    }

    [Fact]
    public void Effects_run_in_a_fixed_order_regardless_of_the_order_they_were_added()
    {
        // A compressor pumping on rumble you were about to remove is a mistake
        // you would need the theory to predict.
        var chain = new List<AudioEffect>
        {
            AudioEffect.Of(AudioEffectKind.Normalise),
            AudioEffect.Of(AudioEffectKind.Compress),
            AudioEffect.Of(AudioEffectKind.HighPass),
            AudioEffect.Of(AudioEffectKind.NoiseReduction),
        };

        var ordered = AudioChains.InOrder(chain).Select(e => e.Kind).ToList();

        Assert.Equal(
            [AudioEffectKind.HighPass, AudioEffectKind.NoiseReduction,
             AudioEffectKind.Compress, AudioEffectKind.Normalise],
            ordered);
    }

    [Fact]
    public void A_chain_reads_back_as_the_sentence_that_would_make_it()
    {
        var chain = AudioChains.Build("Close mic")!;

        var described = AudioChains.Describe(chain);

        Assert.Contains("rumble filter", described);
        Assert.Contains("then", described);
    }

    [Fact]
    public void An_all_off_chain_says_so_rather_than_reading_as_empty()
    {
        var chain = AudioChains.Build("Broadcast")!;
        foreach (var effect in chain) effect.Enabled = false;

        Assert.Contains("all off", AudioChains.Describe(chain));
    }

    [Fact]
    public void Every_named_chain_builds_and_every_kind_produces_a_filter()
    {
        Assert.All(AudioChains.Presets, preset =>
        {
            var chain = AudioChains.Build(preset.Name);
            Assert.NotNull(chain);
            Assert.NotEmpty(AudioEffectFilters.Build(chain!));
        });

        Assert.All(Enum.GetValues<AudioEffectKind>(), kind =>
            Assert.NotEmpty(AudioEffectFilters.Filter(AudioEffect.Of(kind))));
    }

    [Fact]
    public void A_disabled_effect_contributes_nothing_to_the_filter()
    {
        var chain = new List<AudioEffect> { AudioEffect.Of(AudioEffectKind.HighPass) };
        Assert.NotEmpty(AudioEffectFilters.Build(chain));

        chain[0].Enabled = false;
        Assert.Empty(AudioEffectFilters.Build(chain));
    }

    [Fact]
    public void Filters_use_invariant_numbers_so_a_comma_locale_cannot_break_them()
    {
        var filter = AudioEffectFilters.Filter(AudioEffect.Of(AudioEffectKind.Compress, 0.5));

        Assert.Contains("ratio=4.75", filter);
        Assert.DoesNotContain(",75", filter);
    }

    // ---- advice ----------------------------------------------------------

    [Fact]
    public void A_noisy_floor_is_measured_and_the_advice_names_the_command()
    {
        var advice = AudioAdvice.For(loudnessLufs: -16, truePeakDb: -3, noiseFloorDb: -35);

        Assert.True(advice.HasAdvice);
        Assert.Contains(advice.Suggested, e => e.Kind == AudioEffectKind.NoiseReduction);
        Assert.Contains("room tone removal", advice.Announce());
    }

    [Fact]
    public void A_worse_noise_floor_suggests_a_harder_setting()
    {
        var mild = AudioAdvice.For(null, null, -44);
        var bad = AudioAdvice.For(null, null, -25);

        var mildAmount = mild.Suggested.First(e => e.Kind == AudioEffectKind.NoiseReduction).Amount;
        var badAmount = bad.Suggested.First(e => e.Kind == AudioEffectKind.NoiseReduction).Amount;

        Assert.True(badAmount > mildAmount);
    }

    [Fact]
    public void Clipping_is_reported_and_answered()
    {
        var advice = AudioAdvice.For(loudnessLufs: -14, truePeakDb: -0.1, noiseFloorDb: -60);

        Assert.Contains("clipping", advice.Announce());
        Assert.Contains(advice.Suggested, e => e.Kind == AudioEffectKind.Compress);
    }

    [Fact]
    public void Sound_that_measures_well_is_told_so_rather_than_met_with_silence()
    {
        // A command that says nothing reads as one that failed.
        var advice = AudioAdvice.For(loudnessLufs: -14, truePeakDb: -2, noiseFloorDb: -60);

        Assert.False(advice.HasAdvice);
        Assert.Contains("nothing to fix", advice.Announce());
    }

    [Fact]
    public void Loudness_is_reported_as_a_direction_and_a_distance()
    {
        var quiet = AudioAdvice.For(loudnessLufs: -28, truePeakDb: -6, noiseFloorDb: -60);

        Assert.Contains("quieter", quiet.Announce());
        Assert.Contains(quiet.Suggested, e => e.Kind == AudioEffectKind.Normalise);
    }

    [Fact]
    public void Normalise_is_not_suggested_twice_when_two_findings_both_want_it()
    {
        var advice = AudioAdvice.For(loudnessLufs: -30, truePeakDb: -0.1, noiseFloorDb: -60);

        Assert.Single(advice.Suggested, e => e.Kind == AudioEffectKind.Normalise);
    }

    [Fact]
    public void A_wide_dynamic_range_is_treated_as_a_problem_for_a_phone_speaker()
    {
        var advice = AudioAdvice.For(-14, -3, -60, dynamicRangeDb: 20);

        Assert.Contains("phone speaker", advice.Announce());
        Assert.Contains(advice.Suggested, e => e.Kind == AudioEffectKind.Compress);
    }
}
