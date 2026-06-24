using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip tests for conditional-format dxf number format and border fields
/// (wave-b fix: MergeStyles/StackDifferentialStyle now propagate numFmt and border).
/// </summary>
public sealed class XlsxCfDxfNumFmtBorderRoundTripTests
{
    [Fact]
    public void XlsxAdapter_CfDxfWithNumFmtAndBorder_PreservesFieldsAfterSaveAndReload()
    {
        // Arrange: build a workbook with a CellValue CF rule that sets fill + font + numFmt + border.
        var workbook = new Workbook("CfDxfRoundTrip");
        var sheet = workbook.AddSheet("HighlightDxf");

        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(10)));

        var redBorder = new CellBorder(BorderStyle.Thin, new CellColor(255, 0, 0));
        var cfStyle = new CellStyle
        {
            FillColor = new CellColor(255, 199, 206),
            FillPatternStyle = CellFillPatternStyle.Solid,
            Bold = true,
            FontColor = new CellColor(156, 0, 6),
            NumberFormat = "$#,##0.00",
            BorderTop = redBorder,
            BorderRight = redBorder,
            BorderBottom = redBorder,
            BorderLeft = redBorder,
        };

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(addr, addr),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            FormatIfTrue = cfStyle,
        });

        // Act: save → reload
        using var stream = new System.IO.MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        // Assert: the CF rule survives with both numFmt and border intact.
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Should().NotBeNull();
        var rule = reloadedSheet!.ConditionalFormats.Should().ContainSingle().Subject;
        var fmt = rule.FormatIfTrue;
        fmt.Should().NotBeNull("CF rule must carry FormatIfTrue after round-trip");

        fmt!.NumberFormat.Should().Be("$#,##0.00",
            "dxf numFmt must be preserved through save/reload");

        fmt.BorderTop.Style.Should().Be(BorderStyle.Thin,
            "dxf border top style must be preserved through save/reload");
        fmt.BorderTop.Color.Should().Be(new CellColor(255, 0, 0),
            "dxf border top color must be preserved through save/reload");

        fmt.BorderRight.Style.Should().Be(BorderStyle.Thin,
            "dxf border right style must be preserved through save/reload");
        fmt.BorderBottom.Style.Should().Be(BorderStyle.Thin,
            "dxf border bottom style must be preserved through save/reload");
        fmt.BorderLeft.Style.Should().Be(BorderStyle.Thin,
            "dxf border left style must be preserved through save/reload");

        // Also verify that the other dxf fields were not disturbed.
        fmt.FillColor.Should().Be(new CellColor(255, 199, 206), "fill color preserved");
        fmt.Bold.Should().BeTrue("bold preserved");
        fmt.FontColor.Should().Be(new CellColor(156, 0, 6), "font color preserved");
    }
}
