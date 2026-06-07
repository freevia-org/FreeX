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
            FontSize = 18,
            FontColor = new CellColor(12, 34, 56),
            FillColor = new CellColor(90, 91, 92),
            NumberFormat = "$#,##0.00",
            HorizontalAlignment = CellHAlign.Right,
            VerticalAlignment = CellVAlign.Top,
            WrapText = true,
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
            FontColor: font));

        diff.NumberFormat.Should().Be("#,##0.00");
        diff.HAlign.Should().Be(CellHAlign.Center);
        diff.VAlign.Should().Be(CellVAlign.Top);
        diff.WrapText.Should().BeTrue();
        diff.Bold.Should().BeTrue();
        diff.Italic.Should().BeTrue();
        diff.Underline.Should().BeTrue();
        diff.Strikethrough.Should().BeTrue();
        diff.FontSize.Should().Be(13.5);
        diff.FillColor.Should().Be(fill);
        diff.FontColor.Should().Be(font);
    }

    [Fact]
    public void Plan_ClearFillRequestsClearFillWithoutSettingOtherFields()
    {
        var baseStyle = new CellStyle
        {
            FillColor = new CellColor(200, 210, 220),
            FontColor = new CellColor(1, 2, 3)
        };

        var diff = FormatCellsCompactPlanner.Plan(new FormatCellsCompactRequest(ClearFill: true));
        var result = diff.ApplyTo(baseStyle);

        diff.ClearFill.Should().BeTrue();
        diff.FillColor.Should().BeNull();
        result.FillColor.Should().BeNull();
        result.FontColor.Should().Be(baseStyle.FontColor);
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
    public void Plan_FontSizeRejectsInvalidValues(double fontSize)
    {
        Action act = () => FormatCellsCompactPlanner.Plan(new FormatCellsCompactRequest(FontSize: fontSize));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(nameof(FormatCellsCompactRequest.FontSize));
    }

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }
}
