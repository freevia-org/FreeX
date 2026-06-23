using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation;

public static class CellReferenceInputParser
{
    public static bool TryParseCell(string input, SheetId sheetId, out CellAddress address)
    {
        var normalized = AbsoluteCellReferenceNormalizer.Normalize(input);
        return normalized is not null && CellAddress.TryParse(normalized, sheetId, out address) ||
               PageLayoutInputParser.TryParseAbsoluteR1C1CellReference(input, sheetId, out address);
    }
}
