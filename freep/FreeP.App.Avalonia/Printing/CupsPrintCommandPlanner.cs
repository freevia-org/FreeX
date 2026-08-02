using System.Globalization;
using Free.Shared.AppServices.Printing;

namespace FreeP.App.Avalonia.Printing;

public static class CupsPrintCommandPlanner
{
    public static ProcessInvocation ListPrinters() =>
        new("lpstat", ["-p"]);

    public static ProcessInvocation ReadDefaultPrinter() =>
        new("lpstat", ["-d"]);

    public static ProcessInvocation Submit(string pdfPath, PrintSelection selection, string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();

        var arguments = new List<string>
        {
            "-d", printerName,
            "-n", selection.Copies.ToString(CultureInfo.InvariantCulture),
            "-o", $"collate={(selection.Collate ? "true" : "false")}",
        };
        if (selection.EffectivePageRange.ToCupsPageList() is { } pageList)
            arguments.AddRange(["-P", pageList]);
        if (selection.Orientation is PrintOrientation.Portrait or PrintOrientation.Landscape)
        {
            var requested = selection.Orientation == PrintOrientation.Portrait ? "3" : "4";
            arguments.AddRange(["-o", $"orientation-requested={requested}"]);
        }
        arguments.Add(pdfPath);
        return new ProcessInvocation("lp", arguments);
    }
}
