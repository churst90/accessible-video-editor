using System.Runtime.InteropServices;
using System.Text;

namespace AccessibleVideoEditor.Playback;

/// <summary>
/// A thin binding to libmpv.
///
/// mpv is used rather than a decoder of our own because it already understands
/// the <c>edl://</c> protocol - a list of files with in and out points, played
/// as one continuous stream. That is exactly what a cut is, so playback needs
/// no encoding at all and an edit is audible the moment it is made.
/// </summary>
public sealed class MpvClient : IDisposable
{
    private const string Library = "libmpv.so.2";

    private IntPtr _handle;

    public bool IsOpen => _handle != IntPtr.Zero;

    static MpvClient()
    {
        // mpv refuses to work correctly under a non-C numeric locale: it parses
        // and formats numbers with the C library, so a locale using a comma as
        // the decimal separator would turn a seek to 12.5 into a seek to 12.
        // Only LC_NUMERIC is forced, so dates and text stay localised.
        try
        {
            setlocale(LcNumeric, "C");
        }
        catch (DllNotFoundException)
        {
            // Not glibc. mpv will warn; nothing else to do.
        }
    }

    private const int LcNumeric = 1;

    [DllImport("libc", CharSet = CharSet.Ansi)]
    private static extern IntPtr setlocale(int category, string locale);

    /// <summary>
    /// Creates a player. <paramref name="audioOnly"/> is used for the scrub
    /// instance, which must never open a window.
    /// </summary>
    public static MpvClient? TryCreate(bool audioOnly)
    {
        try
        {
            var handle = mpv_create();
            if (handle == IntPtr.Zero) return null;

            var client = new MpvClient { _handle = handle };

            client.SetOption("terminal", "no");
            client.SetOption("idle", "yes");
            client.SetOption("audio-display", "no");
            client.SetOption("keep-open", "yes");

            if (audioOnly)
            {
                // Without this mpv opens a window of its own the moment a file
                // with video is loaded, which steals keyboard focus mid-edit.
                client.SetOption("vid", "no");
                client.SetOption("force-window", "no");
                client.SetOption("video", "no");
                client.SetOption("cache", "no");
            }

            if (mpv_initialize(handle) < 0)
            {
                client.Dispose();
                return null;
            }

            return client;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    public void SetOption(string name, string value)
    {
        if (_handle != IntPtr.Zero) mpv_set_option_string(_handle, name, value);
    }

    public void SetProperty(string name, string value)
    {
        if (_handle != IntPtr.Zero) mpv_set_property_string(_handle, name, value);
    }

    public double GetDouble(string name)
    {
        if (_handle == IntPtr.Zero) return 0;

        return mpv_get_property(_handle, name, MpvFormat.Double, out double value) < 0 ? 0 : value;
    }

    public bool GetFlag(string name)
    {
        if (_handle == IntPtr.Zero) return false;

        return mpv_get_property(_handle, name, MpvFormat.Flag, out long value) >= 0 && value != 0;
    }

    /// <summary>mpv takes commands as a null-terminated array of UTF-8 strings.</summary>
    public void Command(params string[] arguments)
    {
        if (_handle == IntPtr.Zero) return;

        var pointers = new IntPtr[arguments.Length + 1];

        try
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(arguments[i] + '\0');
                pointers[i] = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, pointers[i], bytes.Length);
            }

            pointers[^1] = IntPtr.Zero;
            mpv_command(_handle, pointers);
        }
        finally
        {
            foreach (var pointer in pointers)
            {
                if (pointer != IntPtr.Zero) Marshal.FreeHGlobal(pointer);
            }
        }
    }

    /// <summary>
    /// Blocks until the file mpv was told to load is actually open.
    ///
    /// <c>loadfile</c> is asynchronous: a seek issued straight after it does
    /// nothing at all, silently. That was the cause of Home followed by Space
    /// playing from the wrong place, or not starting.
    /// </summary>
    public bool WaitUntilLoaded(TimeSpan timeout)
    {
        if (_handle == IntPtr.Zero) return false;

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (GetDouble("duration") > 0) return true;
            Thread.Sleep(15);
        }

        return false;
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;

        mpv_terminate_destroy(_handle);
        _handle = IntPtr.Zero;
    }

    private enum MpvFormat
    {
        Flag = 3,
        Double = 5,
    }

    [DllImport(Library)] private static extern IntPtr mpv_create();
    [DllImport(Library)] private static extern int mpv_initialize(IntPtr ctx);
    [DllImport(Library)] private static extern void mpv_terminate_destroy(IntPtr ctx);

    [DllImport(Library, CharSet = CharSet.Ansi)]
    private static extern int mpv_set_option_string(IntPtr ctx, string name, string data);

    [DllImport(Library, CharSet = CharSet.Ansi)]
    private static extern int mpv_set_property_string(IntPtr ctx, string name, string data);

    [DllImport(Library, CharSet = CharSet.Ansi)]
    private static extern int mpv_get_property(IntPtr ctx, string name, MpvFormat format, out double data);

    [DllImport(Library, CharSet = CharSet.Ansi)]
    private static extern int mpv_get_property(IntPtr ctx, string name, MpvFormat format, out long data);

    [DllImport(Library)]
    private static extern int mpv_command(IntPtr ctx, IntPtr[] args);
}
