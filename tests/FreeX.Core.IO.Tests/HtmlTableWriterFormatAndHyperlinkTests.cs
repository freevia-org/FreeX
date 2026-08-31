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

    // R175 F2 (HtmlTableWriter half): AppendBorder read border.Color directly, unlike the
    // ResolveFontColor/ResolveFillColor calls a few lines above it in the same BuildCss method, so a
    // border set via the ribbon's Theme Colors picker saved to .html/.mht with the color baked in at
    // load time instead of the CURRENT workbook theme. Exercises the real Save (HtmlFileAdapter) call
    // site so both the writer and its production caller are covered, not just the private helper.
    [Fact]
    public void Save_ThemeColoredBorder_UsesCurrentThemeColor_NotTheColorBakedAtLoadTime()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");

        var oldTheme = WorkbookTheme.Office;
        var staleBakedColor = oldTheme.GetColor(WorkbookThemeColorSlot.Accent1);
        var newTheme = oldTheme.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 20, 20));
        wb.Theme = newTheme;

        var border = new CellBorder(
            BorderStyle.Thick,
            staleBakedColor,
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1));
        var style = new CellStyle { BorderTop = border };
        var styleId = wb.RegisterStyle(style);
        var cell = Cell.FromValue(new TextValue("x"));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var expected = border.ResolveColor(newTheme);
        expected.Should().NotBe(staleBakedColor, "the test theme swap must actually change Accent1");
        var expectedHex = $"#{expected.R:X2}{expected.G:X2}{expected.B:X2}";
        var staleHex = $"#{staleBakedColor.R:X2}{staleBakedColor.G:X2}{staleBakedColor.B:X2}";

        var html = SaveToString(wb);

        html.Should().Contain($"border-top:3px solid {expectedHex};",
            "the exported border must follow the CURRENT theme's Accent1, not the color baked in at load time");
        html.Should().NotContain(staleHex,
            "the exported border must not still show the stale load-time color after the theme changed");
    }

    [Fact]
    public void Save_ExplicitRgbBorder_StillExportsItsOwnColor_NoRegression()
    {
        // Sibling/no-regression case: a border with NO ThemeColor (a plain RGB swatch, not a Theme
        // Color) must keep exporting its own authored color regardless of the workbook theme.
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");

        var explicitColor = new CellColor(10, 200, 30);
        wb.Theme = wb.Theme.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 20, 20));

        var border = new CellBorder(BorderStyle.Thick, explicitColor, ThemeColor: null);
        var style = new CellStyle { BorderTop = border };
        var styleId = wb.RegisterStyle(style);
        var cell = Cell.FromValue(new TextValue("x"));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var html = SaveToString(wb);

        html.Should().Contain("border-top:3px solid #0AC81E;",
            "an explicit-RGB border must keep exporting its own authored color regardless of the workbook theme");
    }
}
