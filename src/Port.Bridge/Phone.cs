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
