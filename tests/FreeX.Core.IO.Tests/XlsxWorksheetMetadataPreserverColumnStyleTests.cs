using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

// Regression guard: the worksheet column-attribute preservation merge must NOT copy the source
// column's whole-column "style" index onto a rebuilt <col> that matches by min/max range. The
// full-save path rebuilds styles.xml via ClosedXML, which renumbers (and usually shrinks) the
// cellXfs table, so a source column style index -- valid only against the ORIGINAL (possibly much
// larger) source stylesheet -- can point past the end of the rebuilt table. Copying it verbatim
// produces an out-of-range cellXfs reference that crashes FreeX's own reload (ClosedXML LoadStyle
// -> Enumerable.ElementAt -> ArgumentOutOfRangeException) -- the same stale-index hazard the row
// path already guards against (see XlsxWorksheetMetadataPreserverRowStyleTests). Other native-only
// column attributes (e.g. bestFit) still need to round-trip.
public sealed class XlsxWorksheetMetadataPreserverColumnStyleTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void MergeWorksheetColumnAttributes_DoesNotCopyStaleStyleIndex()
    {
        // Source column carries a style index that is valid only against a much larger source
        // stylesheet (e.g. a source cellXfs with 200+ entries) -- 150 would be out of range against
        // a rebuilt table with only a handful of entries.
        var sourceColumns = new XElement(
            Ns + "cols",
            new XElement(
                Ns + "col",
                new XAttribute("min", "3"),
                new XAttribute("max", "3"),
                new XAttribute("width", "20"),
                new XAttribute("customWidth", "1"),
                new XAttribute("style", "150")));

        // Target column (ClosedXML-rebuilt) has no style attribute yet -- its rebuilt cellXfs table
        // is small (far fewer than 150 entries).
        var targetRoot = new XElement(
            Ns + "worksheet",
            new XElement(
                Ns + "cols",
                new XElement(
                    Ns + "col",
                    new XAttribute("min", "3"),
                    new XAttribute("max", "3"),
                    new XAttribute("width", "20"),
                    new XAttribute("customWidth", "1"))));

        XlsxWorksheetMetadataPreserver.MergeWorksheetColumnAttributes(sourceColumns, targetRoot, Ns);

        var targetColumn = targetRoot.Element(Ns + "cols")!.Element(Ns + "col")!;

        targetColumn.Attribute("style").Should().BeNull(
            "the source column's style index points into the stale source stylesheet and would be " +
            "out of range against the rebuilt cellXfs, crashing FreeX's own reload");

        // Native-only, non-stylesheet-index attributes still round-trip.
        targetColumn.Attribute("width")!.Value.Should().Be("20");
        targetColumn.Attribute("customWidth")!.Value.Should().Be("1");
    }

    [Fact]
    public void MergeWorksheetColumnAttributes_ColumnWithNoStyleAttribute_IsUnaffected_NoRegression()
    {
        // Sibling no-regression case: a column that never carried a style index at all must merge
        // its other native-only attributes (e.g. bestFit) exactly as before.
        var sourceColumns = new XElement(
            Ns + "cols",
            new XElement(
                Ns + "col",
                new XAttribute("min", "4"),
                new XAttribute("max", "4"),
                new XAttribute("bestFit", "1")));

        var targetRoot = new XElement(
            Ns + "worksheet",
            new XElement(
                Ns + "cols",
                new XElement(
                    Ns + "col",
                    new XAttribute("min", "4"),
                    new XAttribute("max", "4"))));

        var changed = XlsxWorksheetMetadataPreserver.MergeWorksheetColumnAttributes(sourceColumns, targetRoot, Ns);

        changed.Should().BeTrue();
        var targetColumn = targetRoot.Element(Ns + "cols")!.Element(Ns + "col")!;
        targetColumn.Attribute("bestFit")!.Value.Should().Be("1");
    }
}
