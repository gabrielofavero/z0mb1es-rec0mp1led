using Mono.Cecil;

internal static class VibrationPackageChecks
{
    internal static void Run(AssemblyDefinition assembly, string createGame)
    {
        var module = assembly.MainModule;
        var output = module.GetType("Port.IosGame.DeviceVibration")
            ?? throw new Exception("iOS package is missing device vibration.");
        if (!output.Interfaces.Any(i => i.InterfaceType.FullName == "Port.Bridge.IVibrationOutput"))
            throw new Exception("Device vibration is not connected to the shared feedback API.");
        bool Calls(MethodDefinition method, string type, string name) => method.Body.Instructions
            .Any(i => i.Operand is MethodReference call && call.DeclaringType.FullName == type && call.Name == name);
        var start = output.Methods.Single(m => m.Name == "TryStart");
        if (!Calls(start, "CoreHaptics.CHHapticEngine", "GetHardwareCapabilities") ||
            !Calls(start, "CoreHaptics.CHHapticEngine", "CreatePlayer") ||
            !Calls(start, "System.TimeSpan", "get_TotalSeconds") ||
            !Calls(start, "AudioToolbox.SystemSound", "PlaySystemSound"))
            throw new Exception("Device vibration lost capability detection, timed haptics, or legacy vibration.");
        var configure = output.Methods.Single(m => m.Name == "Configure");
        if (!Calls(configure, "Port.Bridge.GameVibration", "set_DeviceOutput") ||
            !Calls(configure, "UIKit.UIApplication/Notifications", "ObserveWillResignActive"))
            throw new Exception("Device vibration lost routing or UIKit background cancellation.");
        var create = module.GetType("Port.IosGame.Program").Methods.Single(m => m.Name == createGame);
        if (!Calls(create, output.FullName, "Configure"))
            throw new Exception("The iOS host does not install device vibration.");
    }
}
