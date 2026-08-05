using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class AdvancedFilterPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void CreateDefaultListRange_ExpandsSingleCellSelectionToCurrentRegion()
    {
        var sheet = new Sheet(SheetId, "Data");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(SheetId, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(SheetId, 2, 2), new NumberValue(42));

        var selectedCell = new CellAddress(SheetId, 1, 1);

        AdvancedFilterPlanner.CreateDefaultListRange(sheet, new GridRange(selectedCell, selectedCell))
            .Should()
            .Be(new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 2, 2)));
    }

    [Fact]
    public void CreateDefaultListRange_FallsBackToUsedRangeWhenCurrentRegionIsSingleCell()
    {
        var sheet = new Sheet(SheetId, "Data");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(SheetId, 4, 4), new NumberValue(42));

        var selectedCell = new CellAddress(SheetId, 1, 1);

        AdvancedFilterPlanner.CreateDefaultListRange(sheet, new GridRange(selectedCell, selectedCell))
            .Should()
            .Be(new GridRange(
                new CellAddress(SheetId, 1, 1),
                new CellAddress(SheetId, 4, 4)));
    }

    [Fact]
    public void CreatePlan_BuildsDialogResultAndCommandForCopyToHeaderRange()
    {
        var criteriaSheetId = SheetId.New();

        var result = AdvancedFilterPlanner.CreatePlan(
            SheetId,
            listRangeText: "$A$1:$D$20",
            criteriaRangeText: "'Criteria Sheet'!F1:G2",
            copyToRangeText: "R1C10:R1C12",
            outputMode: AdvancedFilterOutputMode.CopyToAnotherLocation,
            uniqueRecordsOnly: true,
            resolveSheetId: sheetName => sheetName == "Criteria Sheet" ? criteriaSheetId : null);

        result.Success.Should().BeTrue();
        result.Error.Should().Be(AdvancedFilterPlanError.None);
        result.InvalidText.Should().BeEmpty();

        result.Plan.Should().NotBeNull();
        var plan = result.Plan!;
        plan.ListRange.Should().Be(new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 20, 4)));
        plan.CriteriaRange.Should().Be(new GridRange(new CellAddress(criteriaSheetId, 1, 6), new CellAddress(criteriaSheetId, 2, 7)));
        plan.OutputMode.Should().Be(AdvancedFilterOutputMode.CopyToAnotherLocation);
        plan.UniqueRecordsOnly.Should().BeTrue();
        plan.HasCopyDestination.Should().BeTrue();
        plan.CopyToCell.Should().Be(new CellAddress(SheetId, 1, 10));
        plan.CopyToRange.Should().Be(new GridRange(new CellAddress(SheetId, 1, 10), new CellAddress(SheetId, 1, 12)));
        plan.CreateCommand().Should().BeOfType<AdvancedFilterCommand>().Which.Label.Should().Be("Advanced Filter");

        AdvancedFilterPlanner.TryCreateDialogResult(result, out var dialogResult).Should().BeTrue();
        dialogResult.Should().Be(new AdvancedFilterDialogResult(
            plan.ListRange,
            plan.CriteriaRange,
            plan.CopyToCell,
            plan.UniqueRecordsOnly,
            plan.CopyToRange));
    }

    [Theory]
    [InlineData("", "F1:G2", AdvancedFilterPlanError.InvalidListRange, "", AdvancedFilterErrorFocusTarget.ListRange)]
    [InlineData("   ", "F1:G2", AdvancedFilterPlanError.InvalidListRange, "", AdvancedFilterErrorFocusTarget.ListRange)]
    [InlineData("bad", "F1:G2", AdvancedFilterPlanError.InvalidListRange, "bad", AdvancedFilterErrorFocusTarget.ListRange)]
    [InlineData("A1", "F1:G2", AdvancedFilterPlanError.ListRangeRequiresDataRows, "A1", AdvancedFilterErrorFocusTarget.ListRange)]
    [InlineData("A1:XFD1048576", "F1:G2", AdvancedFilterPlanError.ListRangeTooLarge, "A1:XFD1048576", AdvancedFilterErrorFocusTarget.ListRange)]
    [InlineData("A1:C5", "", AdvancedFilterPlanError.InvalidCriteriaRange, "", AdvancedFilterErrorFocusTarget.CriteriaRange)]
    [InlineData("A1:C5", "   ", AdvancedFilterPlanError.InvalidCriteriaRange, "", AdvancedFilterErrorFocusTarget.CriteriaRange)]
    [InlineData("A1:C5", "bad", AdvancedFilterPlanError.InvalidCriteriaRange, "bad", AdvancedFilterErrorFocusTarget.CriteriaRange)]
    [InlineData("A1:C5", "F1:G1", AdvancedFilterPlanError.CriteriaRangeRequiresCriteriaRows, "F1:G1", AdvancedFilterErrorFocusTarget.CriteriaRange)]
    [InlineData("A1:C5", "F1:XFD1048576", AdvancedFilterPlanError.CriteriaRangeTooLarge, "F1:XFD1048576", AdvancedFilterErrorFocusTarget.CriteriaRange)]
    public void CreatePlan_ReportsStructuredRequiredRangeErrors(
        string listRangeText,
        string criteriaRangeText,
        AdvancedFilterPlanError expectedError,
        string expectedInvalidText,
        AdvancedFilterErrorFocusTarget expectedFocusTarget)
    {
        var result = AdvancedFilterPlanner.CreatePlan(
            SheetId,
            listRangeText,
            criteriaRangeText,
            copyToRangeText: "",
            outputMode: AdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly: false);

        result.Success.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.Error.Should().Be(expectedError);
        result.InvalidText.Should().Be(expectedInvalidText);
        AdvancedFilterPlanner.FocusTargetForPlanError(result.Error).Should().Be(expectedFocusTarget);
    }

    [Theory]
    [InlineData("", AdvancedFilterPlanError.CopyDestinationRequired, "")]
    [InlineData("   ", AdvancedFilterPlanError.CopyDestinationRequired, "")]
    [InlineData("A8:C9", AdvancedFilterPlanError.InvalidCopyDestinationRange, "A8:C9")]
    [InlineData("A8:XFD8", AdvancedFilterPlanError.CopyDestinationRangeTooLarge, "A8:XFD8")]
    [InlineData("Other!A1", AdvancedFilterPlanError.InvalidCopyDestinationRange, "Other!A1")]
    public void CreatePlan_CopyModeRequiresCurrentSheetSingleRowDestination(
        string copyToRangeText,
        AdvancedFilterPlanError expectedError,
        string expectedInvalidText)
    {
        var result = AdvancedFilterPlanner.CreatePlan(
            SheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToRangeText: copyToRangeText,
            outputMode: AdvancedFilterOutputMode.CopyToAnotherLocation,
            uniqueRecordsOnly: false);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(expectedError);
        result.InvalidText.Should().Be(expectedInvalidText);
        AdvancedFilterPlanner.FocusTargetForPlanError(result.Error).Should().Be(AdvancedFilterErrorFocusTarget.CopyTo);
    }

    [Fact]
    public void CreatePlan_FilterInPlaceIgnoresCopyToText()
    {
        var result = AdvancedFilterPlanner.CreatePlan(
            SheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToRangeText: "NotACell",
            outputMode: AdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly: false);

        result.Success.Should().BeTrue();
        result.Plan!.OutputMode.Should().Be(AdvancedFilterOutputMode.FilterInPlace);
        result.Plan.CopyToCell.Should().BeNull();
        result.Plan.CopyToRange.Should().BeNull();
    }

    [Fact]
    public void CreatePlan_RejectsCopyDestinationOnDifferentSheetThanListRange()
    {
        var currentSheetId = SheetId.New();
        var dataSheetId = SheetId.New();

        var result = AdvancedFilterPlanner.CreatePlan(
            currentSheetId,
            listRangeText: "Data!A1:D20",
            criteriaRangeText: "F1:G2",
            copyToRangeText: "J1",
            outputMode: AdvancedFilterOutputMode.CopyToAnotherLocation,
            uniqueRecordsOnly: false,
            resolveSheetId: sheetName => sheetName == "Data" ? dataSheetId : null);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(AdvancedFilterPlanError.CopyDestinationMustBeOnListSheet);
        result.InvalidText.Should().Be("J1");
        AdvancedFilterPlanner.FocusTargetForPlanError(result.Error).Should().Be(AdvancedFilterErrorFocusTarget.CopyTo);
    }

    [Fact]
    public void TryCreatePlan_ReturnsPlanAndParseResult()
    {
        AdvancedFilterPlanner.TryCreatePlan(
                SheetId,
                listRangeText: "A1:D20",
                criteriaRangeText: "F1:G2",
                copyToRangeText: "J1",
                outputMode: AdvancedFilterOutputMode.CopyToAnotherLocation,
                uniqueRecordsOnly: true,
                out var plan,
                out var result)
            .Should()
            .BeTrue();

        result.Success.Should().BeTrue();
        plan.Should().Be(result.Plan);
        plan.CopyToCell.Should().Be(new CellAddress(SheetId, 1, 10));
    }

    [Theory]
    [InlineData("A1:C10", true, "A1", "C10")]
    [InlineData(" B2 ", true, "B2", "B2")]
    [InlineData("$D$4:$F$6", true, "D4", "F6")]
    [InlineData("R4C4:R4C6", true, "D4", "F4")]
    [InlineData("Missing!A1:B2", false, "", "")]
    [InlineData("A1:B2:C3", false, "", "")]
    public void TryParseRange_ParsesAdvancedFilterReferences(
        string input,
        bool expected,
        string expectedStart,
        string expectedEnd)
    {
        var result = AdvancedFilterPlanner.TryParseRange(
            SheetId,
            input,
            sheetName => string.Equals(sheetName, "Sheet1", StringComparison.OrdinalIgnoreCase) ? SheetId : null,
            out var range);

        result.Should().Be(expected);
        if (expected)
        {
            range.Start.ToA1().Should().Be(expectedStart);
            range.End.ToA1().Should().Be(expectedEnd);
        }
    }

    [Fact]
    public void TryParseRange_ParsesSheetQualifiedRange()
    {
        AdvancedFilterPlanner.TryParseRange(
            SheetId.New(),
            "Sheet1!A1:B2",
            sheetName => string.Equals(sheetName, "Sheet1", StringComparison.OrdinalIgnoreCase) ? SheetId : null,
            out var range).Should().BeTrue();

        range.Start.Sheet.Should().Be(SheetId);
        range.Start.ToA1().Should().Be("A1");
        range.End.ToA1().Should().Be("B2");
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("   ", true, null)]
    [InlineData("$D$4", true, "D4")]
    [InlineData("R4C4", true, "D4")]
    [InlineData("A1:B2", false, null)]
    [InlineData("bad", false, null)]
    public void TryParseCopyDestination_AllowsBlankOrSingleCellAddress(
        string input,
        bool expected,
        string? expectedAddress)
    {
        var result = AdvancedFilterPlanner.TryParseCopyDestination(input, SheetId, out var address);

        result.Should().Be(expected);
        address?.ToA1().Should().Be(expectedAddress);
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("   ", true, null)]
    [InlineData("$D$4", true, "D4:D4")]
    [InlineData("R4C4", true, "D4:D4")]
    [InlineData("D4:F4", true, "D4:F4")]
    [InlineData("D4:F5", false, null)]
    [InlineData("Other!D4", false, null)]
    public void TryParseCopyDestinationRange_AllowsBlankCellOrSingleRowHeaderRangeOnly(
        string input,
        bool expected,
        string? expectedReference)
    {
        var parsed = AdvancedFilterPlanner.TryParseCopyDestinationRange(input, SheetId, out var range);

        parsed.Should().Be(expected);
        range?.ToString().Should().Be(expectedReference);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("Y", true)]
    [InlineData("true", true)]
    [InlineData("no", false)]
    [InlineData("false", false)]
    [InlineData("", false)]
    public void ParseUniqueRecordsOnly_MatchesExcelStyleAffirmativePromptAliases(string input, bool expected)
    {
        AdvancedFilterPlanner.ParseUniqueRecordsOnly(input).Should().Be(expected);
    }

    [Fact]
    public void CreateRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        AdvancedFilterPlanner.CreateRangeSelectionRequest(
                AdvancedFilterRangeSelectionTarget.CriteriaRange,
                " E1:F4 ")
            .Should()
            .Be(new AdvancedFilterRangeSelectionRequest(
                AdvancedFilterRangeSelectionTarget.CriteriaRange,
                "E1:F4",
                CollapseDialog: true));
    }
}
