using System.Text.Json;
using FreeW.App.Avalonia;
using FreeW.Core.Model;

namespace FreeW.Validation.Avalonia;

internal sealed record TablePropertiesX11ValidationOptions(string ResultPath)
{
    public const string Argument = "--table-properties-x11-validation";

    public static bool TryParse(
        IReadOnlyList<string> args,
        out TablePropertiesX11ValidationOptions? options,
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
                    error = $"{Argument} requires one non-empty result path and may appear once.";
                    startupArguments = filtered.ToArray();
                    return false;
                }

                options = new TablePropertiesX11ValidationOptions(argument[(Argument.Length + 1)..]);
                continue;
            }

            if (!string.Equals(argument, Argument, StringComparison.Ordinal))
            {
                filtered.Add(argument);
                continue;
            }

            if (options is not null || index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                error = $"{Argument} requires one non-empty result path and may appear once.";
                startupArguments = filtered.ToArray();
                return false;
            }

            options = new TablePropertiesX11ValidationOptions(args[++index]);
        }

        startupArguments = filtered.ToArray();
        return true;
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
        var resultPath = Path.GetFullPath(options.ResultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        File.WriteAllText(resultPath, JsonSerializer.Serialize(result, JsonOptions));
    }
}
