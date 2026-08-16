using AccessibleVideoEditor.Core;
using AccessibleVideoEditor.Core.Commands;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Images;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Navigation;
using AccessibleVideoEditor.Core.Serialization;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Streaming;
using AccessibleVideoEditor.Core.Timeline;
using AccessibleVideoEditor.Audio;
using AccessibleVideoEditor.Engine;
using AccessibleVideoEditor.Playback;
using AccessibleVideoEditor.Vision;
using AccessibleVideoEditor.Speech;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// The image editor's commands - the menus and choosers its keys open.
///
/// Sizes, corrections and levels are all offered as <b>named presets with the
/// number said afterwards</b>, because you are choosing a decision rather than
/// a value: "fit 1080" is what you mean and "1920 by 1080" is arithmetic you
/// would have to do first to say it.
/// </summary>
public sealed partial class MainWindow
{
    private string SuggestedImageName() =>
        _images.Document is { } document
            ? System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(document.Path) ?? ".",
                System.IO.Path.GetFileNameWithoutExtension(document.Path) + "-edited.png")
            : string.Empty;

    /// <summary>
    /// Sizes by name rather than by number. "Fit 1080" is a decision; "1920 by
    /// 1080" is arithmetic you have to do first.
    /// </summary>
    private void ChooseSizePreset()
    {
        var menu = Gio.Menu.New();

        foreach (var (name, _, _) in ImageEdits.Presets)
        {
            menu.AppendItem(Gio.MenuItem.New(TitleCase(name), $"win.imageSize::{name}"));
        }

        PopUp(menu, "size menu");
    }

    /// <summary>
    /// Corrections by name rather than by number: these are the sentences
    /// people say about a photograph, and each is a nudge so it can be applied
    /// twice when once was not enough.
    /// </summary>
    private void ChooseCorrection()
    {
        var menu = Gio.Menu.New();

        foreach (var preset in ColourEdits.Presets)
        {
            menu.AppendItem(Gio.MenuItem.New(TitleCase(preset), $"win.imageCorrect::{preset}"));
        }

        PopUp(menu, "colour menu. Shift+V measures the picture and suggests one");
    }

    /// <summary>
    /// Levels by name: the black point, the white point, and the three zones
    /// between them. Auto sets the points from the picture's own histogram,
    /// which is the one command that makes a curve worth having without a graph.
    /// </summary>
    private void ChooseLevels()
    {
        var menu = Gio.Menu.New();

        foreach (var preset in LevelEdits.Presets)
        {
            menu.AppendItem(Gio.MenuItem.New(TitleCase(preset), $"win.imageLevel::{preset}"));
        }

        PopUp(menu, "levels menu. Apostrophe reads the histogram");
    }

    /// <summary>
    /// Per-channel levels: the automatic answers first, then the nudges. This
    /// is the only thing that reaches a cast the temperature control cannot.
    /// </summary>
    private void ChooseColourLevels()
    {
        var menu = Gio.Menu.New();

        menu.AppendItem(Gio.MenuItem.New("Auto Colour Levels", "win.imageColourLevel::auto colour levels"));
        menu.AppendItem(Gio.MenuItem.New(
            "Balance On The Pointer", "win.imageColourLevel::balance on the pointer"));

        foreach (var preset in LevelEdits.ChannelPresets)
        {
            menu.AppendItem(Gio.MenuItem.New(TitleCase(preset), $"win.imageColourLevel::{preset}"));
        }

        PopUp(menu, "colour levels. Shift+apostrophe says which way the colour is pulling");
    }

    /// <summary>
    /// A folder of scans, treated like the one already on screen. It asks for
    /// both folders and says what it is about to do before it does it.
    /// </summary>
    private void RunBatch()
    {
        if (_images.Document is null)
        {
            Announce("open one picture and get it right first; the batch copies what you did to it",
                urgent: true);

            return;
        }

        Prompt("Folder of pictures", string.Empty, "Next", source =>
        {
            var preview = _images.PreviewBatch(source);

            if (preview.StartsWith("there are no", StringComparison.Ordinal))
            {
                Announce(preview, urgent: true);
                return;
            }

            Announce(preview, urgent: true);

            Prompt(
                "Where to write them",
                System.IO.Path.Combine(source, "edited"),
                "Run",
                target => ConfirmThen(
                    $"Do that to every picture in {System.IO.Path.GetFileName(source)}?",
                    () => _ = _images.RunBatch(source, target)));
        });
    }

    /// <summary>
    /// The card editor, on a picture. The same window that edits a card on the
    /// timeline - one editor, one vocabulary, and a lower third means the same
    /// thing in both places.
    /// </summary>
    private void EditImageCard()
    {
        if (_images.Document is null)
        {
            Announce("no picture is open", urgent: true);
            return;
        }

        var card = _images.EnsureCard();

        new CardEditor(_window, card, text => Announce(text, urgent: true), _images.CardChanged).Present();
    }

    /// <summary>
    /// Sends the edited picture into the project, so a photograph that has just
    /// been straightened and cropped can go straight onto the timeline without
    /// leaving the application or finding the file again.
    /// </summary>
    private void SendImageToProject()
    {
        if (_images.Document is not { } document)
        {
            Announce("no picture is open", urgent: true);
            return;
        }

        var target = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{System.IO.Path.GetFileNameWithoutExtension(document.Path)}-edited.png");

        _ = SendImageAsync(target);
    }

    private async Task SendImageAsync(string target)
    {
        Announce("saving and importing", urgent: true);

        var written = await _images.ExportTo(target).ConfigureAwait(true);

        if (!written.StartsWith("saved", StringComparison.Ordinal))
        {
            Announce(written, urgent: true);
            return;
        }

        await ImportAsync(target).ConfigureAwait(true);

        Announce("in the media bin. Insert it from there, or press I again after more changes",
            urgent: true);
    }

    private void ChooseCropRatio()
    {
        var menu = Gio.Menu.New();

        foreach (var (name, _) in CropRatios)
        {
            menu.AppendItem(Gio.MenuItem.New(name, $"win.imageCrop::{name}"));
        }

        PopUp(menu, "crop menu. It will ask where to anchor it");
    }

    private static readonly (string Name, double Ratio)[] CropRatios =
    [
        ("Square", 1),
        ("16 by 9", 16.0 / 9),
        ("4 by 3", 4.0 / 3),
        ("3 by 2", 3.0 / 2),
        ("4 by 5", 4.0 / 5),
        ("9 by 16", 9.0 / 16),
    ];
    private void SampleAPoint()
    {
        if (_images.Document is not { } document) return;

        Prompt(
            "Point, as x and y or a cell number",
            $"{document.Width / 2} {document.Height / 2}",
            "Read",
            text =>
            {
                var parts = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 1 && int.TryParse(parts[0], out var cell) && cell is >= 1 and <= 9)
                {
                    var (nx, ny) = new Placement(cell).Resolve();

                    _images.SampleColour(nx * document.SourceWidth, ny * document.SourceHeight);
                    return;
                }

                if (parts.Length >= 2 && double.TryParse(parts[0], out var x) && double.TryParse(parts[1], out var y))
                {
                    _images.SampleColour(x, y);
                    return;
                }

                Announce("say two numbers, or one cell number from 1 to 9", urgent: true);
            });
    }
}
