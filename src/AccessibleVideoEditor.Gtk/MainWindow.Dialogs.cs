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
/// The three ways this application asks a question: for a word, for a
/// choice from a list, and for a yes or no.
///
/// All three close on Escape and say that they did. A dialog that vanishes
/// silently leaves you unsure whether it took the answer with it.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// A one-line text prompt. Shared by every command that needs a name typed,
    /// so they all behave the same: the entry has focus when it opens, Enter
    /// accepts, Escape leaves everything alone.
    /// </summary>
    private void Prompt(string title, string initial, string verb, System.Action<string> commit)
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = title;
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(400, 130);

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 12; box.MarginBottom = 12; box.MarginStart = 12; box.MarginEnd = 12;

        var entry = Gtk_.Entry.New();
        entry.SetText(initial);
        box.Append(entry);

        var accept = Gtk_.Button.NewWithLabel(verb);
        accept.AddCssClass("suggested-action");
        box.Append(accept);

        void Commit()
        {
            var text = entry.GetText().Trim();
            dialog.Close();

            if (text.Length > 0) commit(text);
        }

        accept.OnClicked += (_, _) => Commit();
        entry.OnActivate += (_, _) => Commit();

        var keys = Gtk_.EventControllerKey.New();
        keys.OnKeyPressed += (_, args) =>
        {
            if (args.Keyval != Gdk.Constants.KEY_Escape) return false;

            dialog.Close();
            Announce($"{title} cancelled", urgent: true);

            return true;
        };

        dialog.AddController(keys);
        dialog.SetChild(box);
        dialog.Present();
        entry.GrabFocus();
    }
    /// <summary>
    /// A yes-or-no dialog for the few things that cannot be taken back. Banning
    /// somebody and stopping a broadcast are both in that category; nothing
    /// else in this application is.
    ///
    /// <paramref name="otherwise"/> is for the one question here where No is a
    /// real answer rather than a cancellation - recovery, where declining the
    /// recovered work still means opening the project.
    /// </summary>
    private void ConfirmThen(string question, System.Action confirmed, System.Action? otherwise = null)
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = question;
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(420, 130);

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 12; box.MarginBottom = 12; box.MarginStart = 12; box.MarginEnd = 12;

        var label = Gtk_.Label.New(question);
        label.Wrap = true;
        label.Xalign = 0;
        box.Append(label);

        var buttons = Gtk_.Box.New(Gtk_.Orientation.Horizontal, 8);

        var yes = Gtk_.Button.NewWithLabel("Yes");
        yes.AddCssClass("suggested-action");
        var no = Gtk_.Button.NewWithLabel("No");

        yes.OnClicked += (_, _) =>
        {
            dialog.Close();
            confirmed();
        };

        no.OnClicked += (_, _) =>
        {
            dialog.Close();

            if (otherwise is null)
            {
                Announce("cancelled", urgent: true);
                return;
            }

            otherwise();
        };

        buttons.Append(yes);
        buttons.Append(no);
        box.Append(buttons);

        dialog.SetChild(box);
        dialog.Present();

        // Focus lands on No: the safe answer is the one you get by pressing
        // Enter without having listened.
        no.GrabFocus();
    }
    /// <summary>A modal list. One shape for every "which one?" question.</summary>
    private void ChooseFromList(string title, IReadOnlyList<string> options, System.Action<int> chosen)
    {
        var dialog = Gtk_.Window.New();
        dialog.Title = title;
        dialog.Modal = true;
        dialog.TransientFor = _window;
        dialog.SetDefaultSize(460, 300);

        var box = Gtk_.Box.New(Gtk_.Orientation.Vertical, 8);
        box.MarginTop = 12; box.MarginBottom = 12; box.MarginStart = 12; box.MarginEnd = 12;

        var list = Gtk_.ListBox.New();
        list.SelectionMode = Gtk_.SelectionMode.Single;

        foreach (var option in options) list.Append(Row(option));

        var scroller = Gtk_.ScrolledWindow.New();
        scroller.SetChild(list);
        scroller.Vexpand = true;
        box.Append(scroller);

        void Accept()
        {
            var index = list.GetSelectedRow()?.GetIndex() ?? 0;
            dialog.Close();
            chosen(Math.Clamp(index, 0, options.Count - 1));
        }

        var button = Gtk_.Button.NewWithLabel("Choose");
        button.OnClicked += (_, _) => Accept();
        box.Append(button);

        list.OnRowActivated += (_, _) => Accept();

        dialog.SetChild(box);
        dialog.Present();

        var first = list.GetRowAtIndex(0);
        if (first is not null)
        {
            list.SelectRow(first);
            first.GrabFocus();
        }
    }

    /// <summary>
    /// The fourth way of asking: a popover menu, for a choice that is short
    /// enough to be a list of verbs rather than a window.
    ///
    /// The popover is parented to the window and given focus explicitly. A
    /// <c>PopoverMenu</c> that is created and re-parented on demand silently
    /// does nothing at all, which is a GTK lesson this codebase has already
    /// paid for once.
    /// </summary>
    private void PopUp(Gio.Menu menu, string announce)
    {
        var popover = Gtk_.PopoverMenu.NewFromModel(menu);
        popover.SetParent(_window);
        popover.HasArrow = false;
        popover.Popup();
        popover.GrabFocus();

        Announce(announce, urgent: true);
    }
}
