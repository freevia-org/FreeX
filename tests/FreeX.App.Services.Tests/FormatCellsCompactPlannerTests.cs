using FluentAssertions;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Services.Tests;

public sealed class FormatCellsCompactPlannerTests
{
    private static readonly CellColor Accent = new(33, 115, 70);

    [Fact]
    public void Plan_EmptyRequestLeavesStyleUnchanged()
    {
        var baseStyle = new CellStyle
        {
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            Superscript = true,
            Subscript = false,
            FontName = "Aptos",
            FontSize = 18,
            FontColor = new CellColor(12, 34, 56),
            FillColor = new CellColor(90, 91, 92),
            FillPatternStyle = CellFillPatternStyle.DarkGrid,
            FillPatternColor = new CellColor(111, 112, 113),
            NumberFormat = "$#,##0.00",
            HorizontalAlignment = CellHAlign.Right,
            VerticalAlignment = CellVAlign.Top,
            WrapText = true,
            ShrinkToFit = true,
            DoubleUnderline = true,
            IndentLevel = 7,
            TextRotation = 45,
            Locked = false,
            Hidden = true,
            BorderTop = new CellBorder(BorderStyle.Thick, new CellColor(1, 2, 3)),
            BorderRight = new CellBorder(BorderStyle.Dashed, new CellColor(4, 5, 6)),
            BorderBottom = new CellBorder(BorderStyle.Double, new CellColor(7, 8, 9)),
            BorderLeft = new CellBorder(BorderStyle.Dotted, new CellColor(10, 11, 12))
        };

        var result = FormatCellsCompactPlanner.Plan(new FormatCellsCompactRequest()).ApplyTo(baseStyle);

        result.Should().Be(baseStyle);
    }

    [Fact]
    public void Plan_ChosenFieldsMapToStyleDiff()
    {
        var fill = new CellColor(220, 240, 255);
        var fillPattern = new CellColor(24, 120, 180);
        var font = new CellColor(64, 32, 16);

        var diff = FormatCellsCompactPlanner.Plan(new FormatCellsCompactRequest(
            NumberFormat: "#,##0.00",
            HorizontalAlignment: CellHAlign.Center,
            VerticalAlignment: CellVAlign.Top,
            WrapText: true,
            Bold: true,
            Italic: true,
            Underline: true,
            Strikethrough: true,
            FontSize: 13.5,
            FillColor: fill,
            FontColor: font,
            DoubleUnderline: true,
            ShrinkToFit: true,
            IndentLevel: 4,
            TextRotation: 255,
            FontName: " Aptos ",
            Locked: false,
            Hidden: true,
            Superscript: true,
            Subscript: false,
            FillPatternStyle: CellFillPatternStyle.DarkGrid,
            FillPatternColor: fillPattern));

        diff.NumberFormat.Should().Be("#,##0.00");
        diff.HAlign.Should().Be(CellHAlign.Center);
        diff.VAlign.Should().Be(CellVAlign.Top);
        diff.WrapText.Should().BeTrue();
        diff.ShrinkToFit.Should().BeTrue();
        diff.Bold.Should().BeTrue();
        diff.Italic.Should().BeTrue();
        diff.Underline.Should().BeTrue();
        diff.Strikethrough.Should().BeTrue();
        diff.DoubleUnderline.Should().BeTrue();
        diff.Superscript.Should().BeTrue();
        diff.Subscript.Should().BeFalse();
        diff.FontName.Should().Be("Aptos");
        diff.FontSize.Should().Be(13.5);
        diff.FillColor.Should().Be(fill);
        diff.FillPatternStyle.Should().Be(CellFillPatternStyle.DarkGrid);
        diff.FillPatternColor.Should().Be(fillPattern);
        diff.FontColor.Should().Be(font);
        diff.IndentLevel.Should().Be(4);
        diff.TextRotation.Should().Be(255);
        diff.Locked.Should().BeFalse();
        diff.Hidden.Should().BeTrue();
    }

    [Fact]
    public void Plan_DoubleUnderlineUsesFormatCellsSemanticsWithoutClearingUnderlineOrStrikethrough()
    {
        var baseStyle = new CellStyle
        {
            Underline = true,
            Strikethrough = true,
            DoubleUnderline = false
        };

        var result = FormatCellsCompactPlanner.Plan(new FormatCellsCompactRequest(DoubleUnderline: true))
            .ApplyTo(baseStyle);

        result.Underline.Should().BeTrue();
        result.Strikethrough.Should().BeTrue();
        result.DoubleUnderline.Should().BeTrue();
    }

    [Fact]
    public void Plan_ClearFillRequestsClearFillWithoutSettingOtherFields()
    {
        var baseStyle = new CellStyle
        {
            FillColor = new CellColor(200, 210, 220),
            FillPatternStyle = CellFillPatternStyle.DarkGrid,
            FillPatternColor = new CellColor(100, 110, 120),
            FontColor = new CellColor(1, 2, 3)
        };

        var diff = FormatCellsCompactPlanner.Plan(new FormatCellsCompactRequest(
            FillColor: new CellColor(10, 20, 30),
            ClearFill: true,
            FillPatternStyle: CellFillPatternStyle.LightTrellis,
            FillPatternColor: new CellColor(40, 50, 60)));
        var result = diff.ApplyTo(baseStyle);

        diff.ClearFill.Should().BeTrue();
        diff.FillColor.Should().BeNull();
        diff.FillPatternStyle.Should().BeNull();
        diff.FillPatternColor.Should().BeNull();
        result.FillColor.Should().BeNull();
        result.FillPatternStyle.Should().Be(CellFillPatternStyle.None);
        result.FillPatternColor.Should().BeNull();
        result.FontColor.Should().Be(baseStyle.FontColor);
    }

    [Fact]
    public void Plan_MergeCellsIntentDoesNotCreateStyleDiff()
    {
        var diff = FormatCellsCompactPlanner.Plan(new FormatCellsCompactRequest(MergeCells: true));

        diff.Should().Be(new StyleDiff());
    }

    [Fact]
    public void Plan_BorderPresetMapsThroughCellBorderPresetPlanner()
    {
        var range = Range(2, 3, 4, 5);
        var address = new CellAddress(range.Start.Sheet, 2, 3);
        var request = new FormatCellsCompactRequest(
            BorderPreset: CellBorderPreset.Outside,
            BorderStyle: BorderStyle.Double,
            BorderColor: Accent);

        var diff = FormatCellsCompactPlanner.Plan(request, range, address);
        var expected = CellBorderPresetPlanner.Plan(CellBorderPreset.Outside, range, address, BorderStyle.Double, Accent);

        diff.BorderTop.Should().Be(expected.BorderTop);
        diff.BorderRight.Should().Be(expected.BorderRight);
        diff.BorderBottom.Should().Be(expected.BorderBottom);
        diff.BorderLeft.Should().Be(expected.BorderLeft);
    }

    [Fact]
    public void Plan_NonRangeRelativeBorderPresetDoesNotRequireBorderContext()
    {
        var diff = FormatCellsCompactPlanner.Plan(new FormatCellsCompactRequest(
            BorderPreset: CellBorderPreset.All,
            BorderColor: Accent));

        diff.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        diff.BorderRight.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        diff.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        diff.BorderLeft.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
    }

    [Fact]
    public void GetBorderPresetMetadata_ExposesDisplayNameAndRangeRelativeFlag()
    {
        var metadata = FormatCellsCompactPlanner.GetBorderPresetMetadata();

        metadata.Select(item => item.Preset).Should().Equal(Enum.GetValues<CellBorderPreset>());
        metadata.Should().ContainEquivalentOf(new FormatCellsCompactBorderPresetMetadata(
            CellBorderPreset.Outside,
            "Outside Borders",
            RequiresPerCellPlanning: true));
        metadata.Should().ContainEquivalentOf(new FormatCellsCompactBorderPresetMetadata(
            CellBorderPreset.All,
            "All Borders",
            RequiresPerCellPlanning: false));
    }

    [Fact]
    public void Plan_RangeRelativeBorderPresetRequiresCellContext()
    {
        var request = new FormatCellsCompactRequest(BorderPreset: CellBorderPreset.Inside);

        Action act = () => FormatCellsCompactPlanner.Plan(request);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*require a selected range and cell address*");
    }

    [Theory]
    [InlineData(0.25, 1)]
    [InlineData(1, 1)]
    [InlineData(11.5, 11.5)]
    public void Plan_FontSizeClampsToMinimumAndAcceptsPositiveFiniteValues(double requested, double expected)
    {
        var diff = FormatCellsCompactPlanner.Plan(new FormatCellsCompactRequest(FontSize: requested));

        diff.FontSize.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void TryPlan_FontSizeRejectsInvalidValuesWithClearError(double fontSize)
    {
        var result = FormatCellsCompactPlanner.TryPlan(
            new FormatCellsCompactRequest(FontSize: fontSize),
            out var diff,
            out var errorMessage);

        result.Should().BeFalse();
        diff.Should().Be(new StyleDiff());
        errorMessage.Should().Contain("Font size");
    }

    [Theory]
    [InlineData(-4, 0)]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(15, 15)]
    [InlineData(99, 15)]
    public void Plan_IndentLevelClampsToWpfParserRange(int requested, int expected)
    {
        var diff = FormatCellsCompactPlanner.Plan(new FormatCellsCompactRequest(IndentLevel: requested));

        diff.IndentLevel.Should().Be(expected);
    }

    [Theory]
    [InlineData(-90)]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(255)]
    public void Plan_TextRotationAcceptsWpfParserSupportedValues(int rotation)
    {
        var diff = FormatCellsCompactPlanner.Plan(new FormatCellsCompactRequest(TextRotation: rotation));

        diff.TextRotation.Should().Be(rotation);
    }

    [Theory]
    [InlineData(-91)]
    [InlineData(91)]
    [InlineData(254)]
    [InlineData(256)]
    public void TryPlan_TextRotationRejectsUnsupportedValuesWithClearError(int rotation)
    {
        var result = FormatCellsCompactPlanner.TryPlan(
            new FormatCellsCompactRequest(TextRotation: rotation),
            out var diff,
            out var errorMessage);

        result.Should().BeFalse();
        diff.Should().Be(new StyleDiff());
        errorMessage.Should().Contain("Text rotation");
    }

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }
}
