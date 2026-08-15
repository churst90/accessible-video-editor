using System.Runtime.InteropServices;

namespace AccessibleVideoEditor.Audio;

/// <summary>
/// The audio device, over SDL2.
///
/// SDL rather than a bundled library because it is already installed, and
/// because its callback model is the only one that gives the latency this needs:
/// the viewfinder and the level meter are closed loops with your own body and
/// voice, and feedback that arrives late is worse than none - you correct for a
/// state you are no longer in.
///
/// Falls back to silence rather than failing if SDL is missing, so the rest of
/// the application still runs.
/// </summary>
public sealed class SdlAudioOutput : IDisposable
{
    private const string Library = "libSDL2-2.0.so.0";
    private const uint InitAudio = 0x00000010;
    private const ushort FormatFloat32 = 0x8120;

    private readonly ToneBank _bank;
    private readonly AudioCallback _callback;
    private uint _device;
    private float[] _scratch = [];

    private SdlAudioOutput(ToneBank bank)
    {
        _bank = bank;
        _callback = OnAudio;
    }

    public ToneBank Bank => _bank;

    public bool IsOpen => _device != 0;

    /// <summary>Opens the device, or returns null when SDL is unavailable.</summary>
    public static SdlAudioOutput? TryOpen(int sampleRate = 48000)
    {
        try
        {
            if (SDL_Init(InitAudio) != 0) return null;

            var bank = new ToneBank { SampleRate = sampleRate };
            var output = new SdlAudioOutput(bank);

            var desired = new SdlAudioSpec
            {
                freq = sampleRate,
                format = FormatFloat32,
                channels = 2,

                // 512 frames is about eleven milliseconds - short enough that a
                // tick lands with the movement that caused it.
                samples = 512,
                callback = Marshal.GetFunctionPointerForDelegate(output._callback),
            };

            output._device = SDL_OpenAudioDevice(IntPtr.Zero, 0, ref desired, out _, 0);

            if (output._device == 0) return null;

            SDL_PauseAudioDevice(output._device, 0);
            return output;
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

    public void Play(double frequency, double seconds, double amplitude = 1.0, double pan = 0) =>
        _bank.Play(frequency, seconds, amplitude, pan);

    public void Earcon(AccessibleVideoEditor.Speech.Earcon earcon)
    {
        var (frequency, seconds, amplitude) = Earcons.Voice(earcon);
        _bank.Play(frequency, seconds, amplitude);
    }

    private void OnAudio(IntPtr userdata, IntPtr stream, int lengthBytes)
    {
        var floats = lengthBytes / sizeof(float);

        if (_scratch.Length < floats) _scratch = new float[floats];

        var span = _scratch.AsSpan(0, floats);
        _bank.Fill(span);

        Marshal.Copy(_scratch, 0, stream, floats);
    }

    public void Dispose()
    {
        if (_device == 0) return;

        SDL_CloseAudioDevice(_device);
        _device = 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AudioCallback(IntPtr userdata, IntPtr stream, int length);

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlAudioSpec
    {
        public int freq;
        public ushort format;
        public byte channels;
        public byte silence;
        public ushort samples;
        public ushort padding;
        public uint size;
        public IntPtr callback;
        public IntPtr userdata;
    }

    [DllImport(Library, EntryPoint = "SDL_Init")]
    private static extern int SDL_Init(uint flags);

    [DllImport(Library, EntryPoint = "SDL_OpenAudioDevice")]
    private static extern uint SDL_OpenAudioDevice(
        IntPtr device, int isCapture, ref SdlAudioSpec desired, out SdlAudioSpec obtained, int allowedChanges);

    [DllImport(Library, EntryPoint = "SDL_PauseAudioDevice")]
    private static extern void SDL_PauseAudioDevice(uint device, int pauseOn);

    [DllImport(Library, EntryPoint = "SDL_CloseAudioDevice")]
    private static extern void SDL_CloseAudioDevice(uint device);
}
