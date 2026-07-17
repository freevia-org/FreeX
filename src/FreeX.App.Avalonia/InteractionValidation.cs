using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal sealed record InteractionValidationOptions(
    string OutputDirectory,
    int DialogStart = 0,
    int DialogCount = int.MaxValue,
    bool IncludeCoreResults = true)
{
    public const string Argument = "--interaction-validation";
    public const string DialogStartArgument = "--interaction-validation-dialog-start";
    public const string DialogCountArgument = "--interaction-validation-dialog-count";
    public const string DialogOnlyArgument = "--interaction-validation-dialog-only";

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
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], DialogOnlyArgument, StringComparison.OrdinalIgnoreCase))
            {
                includeCoreResults = false;
                continue;
            }

            if (string.Equals(args[index], DialogStartArgument, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[index], DialogCountArgument, StringComparison.OrdinalIgnoreCase))
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
                else
                    dialogCount = value;
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
            options = new InteractionValidationOptions(outputDirectory, dialogStart, dialogCount, includeCoreResults);
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
    IReadOnlyDictionary<string, int> Summary,
    IReadOnlyList<InteractionValidationResult> Results);

internal static class InteractionValidationCoordinator
{
    private const int ShutdownBackstopMilliseconds = 8000;

    public static void Start(
        MainWindow mainWindow,
        InteractionValidationOptions options,
        AvaloniaAppDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(options);

        mainWindow.Opened += async (_, _) => await RunAsync(mainWindow, options, diagnostics);
    }

    private static async Task RunAsync(
        MainWindow mainWindow,
        InteractionValidationOptions options,
        AvaloniaAppDiagnostics? diagnostics)
    {
        var exitCode = 1;
        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
            var results = await mainWindow.RunInteractionValidationAsync(
                options.OutputDirectory,
                options.DialogStart,
                options.DialogCount,
                options.IncludeCoreResults);
            WriteManifest(options.OutputDirectory, results);
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

    private static void WriteManifest(string outputDirectory, IReadOnlyList<InteractionValidationResult> results)
    {
        var summary = results
            .GroupBy(result => result.Status, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        summary["total"] = results.Count;
        var manifest = new InteractionValidationManifest(
            SchemaVersion: 1,
            Platform: PlatformName(),
            Shell: "avalonia",
            GeneratedUtc: DateTimeOffset.UtcNow,
            Summary: summary,
            Results: results);
        var json = JsonSerializer.Serialize(manifest, JsonOptions());
        File.WriteAllText(Path.Combine(outputDirectory, "interaction-validation.json"), json);
    }

    private static void WriteFailureManifest(string outputDirectory, Exception ex)
    {
        Directory.CreateDirectory(outputDirectory);
        var failure = new
        {
            schemaVersion = 1,
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
