using System.Text.Json;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.Core.Model;

namespace FreeW.Validation.Avalonia;

internal sealed record TablePropertiesX11ValidationOptions(string ResultPath)
{
    private const string ResultPathKey = "resultPath";
    public const string Argument = "--table-properties-x11-validation";

    private static readonly CommandLineValueOptionSpec ResultPathOption = new(
        ResultPathKey,
        Argument,
        $"{Argument} requires one non-empty result path and may appear once.",
        $"{Argument} requires one non-empty result path and may appear once.",
        $"{Argument} requires one non-empty result path and may appear once.",
        AllowEqualsSyntax: true);

    public static bool TryParse(
        IReadOnlyList<string> args,
        out TablePropertiesX11ValidationOptions? options,
        out string[] startupArguments,
        out string? error)
    {
        var parsed = CommandLineValueOptionParser.Parse(args, [ResultPathOption]);
        options = parsed.Error is null && parsed.IsPresent(ResultPathKey)
            ? new TablePropertiesX11ValidationOptions(parsed.Value(ResultPathKey)!)
            : null;
        startupArguments = parsed.RemainingArguments;
        error = parsed.Error;
        return parsed.Error is null;
    }
}

internal static class TablePropertiesX11ValidationCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Start(
        MainWindow.ValidationAccessAdapter access,
        TablePropertiesX11ValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(options);
        access.StartWhenOpened(() => RunAsync(access, options));
    }

    private static async Task RunAsync(
        MainWindow.ValidationAccessAdapter access,
        TablePropertiesX11ValidationOptions options)
    {
        access.InsertTable(2, 2);
        var tableBlock = -1;
        for (var index = 0; index < access.DocumentBlocks.Count; index++)
        {
            if (access.DocumentBlocks[index] is Table table
                && table.Rows.Count == 2
                && table.Rows.All(row => row.Cells.Count == 2))
            {
                tableBlock = index;
            }
        }

        if (tableBlock < 0)
            throw new InvalidOperationException("Table Properties X11 validation fixture did not create a table.");

        access.PlaceCaretInCell(tableBlock, 0, 0, 0, 0);
        var context = access.CaretTableContext()
            ?? throw new InvalidOperationException("Table Properties X11 validation fixture did not select cell A1.");
        var observation = await access.ShowTablePropertiesDialogAsync(context);
        access.ApplyTableProperties(observation.Values);

        var result = new
        {
            schema = "freew.table-properties.x11-result.v1",
            status = observation.Values is null ? "cancelled" : "applied",
            tableRows = observation.TableRows,
            tableColumns = observation.TableColumns,
            values = observation.Values,
            focusTrace = observation.FocusTrace,
        };
        JsonArtifactIO.Write(Path.GetFullPath(options.ResultPath), result, JsonOptions);
    }
}
