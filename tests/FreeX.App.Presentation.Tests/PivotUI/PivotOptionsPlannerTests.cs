using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotOptionsPlannerTests
{
    [Fact]
    public void DialogMetrics_MatchSharedVisualEvidenceContract()
    {
        PivotOptionsPlanner.DialogWidth.Should().Be(520);
        PivotOptionsPlanner.DialogMinHeight.Should().Be(500);
        PivotOptionsPlanner.LayoutAndFormatCaptureHeight.Should().Be(676);
        PivotOptionsPlanner.LayoutAndFormatAvaloniaSpacerHeight.Should().Be(57);
    }

    [Fact]
    public void ReportLayoutRoundTrip_FindsAndResolvesIndex()
    {
        var index = PivotOptionsPlanner.FindReportLayoutIndex(PivotReportLayout.Outline);
        PivotOptionsPlanner.ReportLayoutFromIndex(index).Should().Be(PivotReportLayout.Outline);

        PivotOptionsPlanner.ReportLayoutFromIndex(-1).Should().Be(PivotOptionsPlanner.ReportLayouts[0].Value);
        PivotOptionsPlanner.ReportLayoutFromIndex(99).Should().Be(PivotOptionsPlanner.ReportLayouts[^1].Value);
    }

    [Theory]
    [InlineData(PivotReportLayout.Compact, "Compact")]
    [InlineData(PivotReportLayout.Outline, "Outline")]
    [InlineData(PivotReportLayout.Tabular, "Tabular")]
    [InlineData((PivotReportLayout)99, "Tabular")]
    public void GetReportLayoutLabel_UsesSharedCatalog(PivotReportLayout layout, string expected)
    {
        PivotOptionsPlanner.GetReportLayoutLabel(layout).Should().Be(expected);
    }

    [Fact]
    public void AvaloniaPivotTabs_DelegateReportLayoutLabelsToSharedCatalog()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.PivotTabs.cs"));

        source.Should().Contain("PivotOptionsPlanner.GetReportLayoutLabel(next)");
        source.Should().NotContain("private static string FormatReportLayout(");
    }

    [Fact]
    public void SubtotalPlacementRoundTrip_FindsAndResolvesIndex()
    {
        var index = PivotOptionsPlanner.FindSubtotalPlacementIndex(PivotSubtotalPlacement.Top);
        PivotOptionsPlanner.SubtotalPlacementFromIndex(index).Should().Be(PivotSubtotalPlacement.Top);
    }

    [Fact]
    public void PageFieldLayoutChoices_RoundTripAcrossIndexAndLabelBindings()
    {
        PivotOptionsPlanner.PageFieldLayouts.Select(option => option.Label).Should().Equal(
            "Down, then over",
            "Over, then down");
        PivotOptionsPlanner.FindPageFieldLayoutIndex(false).Should().Be(0);
        PivotOptionsPlanner.FindPageFieldLayoutIndex(true).Should().Be(1);
        PivotOptionsPlanner.PageFieldLayoutFromIndex(-1).Should().BeFalse();
        PivotOptionsPlanner.PageFieldLayoutFromIndex(99).Should().BeTrue();
        PivotOptionsPlanner.GetPageFieldLayoutLabel(true).Should().Be("Over, then down");
        PivotOptionsPlanner.PageFieldLayoutFromLabel("over, THEN down").Should().BeTrue();
        PivotOptionsPlanner.PageFieldLayoutFromLabel("unknown").Should().BeFalse();
    }

    [Theory]
    [InlineData(null, 0, "Automatic")]
    [InlineData(0, 1, "None")]
    [InlineData(42, 2, "Maximum")]
    public void MissingItemsChoices_RoundTripAcrossIndexAndLabelBindings(
        int? value,
        int expectedIndex,
        string expectedLabel)
    {
        PivotOptionsPlanner.FindMissingItemsLimitIndex(value).Should().Be(expectedIndex);
        PivotOptionsPlanner.GetMissingItemsLimitLabel(value).Should().Be(expectedLabel);
        PivotOptionsPlanner.MissingItemsLimitFromIndex(expectedIndex)
            .Should().Be(PivotOptionsPlanner.NormalizeMissingItemsLimit(value));
        PivotOptionsPlanner.MissingItemsLimitFromLabel(expectedLabel.ToLowerInvariant())
            .Should().Be(PivotOptionsPlanner.NormalizeMissingItemsLimit(value));
    }

    [Fact]
    public void Capture_ReadsTotalsLayoutAndDisplayValues()
    {
        var pivot = new PivotTableModel
        {
            Name = "P",
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top,
            ReportLayout = PivotReportLayout.Outline,
            CompactRowLabelIndent = 3,
            RepeatItemLabels = false,
            BlankLineAfterItems = true,
            MergeAndCenterLabels = true,
        };
        pivot.ShowRowGrandTotals = true;
        pivot.ShowColumnGrandTotals = false;

        var values = PivotOptionsPlanner.Capture(pivot);

        values.ShowRowGrandTotals.Should().BeTrue();
        values.ShowColumnGrandTotals.Should().BeFalse();
        values.ShowSubtotals.Should().BeTrue();
        values.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top);
        values.ReportLayout.Should().Be(PivotReportLayout.Outline);
        values.CompactRowLabelIndent.Should().Be(3);
        values.RepeatItemLabels.Should().BeFalse();
        values.BlankLineAfterItems.Should().BeTrue();
        values.MergeAndCenterLabels.Should().BeTrue();
    }

    [Theory]
    [InlineData("5", true, 5)]
    [InlineData("0", true, 0)]
    [InlineData("15", true, 15)]
    [InlineData("16", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("x", false, 0)]
    [InlineData("", false, 0)]
    public void TryParseCompactRowLabelIndent_ValidatesRange(string text, bool expectedOk, int expectedIndent)
    {
        var ok = PivotOptionsPlanner.TryParseCompactRowLabelIndent(text, out var indent, out var error);
        ok.Should().Be(expectedOk);
        if (expectedOk)
        {
            indent.Should().Be(expectedIndent);
            error.Should().BeNull();
        }
        else
        {
            error.Should().Be(PivotOptionsPlanner.CompactIndentRangeMessage);
        }
    }

    [Fact]
    public void CreateResult_BuildsValuesAndClampsIndent()
    {
        var result = PivotOptionsPlanner.CreateResult(
            showRowGrandTotals: false,
            showColumnGrandTotals: true,
            showSubtotals: true,
            subtotalPlacementIndex: PivotOptionsPlanner.FindSubtotalPlacementIndex(PivotSubtotalPlacement.Top),
            reportLayoutIndex: PivotOptionsPlanner.FindReportLayoutIndex(PivotReportLayout.Compact),
            compactRowLabelIndent: 99,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            mergeAndCenterLabels: true);

        result.ShowRowGrandTotals.Should().BeFalse();
        result.ShowColumnGrandTotals.Should().BeTrue();
        result.ShowSubtotals.Should().BeTrue();
        result.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top);
        result.ReportLayout.Should().Be(PivotReportLayout.Compact);
        result.CompactRowLabelIndent.Should().Be(PivotOptionsPlanner.MaxCompactRowLabelIndent);
        result.RepeatItemLabels.Should().BeTrue();
        result.MergeAndCenterLabels.Should().BeTrue();
    }

    [Fact]
    public void CreateDialogValues_NormalizesFullDialogResult()
    {
        var result = PivotOptionsPlanner.CreateDialogValues(
            showRowGrandTotals: true,
            showColumnGrandTotals: false,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Top,
            repeatItemLabels: false,
            blankLineAfterItems: true,
            styleName: "  PivotStyleMedium9  ",
            showRowHeaders: false,
            showColumnHeaders: true,
            showRowStripes: true,
            showColumnStripes: false,
            reportLayout: PivotReportLayout.Outline,
            emptyValueText: "  N/A  ",
            refreshOnOpen: true,
            saveSourceData: false,
            enableRefresh: false,
            preserveSourceSortFilter: false,
            missingItemsLimit: 42,
            altTextTitle: "  Sales pivot ",
            altTextDescription: " Quarterly sales summary ",
            compactRowLabelIndent: 99,
            showExpandCollapseButtons: false,
            autofitColumnsOnUpdate: false,
            preserveFormattingOnUpdate: false,
            showFieldHeaders: false,
            showContextualTooltips: false,
            showPropertiesInTooltips: false,
            showClassicLayout: true,
            mergeAndCenterLabels: true,
            pageOverThenDown: true,
            pageWrap: 999,
            errorValueText: "  #VALUE!  ",
            enableDrill: false);

        result.StyleName.Should().Be("PivotStyleMedium9");
        result.EmptyValueText.Should().Be("N/A");
        result.ErrorValueText.Should().Be("#VALUE!");
        result.AltTextTitle.Should().Be("Sales pivot");
        result.AltTextDescription.Should().Be("Quarterly sales summary");
        result.MissingItemsLimit.Should().Be(PivotOptionsPlanner.MaxMissingItemsLimit);
        result.CompactRowLabelIndent.Should().Be(PivotOptionsPlanner.MaxCompactRowLabelIndent);
        result.PageWrap.Should().Be(PivotOptionsPlanner.MaxPageWrap);
        result.ShowExpandCollapseButtons.Should().BeFalse();
        result.AutofitColumnsOnUpdate.Should().BeFalse();
        result.PreserveFormattingOnUpdate.Should().BeFalse();
        result.ShowFieldHeaders.Should().BeFalse();
        result.ShowClassicLayout.Should().BeTrue();
        result.MergeAndCenterLabels.Should().BeTrue();
        result.PageOverThenDown.Should().BeTrue();
        result.EnableDrill.Should().BeFalse();
    }

    [Fact]
    public void CaptureDialogValues_ReadsPivotAndConnectedCacheOptions()
    {
        var pivot = new PivotTableModel
        {
            Name = "P",
            CacheId = 7,
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top,
            ReportLayout = PivotReportLayout.Compact,
            StyleName = "PivotStyleDark4",
            EmptyValueText = "-",
            ErrorCaption = "(error)",
            CompactRowLabelIndent = 5,
            PageOverThenDown = true,
            PageWrap = 2,
            PrintExpandCollapseButtons = true,
            EnableDrill = false
        };
        pivot.ShowRowGrandTotals = false;
        pivot.ShowColumnGrandTotals = true;

        var cache = new PivotCacheModel
        {
            CacheId = 7,
            RefreshOnLoad = true,
            SaveData = false,
            EnableRefresh = false,
            PreserveSourceSortFilter = false,
            MissingItemsLimit = 0
        };

        var values = PivotOptionsPlanner.CaptureDialogValues(pivot, cache);

        values.ShowRowGrandTotals.Should().BeFalse();
        values.ShowColumnGrandTotals.Should().BeTrue();
        values.StyleName.Should().Be("PivotStyleDark4");
        values.EmptyValueText.Should().Be("-");
        values.ErrorValueText.Should().Be("(error)");
        values.RefreshOnOpen.Should().BeTrue();
        values.SaveSourceData.Should().BeFalse();
        values.EnableRefresh.Should().BeFalse();
        values.PreserveSourceSortFilter.Should().BeFalse();
        values.MissingItemsLimit.Should().Be(0);
        values.PrintExpandCollapseButtons.Should().BeTrue();
        values.CompactRowLabelIndent.Should().Be(5);
        values.PageOverThenDown.Should().BeTrue();
        values.PageWrap.Should().Be(2);
        values.EnableDrill.Should().BeFalse();
    }

    [Theory]
    [InlineData("255", true, 255)]
    [InlineData("256", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("x", false, 0)]
    public void TryParsePageWrap_ValidatesRange(string text, bool expectedOk, int expectedPageWrap)
    {
        var ok = PivotOptionsPlanner.TryParsePageWrap(text, out var pageWrap, out var error);
        ok.Should().Be(expectedOk);
        pageWrap.Should().Be(expectedPageWrap);
        if (expectedOk)
            error.Should().BeNull();
        else
            error.Should().Be(PivotOptionsPlanner.PageWrapRangeMessage);
    }
}
