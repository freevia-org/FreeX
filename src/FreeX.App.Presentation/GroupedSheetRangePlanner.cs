using FreeX.Core.Model;

namespace FreeX.App.Presentation;

public static class GroupedSheetRangePlanner
{
    public static GridRange RemapRangeToSheet(GridRange range, SheetId sheetId) =>
        new(
            new CellAddress(sheetId, range.Start.Row, range.Start.Col),
            new CellAddress(sheetId, range.End.Row, range.End.Col));

    public static ConditionalFormat CloneConditionalFormatForSheet(ConditionalFormat source, SheetId sheetId) =>
        CloneConditionalFormatForSheet(source, sheetId, preserveIdentity: false);

    public static ConditionalFormat CloneConditionalFormatForSheet(
        ConditionalFormat source,
        SheetId sheetId,
        bool preserveIdentity)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Delegate to ConditionalFormat.Clone() so every field (icon overrides, color-scale/dataBar
        // theme provenance, EqualAverage/StdDevCount, native round-trip metadata, ...) stays in sync
        // with the canonical clone instead of being hand-maintained here (the old hand-written
        // initializer silently dropped several of those, R33-commands-conditionalformat-manage-1).
        // A fan-out copy receives a fresh Id and has its sheet-specific x14 extLst id stripped.
        // The primary-sheet manage/apply path may preserve identity for exactly one clone.
        var clone = source.Clone(preserveIdentity ? null : Guid.NewGuid());
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
