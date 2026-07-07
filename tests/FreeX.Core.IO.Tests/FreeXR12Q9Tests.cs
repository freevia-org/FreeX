using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-12 bucket Q9 regression tests.
/// </summary>
public sealed class FreeXR12Q9Tests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // R12-crosscutting-robustness-1: a crafted <col min="1" max="4294967295" .../> must not drive
    // a multi-billion-iteration loop. Excel clamps <col> max to the sheet's column count
    // (CellAddress.MaxCol = 16384); the reader must do the same instead of iterating raw uint span.
    [Fact]
    public void ReadSheetDataLayout_ClampsUnboundedColMaxToModelMaxColumn()
    {
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "cols",
                    new XElement(
                        WorksheetNs + "col",
                        new XAttribute("min", "1"),
                        new XAttribute("max", "4294967295"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "1"))),
                new XElement(WorksheetNs + "sheetData")));

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheet, WorksheetNs);

        // The loop must have been clamped to the model's max column, not run ~4.29 billion times.
        layout.RowColumnLayout.HiddenCols.Count.Should().BeLessThanOrEqualTo((int)CellAddress.MaxCol);
        layout.RowColumnLayout.HiddenCols.Should().Contain(1u);
        layout.RowColumnLayout.HiddenCols.Should().Contain(CellAddress.MaxCol);
        layout.RowColumnLayout.HiddenCols.Should().NotContain(CellAddress.MaxCol + 1);

        layout.RowColumnLayout.ColOutlineLevels.Count.Should().BeLessThanOrEqualTo((int)CellAddress.MaxCol);
        layout.RowColumnLayout.ColOutlineLevels.Should().ContainKey(CellAddress.MaxCol);
    }

    // A <col> whose min is itself beyond the model's max column is entirely out of range and must
    // be skipped rather than clamped into a bogus single-column entry.
    [Fact]
    public void ReadSheetDataLayout_SkipsColWhoseMinIsBeyondModelMaxColumn()
    {
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "cols",
                    new XElement(
                        WorksheetNs + "col",
                        new XAttribute("min", "4294967290"),
                        new XAttribute("max", "4294967295"),
                        new XAttribute("hidden", "1"))),
                new XElement(WorksheetNs + "sheetData")));

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheet, WorksheetNs);

        layout.RowColumnLayout.HiddenCols.Should().BeEmpty();
    }
}
