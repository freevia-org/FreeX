using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

// Regression guard: the worksheet row-attribute preservation merge must NOT copy the source row's
// style index (`s`) or its companion `customFormat` flag onto the rebuilt worksheet. The full-save
// path rebuilds styles.xml via ClosedXML, which renumbers (and usually shrinks) the cellXfs table,
// so a source row style index points into a stale index space. Copying it verbatim produces an
// out-of-range cellXfs reference that crashes FreeX's own reload (ClosedXML LoadStyle ->
// Enumerable.ElementAt -> ArgumentOutOfRangeException). Native-only layout attributes such as
// height still need preservation, while hidden/outline/collapse state is owned by the Sheet model.
public sealed class XlsxWorksheetMetadataPreserverRowStyleTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void MergeWorksheetRowAttributes_DoesNotCopyStaleStyleOrModeledOutlineState()
    {
        // Source row carries a style index that is valid only against the original (large) stylesheet.
        var sourceSheetData = new XElement(
            Ns + "sheetData",
            new XElement(
                Ns + "row",
                new XAttribute("r", "1"),
                new XAttribute("customFormat", "1"),
                new XAttribute("s", "73"),
                new XAttribute("ht", "27"),
                new XAttribute("customHeight", "1"),
                new XAttribute("hidden", "1"),
                new XAttribute("outlineLevel", "1"),
                new XAttribute("collapsed", "1")));

        // Target row (ClosedXML-rebuilt) has no style attributes yet.
        var targetRoot = new XElement(
            Ns + "worksheet",
            new XElement(
                Ns + "sheetData",
                new XElement(Ns + "row", new XAttribute("r", "1"))));

        XlsxWorksheetMetadataPreserver.MergeWorksheetRowAttributes(sourceSheetData, targetRoot, Ns);

        var targetRow = targetRoot.Element(Ns + "sheetData")!.Element(Ns + "row")!;

        targetRow.Attribute("s").Should().BeNull(
            "the source row's style index points into the stale source stylesheet and would be out of range against the rebuilt cellXfs");
        targetRow.Attribute("customFormat").Should().BeNull(
            "customFormat is meaningless without the row's preserved style index");

        // Native-only layout attributes still round-trip, but modeled outline state must not be
        // restored from the source after the rebuilt worksheet intentionally omits it.
        targetRow.Attribute("ht")!.Value.Should().Be("27");
        targetRow.Attribute("customHeight")!.Value.Should().Be("1");
        targetRow.Attribute("hidden").Should().BeNull();
        targetRow.Attribute("outlineLevel").Should().BeNull();
        targetRow.Attribute("collapsed").Should().BeNull();
    }
}
