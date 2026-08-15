namespace AccessibleVideoEditor.Gtk;

public static class ProbeApplication
{
    public static int Run(string[] args)
    {
        var application = global::Gtk.Application.New("dev.videoedit.app", Gio.ApplicationFlags.DefaultFlags);

        application.OnActivate += (sender, _) =>
        {
            var window = new MainWindow((global::Gtk.Application)sender);
            window.Present();
        };

        return application.RunWithSynchronizationContext(null);
    }
}
