using System.Reflection;
using Port.Host;

internal static class MacOnboardingChecks
{
    public static async Task Run(string program, string output)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        var launcher = Assembly.LoadFrom(Path.Combine(Path.GetFullPath(program), "Z0MB1ES.dll"));
        var type = launcher.GetType("MacImportView", throwOnError: true)!;
        foreach (var (name, installed, normal, kind, viking, expected) in new[]
        {
            ("skip-both", false, "__CANCELLED__", "__CANCELLED__", "__CANCELLED__",
                new ImportChoices(true, false, null, false)),
            ("cancel-source-kind", false, "yes", "__CANCELLED__", "__CANCELLED__",
                new ImportChoices(true, false, null, false)),
            ("cancel-kind-keep-viking", false, "yes", "__CANCELLED__", "yes",
                new ImportChoices(true, false, null, true)),
            ("reimport-cancel-kind", true, "yes", "__CANCELLED__", "yes",
                new ImportChoices(true, false, null, true))
        })
        {
            var fixture = Path.GetFullPath(Path.Combine(output, name));
            Directory.CreateDirectory(fixture);
            // Script the native dialog responses without showing UI or changing user data.
            File.WriteAllText(Path.Combine(fixture, "dialogs.applescript"), $$"""
                on run argv
                    set action to item 1 of argv
                    if action is "question" then
                        return "yes"
                    else if action is "optional-art" then
                        if (item 2 of argv) contains "I MAED" then return "{{normal}}"
                        return "{{viking}}"
                    else if action is "normal-kind" then
                        return "{{kind}}"
                    else if action is "sources" then
                        return "Z0MB1ES phone XAP" & linefeed & "I MAED artwork" & linefeed & "Time Viking artwork"
                    end if
                    error "Unexpected dialog"
                end run
                """);
            var view = (IAssetImportView)Activator.CreateInstance(type, [fixture, true])!;
            var choices = await view.ChooseAsync(installed, CancellationToken.None);
            if (choices != expected)
                throw new Exception($"macOS optional cancellation changed onboarding: {name}: {choices}");
        }
        Console.WriteLine("MAC_ONBOARDING_OK skip-both/cancel-source-kind/keep-viking/reimport");
    }
}
