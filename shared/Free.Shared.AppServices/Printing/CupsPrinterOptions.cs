using System.Text.RegularExpressions;

namespace Free.Shared.AppServices.Printing;

internal static class CupsPrinterOptions
{
    // Keep the common print controls authoritative, including the duplex aliases CUPS maps from PPDs.
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "copies", "num-copies", "NumCopies",
        "page-ranges", "page-range", "PageRange", "PageRanges", "page-set",
        "collate", "sheet-collate", "multiple-document-handling",
        "orientation-requested", "orientation", "landscape",
        "sides", "Duplex", "JCLDuplex", "EFDuplex", "EFDuplexing", "KD03Duplex",
        "print-scaling", "scaling", "natural-scaling", "fit-to-page", "fitplot", "ppi",
        "job-name",
    };

    private static readonly Regex ChoiceWordBoundary = new(
        @"(?<=[a-z])(?=[A-Z0-9])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.CultureInvariant);

    public static PrinterOptionsResult Parse(string output)
    {
        var definitions = output.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseOption)
            .OfType<PrinterOptionDefinition>()
            .ToArray();

        if (definitions.Length == 0 && !string.IsNullOrWhiteSpace(output))
            return new(PrinterOptionsStatus.Failed, [], "CUPS returned an unreadable printer options list.");

        var options = definitions
            .Where(option => CanOverride(option.Key) && option.Choices.Count > 0)
            .GroupBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.First())
            .ToArray();

        return options.Length > 0
            ? new(PrinterOptionsStatus.Available, options)
            : new(PrinterOptionsStatus.NoOptions, [], "This printer has no additional options available.");
    }

    public static bool MatchesQueue(PrinterOptionSelection selection, string printerName) =>
        string.Equals(selection.PrinterName, printerName, StringComparison.OrdinalIgnoreCase);

    public static bool AreSafeOverrides(IEnumerable<KeyValuePair<string, string>> options)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return options.All(option =>
            CanOverride(option.Key) && IsChoice(option.Value) && keys.Add(option.Key));
    }

    private static PrinterOptionDefinition? ParseOption(string line)
    {
        // lpoptions -l reports "Keyword/Label: Choice *MarkedChoice". The label may contain colons.
        var slash = line.IndexOf('/');
        var colon = line.LastIndexOf(':');
        if (slash <= 0 || colon <= slash || !IsToken(line[..slash]))
            return null;

        var key = line[..slash];
        var label = line[(slash + 1)..colon].Trim();
        var choices = new List<PrinterOptionChoice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? defaultValue = null;

        foreach (var entry in line[(colon + 1)..].Split(
                     [' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var isDefault = entry[0] == '*';
            var value = isDefault ? entry[1..] : entry;
            if (!IsChoice(value))
                continue;

            if (seen.Add(value))
                choices.Add(new(value, FormatChoiceLabel(value)));
            if (isDefault && defaultValue is null)
                defaultValue = value;
        }

        if (label.Length == 0 || label == key)
        {
            label = key switch
            {
                "cupsPrintQuality" => "Print Quality",
                "cupsFinishingTemplate" => "Finishing",
                _ => FormatChoiceLabel(key),
            };
        }

        return new(key, label, choices.ToArray(), defaultValue);
    }

    private static string FormatChoiceLabel(string value)
    {
        var label = value.ToUpperInvariant() switch
        {
            "AUTO" => "Automatic",
            "FALSE" => "Off",
            "GRAY" or "GRAYSCALE" => "Grayscale",
            "TRUE" => "On",
            _ => ChoiceWordBoundary.Replace(value.Replace('-', ' ').Replace('_', ' '), " ").Trim(),
        };
        return label.Length > 0 ? char.ToUpperInvariant(label[0]) + label[1..] : value;
    }

    private static bool CanOverride(string key) => IsToken(key) && !ReservedKeys.Contains(key);

    // Custom.WIDTHxHEIGHT and similar entries require additional parameters and are not discrete choices.
    private static bool IsChoice(string value) =>
        IsToken(value) && !value.StartsWith("Custom.", StringComparison.OrdinalIgnoreCase);

    // lp parses its -o argument again to find assignments. Allow only literal keyword and choice tokens.
    private static bool IsToken(string value) =>
        value is { Length: > 0 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '+');
}
