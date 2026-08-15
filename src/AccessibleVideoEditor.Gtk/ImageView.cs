using AccessibleVideoEditor.Audio;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Images;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Engine;
using AccessibleVideoEditor.Speech;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// The image editor.
///
/// Three lists and a report: what the picture <b>is</b>, what has been
/// <b>decided</b> about it, and what has been <b>drawn</b> on it. Nothing here
/// is a canvas you point at - every operation is a command with a measured
/// answer, which is the only way editing a picture works without sight.
///
/// The order of the panes is the order of the questions: what have I got, what
/// am I doing to it, what have I added.
/// </summary>
public sealed class ImageView
{
    private readonly Func<IAnnouncer> _announcer;
    private readonly Func<SdlAudioOutput?> _audio;
    private readonly ImageIo _io = new();
    private readonly ImageHistory _history = new();

    /// <summary>The pointer you can hear. Sweeping, as opposed to naming a point.</summary>
    public ImagePointer Pointer { get; } = new();

    private bool _sweeping;
    private Raster? _raster;
    private ColourRaster? _colours;

    private Gtk_.Label _facts = null!;
    private Gtk_.ListBox _steps = null!;
    private Gtk_.ListBox _shapes = null!;
    private Gtk_.Label _status = null!;

    public ImageDocument? Document => _history.Document;

    public ImageView(Func<IAnnouncer> announcer, Func<SdlAudioOutput?> audio)
    {
        _announcer = announcer;
        _audio = audio;
    }

    public Gtk_.Widget Build()
    {
        _status = Gtk_.Label.New("no picture open. O opens one");
        _status.Xalign = 0;
        _status.Wrap = true;
        _status.AddCssClass("readout");

        _facts = Gtk_.Label.New(string.Empty);
        _facts.Xalign = 0;
        _facts.Yalign = 0;
        _facts.Wrap = true;
        _facts.Focusable = true;
        _facts.MarginTop = 10;
        _facts.MarginBottom = 10;
        _facts.MarginStart = 12;
        _facts.MarginEnd = 12;

        _steps = List();
        _shapes = List();

        var left = Gtk_.Box.New(Gtk_.Orientation.Vertical, 6);
        left.Append(Heading("The picture"));
        left.Append(Scrolled(_facts));
        left.SetSizeRequest(420, -1);

        var right = Gtk_.Box.New(Gtk_.Orientation.Vertical, 6);
        right.Append(Heading("What has been decided"));
        right.Append(Scrolled(_steps));
        right.Append(Heading("Drawn on top"));
        right.Append(Scrolled(_shapes));
        right.Hexpand = true;

        var split = Gtk_.Box.New(Gtk_.Orientation.Horizontal, 10);
        split.Append(left);
        split.Append(right);

        var root = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        root.Append(_status);
        root.Append(split);

        Refresh();

        return root;
    }

    private static Gtk_.ListBox List()
    {
        var list = Gtk_.ListBox.New();
        list.SelectionMode = Gtk_.SelectionMode.Single;
        list.Vexpand = true;

        return list;
    }

    private static Gtk_.Widget Scrolled(Gtk_.Widget child)
    {
        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(child);
        scroller.Vexpand = true;
        scroller.AddCssClass("pane");

        return scroller;
    }

    private static Gtk_.Label Heading(string text)
    {
        var label = Gtk_.Label.New(text.ToUpperInvariant());
        label.Xalign = 0;
        label.AddCssClass("pane-heading");

        return label;
    }

    private static Gtk_.ListBoxRow Row(string text)
    {
        var label = Gtk_.Label.New(text);
        label.Xalign = 0;
        label.Wrap = true;
        label.MarginTop = 7;
        label.MarginBottom = 7;
        label.MarginStart = 10;
        label.MarginEnd = 10;

        var row = Gtk_.ListBoxRow.New();
        row.SetChild(label);

        return row;
    }

    // ---- opening -----------------------------------------------------------

    /// <summary>
    /// Opening measures first and says what it found, because the first
    /// question about any picture is "what have I actually got here" - and it
    /// is the question that normally needs eyes.
    /// </summary>
    public async void Open(string path)
    {
        await Guard(async () =>
        {
            Say($"opening {System.IO.Path.GetFileName(path)}");

            var examined = await _io.ExamineAsync(path);

            if (examined is not { } found)
            {
                Say("that is not a picture I can read", urgent: true);
                return;
            }

            var (facts, report) = found;

            var document = ImageDocument.Open(path, facts.Width, facts.Height, facts.Dpi);
            document.Report = report;

            // Opening starts a new history rather than adding to the old one:
            // undoing past the moment a picture was opened would land you in a
            // different picture.
            _history.Open(document);

            // Kept for the pointer and the colour advice: both ask questions about
            // the picture, and neither is worth a round trip to ffmpeg per press.
            _raster = await _io.DecodeAsync(path);
            _colours = await _io.DecodeColourAsync(path);

            Refresh();

            Say($"{document.Describe()}. {report.Describe()}. {report.Offer()}", urgent: true);
        }, "opening the picture");
    }

    /// <summary>
    /// The description, from the same command that reviews a video frame. This
    /// is the one part that genuinely needs eyes, done by something that has
    /// them.
    /// </summary>
    public async void Describe()
    {
        await Guard(async () =>
        {
            if (Document is not { } document) return;

            Say("looking at it");

            var describer = new FrameDescriber();

            if (!describer.IsAvailable)
            {
                Say("the claude command is not installed, so pictures cannot be described", urgent: true);
                return;
            }

            Say(await describer.DescribeAsync(document.Path), urgent: true);
        }, "describing it");
    }

    // ---- what is on screen -------------------------------------------------

    public void Refresh()
    {
        if (Document is not { } document)
        {
            _facts.SetText("No picture open.\n\nO opens one.");
            _status.SetText("no picture open. O opens one");

            Clear(_steps);
            Clear(_shapes);
            _steps.Append(Row("nothing to do until a picture is open"));

            return;
        }

        _facts.SetText(FactsText(document));
        _status.SetText(
            $"{System.IO.Path.GetFileName(document.Path)}   "
            + $"{document.Width} by {document.Height}   {document.Ratio()}   "
            + $"{document.Shapes.Count} shapes");

        Clear(_steps);

        foreach (var step in Steps(document)) _steps.Append(Row(step));

        Clear(_shapes);

        if (document.Shapes.Count == 0)
        {
            _shapes.Append(Row("nothing drawn. Shift+D says a shape, like: circle at centre, radius 20 percent, white"));
        }
        else
        {
            for (var i = 0; i < document.Shapes.Count; i++)
            {
                _shapes.Append(Row($"{i + 1}. {document.Shapes[i].Describe()}"));
            }
        }
    }

    private static string FactsText(ImageDocument document)
    {
        var lines = new List<string>
        {
            System.IO.Path.GetFileName(document.Path),
            string.Empty,
            document.Describe(),
        };

        if (document.Report is { } report)
        {
            lines.Add(string.Empty);
            lines.Add(report.Describe());

            if (report.Offer() != "nothing needs fixing")
            {
                lines.Add($"Shift+F would {report.Offer()}.");
            }
        }

        if (document.IsEnlarged)
        {
            lines.Add(string.Empty);
            lines.Add("This is bigger than the original, so it will look softer.");
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Everything decided so far, as a list you can step through. Nothing is
    /// destructive, so this is the whole edit and every line of it is undoable.
    /// </summary>
    private static IEnumerable<string> Steps(ImageDocument document)
    {
        yield return $"opened at {document.SourceWidth} by {document.SourceHeight}";

        if (Math.Abs(document.RotationDegrees) > 0.01)
        {
            yield return $"rotated {document.RotationDegrees:0.0} degrees";
        }

        if (document.Crop.Width != document.SourceWidth || document.Crop.Height != document.SourceHeight)
        {
            yield return $"cropped to {document.Crop.Width} by {document.Crop.Height} "
                         + $"at {document.Crop.X}, {document.Crop.Y}";
        }

        if (document.IsResampled)
        {
            yield return $"resized to {document.Width} by {document.Height}";
        }

        var (inches, high) = document.PrintSize;

        yield return $"prints at {inches:0.#} by {high:0.#} inches, {document.Dpi} dpi";
        yield return $"about {document.EstimatedMegabytes():0.##} megabytes as a jpeg";
    }

    private static void Clear(Gtk_.ListBox list)
    {
        while (list.GetFirstChild() is { } child) list.Remove(child);
    }

    // ---- editing -----------------------------------------------------------

    /// <summary>
    /// Every edit goes through the history, so everything is undoable by
    /// construction rather than by remembering to record it.
    /// </summary>
    public void Apply(string label, Func<ImageDocument, EditResult> operation)
    {
        if (Document is null)
        {
            Say("no picture is open", urgent: true);
            return;
        }

        var result = _history.Do(label, operation);

        Refresh();
        Say(result.Announce(), urgent: true);
    }

    public void Undo()
    {
        var result = _history.Undo();

        Refresh();
        Say(result.Announce(), urgent: true);
    }

    public void Redo()
    {
        var result = _history.Redo();

        Refresh();
        Say(result.Announce(), urgent: true);
    }

    /// <summary>
    /// What would be undone, without undoing it - the only way to be sure the
    /// key is about to do what you think it will.
    /// </summary>
    public void DescribeHistory() => Say(_history.Describe(), urgent: true);

    /// <summary>
    /// The card laid over the picture, using the video editor's own card model
    /// so it is edited by the same editor and described by the same sentence.
    /// </summary>
    public CardComposition EnsureCard()
    {
        if (Document is not { } document) return CardTemplates.LowerThird("", "");

        document.Card ??= CardTemplates.LowerThird("Title", "Subtitle");

        return document.Card;
    }

    public void CardChanged()
    {
        Refresh();

        if (Document?.Card is { } card) Say(card.Summarise(), urgent: true);
    }

    public bool HasPicture => Document is not null;

    /// <summary>
    /// Arrow keys. Held to a short sentence because this is the fast path -
    /// the full report on every press would be unusable at speed.
    /// </summary>
    public void Nudge(bool horizontal, int by)
    {
        if (Document is null) return;

        var result = _history.Do("resize", document => ImageEdits.Nudge(document, horizontal, by));

        Refresh();
        Say(result.Description);
    }

    public void NudgeEdge(CropEdge edge, int by) =>
        Apply("moving an edge", document => ImageEdits.NudgeEdge(document, edge, by));

    public void AddShape(string sentence)
    {
        if (Document is not { } document) return;

        if (ShapeLanguage.Parse(sentence) is not { } shape)
        {
            Say($"I did not understand that. {ShapeLanguage.Help()}", urgent: true);
            return;
        }

        _history.Do("drawing", target =>
        {
            target.Shapes.Add(shape);

            return EditResult.Ok(shape.Describe());
        });

        Refresh();

        // Painted onto a scratch canvas at a workable size purely to find out
        // how much of the picture it covers - which is the part you cannot see.
        var canvas = new Canvas(Math.Min(400, document.Width), Math.Min(400, document.Height));

        Say(shape.DrawOn(canvas), urgent: true);
    }

    public void RemoveShape()
    {
        if (Document is not { Shapes.Count: > 0 } document) return;

        var index = _shapes.GetSelectedRow()?.GetIndex() ?? document.Shapes.Count - 1;

        if (index < 0 || index >= document.Shapes.Count) return;

        var result = _history.Do("removing a shape", target =>
        {
            var removed = target.Shapes[index];
            target.Shapes.RemoveAt(index);

            return EditResult.Ok($"removed {removed.Describe()}");
        });

        Refresh();
        Say(result.Announce(), urgent: true);
    }

    /// <summary>
    /// What the whole thing looks like as colours, without describing it. Fast,
    /// free, and often enough - "mostly navy, a fifth white" answers most
    /// questions about an abstract picture.
    /// </summary>
    public void DescribeColours()
    {
        if (Document is not { } document) return;

        if (document.Shapes.Count == 0)
        {
            Say("nothing has been drawn on it; F8 describes the picture itself", urgent: true);
            return;
        }

        var canvas = new Canvas(200, 200);

        foreach (var shape in document.Shapes) shape.DrawOn(canvas);

        Say(canvas.Describe(), urgent: true);
    }

    /// <summary>The colour at a point in the real image, named before it is valued.</summary>
    public async void SampleColour(double x, double y)
    {
        await Guard(async () =>
        {
            if (Document is not { } document) return;

            var point = await _io.SampleAsync(
                document.Path,
                (int)Math.Clamp(x, 0, document.SourceWidth - 1),
                (int)Math.Clamp(y, 0, document.SourceHeight - 1));

            Say(point is { } colour
                ? $"{Colours.Describe(colour.R, colour.G, colour.B)}, "
                  + $"{document.PlacementAt(x, y).Describe()}"
                : "could not read that point", urgent: true);
        }, "reading that point");
    }

    // ---- the pointer you can hear ------------------------------------------

    /// <summary>
    /// Turns sweeping on. From here the arrow keys move a pointer rather than
    /// resizing, and every move plays a tone panned to where it is and pitched
    /// to how far up it is.
    /// </summary>
    public void ToggleSweep()
    {
        if (Document is null)
        {
            Say("no picture is open", urgent: true);
            return;
        }

        _sweeping = !_sweeping;

        if (!_sweeping)
        {
            Say("pointer off, arrows resize again", urgent: true);
            return;
        }

        Say($"pointer on. {Pointer.Describe(Document.Width, Document.Height)}. "
            + "Arrows sweep, Enter reads the colour, escape leaves", urgent: true);

        Sound();
    }

    public bool IsSweeping => _sweeping;

    /// <summary>
    /// One step. The tone plays on every move; the words only when the pointer
    /// crosses into a new cell, because two numbers per press is unusable at
    /// speed and silence is unusable at all.
    /// </summary>
    public void Sweep(double dx, double dy)
    {
        if (Document is not { } document) return;

        var before = Pointer.Placement;

        if (!Pointer.Move(dx, dy))
        {
            Say("edge of the picture");
            return;
        }

        Sound();

        if (Pointer.CrossedInto(before) is { } cell) Say(cell);
    }

    public void SweepStep(bool finer) =>
        Say(finer ? Pointer.Finer() : Pointer.Coarser(), urgent: true);

    public void SweepTo(Placement placement)
    {
        if (Document is not { } document) return;

        Pointer.MoveTo(placement);
        Sound();

        Say(Pointer.Describe(document.Width, document.Height), urgent: true);
    }

    /// <summary>Where the pointer is, in words, on demand.</summary>
    public void WhereIsThePointer()
    {
        if (Document is not { } document) return;

        Say(Pointer.Describe(document.Width, document.Height), urgent: true);
    }

    /// <summary>
    /// The colour under the pointer, taken from the small copy so it answers
    /// instantly. The exact value is available from the file when it is asked
    /// for by coordinates instead.
    /// </summary>
    public void ReadUnderPointer()
    {
        if (Document is not { } document) return;

        if (_raster is not { } raster)
        {
            SampleColour(Pointer.X * document.SourceWidth, Pointer.Y * document.SourceHeight);
            return;
        }

        var (x, y) = Pointer.PixelIn(raster.Width, raster.Height);
        var grey = raster.At(x, y);

        Say($"{Colours.NameOf(grey, grey, grey)} in brightness, "
            + $"{Math.Round(grey * 100.0 / 255)} percent, "
            + $"{ShapeLanguage.CellName(Pointer.Placement)}", urgent: true);
    }

    private void Sound()
    {
        var tone = Pointer.Tone;

        _audio()?.Play(tone.PitchHz, 0.05, 0.35, tone.Pan);
    }

    // ---- colour ------------------------------------------------------------

    /// <summary>
    /// What is wrong with the picture and what would fix it, in the same words
    /// the corrections are called - so the advice can be acted on by pressing
    /// the thing it just named.
    /// </summary>
    public void AdviseColour()
    {
        if (Document is null) return;

        if (_raster is not { } raster)
        {
            Say("nothing measured yet", urgent: true);
            return;
        }

        Say(ColourEdits.Advise(raster), urgent: true);
    }

    public void Correct(string preset) =>
        Apply(preset, document => ColourEdits.Apply(document, preset));

    // ---- levels ------------------------------------------------------------

    /// <summary>
    /// The curve, as numbers with names. Auto is separate because it needs the
    /// picture's own histogram rather than a fixed step.
    /// </summary>
    public void Level(string preset)
    {
        if (preset != "auto levels")
        {
            Apply(preset, document => LevelEdits.Apply(document, preset));
            return;
        }

        if (_raster is not { } raster)
        {
            Say("nothing measured yet", urgent: true);
            return;
        }

        Apply("auto levels", document => LevelEdits.Auto(document, raster));
    }

    /// <summary>
    /// Per channel. Automatic from the whole picture, or balanced on the point
    /// the pointer is sitting on - the eyedropper, done without pointing.
    /// </summary>
    public void ColourLevel(string preset)
    {
        if (_colours is not { } colours)
        {
            Say("no picture is open", urgent: true);
            return;
        }

        switch (preset)
        {
            case "auto colour levels":
                Apply(preset, document => LevelEdits.AutoColour(document, colours));
                return;

            case "balance on the pointer":
                Apply("white balance", document =>
                    LevelEdits.NeutraliseAt(document, colours, Pointer.X, Pointer.Y));
                return;

            default:
                Apply(preset, document => LevelEdits.Channel(document, preset));
                return;
        }
    }

    /// <summary>Which way the colour is pulling, and by how much.</summary>
    public void ReadCast()
    {
        if (_colours is not { } colours)
        {
            Say("no picture is open", urgent: true);
            return;
        }

        Say(ColourCast.Of(colours).Describe(), urgent: true);
    }

    /// <summary>
    /// The histogram, as five numbers. This is what the curve was drawn on top
    /// of, and it is the part that tells you which way to move.
    /// </summary>
    public void ReadHistogram()
    {
        if (_raster is not { } raster)
        {
            Say("no picture is open", urgent: true);
            return;
        }

        var zones = ToneZones.Of(raster);

        Say($"{zones.Summarise()}. {zones.Describe()}", urgent: true);
    }

    // ---- doing it to a folder ----------------------------------------------

    /// <summary>
    /// The corrections travel; the geometry is measured per picture. Said out
    /// loud before it runs, because a batch can go wrong a hundred times before
    /// anybody notices.
    /// </summary>
    public string PreviewBatch(string directory) =>
        BatchProcessor.Preview(directory, JobFromHere());

    public BatchJob JobFromHere() =>
        Document is { } document ? BatchJob.From(document, autoLevels: true) : new BatchJob();

    public async Task RunBatch(string sourceDirectory, string outputDirectory)
    {
        var job = JobFromHere();

        Say($"starting. {BatchProcessor.Preview(sourceDirectory, job)}", urgent: true);

        var result = await new BatchProcessor().RunAsync(
            sourceDirectory,
            outputDirectory,
            job,
            progress => Say(progress));

        Say(result.Describe(), urgent: true);
    }

    // ---- exporting ---------------------------------------------------------

    public async void Export(string path)
    {
        await Guard(async () =>
        {
            if (Document is null) return;

            Say("saving");
            Say(await ExportTo(path), urgent: true);
        }, "saving");
    }

    /// <summary>
    /// The awaitable form, for callers that have something to do afterwards -
    /// sending the picture into the project has to know the file arrived before
    /// it tries to import it.
    /// </summary>
    public async Task<string> ExportTo(string path) =>
        Document is { } document
            ? await _io.ExportAsync(document, path)
            : "no picture is open";

    public async void Split(string directory)
    {
        await Guard(async () =>
        {
            if (Document is not { } document) return;

            if (document.Report is not { Regions.Count: > 1 })
            {
                Say("there is only one picture here", urgent: true);
                return;
            }

            Say("splitting");

            Say(await _io.SplitAsync(document, directory), urgent: true);
        }, "splitting");
    }

    // ---- speech ------------------------------------------------------------

    private void Say(string text, bool urgent = false) =>
        _announcer().Say(text, urgent ? AnnouncePriority.Urgent : AnnouncePriority.Normal);

    /// <summary>
    /// Guards an entry point called from a key or a menu. Without it an
    /// exception in one of these takes the process down instead of being
    /// announced.
    /// </summary>
    private async Task Guard(Func<Task> work, string what)
    {
        try
        {
            await work();
        }
        catch (Exception exception)
        {
            Say($"{what} failed: {exception.Message}", urgent: true);
        }
    }

}
