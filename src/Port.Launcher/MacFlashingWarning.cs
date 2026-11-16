using System.Diagnostics;
using Port.Prepare;

internal static class MacFlashingWarning
{
    // NSAlert's optional checkbox supplies the initial graphics choice. Its visibility
    // on future launches is controlled by our persisted acknowledgment, not AppKit.
    internal const string Script = """
        ObjC.import('Cocoa');
        function run(argv) {
            const app = $.NSApplication.sharedApplication;
            app.setActivationPolicy($.NSApplicationActivationPolicyAccessory);
            app.activateIgnoringOtherApps(true);
            const alert = $.NSAlert.alloc.init;
            alert.messageText = argv[0];
            alert.informativeText = argv[1];
            alert.addButtonWithTitle('Continue');
            alert.addButtonWithTitle('Quit');
            alert.showsSuppressionButton = true;
            alert.suppressionButton.title = argv[2];
            alert.suppressionButton.state = argv[3] === 'true' ? 1 : 0;
            if (alert.runModal !== $.NSAlertFirstButtonReturn) return 'cancel';
            return alert.suppressionButton.state === 1 ? 'true' : 'false';
        }
        """;

    public static Task<bool> ShowAsync(string saveDirectory) =>
        FirstLaunchWarning.EnsureAsync(saveDirectory, async initial =>
        {
            var start = new ProcessStartInfo("/usr/bin/osascript")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "-l", "JavaScript", "-e", Script,
                         FirstLaunchWarning.Title, FirstLaunchWarning.Text,
                         FirstLaunchWarning.CheckboxLabel, initial ? "true" : "false" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Could not show the flashing lights warning.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new IOException("Could not show the flashing lights warning: " + (await error).Trim());
            return (await output).Trim() switch
            {
                "true" => true,
                "false" => false,
                "cancel" => (bool?)null,
                _ => throw new IOException("Unexpected flashing lights warning response.")
            };
        });
}
