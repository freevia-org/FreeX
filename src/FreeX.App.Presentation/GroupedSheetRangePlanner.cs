using FreeX.Core.Model;

namespace FreeX.App.Presentation;

public static class GroupedSheetRangePlanner
{
    public static GridRange RemapRangeToSheet(GridRange range, SheetId sheetId) =>
        new(
            new CellAddress(sheetId, range.Start.Row, range.Start.Col),
            new CellAddress(sheetId, range.End.Row, range.End.Col));

    public static ConditionalFormat CloneConditionalFormatForSheet(ConditionalFormat source, SheetId sheetId)
    {
        // Delegate to ConditionalFormat.Clone() so every field (icon overrides, color-scale/dataBar
        // theme provenance, EqualAverage/StdDevCount, native round-trip metadata, ...) stays in sync
        // with the canonical clone instead of being hand-maintained here (the old hand-written
        // initializer silently dropped several of those, R33-commands-conditionalformat-manage-1).
        // This is a fan-out of the rule to ANOTHER sheet in the same grouped edit, so the copy must
        // get a fresh Id and the sheet-specific x14 extLst id must be stripped (Clone(newId) does
        // both) so the two sheets' rules do not collide on the same x14 id.
        var clone = source.Clone(Guid.NewGuid());
        clone.AppliesTo = RemapRangeToSheet(source.AppliesTo, sheetId);
        clone.AdditionalRanges = source.AdditionalRanges is null
            ? null
            : source.AdditionalRanges.Select(r => RemapRangeToSheet(r, sheetId)).ToList();
        return clone;
    }

    public static DataValidation CloneDataValidationForSheet(DataValidation source, SheetId sheetId)
    {
        return source.CloneWithNewIdentity(
            RemapRangeToSheet(source.AppliesTo, sheetId),
            source.AdditionalRanges.Select(range => RemapRangeToSheet(range, sheetId)));
    }
}
