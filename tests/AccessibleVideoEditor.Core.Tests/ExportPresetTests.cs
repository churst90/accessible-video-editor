using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Engine;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Named output targets. The part that matters without sight is that a preset
/// says what it will <b>cost</b> - a vertical export throws away half the width
/// of every frame, and that is otherwise something you find out by watching.
/// </summary>
public class ExportPresetTests
{
    private static ExportPreset Named(string name) => ExportPreset.ByName(name)!;

    [Fact]
    public void Presets_are_named_for_what_they_are_for()
    {
        Assert.All(ExportPreset.BuiltIn, preset =>
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));
            Assert.False(string.IsNullOrWhiteSpace(preset.Purpose));
        });
    }

    [Fact]
    public void The_description_leads_with_the_name_and_the_purpose_not_the_numbers()
    {
        var described = Named("YouTube 1080p").Describe();

        Assert.StartsWith("YouTube 1080p", described);
        Assert.Contains("the normal one", described);
        Assert.Contains("1920 by 1080", described);
    }

    [Fact]
    public void Exporting_at_the_same_shape_says_nothing_is_lost()
    {
        var cost = Named("YouTube 1080p").DescribeCost(1920, 1080);

        Assert.Contains("nothing is lost", cost);
    }

    [Fact]
    public void A_vertical_export_says_how_much_of_the_frame_goes()
    {
        // 16:9 to 9:16 loses about 68 percent of the width. Being told "the
        // sides are cropped" without the number leaves you unable to judge
        // whether the shot survives it.
        var cost = Named("Shorts").DescribeCost(1920, 1080);

        Assert.Contains("sides", cost);
        Assert.Contains("cropped", cost);
        Assert.Matches(@"\d+ percent", cost);
    }

    [Fact]
    public void A_square_export_loses_less_than_a_vertical_one()
    {
        var square = Percent(Named("Square").DescribeCost(1920, 1080));
        var vertical = Percent(Named("Shorts").DescribeCost(1920, 1080));

        Assert.True(vertical > square, $"vertical {vertical} should lose more than square {square}");
    }

    [Fact]
    public void Audio_only_says_the_picture_is_dropped()
    {
        var preset = Named("Audio only");

        Assert.Contains("picture is dropped", preset.DescribeCost(1920, 1080));
        Assert.Equal(".m4a", preset.Extension);
    }

    [Fact]
    public void A_letterboxing_preset_says_bars_rather_than_cropping()
    {
        var fit = new ExportPreset
        {
            Name = "Fitted", Purpose = "test", Width = 1080, Height = 1920, Fit = FitMode.Fit,
        };

        Assert.Contains("bars", fit.DescribeCost(1920, 1080));
        Assert.DoesNotContain("cropped", fit.DescribeCost(1920, 1080));
    }

    [Fact]
    public void Each_preset_writes_to_its_own_file_so_two_exports_do_not_overwrite()
    {
        var names = ExportPreset.BuiltIn.Select(p => p.FileName).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.DoesNotContain(names, n => n.Contains(' '));
    }

    [Fact]
    public void Looking_a_preset_up_by_name_ignores_case()
    {
        Assert.NotNull(ExportPreset.ByName("shorts"));
        Assert.Null(ExportPreset.ByName("imax"));
    }

    // ---- the reshape filter ----------------------------------------------

    [Fact]
    public void A_preset_that_matches_the_canvas_needs_no_rescale()
    {
        Assert.Null(FfmpegRenderEngine.Reshape(Named("YouTube 1080p"), 1920, 1080));
    }

    [Fact]
    public void Filling_crops_and_fitting_pads()
    {
        var fill = FfmpegRenderEngine.Reshape(Named("Shorts"), 1920, 1080)!;

        Assert.Contains("crop=1080:1920", fill);
        Assert.Contains("increase", fill);

        var fit = FfmpegRenderEngine.Reshape(
            new ExportPreset
            {
                Name = "f", Purpose = "p", Width = 1080, Height = 1920, Fit = FitMode.Fit,
            },
            1920, 1080)!;

        Assert.Contains("pad=1080:1920", fit);
        Assert.Contains("decrease", fit);
    }

    [Fact]
    public void An_audio_only_preset_has_no_shape_to_reshape_to()
    {
        Assert.Null(FfmpegRenderEngine.Reshape(Named("Audio only"), 1920, 1080));
    }

    private static double Percent(string text) =>
        double.Parse(System.Text.RegularExpressions.Regex.Match(text, @"(\d+) percent").Groups[1].Value);
}
