using System.Text.Json;
using System.Text.Json.Serialization;
using FreeP.App.Avalonia;

namespace FreeP.Validation.Avalonia;

internal sealed record AccessibilityValidationOptions(string OutputDirectory)
{
    public const string Argument = "--accessibility-validation";

    public static bool TryParse(
        IReadOnlyList<string> args,
        out AccessibilityValidationOptions? options,
        out string[] startupArguments,
        out string? error)
    {
        var filtered = new List<string>(args.Count);
        options = null;
        error = null;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument.StartsWith(Argument + "=", StringComparison.Ordinal))
            {
                if (options is not null || argument.Length == Argument.Length + 1)
                {
                    error = $"{Argument} requires one non-empty output directory and may appear once.";
                    startupArguments = filtered.ToArray();
                    return false;
                }

                options = new AccessibilityValidationOptions(argument[(Argument.Length + 1)..]);
                continue;
            }

            if (!string.Equals(argument, Argument, StringComparison.Ordinal))
            {
                filtered.Add(argument);
                continue;
            }

            if (options is not null || index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                error = $"{Argument} requires one non-empty output directory and may appear once.";
                startupArguments = filtered.ToArray();
                return false;
            }

            options = new AccessibilityValidationOptions(args[++index]);
        }

        startupArguments = filtered.ToArray();
        return true;
    }
}

internal sealed record LivePaneAccessibilityManifest(
    int SchemaVersion,
    string Suite,
    string Platform,
    string Shell,
    string App,
    string EvidenceLevel,
    IReadOnlyList<MainWindow.ValidationAccessibilityPaneObservation> Observations,
    string Limitation);

internal static class AccessibilityValidationCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Start(MainWindow.ValidationAccessAdapter access, AccessibilityValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(options);
        access.StartWhenOpened(() => RunAsync(access, options));
    }

    private static async Task RunAsync(
        MainWindow.ValidationAccessAdapter access,
        AccessibilityValidationOptions options)
    {
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        // Open representative live panes before reading their metadata. This is deliberately
        // a host-level observation of controls already wired by MainWindow, not a planner dump.
        access.ShowRepresentativeAccessibilityPanes();
        await Task.Delay(250);

        var observations = access.CaptureAccessibilityPanes();

        var manifest = new LivePaneAccessibilityManifest(
            1,
            "freep-live-pane-accessibility",
            "linux",
            "avalonia",
            "FreeP",
            "physical-live-control",
            observations,
            "Avalonia AutomationProperties are observed from live controls. The companion AT-SPI probe is the OS-level check; current Avalonia/X11 builds may not publish these controls to the AT-SPI desktop.");
        var manifestPath = Path.Combine(outputDirectory, "live-pane-accessibility.json");
        var temporaryManifestPath = manifestPath + ".tmp";
        await File.WriteAllTextAsync(temporaryManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temporaryManifestPath, manifestPath, overwrite: true);

        var atSpiResultPath = Path.Combine(outputDirectory, "atspi-result.json");
        var atSpiReadyPath = Path.Combine(outputDirectory, "atspi-ready.json");
        var readyDeadline = DateTime.UtcNow.AddSeconds(60);
        while (!File.Exists(atSpiReadyPath) && DateTime.UtcNow < readyDeadline)
            await Task.Delay(100);

        if (File.Exists(atSpiReadyPath))
            access.FocusRepresentativeAccessibilityPanes();

        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (!File.Exists(atSpiResultPath) && DateTime.UtcNow < deadline)
            await Task.Delay(100);

        access.CloseWithoutDirtyPrompt();
    }
}
