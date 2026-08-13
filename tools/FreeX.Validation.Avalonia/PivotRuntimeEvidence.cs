using Free.Shared.AppServices;
using FreeX.App.Avalonia;

namespace FreeX.Validation.Avalonia;

internal sealed record PivotRuntimeEvidenceOptions(string Path)
{
    private const string PathKey = "path";
    internal const string Argument = "--freex-pivot-runtime-evidence";

    private static readonly CommandLineValueOptionSpec PathOption = new(
        PathKey,
        Argument,
        $"{Argument} requires a non-empty evidence path.",
        $"{Argument} requires a non-empty evidence path.",
        $"{Argument} was specified more than once.");

    internal static bool TryParse(
        IReadOnlyList<string> arguments,
        out PivotRuntimeEvidenceOptions? options,
        out string[] startupArguments,
        out string error)
    {
        var parsed = CommandLineValueOptionParser.Parse(
            arguments,
            [PathOption],
            StringComparison.OrdinalIgnoreCase);
        options = parsed.Error is null && parsed.IsPresent(PathKey)
            ? new PivotRuntimeEvidenceOptions(parsed.Value(PathKey)!)
            : null;
        startupArguments = parsed.Error is null ? parsed.RemainingArguments : [];
        error = parsed.Error ?? "";
        return parsed.Error is null;
    }
}

internal static class PivotRuntimeEvidenceCoordinator
{
    internal static void Start(
        MainWindow.PivotRuntimeObservationAccessAdapter access,
        PivotRuntimeEvidenceOptions options,
        IReadOnlyList<string> startupArguments)
    {
        access.SetObserver(observation => Append(options.Path, startupArguments, observation));
    }

    private static void Append(
        string path,
        IReadOnlyList<string> startupArguments,
        PivotRuntimeObservation observation)
    {
        try
        {
            var payload = new
            {
                utc = DateTimeOffset.UtcNow,
                stage = observation.Stage,
                activeSheet = observation.ActiveSheet,
                activeSheetId = observation.ActiveSheetId,
                activeCellSheetId = observation.ActiveCellSheetId,
                activeCellRow = observation.ActiveCellRow,
                activeCellColumn = observation.ActiveCellColumn,
                startupArguments = startupArguments.ToArray(),
                currentFilePath = observation.CurrentFilePath,
                workbookName = observation.WorkbookName,
                workbookSheets = observation.WorkbookSheets.Select(item => new
                {
                    item.Name,
                    pivotCount = item.PivotCount,
                }).ToArray(),
                sheetPivotCount = observation.SheetPivotCount,
                pivots = observation.Pivots.Select(item => new
                {
                    item.Name,
                    targetStart = item.TargetStart,
                    targetEnd = item.TargetEnd,
                    renderedStart = item.RenderedStart,
                    renderedEnd = item.RenderedEnd,
                }).ToArray(),
                resolvedPivot = observation.ResolvedPivot,
                paneVisible = observation.PaneVisible,
                paneWidth = observation.PaneWidth,
                userHidden = observation.UserHidden,
            };
            JsonArtifactIO.AppendLine(path, payload);
        }
        catch
        {
            // Evidence is opt-in and must never affect worksheet behavior.
        }
    }
}
