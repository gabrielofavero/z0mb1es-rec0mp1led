using System.Diagnostics;
using Microsoft.Xna.Framework.Media;

namespace Port.Hooks;

public static class DesktopMediaSources
{
    public static IList<MediaSource> GetAvailableMediaSources()
    {
        Trace.TraceInformation("Desktop media library enumeration: no phone media sources are available.");
        return Array.Empty<MediaSource>();
    }
}
