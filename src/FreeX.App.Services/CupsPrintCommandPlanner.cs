using System.Globalization;

namespace FreeX.App.Services;

/// <summary>
/// Builds the command-line arguments for the CUPS printing utilities (<c>lp</c>, <c>lpstat</c>) and
/// parses their text output, with no process I/O of its own. Keeping this pure makes the
/// platform-specific behaviour (which flags map to copies / collate / page-range, and how the printer
/// listing is shaped) fully unit-testable; the thin platform glue only has to launch the process and
/// feed stdout back here. This lives in the shared layer so any Unix host (Linux today, macOS later —
/// macOS also ships CUPS) can reuse it.
/// </summary>
public static class CupsPrintCommandPlanner
{
    /// <summary>The utility that submits a job ("line printer", CUPS-compatible).</summary>
    public const string SubmitProgram = "lp";

    /// <summary>The utility that reports printer status / the default destination.</summary>
    public const string StatusProgram = "lpstat";

    /// <summary>
    /// Arguments for <c>lpstat -e</c> — the portable way to list accepting destinations by name.
    /// </summary>
    public static IReadOnlyList<string> BuildListPrintersArguments() => ["-e"];

    /// <summary>Arguments for <c>lpstat -d</c> — reports the system default destination.</summary>
    public static IReadOnlyList<string> BuildDefaultPrinterArguments() => ["-d"];

    /// <summary>
    /// Builds the <c>lp</c> argument list for <paramref name="submission"/>: target printer (<c>-d</c>),
    /// copies (<c>-n</c>), page range (<c>-P first-last</c>), collation (<c>-o collate=true|false</c>),
    /// a job title (<c>-t</c>), and finally the document path. The caller writes the document bytes to a
    /// temp file and passes that path.
    /// </summary>
    public static IReadOnlyList<string> BuildSubmitArguments(PrintJobSubmission submission, string documentPath)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        var args = new List<string>();

        if (!string.IsNullOrWhiteSpace(submission.PrinterId))
        {
            args.Add("-d");
            args.Add(submission.PrinterId);
        }

        var copies = Math.Max(1, submission.Copies);
        if (copies > 1)
        {
            args.Add("-n");
            args.Add(copies.ToString(CultureInfo.InvariantCulture));
        }

        if (submission.FirstPage >= 1 && submission.LastPage >= submission.FirstPage)
        {
            args.Add("-P");
            args.Add($"{submission.FirstPage}-{submission.LastPage}");
        }

        args.Add("-o");
        args.Add(submission.Collate ? "collate=true" : "collate=false");

        if (!string.IsNullOrWhiteSpace(submission.JobTitle))
        {
            args.Add("-t");
            args.Add(submission.JobTitle.Trim());
        }

        args.Add(documentPath);
        return args;
    }

    /// <summary>
    /// Parses the printer names from <c>lpstat -e</c> stdout (one destination name per line) and marks the
    /// one matching <paramref name="defaultPrinterId"/> as default, ordering it first. Blank lines are
    /// ignored; the result is empty when no destinations are configured.
    /// </summary>
    public static IReadOnlyList<PrinterDescriptor> ParsePrinters(
        string? listOutput,
        string? defaultPrinterId)
    {
        var defaultId = NormalizeDefault(defaultPrinterId);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var printers = new List<PrinterDescriptor>();

        if (!string.IsNullOrWhiteSpace(listOutput))
        {
            foreach (var rawLine in listOutput.Split('\n'))
            {
                var name = rawLine.Trim();
                if (name.Length == 0 || !seen.Add(name))
                    continue;

                printers.Add(new PrinterDescriptor(name, name, IsDefault: defaultId is not null && name == defaultId));
            }
        }

        // The default destination might not appear in -e (rare, but be defensive): surface it anyway.
        if (defaultId is not null && seen.Add(defaultId))
            printers.Add(new PrinterDescriptor(defaultId, defaultId, IsDefault: true));

        // Default first, then declaration order — stable and predictable for the dialog's pre-selection.
        return printers
            .OrderByDescending(p => p.IsDefault)
            .ToList();
    }

    /// <summary>
    /// Extracts the default destination name from <c>lpstat -d</c> stdout, which reads either
    /// "system default destination: NAME" or "no system default destination". Returns null when none.
    /// </summary>
    public static string? ParseDefaultPrinter(string? defaultOutput)
    {
        if (string.IsNullOrWhiteSpace(defaultOutput))
            return null;

        const string marker = "destination:";
        var index = defaultOutput.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        var name = defaultOutput[(index + marker.Length)..].Trim();
        return name.Length == 0 ? null : name;
    }

    private static string? NormalizeDefault(string? defaultPrinterId)
    {
        var trimmed = defaultPrinterId?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
