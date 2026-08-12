using System.Text.Json;
using FreeX.App.Avalonia;

namespace FreeX.Validation.Avalonia;

internal sealed record PivotRuntimeEvidenceOptions(string Path)
{
    internal const string Argument = "--freex-pivot-runtime-evidence";

    internal static bool TryParse(
        IReadOnlyList<string> arguments,
        out PivotRuntimeEvidenceOptions? options,
        out string[] startupArguments,
        out string error)
    {
        options = null;
        error = "";
        var filtered = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], Argument, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(arguments[index]);
                continue;
            }

            if (options is not null)
            {
                startupArguments = [];
                error = $"{Argument} was specified more than once.";
                return false;
            }

            if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                startupArguments = [];
                error = $"{Argument} requires a non-empty evidence path.";
                return false;
            }

            options = new PivotRuntimeEvidenceOptions(arguments[++index]);
        }

        startupArguments = filtered.ToArray();
        return true;
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
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

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
            File.AppendAllText(path, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch
        {
            // Evidence is opt-in and must never affect worksheet behavior.
        }
    }
}
