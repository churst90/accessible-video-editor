using AccessibleVideoEditor.Core.Model;
using Gtk_ = Gtk;

namespace AccessibleVideoEditor.Gtk;

/// <summary>
/// Editing a card.
///
/// A list of layers, one per row, each announcing what it says and where it
/// lands. That shape is deliberate: a card is the one segment whose contents
/// are entirely invisible, so the editor has to be something you can read
/// through rather than a canvas you point at.
///
/// Everything is a keystroke on the focused layer. There is no drag, no
/// selection tool, and nothing that requires knowing where anything is on
/// screen.
/// </summary>
public sealed class CardEditor
{
    private readonly Gtk_.Window _window;
    private readonly Gtk_.ListBox _layers;
    private readonly Gtk_.Label _summary;
    private readonly CardComposition _card;
    private readonly Action<string> _announce;
    private readonly Action _changed;

    public CardEditor(
        Gtk_.Window parent,
        CardComposition card,
        Action<string> announce,
        Action changed)
    {
        _card = card;
        _announce = announce;
        _changed = changed;

        _window = Gtk_.Window.New();
        _window.Title = "Card";
        _window.Modal = true;
        _window.TransientFor = parent;
        _window.SetDefaultSize(720, 520);

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 12;
        box.MarginBottom = 12;
        box.MarginStart = 12;
        box.MarginEnd = 12;

        _summary = Gtk_.Label.New(string.Empty);
        _summary.Xalign = 0;
        _summary.Wrap = true;
        box.Append(_summary);

        _layers = Gtk_.ListBox.New();
        _layers.SelectionMode = Gtk_.SelectionMode.Single;
        _layers.OnRowActivated += (_, _) => EditFocused();

        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(_layers);
        scroller.Vexpand = true;
        box.Append(scroller);

        var help = Gtk_.Label.New(
            "Enter edit  ·  T add text  ·  I add image  ·  Delete remove  ·  "
            + "Alt+Up/Down reorder  ·  Numpad 1-9 place  ·  B background  ·  "
            + "L layout  ·  Escape close");
        help.Xalign = 0;
        help.Wrap = true;
        box.Append(help);

        var keys = Gtk_.EventControllerKey.New();
        keys.SetPropagationPhase(Gtk_.PropagationPhase.Capture);
        keys.OnKeyPressed += (_, args) => OnKey(args);
        _window.AddController(keys);

        _window.SetChild(box);
        Rebuild();
    }

    public void Present()
    {
        _window.Present();

        var first = _layers.GetRowAtIndex(0);
        if (first is not null)
        {
            _layers.SelectRow(first);
            first.GrabFocus();
        }

        _announce($"card editor. {_card.Summarise()}");
    }

    private int FocusedIndex => _layers.GetSelectedRow()?.GetIndex() ?? -1;

    private CardLayer? Focused =>
        FocusedIndex >= 0 && FocusedIndex < _card.Layers.Count ? _card.Layers[FocusedIndex] : null;

    private bool OnKey(Gtk_.EventControllerKey.KeyPressedSignalArgs args)
    {
        var alt = args.State.HasFlag(Gdk.ModifierType.AltMask);

        switch (args.Keyval)
        {
            case Gdk.Constants.KEY_Escape:
                _window.Close();
                return true;

            case Gdk.Constants.KEY_Return or Gdk.Constants.KEY_KP_Enter:
                EditFocused();
                return true;

            case Gdk.Constants.KEY_t or Gdk.Constants.KEY_T:
                AddText();
                return true;

            case Gdk.Constants.KEY_i or Gdk.Constants.KEY_I:
                _announce("adding an image needs the media bin, which is not wired yet");
                return true;

            case Gdk.Constants.KEY_Delete:
                RemoveFocused();
                return true;

            case Gdk.Constants.KEY_Up when alt:
                Reorder(-1);
                return true;

            case Gdk.Constants.KEY_Down when alt:
                Reorder(1);
                return true;

            case Gdk.Constants.KEY_b or Gdk.Constants.KEY_B:
                ChooseBackground();
                return true;

            case Gdk.Constants.KEY_l or Gdk.Constants.KEY_L:
                ToggleLayout();
                return true;

            // The numpad places the focused layer, exactly as it does on the
            // timeline. One idiom, learned once.
            case >= Gdk.Constants.KEY_KP_1 and <= Gdk.Constants.KEY_KP_9:
                Place((int)(args.Keyval - Gdk.Constants.KEY_KP_1) + 1);
                return true;

            case >= Gdk.Constants.KEY_1 and <= Gdk.Constants.KEY_9:
                Place((int)(args.Keyval - Gdk.Constants.KEY_1) + 1);
                return true;

            default:
                return false;
        }
    }

    private void Place(int cell)
    {
        if (Focused is not { } layer) return;

        layer.Placement = layer.Placement with { Cell = cell, SubCell = 0 };

        // Placing only means something on a grid, so say so rather than
        // silently doing nothing useful.
        if (_card.Layout == CardLayout.Stack)
        {
            _card.Layout = CardLayout.Grid;
            _announce($"switched to grid layout. {layer.Placement.Describe()}");
        }
        else
        {
            _announce(layer.Placement.Describe());
        }

        Rebuild();
    }

    private void ToggleLayout()
    {
        _card.Layout = _card.Layout == CardLayout.Stack ? CardLayout.Grid : CardLayout.Stack;

        _announce(_card.Layout == CardLayout.Stack
            ? "stacked: layers flow top to bottom with automatic spacing"
            : "grid: each layer sits where its numpad cell puts it");

        Rebuild();
    }

    private void AddText()
    {
        Prompt("New text layer", string.Empty, text =>
        {
            if (text.Length == 0) return;

            _card.Layers.Add(new TextLayer { Text = text, Size = TextSize.Medium });
            Rebuild();
            Select(_card.Layers.Count - 1);
            _announce($"added text, {text}");
        });
    }

    private void EditFocused()
    {
        if (Focused is not TextLayer text)
        {
            _announce(Focused is null ? "no layer" : "only text layers can be edited here");
            return;
        }

        Prompt("Text", text.Text, updated =>
        {
            text.Text = updated;
            Rebuild();
            _announce($"text is now {updated}");
        });
    }

    private void RemoveFocused()
    {
        var index = FocusedIndex;
        if (index < 0 || index >= _card.Layers.Count) return;

        var describe = _card.Layers[index].Describe();
        _card.Layers.RemoveAt(index);

        Rebuild();
        Select(Math.Min(index, _card.Layers.Count - 1));
        _announce($"removed {describe}");
    }

    private void Reorder(int delta)
    {
        var index = FocusedIndex;
        var target = index + delta;

        if (index < 0 || target < 0 || target >= _card.Layers.Count)
        {
            _announce(delta < 0 ? "already first" : "already last");
            return;
        }

        (_card.Layers[index], _card.Layers[target]) = (_card.Layers[target], _card.Layers[index]);

        Rebuild();
        Select(target);
        _announce($"moved {(delta < 0 ? "up" : "down")}, now {target + 1} of {_card.Layers.Count}");
    }

    private void ChooseBackground()
    {
        var options = new[]
        {
            "Solid colour",
            "Gradient",
            "Transparent - over the video",
            "Black",
        };

        ChooseFrom("Background", options, index =>
        {
            switch (index)
            {
                case 0:
                    Prompt("Colour, hex like #101014", _card.Background.Colour, value =>
                    {
                        _card.Background.Kind = BackgroundKind.Solid;
                        _card.Background.Colour = Normalise(value, "#101014");
                        Rebuild();
                        _announce(_card.Background.Describe());
                    });
                    break;

                case 1:
                    Prompt("Gradient from, hex", _card.Background.Colour, from =>
                        Prompt("Gradient to, hex", _card.Background.SecondColour, to =>
                            ChooseFrom("Direction", ["Vertical", "Horizontal", "Diagonal"], direction =>
                            {
                                _card.Background.Kind = BackgroundKind.Gradient;
                                _card.Background.Colour = Normalise(from, "#101014");
                                _card.Background.SecondColour = Normalise(to, "#2A2A3A");
                                _card.Background.Direction = (GradientDirection)direction;
                                Rebuild();
                                _announce(_card.Background.Describe());
                            })));
                    break;

                case 2:
                    _card.Background.Kind = BackgroundKind.Transparent;
                    Rebuild();
                    _announce("transparent, composites over the video");
                    break;

                default:
                    _card.Background.Kind = BackgroundKind.Solid;
                    _card.Background.Colour = "#000000";
                    Rebuild();
                    _announce("solid black");
                    break;
            }
        });
    }

    /// <summary>A hex colour, or the fallback. Never a silently broken value.</summary>
    private static string Normalise(string value, string fallback)
    {
        var text = value.Trim();
        if (!text.StartsWith('#')) text = "#" + text;

        return text.Length is 4 or 7
               && text[1..].All(c => char.IsAsciiHexDigit(c))
            ? text.ToUpperInvariant()
            : fallback;
    }

    private void Select(int index)
    {
        if (index < 0) return;

        var row = _layers.GetRowAtIndex(index);
        if (row is null) return;

        _layers.SelectRow(row);
        row.GrabFocus();
    }

    private void Rebuild()
    {
        var selected = FocusedIndex;

        while (_layers.GetRowAtIndex(0) is { } row) _layers.Remove(row);

        var lines = _card.LayerLines();

        if (lines.Count == 0)
        {
            _layers.Append(RowFor("No layers. Press T to add text."));
        }
        else
        {
            foreach (var line in lines) _layers.Append(RowFor(line));
        }

        _summary.SetText(_card.Summarise());
        _changed();

        Select(Math.Clamp(selected, 0, Math.Max(0, _card.Layers.Count - 1)));
    }

    private static Gtk_.ListBoxRow RowFor(string text)
    {
        var label = Gtk_.Label.New(text);
        label.Xalign = 0;
        label.MarginTop = 8;
        label.MarginBottom = 8;
        label.MarginStart = 10;
        label.MarginEnd = 10;

        var row = Gtk_.ListBoxRow.New();
        row.SetChild(label);
        return row;
    }

    private void Prompt(string title, string initial, Action<string> accepted)
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = title;
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(420, 120);

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 12; box.MarginBottom = 12; box.MarginStart = 12; box.MarginEnd = 12;

        var entry = Gtk_.Entry.New();
        entry.SetText(initial);
        box.Append(entry);

        void Commit()
        {
            var text = entry.GetText();
            dialog.Close();
            accepted(text);
        }

        var button = Gtk_.Button.NewWithLabel("OK");
        button.OnClicked += (_, _) => Commit();
        entry.OnActivate += (_, _) => Commit();
        box.Append(button);

        dialog.SetChild(box);
        dialog.Present();
        entry.GrabFocus();
    }

    private void ChooseFrom(string title, IReadOnlyList<string> options, Action<int> chosen)
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = title;
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(420, 260);

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 12; box.MarginBottom = 12; box.MarginStart = 12; box.MarginEnd = 12;

        var list = Gtk_.ListBox.New();
        list.SelectionMode = Gtk_.SelectionMode.Single;
        foreach (var option in options) list.Append(RowFor(option));

        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(list);
        scroller.Vexpand = true;
        box.Append(scroller);

        void Accept()
        {
            var index = Math.Clamp(list.GetSelectedRow()?.GetIndex() ?? 0, 0, options.Count - 1);
            dialog.Close();
            chosen(index);
        }

        var button = Gtk_.Button.NewWithLabel("Choose");
        button.OnClicked += (_, _) => Accept();
        list.OnRowActivated += (_, _) => Accept();
        box.Append(button);

        dialog.SetChild(box);
        dialog.Present();

        var first = list.GetRowAtIndex(0);
        if (first is not null)
        {
            list.SelectRow(first);
            first.GrabFocus();
        }
    }
}
