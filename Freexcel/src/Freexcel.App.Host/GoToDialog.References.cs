using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class GoToDialog
{
    public static bool TryParseAddress(string text, SheetId sheetId, out CellAddress address)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                address = default;
                return false;
            }

            address = CellAddress.Parse(text.Trim(), sheetId);
            return true;
        }
        catch
        {
            address = default;
            return false;
        }
    }

    public static IReadOnlyList<string> BuildReferenceChoices(
        string defaultAddress,
        IEnumerable<string>? recentReferences,
        IEnumerable<string>? definedNames)
    {
        var choices = new List<string>();
        Add(defaultAddress);
        foreach (var reference in recentReferences ?? [])
            Add(reference);
        foreach (var name in (definedNames ?? []).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            Add(name);

        return choices.Count == 0 ? ["A1"] : choices;

        void Add(string? reference)
        {
            var trimmed = reference?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return;

            if (choices.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                return;

            choices.Add(trimmed);
        }
    }

    public static bool TryParseReference(
        string text,
        SheetId sheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        out CellAddress address)
    {
        if (TryParseReferenceRange(text, sheetId, definedNames, out var range))
        {
            address = range.Start;
            return true;
        }

        address = default;
        return false;
    }

    public static bool TryParseReferenceRange(
        string text,
        SheetId sheetId,
        IReadOnlyDictionary<string, GridRange>? definedNames,
        out GridRange range)
    {
        if (TryParseAddress(text, sheetId, out var address))
        {
            range = new GridRange(address, address);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(text) &&
            WorkbookRangeTextCodec.TryParse(sheetId, text, _ => null, out range))
            return true;

        if (definedNames is not null &&
            definedNames.TryGetValue(text.Trim(), out var namedRange))
        {
            range = namedRange;
            return true;
        }

        range = default;
        return false;
    }
}
