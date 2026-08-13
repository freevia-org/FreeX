using FluentAssertions;

using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ConditionalFormatCommandPlannerTests
{
    [Fact]
    public void PlanApplyPreset_NormalizesInputAndOwnsFeedbackAndRefresh()
    {
        var workbook = CreateWorkbook(out var sheet, out _);
        var range = Range(sheet.Id, 2, 3, 6, 3);
        var plan = ConditionalFormatCommandPlanner.PlanApplyPreset(
            [sheet.Id],
            [range],
            ConditionalFormatPreset.HighlightGreaterThan,
            " 42 ");

        plan.Command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var rule = sheet.ConditionalFormats.Should().ContainSingle().Which;
        rule.RuleType.Should().Be(CfRuleType.CellValue);
        rule.Value1.Should().Be("42");
        plan.CommandLabel.Should().Be(ConditionalFormatCommandPlanner.CommandLabel);
        plan.SuccessStatus.ResourceKey.Should().Be("InsertLoc_CfAppliedPreset");
        plan.SuccessStatus.Arguments.Should().Equal(
            ConditionalFormatPresetFactory.DisplayName(ConditionalFormatPreset.HighlightGreaterThan),
            "C2:C6");
        plan.FailureResourceKey.Should().Be("InsertLoc_CfFailed");
        plan.RefreshPolicy.Should().Be(ConditionalFormatStateRefreshPolicy.WorksheetVisualState);
    }

    [Fact]
    public void PlanApplyRule_FansOutRangesAndSheetsWithIndependentRuleIds()
    {
        var workbook = CreateWorkbook(out var primary, out var grouped);
        var firstRange = Range(primary.Id, 1, 1, 2, 1);
        var secondRange = Range(primary.Id, 4, 2, 5, 2);
        var source = ConditionalFormatPresetFactory.BuildRule(
            ConditionalFormatPreset.ColorScale,
            firstRange);
        var plan = ConditionalFormatCommandPlanner.PlanApplyRule(
            [primary.Id, grouped.Id],
            [firstRange, secondRange],
            source);

        plan.Command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        primary.ConditionalFormats.Should().HaveCount(2);
        grouped.ConditionalFormats.Should().HaveCount(2);
        primary.ConditionalFormats[0].Id.Should().Be(source.Id);
        workbook.Sheets
            .SelectMany(sheet => sheet.ConditionalFormats)
            .Select(rule => rule.Id)
            .Should().OnlyHaveUniqueItems();
        primary.ConditionalFormats.Select(rule => rule.AppliesTo)
            .Should().Equal(firstRange, secondRange);
        grouped.ConditionalFormats.Select(rule => rule.AppliesTo)
            .Should().Equal(
                GroupedSheetRangePlanner.RemapRangeToSheet(firstRange, grouped.Id),
                GroupedSheetRangePlanner.RemapRangeToSheet(secondRange, grouped.Id));
    }

    [Fact]
    public void PlanReplaceAll_PreservesPrimaryIdentityAndReidentifiesGroupedCopies()
    {
        var workbook = CreateWorkbook(out var primary, out var grouped);
        var source = ConditionalFormatPresetFactory.BuildRule(
            ConditionalFormatPreset.IconSet,
            Range(primary.Id, 3, 2, 7, 2));
        source.AdditionalRanges = [Range(primary.Id, 3, 4, 7, 4)];
        var plan = ConditionalFormatCommandPlanner.PlanReplaceAll(
            [primary.Id, grouped.Id],
            primary.Id,
            [source]);

        plan.Command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var primaryRule = primary.ConditionalFormats.Should().ContainSingle().Which;
        var groupedRule = grouped.ConditionalFormats.Should().ContainSingle().Which;
        primaryRule.Id.Should().Be(source.Id);
        groupedRule.Id.Should().NotBe(source.Id);
        groupedRule.AppliesTo.Start.Sheet.Should().Be(grouped.Id);
        groupedRule.AdditionalRanges.Should().ContainSingle()
            .Which.Start.Sheet.Should().Be(grouped.Id);
        primaryRule.Should().NotBeSameAs(source);
        plan.CommandLabel.Should().Be(ConditionalFormatCommandPlanner.ManageRulesCommandLabel);
        plan.SuccessStatus.ResourceKey.Should().Be("InsertLoc_CfManageRulesApplied");
    }

    [Fact]
    public void PlanClear_RemapsEveryTargetAndUsesOnePortableRefreshContract()
    {
        var workbook = CreateWorkbook(out var primary, out var grouped);
        var range = Range(primary.Id, 2, 2, 4, 2);
        primary.ConditionalFormats.Add(ConditionalFormatPresetFactory.BuildRule(
            ConditionalFormatPreset.DataBar,
            range));
        grouped.ConditionalFormats.Add(ConditionalFormatPresetFactory.BuildRule(
            ConditionalFormatPreset.DataBar,
            GroupedSheetRangePlanner.RemapRangeToSheet(range, grouped.Id)));
        var plan = ConditionalFormatCommandPlanner.PlanClear(
            [primary.Id, grouped.Id],
            [range]);

        plan.Command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        primary.ConditionalFormats.Should().BeEmpty();
        grouped.ConditionalFormats.Should().BeEmpty();
        plan.CommandLabel.Should().Be(ConditionalFormatCommandPlanner.ClearRulesCommandLabel);
        plan.SuccessStatus.ResourceKey.Should().Be("InsertLoc_CfCleared");
        plan.SuccessStatus.Arguments.Should().Equal("B2:B4");
        plan.RefreshPolicy.Should().Be(ConditionalFormatStateRefreshPolicy.WorksheetVisualState);
    }

    [Fact]
    public void ManageSession_CreateApplyPlanUsesBufferedRulesWithoutMutatingSource()
    {
        var workbook = CreateWorkbook(out var sheet, out _);
        var source = ConditionalFormatPresetFactory.BuildRule(
            ConditionalFormatPreset.DataBar,
            Range(sheet.Id, 1, 1, 3, 1));
        sheet.ConditionalFormats.Add(source);
        var session = new ManageConditionalFormatsSession(
            sheet.ConditionalFormats,
            scope: null,
            ManageConditionalFormatsWorkingCopyPolicy.FullSheet);
        session.Delete(source.Id).Should().BeTrue();

        var plan = session.CreateApplyPlan([sheet.Id], sheet.Id);

        sheet.ConditionalFormats.Should().ContainSingle();
        plan.Command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.ConditionalFormats.Should().BeEmpty();
    }

    private static Workbook CreateWorkbook(out Sheet primary, out Sheet grouped)
    {
        var workbook = new Workbook("Book");
        primary = workbook.AddSheet("Sheet1");
        grouped = workbook.AddSheet("Sheet2");
        return workbook;
    }

    private static GridRange Range(
        SheetId sheetId,
        uint startRow,
        uint startColumn,
        uint endRow,
        uint endColumn) =>
        new(
            new CellAddress(sheetId, startRow, startColumn),
            new CellAddress(sheetId, endRow, endColumn));

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
