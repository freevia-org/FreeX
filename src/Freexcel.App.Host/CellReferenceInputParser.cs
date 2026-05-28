using Freexcel.Core.Model;

namespace Freexcel.App.Host;

internal static class CellReferenceInputParser
{
    public static bool TryParseCell(string input, SheetId sheetId, out CellAddress address)
    {
        var normalized = AbsoluteCellReferenceNormalizer.Normalize(input);
        return normalized is not null && CellAddress.TryParse(normalized, sheetId, out address) ||
               PageLayoutInputParser.TryParseAbsoluteR1C1CellReference(input, sheetId, out address);
    }
}
