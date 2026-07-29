using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Automation;
using Avalonia.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

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

internal sealed record LivePaneAccessibilityObservation(
    string PaneId,
    string AutomationId,
    string Name,
    string HelpText,
    string Role,
    string State,
    string Value,
    bool IsVisible,
    bool Focusable,
    bool IsTabStop,
    int TabIndex);

internal sealed record LivePaneAccessibilityManifest(
    int SchemaVersion,
    string Suite,
    string Platform,
    string Shell,
    string App,
    string EvidenceLevel,
    IReadOnlyList<LivePaneAccessibilityObservation> Observations,
    string Limitation);

internal static class AccessibilityValidationCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Start(MainWindow window, AccessibilityValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);
        window.Opened += async (_, _) => await RunAsync(window, options);
    }

    private static async Task RunAsync(MainWindow window, AccessibilityValidationOptions options)
    {
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        // Open representative live panes before reading their metadata. This is deliberately
        // a host-level observation of controls already wired by MainWindow, not a planner dump.
        window.ShowReviewCommentsPane();
        window.ShowSelectionPane();
        window.ShowAnimationPane();
        await Task.Delay(250);

        var snapshot = window.PaneAccessibilitySnapshotForTests;
        var observations = new[]
        {
            Observe(window.SlidePaneForAccessibilityTests, PresentationPaneAccessibilityPlanner.SlidePaneId, snapshot,
                textValue: $"Items={window.SlidePaneItemsForAccessibilityTests.Count}"),
            Observe(window.NotesPaneForAccessibilityTests, PresentationPaneAccessibilityPlanner.NotesPaneId, snapshot,
                textValue: $"Text={(string.IsNullOrEmpty(window.NotesPaneForAccessibilityTests.Text) ? "<empty>" : window.NotesPaneForAccessibilityTests.Text)}"),
            Observe(window.CommentsPaneForAccessibilityTests, PresentationPaneAccessibilityPlanner.CommentsPaneId, snapshot,
                textValue: $"Items={window.CommentsPaneItemsForAccessibilityTests.Count}"),
            Observe(window.SelectionPaneForAccessibilityTests, PresentationPaneAccessibilityPlanner.SelectionPaneId, snapshot,
                textValue: $"Items={window.SelectionPaneItemsForAccessibilityTests.Count}"),
            Observe(window.AnimationPaneForAccessibilityTests, PresentationPaneAccessibilityPlanner.AnimationPaneId, snapshot,
                textValue: $"Items={window.AnimationPaneItemsForAccessibilityTests.Count}"),
        };

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
            window.FocusRepresentativePanesForAccessibilityValidation();

        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (!File.Exists(atSpiResultPath) && DateTime.UtcNow < deadline)
            await Task.Delay(100);

        window.AllowCloseWithoutDirtyPromptForPhysicalValidation();
        window.Close();
    }

    private static LivePaneAccessibilityObservation Observe(
        Control control,
        string paneId,
        IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> snapshot,
        string textValue)
    {
        var state = snapshot.Single(entry => entry.PaneId == paneId);
        return new LivePaneAccessibilityObservation(
            paneId,
            AutomationProperties.GetAutomationId(control) ?? string.Empty,
            AutomationProperties.GetName(control) ?? string.Empty,
            AutomationProperties.GetHelpText(control) ?? string.Empty,
            control.GetType().Name,
            AutomationProperties.GetItemStatus(control) ?? state.State,
            textValue,
            control.IsVisible,
            control.Focusable,
            control.IsTabStop,
            control.TabIndex);
    }
}
