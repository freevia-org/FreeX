using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Free.Shared.AppServices;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.Interactions;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal sealed record InteractionValidationOptions(
    string OutputDirectory,
    int DialogStart = 0,
    int DialogCount = int.MaxValue,
    bool IncludeCoreResults = true,
    int RibbonCommandStart = 0,
    int RibbonCommandCount = int.MaxValue,
    bool RibbonOnly = false,
    string? CoreSection = null,
    int ContextMenuDispatchStart = 0,
    int ContextMenuDispatchCount = int.MaxValue)
{
    public const string Argument = "--interaction-validation";
    public const string DialogStartArgument = "--interaction-validation-dialog-start";
    public const string DialogCountArgument = "--interaction-validation-dialog-count";
    public const string DialogOnlyArgument = "--interaction-validation-dialog-only";
    public const string RibbonStartArgument = "--interaction-validation-ribbon-start";
    public const string RibbonCountArgument = "--interaction-validation-ribbon-count";
    public const string RibbonOnlyArgument = "--interaction-validation-ribbon-only";
    public const string CoreSectionArgument = "--interaction-validation-core-section";
    public const string ContextStartArgument = "--interaction-validation-context-start";
    public const string ContextCountArgument = "--interaction-validation-context-count";
    public const string NameBoxDropdownPhysicalFixtureArgument = "--freex-name-box-dropdown-physical";
    public const string NameBoxDropdownPhysicalEvidenceArgument = "--freex-name-box-dropdown-physical-evidence";
    public const string NameBoxDropdownParityPhysicalFixtureArgument = "--freex-name-box-dropdown-parity-physical";

    public static bool TryParse(
        IReadOnlyList<string> args,
        out InteractionValidationOptions? options,
        out string[] startupArguments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        options = null;
        error = "";
        var filtered = new List<string>();
        string? outputDirectory = null;
        var dialogStart = 0;
        var dialogCount = int.MaxValue;
        var includeCoreResults = true;
        var ribbonCommandStart = 0;
        var ribbonCommandCount = int.MaxValue;
        var ribbonOnly = false;
        string? coreSection = null;
        var contextMenuDispatchStart = 0;
        var contextMenuDispatchCount = int.MaxValue;
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], DialogOnlyArgument, StringComparison.OrdinalIgnoreCase))
            {
                includeCoreResults = false;
                continue;
            }

            if (string.Equals(args[index], RibbonOnlyArgument, StringComparison.OrdinalIgnoreCase))
            {
                ribbonOnly = true;
                continue;
            }

            if (string.Equals(args[index], CoreSectionArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[++index]))
                {
                    startupArguments = [];
                    error = $"{CoreSectionArgument} requires a section name.";
                    return false;
                }
                coreSection = args[index];
                continue;
            }

            if (string.Equals(args[index], DialogStartArgument, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[index], DialogCountArgument, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[index], RibbonStartArgument, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[index], RibbonCountArgument, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[index], ContextStartArgument, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[index], ContextCountArgument, StringComparison.OrdinalIgnoreCase))
            {
                var optionName = args[index];
                if (index + 1 >= args.Count || !int.TryParse(args[++index], out var value) || value < 0)
                {
                    startupArguments = [];
                    error = $"{optionName} requires a non-negative integer.";
                    return false;
                }

                if (string.Equals(optionName, DialogStartArgument, StringComparison.OrdinalIgnoreCase))
                    dialogStart = value;
                else if (string.Equals(optionName, DialogCountArgument, StringComparison.OrdinalIgnoreCase))
                    dialogCount = value;
                else if (string.Equals(optionName, RibbonStartArgument, StringComparison.OrdinalIgnoreCase))
                    ribbonCommandStart = value;
                else if (string.Equals(optionName, RibbonCountArgument, StringComparison.OrdinalIgnoreCase))
                    ribbonCommandCount = value;
                else if (string.Equals(optionName, ContextStartArgument, StringComparison.OrdinalIgnoreCase))
                    contextMenuDispatchStart = value;
                else
                    contextMenuDispatchCount = value;
                continue;
            }

            if (!string.Equals(args[index], Argument, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(args[index]);
                continue;
            }

            if (outputDirectory is not null)
            {
                startupArguments = [];
                error = $"{Argument} was specified more than once.";
                return false;
            }

            if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                startupArguments = [];
                error = $"{Argument} requires an output directory path.";
                return false;
            }

            outputDirectory = args[++index];
        }

        if (outputDirectory is not null)
            options = new InteractionValidationOptions(
                outputDirectory,
                dialogStart,
                dialogCount,
                includeCoreResults,
                ribbonCommandStart,
                ribbonCommandCount,
                ribbonOnly,
                coreSection,
                contextMenuDispatchStart,
                contextMenuDispatchCount);
        startupArguments = filtered.ToArray();
        return true;
    }
}

internal sealed record InteractionValidationResult(
    string Id,
    string Category,
    string Status,
    string EvidenceLevel,
    string Evidence,
    string Note = "");

internal sealed record InteractionValidationManifest(
    int SchemaVersion,
    string Platform,
    string Shell,
    DateTimeOffset GeneratedUtc,
    int DialogCatalogCount,
    int RibbonCommandCatalogCount,
    int ContextMenuDispatchCatalogCount,
    string ValidationSection,
    bool IncludeCoreResults,
    bool RibbonOnly,
    int DialogStart,
    int DialogCount,
    int RibbonCommandStart,
    int RibbonCommandCount,
    int ContextMenuDispatchStart,
    int ContextMenuDispatchCount,
    IReadOnlyList<string> DialogCatalogIds,
    IReadOnlyList<string> RibbonCommandCatalogIds,
    IReadOnlyList<string> ContextMenuDispatchCatalogIds,
    IReadOnlyList<string> ContextMenuFamilyCatalogIds,
    IReadOnlyList<string> ContextMenuVariantCatalogIds,
    IReadOnlyList<string> ValidationSelectionIds,
    IReadOnlyDictionary<string, int> Summary,
    IReadOnlyList<InteractionValidationResult> Results);

internal static class InteractionValidationCoordinator
{
    private const int ShutdownBackstopMilliseconds = 8000;

    public static void Start(
        MainWindow mainWindow,
        InteractionValidationOptions options,
        LocalAppDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(options);

        mainWindow.Opened += async (_, _) => await RunAsync(mainWindow, options, diagnostics);
    }

    private static async Task RunAsync(
        MainWindow mainWindow,
        InteractionValidationOptions options,
        LocalAppDiagnostics? diagnostics)
    {
        var exitCode = 1;
        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
            var results = await mainWindow.RunInteractionValidationAsync(
                options.OutputDirectory,
                options.DialogStart,
                options.DialogCount,
                options.IncludeCoreResults,
                options.RibbonCommandStart,
                options.RibbonCommandCount,
                options.RibbonOnly,
                options.CoreSection,
                options.ContextMenuDispatchStart,
                options.ContextMenuDispatchCount);
            WriteManifest(options.OutputDirectory, options, results);
            exitCode = results.Any(result => string.Equals(result.Status, "failed", StringComparison.Ordinal)) ? 1 : 0;
            diagnostics?.RecordEvent("interaction_validation", new Dictionary<string, string?>
            {
                ["source"] = "interaction_validation",
                ["scope"] = "linux_x11",
                ["status"] = exitCode == 0 ? "completed" : "failed",
                ["passed"] = results.Count(result => result.Status == "passed").ToString(),
                ["failed"] = results.Count(result => result.Status == "failed").ToString(),
                ["total"] = results.Count.ToString(),
            });
        }
        catch (Exception ex)
        {
            diagnostics?.RecordCrash(ex, "interaction_validation");
            WriteFailureManifest(options.OutputDirectory, ex);
        }
        finally
        {
            mainWindow.AllowCloseWithoutDirtyPromptForParityCapture();
            Shutdown(exitCode);
        }
    }

    private static void WriteManifest(
        string outputDirectory,
        InteractionValidationOptions options,
        IReadOnlyList<InteractionValidationResult> results)
    {
        var summary = results
            .GroupBy(result => result.Status, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        summary["total"] = results.Count;
        var ribbonCommandCatalogIds = AvaloniaRibbonComposition
            .EnumerateSurfaceRows(AvaloniaRibbonComposition.BuildDefinition())
            .Select(row => row.CommandId.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var contextMenuInventory = MainWindow.BuildContextMenuValidationInventory();
        var contextMenuDispatchCatalogIds = contextMenuInventory
            .Select(row => $"{row.FamilyId}|{row.VariantId}|{row.ActionKey}|{row.IsEnabled}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var dialogCatalogIds = MainWindow.InteractiveValidationDialogRoutes
            .Select(route => route.CatalogId)
            .ToArray();
        var validationSelectionIds = options.CoreSection == "context-menus"
            ? contextMenuDispatchCatalogIds
                .Skip(Math.Max(0, options.ContextMenuDispatchStart))
                .Take(Math.Max(0, options.ContextMenuDispatchCount))
                .ToArray()
            : options.RibbonOnly
                ? ribbonCommandCatalogIds
                    .Skip(Math.Max(0, options.RibbonCommandStart))
                    .Take(Math.Max(0, options.RibbonCommandCount))
                    .ToArray()
            : options.CoreSection == "ribbon-bindings"
                ? ribbonCommandCatalogIds
            : !options.IncludeCoreResults
                ? dialogCatalogIds
                    .Skip(Math.Max(0, options.DialogStart))
                    .Take(Math.Max(0, options.DialogCount))
                    .ToArray()
            : [];
        var manifest = new InteractionValidationManifest(
            SchemaVersion: 2,
            Platform: PlatformName(),
            Shell: "avalonia",
            GeneratedUtc: DateTimeOffset.UtcNow,
            DialogCatalogCount: MainWindow.InteractiveValidationDialogRouteCount,
            RibbonCommandCatalogCount: MainWindow.InteractiveValidationRibbonCommandCount,
            ContextMenuDispatchCatalogCount: MainWindow.InteractiveValidationContextMenuDispatchCount,
            ValidationSection: options.RibbonOnly
                ? "ribbon-only"
                : options.CoreSection ?? (options.IncludeCoreResults ? "full" : "dialogs"),
            IncludeCoreResults: options.IncludeCoreResults,
            RibbonOnly: options.RibbonOnly,
            DialogStart: options.DialogStart,
            DialogCount: options.DialogCount,
            RibbonCommandStart: options.RibbonCommandStart,
            RibbonCommandCount: options.RibbonCommandCount,
            ContextMenuDispatchStart: options.ContextMenuDispatchStart,
            ContextMenuDispatchCount: options.ContextMenuDispatchCount,
            DialogCatalogIds: dialogCatalogIds,
            RibbonCommandCatalogIds: ribbonCommandCatalogIds,
            ContextMenuDispatchCatalogIds: contextMenuDispatchCatalogIds,
            ContextMenuFamilyCatalogIds: MainWindow.InteractiveValidationContextMenuFamilyIds,
            ContextMenuVariantCatalogIds: MainWindow.InteractiveValidationContextMenuVariantIds,
            ValidationSelectionIds: validationSelectionIds,
            Summary: summary,
            Results: results);
        var json = JsonSerializer.Serialize(manifest, JsonOptions());
        File.WriteAllText(Path.Combine(outputDirectory, "interaction-validation.json"), json);
    }

    internal static void WriteManifestForTest(
        string outputDirectory,
        InteractionValidationOptions options,
        IReadOnlyList<InteractionValidationResult> results) =>
        WriteManifest(outputDirectory, options, results);

    private static void WriteFailureManifest(string outputDirectory, Exception ex)
    {
        Directory.CreateDirectory(outputDirectory);
        var failure = new
        {
            schemaVersion = 2,
            platform = PlatformName(),
            shell = "avalonia",
            error = $"{ex.GetType().Name}: {ex.Message}",
        };
        File.WriteAllText(
            Path.Combine(outputDirectory, "interaction-validation.json"),
            JsonSerializer.Serialize(failure, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string PlatformName() =>
        OperatingSystem.IsLinux() ? "linux" :
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "macos" : "unknown";

    private static void Shutdown(int exitCode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.TryShutdown(exitCode);
        }, DispatcherPriority.Background);

        _ = Task.Run(async () =>
        {
            await Task.Delay(ShutdownBackstopMilliseconds).ConfigureAwait(false);
            Environment.Exit(exitCode);
        });
    }
}
