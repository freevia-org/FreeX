using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Targeted tests for R32-io-print-export-fidelity-deep-2 (HTML export ignoring cell NumberFormat) and
/// R32-io-print-export-fidelity-deep-3 (HTML export dropping hyperlinks).
/// </summary>
public sealed class HtmlTableWriterFormatAndHyperlinkTests
{
    private static string SaveToString(Workbook wb)
    {
        using var stream = new MemoryStream();
        new HtmlFileAdapter().Save(wb, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public void Save_AppliesPercentNumberFormatInsteadOfRawInvariantNumber()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        var percentStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "0%" });
        var cell = Cell.FromValue(new NumberValue(0.5));
        cell.StyleId = percentStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var html = SaveToString(wb);

        html.Should().Contain(">50%<");
        html.Should().NotContain(">0.5<");
    }

    [Fact]
    public void Save_AppliesCustomDateNumberFormatInsteadOfRawSerial()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        var dateStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "mm/dd/yyyy" });
        var cell = Cell.FromValue(DateTimeValue.FromDateTime(new DateTime(2024, 1, 31)));
        cell.StyleId = dateStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var html = SaveToString(wb);

        html.Should().Contain(">01/31/2024<");
    }

    [Fact]
    public void Save_PlainNumberAndDateWithoutExplicitFormatKeepInvariantRendering()
    {
        // Sibling/regression case: a cell with no style (or an explicit "General" format) must keep the
        // prior self-contained invariant rendering so plain number/date round-trips via HtmlTableReader
        // (which only recognizes the writer's own yyyy-MM-dd / plain-number shapes) are unaffected.
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(0.5));
        var generalStyle = wb.RegisterStyle(new CellStyle { NumberFormat = "General" });
        var dateCell = Cell.FromValue(DateTimeValue.FromDateTime(new DateTime(2024, 1, 31)));
        dateCell.StyleId = generalStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), dateCell);

        var html = SaveToString(wb);

        html.Should().Contain(">0.5<");
        html.Should().Contain(">2024-01-31<");
    }

    [Fact]
    public void Save_EmitsAnchorTagForHyperlinkedCell()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Visit"));
        sheet.Hyperlinks[new CellAddress(sheet.Id, 1, 1)] = "https://example.com/";

        var html = SaveToString(wb);

        html.Should().Contain("<a href=\"https://example.com/\">Visit</a>");
    }

    [Fact]
    public void Save_CellWithoutHyperlinkEmitsNoAnchorTag()
    {
        // Sibling case: an ordinary (non-hyperlinked) cell must not gain a spurious <a> wrapper.
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Plain"));

        var html = SaveToString(wb);

        html.Should().Contain(">Plain<");
        html.Should().NotContain("<a href=");
    }
}
