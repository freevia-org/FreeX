using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PrintSettingsPlannerTests
{
    [Fact]
    public void Build_SummarizesActiveSheetSettingsWithDefaultText()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1")
        {
            PageOrientation = WorksheetPageOrientation.Landscape,
            PaperSize = WorksheetPaperSize.Letter,
            PrintGridlines = true,
            PrintHeadings = true,
            ScaleToFit = new WorksheetScaleToFit(85, 1, 2)
        };

        var plan = PrintSettingsPlanner.Build(sheet);

        plan.Lines.Should().Equal(
            "Print active sheet",
            "Orientation: Landscape",
            "Paper size: Letter",
            "Scaling: 85%; fit 1 page wide by 2 tall",
            "Gridlines: on",
            "Headings: on");
        plan.Summary.Should().Be("Print active sheet; Orientation: Landscape; Paper size: Letter; Scaling: 85%; fit 1 page wide by 2 tall; Gridlines: on; Headings: on");
    }

    [Fact]
    public void Build_UsesTextResolverForLocalizedSummary()
    {
        var requestedKeys = new List<string>();
        var resolver = new PrintSettingsTextResolver(
            key =>
            {
                requestedKeys.Add(key);
                return key switch
                {
                    "PrintSettings_PrintActiveSheet" => "Localized active sheet",
                    "PageSetup_Portrait" => "Localized portrait",
                    "MainWindow_Header_A4" => "Localized A4",
                    "PrintSettings_Automatic" => "Localized automatic",
                    "PrintSettings_Off" => "localized off",
                    _ => "[" + key + "]"
                };
            },
            (key, args) =>
            {
                requestedKeys.Add(key);
                return key switch
                {
                    "PrintSettings_OrientationFormat" => "Orientation localized: " + args[0],
                    "PrintSettings_PaperSizeFormat" => "Paper localized: " + args[0],
                    "PrintSettings_ScalingFormat" => "Scaling localized: " + args[0],
                    "PrintSettings_GridlinesFormat" => "Gridlines localized: " + args[0],
                    "PrintSettings_HeadingsFormat" => "Headings localized: " + args[0],
                    _ => "[" + key + "]"
                };
            });

        var plan = PrintSettingsPlanner.Build(
            new Sheet(SheetId.New(), "Sheet1") { ScaleToFit = WorksheetScaleToFit.Default },
            textResolver: resolver);

        plan.Lines.Should().Equal(
            "Localized active sheet",
            "Orientation localized: Localized portrait",
            "Paper localized: Localized A4",
            "Scaling localized: 100%",
            "Gridlines localized: localized off",
            "Headings localized: localized off");
        requestedKeys.Should().Contain("PrintSettings_OrientationFormat");
    }

    [Fact]
    public void Build_SummarizesIgnoredPrintArea()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sheet1")
        {
            PrintArea = GridRange.Parse("B2:D10", sheetId)
        };

        var normal = PrintSettingsPlanner.Build(sheet);
        var ignored = PrintSettingsPlanner.Build(sheet, ignorePrintArea: true);

        normal.Lines[0].Should().Be("Print selected print area");
        ignored.Lines[0].Should().Be("Print active sheet (ignore print area)");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void ScaleIndexToScaleToFit_DefaultForUnrecognisedIndex(int index)
    {
        PrintSettingsPlanner.ScaleIndexToScaleToFit(index).Should().Be(WorksheetScaleToFit.Default);
    }

    [Fact]
    public void ScaleIndexToScaleToFit_Index1_FitsSheetOnOnePage()
    {
        var result = PrintSettingsPlanner.ScaleIndexToScaleToFit(1);

        result.FitToPagesWide.Should().Be(1);
        result.FitToPagesTall.Should().Be(1);
        result.ScalePercent.Should().BeNull();
    }

    [Fact]
    public void ScaleIndexToScaleToFit_Index2_FitsAllColumnsOnOnePage()
    {
        var result = PrintSettingsPlanner.ScaleIndexToScaleToFit(2);

        result.FitToPagesWide.Should().Be(1);
        result.FitToPagesTall.Should().BeNull();
        result.ScalePercent.Should().BeNull();
    }

    [Fact]
    public void ScaleIndexToScaleToFit_Index3_FitsAllRowsOnOnePage()
    {
        var result = PrintSettingsPlanner.ScaleIndexToScaleToFit(3);

        result.FitToPagesWide.Should().BeNull();
        result.FitToPagesTall.Should().Be(1);
        result.ScalePercent.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ScaleIndexRoundTrip_IsSymmetric(int index)
    {
        var stf = PrintSettingsPlanner.ScaleIndexToScaleToFit(index);

        PrintSettingsPlanner.ScaleToFitToIndex(stf).Should().Be(index);
    }

    [Theory]
    [InlineData(0, PrintPreviewSidesMode.OneSided)]
    [InlineData(1, PrintPreviewSidesMode.TwoSidedLongEdge)]
    [InlineData(2, PrintPreviewSidesMode.TwoSidedShortEdge)]
    [InlineData(99, PrintPreviewSidesMode.OneSided)]
    public void SidesIndexToMode_MapsAllKnownIndices(int index, PrintPreviewSidesMode expected)
    {
        PrintSettingsPlanner.SidesIndexToMode(index).Should().Be(expected);
    }

    [Theory]
    [InlineData(PrintPreviewSidesMode.OneSided, 0)]
    [InlineData(PrintPreviewSidesMode.TwoSidedLongEdge, 1)]
    [InlineData(PrintPreviewSidesMode.TwoSidedShortEdge, 2)]
    public void SidesModeToIndex_MapsAllModes(PrintPreviewSidesMode mode, int expected)
    {
        PrintSettingsPlanner.SidesModeToIndex(mode).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(500, 500)]
    [InlineData(999, 999)]
    [InlineData(1000, 999)]
    [InlineData(int.MaxValue, 999)]
    public void ClampCopies_ClampsToValidRange(int input, int expected)
    {
        PrintSettingsPlanner.ClampCopies(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, 5, true, 1, 5)]
    [InlineData(2, 4, 5, true, 2, 4)]
    [InlineData(3, null, 5, true, 3, 5)]
    [InlineData(null, 3, 5, true, 1, 3)]
    [InlineData(3, 3, 5, true, 3, 3)]
    [InlineData(0, 3, 5, false, 0, 0)]
    [InlineData(3, 0, 5, false, 0, 0)]
    [InlineData(4, 3, 5, false, 0, 0)]
    [InlineData(1, 6, 5, false, 0, 0)]
    public void TryValidatePageRange_ValidatesAndNormalizes(
        int? fromRaw,
        int? toRaw,
        int totalPages,
        bool expectedResult,
        int expectedFrom,
        int expectedTo)
    {
        var result = PrintSettingsPlanner.TryValidatePageRange(fromRaw, toRaw, totalPages, out var from, out var to);

        result.Should().Be(expectedResult);
        if (expectedResult)
        {
            from.Should().Be(expectedFrom);
            to.Should().Be(expectedTo);
        }
    }

    [Fact]
    public void TryValidatePageRange_ZeroTotalPages_BothNullStillReturnsTrue()
    {
        var result = PrintSettingsPlanner.TryValidatePageRange(null, null, 0, out var from, out var to);

        result.Should().BeTrue();
        from.Should().Be(1);
        to.Should().Be(1);
    }
}
