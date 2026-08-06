using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ManageConditionalFormatsPlannerTests
{
    [Fact]
    public void SourceOwnership_UsesSharedStructuredTableOverlapPolicy()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryFileLocator.FindDirectory(
                "src",
                "FreeX.App.Presentation",
                "ConditionalFormatting"),
            "ManageConditionalFormatsPlanner.cs"));

        source.Should().Contain("StructuredTableSelectionPlanner.FindOverlappingTableRange(sheet, selectionRange)");
        source.Should().NotContain("foreach (var table in sheet.StructuredTables)");
    }

    [Fact]
    public void CreateDialogPlan_DefaultsToSelectionAndIncludesIntersectingTableScope()
    {
        var sheet = new Workbook("Book").AddSheet("Sheet1");
        var tableRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 6, 4));
        var selection = new GridRange(new CellAddress(sheet.Id, 3, 3), new CellAddress(sheet.Id, 3, 3));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "Sales", DisplayName = "Sales", Range = tableRange });

        var plan = ManageConditionalFormatsPlanner.CreateDialogPlan(sheet, selection);

        plan.DefaultScope.Should().Be(ManageConditionalFormatScope.Selection);
        plan.ScopeOptions.Select(option => option.Scope).Should().Equal(
            ManageConditionalFormatScope.Sheet,
            ManageConditionalFormatScope.Table,
            ManageConditionalFormatScope.Selection);
        plan.ScopeOptions.Single(option => option.Scope == ManageConditionalFormatScope.Table).Range.Should().Be(tableRange);
        plan.DefaultScopeOption.LabelKey.Should().Be(ManageConditionalFormatsPlanner.ScopeCurrentSelectionKey);
        plan.DefaultNewRuleRange.Should().Be(selection);
    }

    [Fact]
    public void DefaultNewRuleRange_UsesFirstRuleThenA1WhenNoSelectionExists()
    {
        var sheet = new Workbook("Book").AddSheet("Sheet1");
        var firstRule = CreateRule(sheet.Id, 4, 3, 1);
        sheet.ConditionalFormats.Add(firstRule);

        ManageConditionalFormatsPlanner.DefaultNewRuleRange(sheet, selection: null)
            .Should().Be(firstRule.AppliesTo);

        var emptySheet = new Workbook("Book").AddSheet("Sheet1");
        ManageConditionalFormatsPlanner.DefaultNewRuleRange(emptySheet, selection: null)
            .Should().Be(new GridRange(new CellAddress(emptySheet.Id, 1, 1), new CellAddress(emptySheet.Id, 1, 1)));
    }

    [Fact]
    public void AppliesToRangeText_RoundTripsExcelAbsoluteReferencesAndRequestText()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 5, 4));
        var ruleId = Guid.NewGuid();

        ManageConditionalFormatsPlanner.FormatAppliesToRange(range).Should().Be("$B$2:$D$5");
        ManageConditionalFormatsPlanner.TryParseAppliesToText(" $B$2:$D$5 ", sheetId, out var parsed)
            .Should().BeTrue();
        parsed.Should().Be(range);
        ManageConditionalFormatsPlanner.CreateAppliesToRangeSelectionRequest(ruleId, " $B$2:$D$5 ")
            .Should().Be(new ConditionalFormatAppliesToRangeSelectionRequest(ruleId, "$B$2:$D$5", CollapseDialog: true));
    }

    [Fact]
    public void DescribeRule_ReturnsResourcePlanForIconSetFlags()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "5Arrows",
            IconSetShowValue = false,
            IconSetReverse = true
        };
        rule.IconOverrides.Add(new CfIconOverride("3TrafficLights1", 0));

        var description = ManageConditionalFormatsPlanner.DescribeRule(rule);

        description.ResourceKey.Should().Be("ManageConditionalFormats_RuleIconSetWithFlags");
        description.Arguments[0].Should().Be(new LiteralDescriptionArgument("5Arrows"));
        description.Arguments[1]
            .Should().BeOfType<ResourceListDescriptionArgument>()
            .Which.ResourceKeys.Should().Equal(
                "ManageConditionalFormats_IconFlagReverse",
                "ManageConditionalFormats_IconFlagIconsOnly",
                "ManageConditionalFormats_IconFlagCustomIcons");
    }

    [Fact]
    public void DescribeRule_UsesResourceArgumentForLocalizedDatePeriod()
    {
        var description = ManageConditionalFormatsPlanner.DescribeRule(new ConditionalFormat
        {
            RuleType = CfRuleType.DateOccurring,
            DateOccurringPeriod = "last7Days"
        });

        description.ResourceKey.Should().Be("ManageConditionalFormats_RuleDateOccurring");
        description.Arguments.Should().ContainSingle()
            .Which.Should().Be(new ResourceDescriptionArgument("ManageConditionalFormats_DateLast7Days"));
    }

    [Fact]
    public void CreatePreviewPlan_UsesPortableFillAndTextStyle()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = new RgbColor(99, 190, 123),
            MidColor = new RgbColor(255, 235, 132),
            MaxColor = new RgbColor(248, 105, 107),
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(12, 34, 56),
                Bold = true,
                Underline = true
            }
        };

        var preview = ManageConditionalFormatsPlanner.CreatePreviewPlan(rule);

        preview.SampleTextKey.Should().Be(ManageConditionalFormatsPlanner.FormatPreviewSampleKey);
        preview.Fill.IsGradient.Should().BeTrue();
        preview.Fill.Stops.Should().Equal(
            new PresentationRgb(99, 190, 123),
            new PresentationRgb(255, 235, 132),
            new PresentationRgb(248, 105, 107));
        preview.Foreground.Should().Be(new PresentationRgb(12, 34, 56));
        preview.Bold.Should().BeTrue();
        preview.Underline.Should().BeTrue();
        preview.Italic.Should().BeFalse();
        preview.Strikethrough.Should().BeFalse();
    }

    [Fact]
    public void StopIfTrueTextKey_ReturnsResourceKeyOnlyForEnabledRules()
    {
        ManageConditionalFormatsPlanner.StopIfTrueTextKey(new ConditionalFormat { StopIfTrue = true })
            .Should().Be(ManageConditionalFormatsPlanner.StopIfTrueEnabledKey);
        ManageConditionalFormatsPlanner.StopIfTrueTextKey(new ConditionalFormat())
            .Should().BeNull();
    }

    [Fact]
    public void DuplicateRule_InsertsDeepCopyBelowSelectedRuleWithNewIdentity()
    {
        var sheetId = SheetId.New();
        var first = CreateRule(sheetId, 1, 1, 1);
        var selected = CreateRule(sheetId, 2, 1, 2);
        selected.RuleType = CfRuleType.IconSet;
        selected.IconSetStyle = "5Arrows";
        selected.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "25"));
        selected.IconOverrides.Add(new CfIconOverride("3TrafficLights1", 0));
        selected.FormatIfTrue = new CellStyle { Bold = true, FillColor = new CellColor(1, 2, 3) };
        selected.NativeChildXmls =
        [
            """<extLst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><ext uri="{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"><x14:id xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">{11111111-2222-3333-4444-555555555555}</x14:id></ext><ext uri="{FUTURE}" /></extLst>"""
        ];
        var duplicateId = Guid.NewGuid();

        var result = ManageConditionalFormatsPlanner.DuplicateRule([first, selected], selected.Id, duplicateId);

        result.Select(rule => rule.Id).Should().Equal(first.Id, selected.Id, duplicateId);
        result.Select(rule => rule.Priority).Should().Equal(1, 2, 3);

        var duplicate = result[2];
        duplicate.AppliesTo.Should().Be(selected.AppliesTo);
        duplicate.RuleType.Should().Be(CfRuleType.IconSet);
        duplicate.IconSetThresholds.Should().Equal(selected.IconSetThresholds);
        duplicate.IconOverrides.Should().Equal(selected.IconOverrides);
        duplicate.FormatIfTrue.Should().NotBeSameAs(selected.FormatIfTrue);
        duplicate.FormatIfTrue.Should().Be(selected.FormatIfTrue);
        duplicate.NativeChildXmls.Should().ContainSingle(xml => xml.Contains("{FUTURE}", StringComparison.Ordinal));
        duplicate.NativeChildXmls.Should().NotContain(xml => xml.Contains("11111111-2222-3333-4444-555555555555", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplaceRule_PreservesRuleSlotAndReprioritizesEditedRule()
    {
        var sheetId = SheetId.New();
        var first = CreateRule(sheetId, 1, 1, 1);
        var second = CreateRule(sheetId, 2, 1, 2);
        var edited = CreateRule(sheetId, 5, 3, 99, second.Id);
        edited.StopIfTrue = true;

        var result = ManageConditionalFormatsPlanner.ReplaceRule([first, second], edited);

        result.Select(rule => rule.Id).Should().Equal(first.Id, second.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2);
        result[1].AppliesTo.Should().Be(edited.AppliesTo);
        result[1].StopIfTrue.Should().BeTrue();
    }

    [Fact]
    public void DeleteRule_RemovesOnlyMatchingRuleAndReassignsPriorities()
    {
        var sheetId = SheetId.New();
        var first = CreateRule(sheetId, 1, 1, 5);
        var second = CreateRule(sheetId, 2, 1, 8);
        var third = CreateRule(sheetId, 3, 1, 13);

        var result = ManageConditionalFormatsPlanner.DeleteRule([first, second, third], second.Id);

        result.Select(rule => rule.Id).Should().Equal(first.Id, third.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2);
    }

    [Fact]
    public void MoveRule_ReordersOneStepAndReassignsPriorities()
    {
        var sheetId = SheetId.New();
        var first = CreateRule(sheetId, 1, 1, 1);
        var second = CreateRule(sheetId, 2, 1, 2);
        var third = CreateRule(sheetId, 3, 1, 3);

        var movedDown = ManageConditionalFormatsPlanner.MoveRule(
            [first, second, third],
            first.Id,
            ConditionalFormatRuleMoveDirection.Down);
        var movedBackUp = ManageConditionalFormatsPlanner.MoveRule(
            movedDown,
            first.Id,
            ConditionalFormatRuleMoveDirection.Up);

        movedDown.Select(rule => rule.Id).Should().Equal(second.Id, first.Id, third.Id);
        movedDown.Select(rule => rule.Priority).Should().Equal(1, 2, 3);
        movedBackUp.Select(rule => rule.Id).Should().Equal(first.Id, second.Id, third.Id);
    }

    [Fact]
    public void ApplyRuleRange_UpdatesOnlyTargetRuleRange()
    {
        var sheetId = SheetId.New();
        var first = CreateRule(sheetId, 1, 1, 1);
        var second = CreateRule(sheetId, 2, 1, 2);
        var newRange = new GridRange(new CellAddress(sheetId, 4, 4), new CellAddress(sheetId, 8, 6));

        var result = ManageConditionalFormatsPlanner.ApplyRuleRange([first, second], second.Id, newRange);

        result.Select(rule => rule.Id).Should().Equal(first.Id, second.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2);
        result[0].AppliesTo.Should().Be(first.AppliesTo);
        result[1].AppliesTo.Should().Be(newRange);
    }

    [Fact]
    public void BuildResultRules_FilteredScopeKeepsEditedRulesInOriginalVisibleSlots()
    {
        var sheetId = SheetId.New();
        var firstVisible = CreateRule(sheetId, 2, 1, 1);
        var hidden = CreateRule(sheetId, 8, 1, 2);
        var secondVisible = CreateRule(sheetId, 3, 1, 3);
        var editedSecond = CreateRule(sheetId, 20, 4, 7, secondVisible.Id);
        var selection = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 1));

        var result = ManageConditionalFormatsPlanner.BuildResultRules(
            [firstVisible, hidden, secondVisible],
            selection,
            filterToSelection: true,
            [editedSecond, firstVisible]);

        result.Select(rule => rule.Id).Should().Equal(secondVisible.Id, hidden.Id, firstVisible.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2, 3);
        result[0].AppliesTo.Should().Be(editedSecond.AppliesTo);
    }

    private static ConditionalFormat CreateRule(
        SheetId sheetId,
        uint row,
        uint col,
        int priority,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            AppliesTo = new GridRange(new CellAddress(sheetId, row, col), new CellAddress(sheetId, row, col)),
            Priority = priority,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "1",
            FormatIfTrue = new CellStyle { Italic = true }
        };
}
