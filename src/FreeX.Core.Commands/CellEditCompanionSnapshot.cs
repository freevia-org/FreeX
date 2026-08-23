using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal sealed record CellEditCompanionSnapshot(
    CellAddress Address,
    Cell? Cell,
    StyleId? StyleOnly,
    bool HadRichTextRuns,
    IReadOnlyList<CellTextRun>? RichTextRuns,
    bool HadHyperlink,
    string? Hyperlink,
    bool HadHyperlinkMetadata,
    HyperlinkMetadata? HyperlinkMetadata,
    bool HadPhoneticGuide,
    CellPhoneticGuide? PhoneticGuide)
{
    internal static CellEditCompanionSnapshot Capture(Sheet sheet, CellAddress address)
    {
        var hadRichTextRuns = sheet.RichTextRuns.TryGetValue(address, out var richTextRuns);
        var hadHyperlink = sheet.Hyperlinks.TryGetValue(address, out var hyperlink);
        var hadHyperlinkMetadata = sheet.HyperlinkMetadata.TryGetValue(address, out var hyperlinkMetadata);
        var hadPhoneticGuide = sheet.CellPhoneticGuides.TryGetValue(address, out var phoneticGuide);

        return new(
            address,
            sheet.GetCell(address)?.Clone(),
            sheet.GetStyleOnly(address.Row, address.Col),
            hadRichTextRuns,
            richTextRuns,
            hadHyperlink,
            hyperlink,
            hadHyperlinkMetadata,
            hyperlinkMetadata,
            hadPhoneticGuide,
            phoneticGuide);
    }

    internal void Restore(Sheet sheet)
    {
        if (Cell is null)
        {
            sheet.ClearCell(Address);
            if (StyleOnly.HasValue)
                sheet.SetStyleOnly(Address.Row, Address.Col, StyleOnly.Value);
            else
                sheet.ClearStyleOnly(Address.Row, Address.Col);
        }
        else
        {
            sheet.SetCell(Address, Cell.Clone());
        }

        RestoreEntry(sheet.RichTextRuns, Address, HadRichTextRuns, RichTextRuns);
        RestoreEntry(sheet.Hyperlinks, Address, HadHyperlink, Hyperlink);
        RestoreEntry(sheet.HyperlinkMetadata, Address, HadHyperlinkMetadata, HyperlinkMetadata);
        RestoreEntry(sheet.CellPhoneticGuides, Address, HadPhoneticGuide, PhoneticGuide);
    }

    private static void RestoreEntry<T>(IDictionary<CellAddress, T> entries, CellAddress address, bool existed, T? value)
    {
        if (existed && value is not null)
            entries[address] = value;
        else
            entries.Remove(address);
    }
}
