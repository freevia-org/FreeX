using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// R170-freex-autofilter-sort-F2: allocates a workbook-level &lt;dxfs&gt; entry for every saved
/// Sort On: Cell Colour/Font Colour condition that resolved a target colour
/// (<see cref="WorksheetSortConditionModel.TargetColor"/>) but has no <c>dxfId</c> yet, and stamps
/// the allocated index onto <see cref="WorksheetSortConditionModel.DxfId"/>.
/// <para>
/// Mirrors <see cref="XlsxAutoFilterColorFilterDxfWriter"/> (R89) exactly, which solved the
/// identical round-trip gap for AutoFilter's "Filter by Cell/Font Colour": before that writer
/// existed, FreeX had no way to turn a chosen colour into the <c>dxfId</c> OOXML requires. This is
/// the same gap for Sort On: Cell/Font Colour's saved &lt;sortCondition&gt; -- real Excel always
/// writes a <c>dxfId</c> for a saved colour-sort level so reopening Data &gt; Sort shows which
/// colour was actually sorted on. Unlike <see cref="WorksheetAutoFilterColorFilterModel"/> (an
/// immutable record, so that writer returns a side lookup table the caller threads back through),
/// <see cref="WorksheetSortConditionModel"/> is a plain mutable class, so this mutates
/// <see cref="WorksheetSortConditionModel.DxfId"/> in place -- no lookup table or extra parameter
/// needs to flow through <see cref="XlsxWorksheetSortStateMapper"/>'s call chain.
/// </para>
/// <para>
/// An icon sort needs no allocation at all: <c>iconSet</c>/<c>iconId</c> alone fully identify the
/// chosen icon in the &lt;sortCondition&gt; schema (no colour to resolve), so
/// <see cref="SortCommand"/> sets those directly and this writer never touches icon conditions.
/// </para>
/// </summary>
internal static class XlsxSortStateColorDxfWriter
{
    public static bool HasUnallocatedSortColors(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            var conditions = sheet.SortState?.Conditions;
            if (conditions is null)
                continue;

            foreach (var condition in conditions)
            {
                if (NeedsAllocation(condition))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Allocates dxfs (mutating xl/styles.xml in <paramref name="archive"/>) for every saved sort
    /// condition that needs one, stamping the allocated index directly onto each condition's
    /// <see cref="WorksheetSortConditionModel.DxfId"/>. A condition that already carries a
    /// <c>DxfId</c> (e.g. read from a file Excel wrote) is left completely alone.
    /// </summary>
    public static void Save(ZipArchive archive, Workbook workbook, XNamespace workbookNs)
    {
        var pending = new List<(WorksheetSortConditionModel Condition, CellStyle Style)>();
        foreach (var sheet in workbook.Sheets)
        {
            var conditions = sheet.SortState?.Conditions;
            if (conditions is null)
                continue;

            foreach (var condition in conditions)
            {
                if (!NeedsAllocation(condition))
                    continue;

                pending.Add((condition, ToDxfStyle(condition)));
            }
        }

        if (pending.Count == 0)
            return;

        var stylesEntry = archive.GetEntry("xl/styles.xml");
        var stylesXml = stylesEntry is not null
            ? XlsxPackageXmlEditor.LoadXml(stylesEntry)
            : new XDocument(new XElement(workbookNs + "styleSheet"));
        var root = stylesXml.Root;
        if (root is null)
            return;

        var dxfs = XlsxDifferentialStyleAllocator.GetOrCreateDxfsElement(root, workbookNs);
        var nextNumFmtId = XlsxDifferentialStyleAllocator.ComputeNextCustomNumFmtId(root, dxfs, workbookNs);

        foreach (var (condition, style) in pending)
        {
            // A plain fill/font colour never sets a NumberFormat, so ToDifferentialStyleXml never
            // actually emits a <numFmt> here -- nextNumFmtId is passed only so the shared builder's
            // signature matches the conditional-format writer's; it is not consumed for these dxfs.
            var dxfXml = XlsxAdvancedConditionalFormatWriter.ToDifferentialStyleXml(style, workbookNs, nextNumFmtId);
            var index = XlsxDifferentialStyleAllocator.AllocateOrReuse(dxfs, dxfXml, workbookNs);
            condition.DxfId = index.ToString(CultureInfo.InvariantCulture);
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
    }

    private static bool NeedsAllocation(WorksheetSortConditionModel condition) =>
        condition.TargetColor is not null && condition.DxfId is null;

    private static CellStyle ToDxfStyle(WorksheetSortConditionModel condition) =>
        condition.SortBy == "fontColor"
            ? new CellStyle { FontColor = condition.TargetColor!.Value }
            : new CellStyle { FillColor = condition.TargetColor!.Value, FillPatternStyle = CellFillPatternStyle.Solid };
}
