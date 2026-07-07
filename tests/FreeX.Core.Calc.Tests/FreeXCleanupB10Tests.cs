using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression test for cleanup finding P49: a CellIs "equal to"/"not equal to" text rule loaded
/// from an Excel-authored XLSX stores the comparand as the raw ClosedXML formula text of a quoted
/// string literal (e.g. <c>"abc"</c>, quotes included) or a bare cell reference (e.g. <c>$B$1</c>).
/// The evaluator must unwrap the literal / resolve the reference through the same threshold-formula
/// cache the numeric branch already uses, rather than comparing the cell's display text against the
/// still-quoted raw source. See ViewportConditionalFormatEvaluator.Aggregates.cs's MatchesCellValue.
/// </summary>
public partial class ConditionalFormatTests
{
    [Fact]
    public void CellValue_EqualToQuotedTextLiteral_MatchesOnlyTheLiteralText()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("abc")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("xyz")));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.Equal,
            // Mirrors how XlsxConditionalFormatClosedXmlMapper stores an Excel <formula>"abc"</formula>
            // verbatim: the quotes are part of the stored comparand text.
            Value1 = "\"abc\"",
            FormatIfTrue = redStyle
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(
            new CellColor(255, 0, 0),
            "A1's text \"abc\" must match the unquoted literal comparand \"abc\"");
        GetCell(vp, 2, 1).Style?.FillColor.Should().NotBe(new CellColor(255, 0, 0));
    }

    [Fact]
    public void CellValue_NotEqualToQuotedTextLiteral_DoesNotMatchEverything()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("abc")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("xyz")));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.NotEqual,
            Value1 = "\"abc\"",
            FormatIfTrue = redStyle
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style?.FillColor.Should().NotBe(
            new CellColor(255, 0, 0),
            "A1 equals the literal \"abc\", so the NotEqual rule must NOT highlight it");
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(
            new CellColor(255, 0, 0),
            "A2 (\"xyz\") does not equal \"abc\", so NotEqual should highlight it");
    }

    [Fact]
    public void CellValue_EqualToCellReferenceText_ResolvesReferenceNotLiteralRefText()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new TextValue("target")));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("target")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("$C$1")));

        var greenStyle = new CellStyle { FillColor = new CellColor(0, 255, 0) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.Equal,
            Value1 = "$C$1",
            FormatIfTrue = greenStyle
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(
            new CellColor(0, 255, 0),
            "A1's text equals the value of referenced cell $C$1 (\"target\")");
        GetCell(vp, 2, 1).Style?.FillColor.Should().NotBe(
            new CellColor(0, 255, 0),
            "A2's text is the literal string \"$C$1\", not the resolved reference value, so it must not match");
    }
}
