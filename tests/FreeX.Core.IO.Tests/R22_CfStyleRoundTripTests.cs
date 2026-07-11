using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 22 regression tests for <c>XlsxConditionalFormatClosedXmlMapper</c>'s CF-rule style
/// writer (<c>ApplyStyle</c>/<c>ApplyBorderEdge</c>), which dropped several style properties
/// that the primary cell-style writer (<c>XlsxClosedXmlCellMapper</c>) already preserves:
/// <list type="bullet">
///   <item>
///     R22-io-styles-roundtrip-1 — double-underline was written back as single/none underline.
///   </item>
///   <item>
///     R22-io-styles-roundtrip-2 — 7 of the 13 legal OOXML border styles (Hair, SlantDashDot,
///     MediumDashed, DashDot, MediumDashDot, DashDotDot, MediumDashDotDot) collapsed to "no
///     border" on save.
///   </item>
///   <item>
///     R22-io-styles-roundtrip-3 — diagonal borders (BorderDiagonalUp/BorderDiagonalDown) were
///     never written at all.
///   </item>
/// </list>
/// </summary>
public sealed class R22_CfStyleRoundTripTests
{
    private static Workbook BuildWorkbookWithRule(CellStyle formatIfTrue)
    {
        var workbook = new Workbook("CfStyleRoundTrip");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            FormatIfTrue = formatIfTrue
        });

        return workbook;
    }

    private static CellStyle RoundTrip(CellStyle formatIfTrue)
    {
        var workbook = BuildWorkbookWithRule(formatIfTrue);

        using var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;
        var loaded = new XlsxFileAdapter().Load(ms);

        loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle();
        var rule = loaded.GetSheetAt(0).ConditionalFormats[0];
        rule.FormatIfTrue.Should().NotBeNull();
        return rule.FormatIfTrue!;
    }

    // ── R22-io-styles-roundtrip-1 ───────────────────────────────────────────

    [Fact]
    public void RoundTrip_ClassicCellIsConditionalFormat_PreservesDoubleUnderline()
    {
        var reloaded = RoundTrip(new CellStyle { DoubleUnderline = true });

        reloaded.DoubleUnderline.Should().BeTrue(
            "a classic cellIs conditional format's double-underline must survive save/load instead of " +
            "being downgraded to single/none underline");
    }

    // ── R22-io-styles-roundtrip-2 ───────────────────────────────────────────

    [Theory]
    [InlineData(BorderStyle.Hair)]
    [InlineData(BorderStyle.SlantDashDot)]
    [InlineData(BorderStyle.MediumDashed)]
    [InlineData(BorderStyle.DashDot)]
    [InlineData(BorderStyle.MediumDashDot)]
    [InlineData(BorderStyle.DashDotDot)]
    [InlineData(BorderStyle.MediumDashDotDot)]
    public void RoundTrip_ClassicCellIsConditionalFormat_PreservesExtendedBorderStyles(BorderStyle borderStyle)
    {
        var reloaded = RoundTrip(new CellStyle
        {
            BorderTop = new CellBorder(borderStyle, CellColor.FromArgb(255, 0, 0))
        });

        reloaded.BorderTop.Style.Should().Be(borderStyle,
            $"the extended border style {borderStyle} must survive save/load instead of collapsing to " +
            "\"no border\", just like Thin/Medium/Thick/Dashed/Dotted/Double already do");
    }

    // ── R22-io-styles-roundtrip-3 ───────────────────────────────────────────

    [Fact]
    public void RoundTrip_ClassicCellIsConditionalFormat_PreservesDiagonalBorders()
    {
        var reloaded = RoundTrip(new CellStyle
        {
            BorderDiagonalDown = new CellBorder(BorderStyle.Thin, CellColor.FromArgb(0, 0, 255)),
            BorderDiagonalUp = new CellBorder(BorderStyle.Thin, CellColor.FromArgb(0, 0, 255))
        });

        reloaded.BorderDiagonalDown.Style.Should().Be(BorderStyle.Thin,
            "a classic cellIs conditional format's diagonal-down border must survive save/load");
        reloaded.BorderDiagonalUp.Style.Should().Be(BorderStyle.Thin,
            "a classic cellIs conditional format's diagonal-up border must survive save/load");
    }
}
