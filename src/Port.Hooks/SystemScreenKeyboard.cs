using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Port.Hooks;

// SDL supplies the mobile keyboard. Desktop backends without that integration
// need to ask the system's keyboard service instead.
internal static class SystemScreenKeyboard
{
    internal static Action? Show()
    {
        try
        {
            if (OperatingSystem.IsMacOS()) return ShowMacKeyboard();
            if (OperatingSystem.IsWindows())
            {
                using var process = Process.Start(new ProcessStartInfo(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "osk.exe"))
                    { UseShellExecute = true });
                return null; // The Windows keyboard manages its own lifetime.
            }
            if (OperatingSystem.IsLinux())
            {
                // Use an installed desktop keyboard; never install one implicitly.
                foreach (var name in new[] { "onboard", "wvkbd-mobintl", "matchbox-keyboard" })
                {
                    var executable = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                        .Where(Path.IsPathFullyQualified).Select(directory => Path.Combine(directory, name))
                        .FirstOrDefault(File.Exists);
                    if (executable is null) continue;
                    var process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false });
                    return () =>
                    {
                        if (process is null) return;
                        try { if (!process.HasExited) process.Kill(); }
                        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
                        { Console.Error.WriteLine($"SCREEN_KEYBOARD_CLOSE_FAILED {error.GetType().Name}"); }
                        finally { process.Dispose(); }
                    };
                }
                Console.Error.WriteLine("SCREEN_KEYBOARD_UNAVAILABLE no installed desktop keyboard");
            }
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"SCREEN_KEYBOARD_FAILED {error.GetType().Name}: {error.Message}");
        }
        return null;
    }

    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private static Action? ShowMacKeyboard()
    {
        var library = NativeLibrary.Load(Carbon);
        var sources = TISCreateInputSourceList(IntPtr.Zero, true);
        try
        {
            if (sources == IntPtr.Zero) return null;
            IntPtr Key(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));
            var idKey = Key("kTISPropertyInputSourceID");
            var enabledKey = Key("kTISPropertyInputSourceIsEnabled");
            var selectedKey = Key("kTISPropertyInputSourceIsSelected");
            for (nint index = 0; index < CFArrayGetCount(sources); index++)
            {
                var source = CFArrayGetValueAtIndex(sources, index);
                var identifier = new StringBuilder(256);
                if (!CFStringGetCString(TISGetInputSourceProperty(source, idKey), identifier,
                        256, 0x08000100) || identifier.ToString() is not
                        ("com.apple.inputmethod.AssistiveControl" or "com.apple.KeyboardViewer")) continue;
                var enabled = CFBooleanGetValue(TISGetInputSourceProperty(source, enabledKey));
                var selected = CFBooleanGetValue(TISGetInputSourceProperty(source, selectedKey));
                if (!enabled && TISEnableInputSource(source) != 0) continue;
                if (TISSelectInputSource(source) != 0)
                {
                    if (!enabled) TISDisableInputSource(source);
                    continue;
                }
                CFRetain(source);
                return () =>
                {
                    // Restore the user's palette state when editing finishes.
                    if (!selected) TISDeselectInputSource(source);
                    if (!enabled) TISDisableInputSource(source);
                    CFRelease(source);
                };
            }
            Console.Error.WriteLine("SCREEN_KEYBOARD_UNAVAILABLE no system keyboard palette");
            return null;
        }
        finally
        {
            if (sources != IntPtr.Zero) CFRelease(sources);
            NativeLibrary.Free(library);
        }
    }

    [DllImport(Carbon)] private static extern IntPtr TISCreateInputSourceList(IntPtr properties,
        [MarshalAs(UnmanagedType.I1)] bool includeAllInstalled);
    [DllImport(Carbon)] private static extern IntPtr TISGetInputSourceProperty(IntPtr source, IntPtr key);
    [DllImport(Carbon)] private static extern int TISEnableInputSource(IntPtr source);
    [DllImport(Carbon)] private static extern int TISDisableInputSource(IntPtr source);
    [DllImport(Carbon)] private static extern int TISSelectInputSource(IntPtr source);
    [DllImport(Carbon)] private static extern int TISDeselectInputSource(IntPtr source);
    [DllImport(CoreFoundation)] private static extern nint CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr value, [Out] StringBuilder buffer, nint length, uint encoding);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFBooleanGetValue(IntPtr value);
    [DllImport(CoreFoundation)] private static extern IntPtr CFRetain(IntPtr value);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
}
