using System.Runtime.InteropServices;
using System.Text;
using Port.Content;

namespace Port.IosProbe;

public sealed class NativeAudioConverter(string identity) : IContentAudioConverter
{
    public string ToolSha256 => identity;

    public void Convert(string input, string output, Action<long>? onProgress)
    {
        var error = new StringBuilder(512);
        var result = Transcode(input, output, error, error.Capacity);
        if (result != 0) throw new InvalidDataException($"In-process audio conversion failed: {error}");
        // ContentPreparer reports completion and verifies the Ogg identification header.
    }

    [DllImport("__Internal", EntryPoint = "z0_audio_convert", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Transcode(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string input,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string output,
        [Out] StringBuilder error, int errorCapacity);
}
