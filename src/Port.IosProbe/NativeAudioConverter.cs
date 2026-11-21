using System.Runtime.InteropServices;
using Port.Content;

namespace Port.IosProbe;

public sealed class NativeAudioConverter(string identity) : IContentAudioConverter
{
    public string ToolSha256 => identity;
    public string OutputExtension => ".qoa";
    private readonly object gate = new();

    public void Convert(string input, string output, Action<long>? onProgress)
    {
        // The decoder buffers a song's PCM. Limit peak memory on the importing device.
        lock (gate)
        {
            if (Transcode(input, output) != 0)
                throw new InvalidDataException("In-process audio conversion failed.");
        }
        // ContentPreparer reports completion and verifies the QOA header.
    }

    [DllImport("__Internal", EntryPoint = "z0_audio_convert", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Transcode(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string input,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string output);
}
