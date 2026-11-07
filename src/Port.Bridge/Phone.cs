using Port.Bridge;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.Phone.Tasks
{
    public enum MarketplaceContentType { }

    public sealed class MarketplaceDetailTask
    {
        public MarketplaceContentType ContentType { get; set; }
        public void Show() => DesktopPhoneFallback.MarketplaceUnavailable("MarketplaceDetailTask.Show()");
    }
}

namespace Microsoft.Devices
{
    public sealed class VibrateController
    {
        private static readonly VibrateController Instance = new();
        private static bool absenceReported;
        private VibrateController() { }
        public static VibrateController Default => Instance;
        public void Start(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
            if (absenceReported) return;
            absenceReported = true;
            Trace.TraceWarning("VibrateController.Start(TimeSpan): no phone vibration motor is available; play continues without vibration.");
        }
    }
}

namespace Port.Bridge
{
    public static class DesktopPhoneFallback
    {
        public static int? ShowChoice(string title, string text, IReadOnlyList<string> buttons,
            int focusButton)
        {
            if (buttons.Count is < 1 or > 3)
            {
                Trace.TraceWarning($"Guide message box has {buttons.Count} choices; dismissing it.");
                return null;
            }
            if (!OperatingSystem.IsWindows() || !Environment.UserInteractive ||
                AppContext.TryGetSwitch("Port.Bridge.SuppressPhoneDialogs", out var suppress) && suppress)
            {
                Trace.TraceInformation("Guide message box dismissed without desktop dialog.");
                return null;
            }
            const uint topmost = 0x00040000 | 0x00010000;
            var style = buttons.Count switch { 1 => 0u, 2 => 4u, _ => 3u };
            if (focusButton > 0 && focusButton < buttons.Count)
                style |= (uint)focusButton << 8;
            var displayedText = buttons.Count switch
            {
                2 => $"{text}\n\nYes: {buttons[0]}    No: {buttons[1]}",
                3 => $"{text}\n\nYes: {buttons[0]}    No: {buttons[1]}    Cancel: {buttons[2]}",
                _ => text
            };
            var choice = MessageBoxW(IntPtr.Zero, displayedText, title, style | topmost);
            return choice switch
            {
                1 when buttons.Count == 1 => 0,
                6 when buttons.Count >= 2 => 0,
                7 when buttons.Count >= 2 => 1,
                2 when buttons.Count == 3 => 2,
                _ => null
            };
        }

        public static void MarketplaceUnavailable(string caller)
        {
            Trace.TraceWarning($"{caller}: the phone marketplace is unavailable in this offline desktop build.");
            if (!OperatingSystem.IsWindows() || !Environment.UserInteractive ||
                AppContext.TryGetSwitch("Port.Bridge.SuppressPhoneDialogs", out var suppress) && suppress)
                return;
            if (MessageBoxW(IntPtr.Zero,
                "The phone marketplace is unavailable in this offline desktop build.",
                "Marketplace unavailable", 0x00000040) == 0)
                Trace.TraceError("Marketplace notice could not be displayed.");
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
    }
}

namespace Microsoft.Devices.Sensors
{
    public sealed class Accelerometer
    {
        private EventHandler<AccelerometerReadingEventArgs>? readingChanged;
        private bool started;
        public event EventHandler<AccelerometerReadingEventArgs> ReadingChanged
        {
            add => readingChanged += value;
            remove => readingChanged -= value;
        }
        public void Start()
        {
            if (started) return;
            started = true;
            System.Diagnostics.Trace.TraceWarning(
                "Accelerometer.Start(): no desktop sensor is connected; no readings will be emitted.");
        }
    }

    public sealed class AccelerometerReadingEventArgs : EventArgs
    {
        public double X => Unavailable.Api<double>("AccelerometerReadingEventArgs.X");
        public double Y => Unavailable.Api<double>("AccelerometerReadingEventArgs.Y");
        public double Z => Unavailable.Api<double>("AccelerometerReadingEventArgs.Z");
    }
}
