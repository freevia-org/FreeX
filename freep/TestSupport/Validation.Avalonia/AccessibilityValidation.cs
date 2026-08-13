using System.Text.Json;
using Free.Shared.AppServices;
using FreeP.App.Avalonia;

namespace FreeP.Validation.Avalonia;

internal sealed record AccessibilityValidationOptions(string OutputDirectory)
{
    private const string OutputDirectoryKey = "outputDirectory";
    public const string Argument = "--accessibility-validation";

    private static readonly CommandLineValueOptionSpec OutputDirectoryOption = new(
        OutputDirectoryKey,
        Argument,
        $"{Argument} requires one non-empty output directory and may appear once.",
        $"{Argument} requires one non-empty output directory and may appear once.",
        $"{Argument} requires one non-empty output directory and may appear once.",
        AllowEqualsSyntax: true);

    public static bool TryParse(
        IReadOnlyList<string> args,
        out AccessibilityValidationOptions? options,
        out string[] startupArguments,
        out string? error)
    {
        var parsed = CommandLineValueOptionParser.Parse(args, [OutputDirectoryOption]);
        options = parsed.Error is null && parsed.IsPresent(OutputDirectoryKey)
            ? new AccessibilityValidationOptions(parsed.Value(OutputDirectoryKey)!)
            : null;
        startupArguments = parsed.RemainingArguments;
        error = parsed.Error;
        return parsed.Error is null;
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
    private static readonly JsonSerializerOptions JsonOptions =
        JsonArtifactIO.CreateSerializerOptions(ignoreNullValues: true);

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
        await JsonArtifactIO.WriteAtomicAsync(manifestPath, manifest, JsonOptions);

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
