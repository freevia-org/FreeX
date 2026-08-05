using FluentAssertions;

using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the non-UI glue backing the Avalonia conditional-format editor / presets / manage
/// flow: building a Core <see cref="ConditionalFormat"/> from the portable schema input for each rule
/// type, mapping it onto the add command, the quick-preset → rule construction, and the manage
/// list → delete/edit command. The apply path is exercised end-to-end through a real
/// <see cref="WorkbookSession"/> (the same command path the shell uses); no running UI is required.
/// </summary>
public sealed class ConditionalFormatRuleBuilderTests
{
    // ── Build: per-rule-type construction from schema input ──────────────────

    [Fact]
    public void Build_CellValue_SetsOperatorAndValuesAndHighlightStyle()
    {
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.Between,
            Value1 = "10",
            Value2 = "20",
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(CfRuleType.CellValue);
        rule.Operator.Should().Be(CfOperator.Between);
        rule.Value1.Should().Be("10");
        rule.Value2.Should().Be("20");
        rule.FormatIfTrue.Should().NotBeNull();
        rule.FormatIfTrue!.FillColor.Should().Be(new CellColor(255, 199, 206));
    }

    [Fact]
    public void Build_Formula_StripsLeadingEquals()
    {
        var input = new CfRuleInput { RuleType = CfRuleType.Formula, Formula = "=A1>10" };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(CfRuleType.Formula);
        rule.FormulaText.Should().Be("A1>10");
        rule.FormatIfTrue.Should().NotBeNull();
    }

    [Fact]
    public void Build_Top10_ParsesRankAndPercentFlag()
    {
        var input = new CfRuleInput { RuleType = CfRuleType.Top10, Rank = "5", IsPercent = true };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(CfRuleType.Top10);
        rule.TopBottomRank.Should().Be(5);
        rule.TopBottomPercent.Should().BeTrue();
    }

    [Fact]
    public void Build_IconSet_AppliesStyleAndDefaultThresholds_AndNoFormatStyle()
    {
        var input = new CfRuleInput { RuleType = CfRuleType.IconSet, IconSetStyle = "4Arrows" };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(CfRuleType.IconSet);
        rule.IconSetStyle.Should().Be("4Arrows");
        rule.IconSetThresholds.Should().HaveCount(4);
        rule.FormatIfTrue.Should().BeNull("icon sets carry their own appearance");
    }

    [Fact]
    public void Build_IconSet_BlankStyleFallsBackToDefault()
    {
        var input = new CfRuleInput { RuleType = CfRuleType.IconSet, IconSetStyle = " " };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.IconSetStyle.Should().Be(ConditionalFormatIconSetCatalog.DefaultStyle);
        rule.IconSetThresholds.Should().HaveCount(3);
    }

    [Fact]
    public void Build_DataBar_SetsThresholdTypes_AndNoFormatStyle()
    {
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.DataBar,
            DataBarMinType = CfThresholdType.Number,
            DataBarMaxType = CfThresholdType.Percentile,
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(CfRuleType.DataBar);
        rule.DataBarMinThresholdType.Should().Be(CfThresholdType.Number);
        rule.DataBarMaxThresholdType.Should().Be(CfThresholdType.Percentile);
        rule.FormatIfTrue.Should().BeNull();
    }

    [Fact]
    public void Build_ColorScale_ParsesColorsAndThreeColorFlag()
    {
        var input = new CfRuleInput
        {
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = "1,2,3",
            MidColor = "4,5,6",
            MaxColor = "7,8,9",
        };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(CfRuleType.ColorScale);
        rule.UseThreeColorScale.Should().BeTrue();
        rule.MinColor.Should().Be(new RgbColor(1, 2, 3));
        rule.MidColor.Should().Be(new RgbColor(4, 5, 6));
        rule.MaxColor.Should().Be(new RgbColor(7, 8, 9));
        rule.FormatIfTrue.Should().BeNull();
    }

    [Theory]
    [InlineData(CfRuleType.ContainsText)]
    [InlineData(CfRuleType.NotContainsText)]
    [InlineData(CfRuleType.BeginsWith)]
    [InlineData(CfRuleType.EndsWith)]
    public void Build_TextRules_SetTextRuleText(CfRuleType ruleType)
    {
        var input = new CfRuleInput { RuleType = ruleType, Text = "abc" };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(ruleType);
        rule.TextRuleText.Should().Be("abc");
    }

    [Theory]
    [InlineData(CfRuleType.DuplicateValues)]
    [InlineData(CfRuleType.UniqueValues)]
    [InlineData(CfRuleType.AboveAverage)]
    public void Build_ChoiceOnlyRules_CarryHighlightStyle(CfRuleType ruleType)
    {
        var input = new CfRuleInput { RuleType = ruleType };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range());

        rule.RuleType.Should().Be(ruleType);
        rule.FormatIfTrue.Should().NotBeNull();
    }

    [Fact]
    public void Build_HonorsExplicitIdForEdit()
    {
        var id = Guid.NewGuid();
        var input = new CfRuleInput { RuleType = CfRuleType.CellValue, Value1 = "1" };

        var rule = ConditionalFormatRuleBuilder.Build(input, Range(), id: id);

        rule.Id.Should().Be(id);
    }

    [Fact]
    public void Build_UsesChosenHighlightPreset()
    {
        var input = new CfRuleInput { RuleType = CfRuleType.CellValue, Value1 = "1" };
        var preset = ConditionalFormatHighlightPreset.Presets.Single(p => p.Label == "Green Fill");

        var rule = ConditionalFormatRuleBuilder.Build(input, Range(), preset);

        rule.FormatIfTrue!.FillColor.Should().Be(new CellColor(198, 239, 206));
        rule.FormatIfTrue.FontColor.Should().Be(CellColor.Black);
        rule.FormatIfTrue.Bold.Should().BeFalse();
    }

    // ── TryBuildApplyCommand: validation + add-command mapping ─────────────────

    [Fact]
    public void TryBuildApplyCommand_InvalidInput_ReportsValidationErrors()
    {
        var input = new CfRuleInput { RuleType = CfRuleType.CellValue, Value1 = "" };

        var result = ConditionalFormatRuleBuilder.TryBuildApplyCommand(input, SheetId(), Range());

        result.IsValid.Should().BeFalse();
        result.Command.Should().BeNull();
        result.Validation.Errors.Should().ContainSingle()
            .Which.Field.Should().Be(CfInputField.Value1);
    }

    [Fact]
    public void TryBuildApplyCommand_ValidInput_ProducesAddCommand()
    {
        var input = new CfRuleInput { RuleType = CfRuleType.CellValue, Operator = CfOperator.GreaterThan, Value1 = "5" };

        var result = ConditionalFormatRuleBuilder.TryBuildApplyCommand(input, SheetId(), Range());

        result.IsValid.Should().BeTrue();
        result.Rule.Should().NotBeNull();
        result.Command.Should().BeOfType<ApplyConditionalFormatCommand>();
    }

    // ── End-to-end apply through the real session command path ────────────────

    [Fact]
    public void ApplyCommand_ThroughSession_AddsRuleToSheet()
    {
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        var input = new CfRuleInput { RuleType = CfRuleType.CellValue, Operator = CfOperator.GreaterThan, Value1 = "5" };
        var rule = ConditionalFormatRuleBuilder.Build(input, SelectionRange(session));

        var outcome = session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, rule));

        outcome.Success.Should().BeTrue();
        session.ActiveSheet.ConditionalFormats.Should().ContainSingle().Which.Id.Should().Be(rule.Id);
    }

    [Fact]
    public void ApplyCommand_ReusingId_ReplacesRuleInPlace()
    {
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        var range = SelectionRange(session);
        var original = ConditionalFormatRuleBuilder.Build(
            new CfRuleInput { RuleType = CfRuleType.CellValue, Value1 = "5" }, range);
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, original));

        var edited = ConditionalFormatRuleBuilder.Build(
            new CfRuleInput { RuleType = CfRuleType.CellValue, Value1 = "9" }, range, id: original.Id);
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, edited));

        session.ActiveSheet.ConditionalFormats.Should().ContainSingle()
            .Which.Value1.Should().Be("9");
    }

    // ── Presets → rule construction ───────────────────────────────────────────

    [Fact]
    public void Preset_DataBar_BuildsDataBarRule()
    {
        ConditionalFormatPresetFactory.BuildRule(ConditionalFormatPreset.DataBar, Range())
            .RuleType.Should().Be(CfRuleType.DataBar);
    }

    [Fact]
    public void Preset_ColorScale_BuildsThreeColorScaleRule()
    {
        var rule = ConditionalFormatPresetFactory.BuildRule(ConditionalFormatPreset.ColorScale, Range());

        rule.RuleType.Should().Be(CfRuleType.ColorScale);
        rule.UseThreeColorScale.Should().BeTrue();
    }

    [Fact]
    public void Preset_IconSet_BuildsIconSetRuleWithDefaultStyle()
    {
        var rule = ConditionalFormatPresetFactory.BuildRule(ConditionalFormatPreset.IconSet, Range());

        rule.RuleType.Should().Be(CfRuleType.IconSet);
        rule.IconSetStyle.Should().Be(ConditionalFormatIconSetCatalog.DefaultStyle);
    }

    [Fact]
    public void Preset_HighlightGreaterThan_UsesSuppliedValue()
    {
        var rule = ConditionalFormatPresetFactory.BuildRule(ConditionalFormatPreset.HighlightGreaterThan, Range(), "42");

        rule.RuleType.Should().Be(CfRuleType.CellValue);
        rule.Operator.Should().Be(CfOperator.GreaterThan);
        rule.Value1.Should().Be("42");
        rule.FormatIfTrue.Should().NotBeNull();
    }

    [Fact]
    public void Preset_HighlightGreaterThan_DefaultsValueToZero()
    {
        ConditionalFormatPresetFactory.BuildRule(ConditionalFormatPreset.HighlightGreaterThan, Range())
            .Value1.Should().Be("0");
    }

    [Fact]
    public void Preset_Top10_BuildsTop10Rule()
    {
        var rule = ConditionalFormatPresetFactory.BuildRule(ConditionalFormatPreset.Top10, Range());

        rule.RuleType.Should().Be(CfRuleType.Top10);
        rule.TopBottomRank.Should().Be(10);
        rule.TopBottomPercent.Should().BeFalse();
    }

    [Fact]
    public void Preset_BuildApplyCommand_ProducesAddCommand()
    {
        ConditionalFormatPresetFactory.BuildApplyCommand(ConditionalFormatPreset.DataBar, SheetId(), Range())
            .Should().BeOfType<ApplyConditionalFormatCommand>();
    }

    [Fact]
    public void Preset_AppliesThroughSession()
    {
        var session = CreateSession();
        var command = ConditionalFormatPresetFactory.BuildApplyCommand(
            ConditionalFormatPreset.IconSet, session.ActiveSheet.Id, SelectionRange(session));

        session.ExecuteReviewCommand(command).Success.Should().BeTrue();
        session.ActiveSheet.ConditionalFormats.Should().ContainSingle()
            .Which.RuleType.Should().Be(CfRuleType.IconSet);
    }

    // ── Manage: list, delete, edit command mapping ────────────────────────────

    [Fact]
    public void Manage_BuildList_FiltersBySelectionOverlap()
    {
        var sheet = SheetId();
        var overlapping = RuleAt(sheet, 0, 0, 4, 4);
        var disjoint = RuleAt(sheet, 10, 10, 12, 12);
        var rules = new List<ConditionalFormat> { overlapping, disjoint };

        var listed = CreateManageSession(rules, RangeAt(sheet, 1, 1, 2, 2)).BuildProjection();

        listed.Should().ContainSingle().Which.Id.Should().Be(overlapping.Id);
    }

    [Fact]
    public void Manage_BuildList_NullScopeListsAllRules()
    {
        var sheet = SheetId();
        var rules = new List<ConditionalFormat> { RuleAt(sheet, 0, 0, 1, 1), RuleAt(sheet, 5, 5, 6, 6) };

        CreateManageSession(rules).BuildProjection().Should().HaveCount(2);
    }

    [Fact]
    public void Manage_Delete_UnknownId_ReturnsFalse()
    {
        var sheet = SheetId();
        var rules = new List<ConditionalFormat> { RuleAt(sheet, 0, 0, 1, 1) };

        CreateManageSession(rules).Delete(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void Manage_DeleteCommand_RemovesRuleThroughSession()
    {
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        var range = SelectionRange(session);
        var keep = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.DataBar }, range);
        var drop = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.IconSet }, range);
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, keep));
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, drop));

        var manager = CreateManageSession(session.ActiveSheet.ConditionalFormats);
        manager.Delete(drop.Id).Should().BeTrue();
        session.ExecuteReviewCommand(manager.CreateApplyCommand(sheetId)).Success.Should().BeTrue();

        session.ActiveSheet.ConditionalFormats.Should().ContainSingle().Which.Id.Should().Be(keep.Id);
    }

    [Fact]
    public void Manage_EditCommand_ReplacesRuleThroughSession()
    {
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        var range = SelectionRange(session);
        var rule = ConditionalFormatRuleBuilder.Build(
            new CfRuleInput { RuleType = CfRuleType.CellValue, Value1 = "1" }, range);
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, rule));

        var edited = ConditionalFormatRuleBuilder.Build(
            new CfRuleInput { RuleType = CfRuleType.CellValue, Value1 = "2" }, range, id: rule.Id);
        var manager = CreateManageSession(session.ActiveSheet.ConditionalFormats);
        manager.Replace(edited).Should().BeTrue();
        session.ExecuteReviewCommand(manager.CreateApplyCommand(sheetId)).Success.Should().BeTrue();

        session.ActiveSheet.ConditionalFormats.Should().ContainSingle().Which.Value1.Should().Be("2");
    }

    [Fact]
    public void Manage_DuplicateCommand_InsertsCopiedRuleThroughSession()
    {
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        var range = SelectionRange(session);
        var rule = ConditionalFormatRuleBuilder.Build(
            new CfRuleInput { RuleType = CfRuleType.CellValue, Value1 = "7" }, range);
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, rule));

        var duplicateId = Guid.NewGuid();
        var manager = CreateManageSession(session.ActiveSheet.ConditionalFormats);
        manager.Duplicate(rule.Id, duplicateId).Should().BeTrue();
        session.ExecuteReviewCommand(manager.CreateApplyCommand(sheetId)).Success.Should().BeTrue();

        session.ActiveSheet.ConditionalFormats.Should().HaveCount(2);
        var duplicate = session.ActiveSheet.ConditionalFormats.Single(r => r.Id == duplicateId);
        duplicate.Value1.Should().Be("7");
        duplicate.Priority.Should().Be(2);
    }

    [Fact]
    public void Manage_Describe_SummarizesRuleTypes()
    {
        var sheet = SheetId();
        ManageConditionalFormatsPlanner.DescribeRule(new ConditionalFormat
        {
            AppliesTo = RangeAt(sheet, 0, 0, 1, 1),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
        }).ResourceKey.Should().Be("ManageConditionalFormats_RuleCellValue");

        ManageConditionalFormatsPlanner.DescribeRule(new ConditionalFormat
        {
            AppliesTo = RangeAt(sheet, 0, 0, 1, 1),
            RuleType = CfRuleType.DataBar,
        }).ResourceKey.Should().Be("ManageConditionalFormats_RuleDataBar");
    }

    [Fact]
    public void Build_Top10_DefaultsToTop_SettingAboveAverageTrue()
    {
        var input = new CfRuleInput { RuleType = CfRuleType.Top10, Rank = "3" };

        ConditionalFormatRuleBuilder.Build(input, Range()).AboveAverage.Should().BeTrue();
    }

    [Fact]
    public void Build_Top10_Bottom_SetsAboveAverageFalse()
    {
        var input = new CfRuleInput { RuleType = CfRuleType.Top10, Rank = "3", IsTop = false };

        ConditionalFormatRuleBuilder.Build(input, Range()).AboveAverage.Should().BeFalse();
    }

    // ── Manage: reorder + applies-to command mapping ──────────────────────────

    [Fact]
    public void Manage_Move_UnknownId_ReturnsFalse()
    {
        var sheet = SheetId();
        var rules = new List<ConditionalFormat> { RuleAt(sheet, 0, 0, 1, 1) };

        CreateManageSession(rules).Move(Guid.NewGuid(), ConditionalFormatRuleMoveDirection.Up)
            .Should().BeFalse();
    }

    [Fact]
    public void Manage_Move_AtBoundary_ReturnsFalse()
    {
        var sheet = SheetId();
        var first = RuleAt(sheet, 0, 0, 1, 1);
        first.Priority = 1;
        var rules = new List<ConditionalFormat> { first };

        CreateManageSession(rules).Move(first.Id, ConditionalFormatRuleMoveDirection.Up)
            .Should().BeFalse();
    }

    [Fact]
    public void Manage_MoveCommand_SwapsPriorityThroughSession()
    {
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        var range = SelectionRange(session);
        var first = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.DataBar }, range);
        var second = ConditionalFormatRuleBuilder.Build(new CfRuleInput { RuleType = CfRuleType.IconSet }, range);
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, first));
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, second));

        var manager = CreateManageSession(session.ActiveSheet.ConditionalFormats);
        manager.Move(second.Id, ConditionalFormatRuleMoveDirection.Up).Should().BeTrue();
        session.ExecuteReviewCommand(manager.CreateApplyCommand(sheetId)).Success.Should().BeTrue();

        session.ActiveSheet.ConditionalFormats.Single(r => r.Id == second.Id).Priority
            .Should().Be(1);
        session.ActiveSheet.ConditionalFormats.Single(r => r.Id == first.Id).Priority
            .Should().Be(2);
    }

    [Fact]
    public void Manage_ApplyRange_UnknownId_ReturnsFalse()
    {
        var sheet = SheetId();
        var rules = new List<ConditionalFormat> { RuleAt(sheet, 0, 0, 1, 1) };

        CreateManageSession(rules)
            .ApplyRange(Guid.NewGuid(), RangeAt(sheet, 2, 2, 3, 3))
            .Should().BeFalse();
    }

    [Fact]
    public void Manage_AppliesToCommand_ChangesRangeThroughSession()
    {
        var session = CreateSession();
        var sheetId = session.ActiveSheet.Id;
        var rule = ConditionalFormatRuleBuilder.Build(
            new CfRuleInput { RuleType = CfRuleType.DataBar }, SelectionRange(session));
        session.ExecuteReviewCommand(ConditionalFormatRuleBuilder.ToApplyCommand(sheetId, rule));

        var newRange = new GridRange(new CellAddress(sheetId, 9, 9), new CellAddress(sheetId, 12, 12));
        var manager = CreateManageSession(session.ActiveSheet.ConditionalFormats);
        manager.ApplyRange(rule.Id, newRange).Should().BeTrue();
        session.ExecuteReviewCommand(manager.CreateApplyCommand(sheetId)).Success.Should().BeTrue();

        session.ActiveSheet.ConditionalFormats.Single(r => r.Id == rule.Id).AppliesTo
            .Should().Be(newRange);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SheetId SheetId() => new(Guid.NewGuid());

    private static GridRange Range() => RangeAt(SheetId(), 0, 0, 4, 0);

    private static GridRange RangeAt(SheetId sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet, r1, c1), new CellAddress(sheet, r2, c2));

    private static ConditionalFormat RuleAt(SheetId sheet, uint r1, uint c1, uint r2, uint c2) =>
        new() { AppliesTo = RangeAt(sheet, r1, c1, r2, c2), RuleType = CfRuleType.DataBar };

    private static ManageConditionalFormatsSession CreateManageSession(
        IReadOnlyList<ConditionalFormat> rules,
        GridRange? scope = null) =>
        new(rules, scope, ManageConditionalFormatsWorkingCopyPolicy.FullSheet);

    private static WorkbookSession CreateSession() =>
        new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);

    private static GridRange SelectionRange(WorkbookSession session)
    {
        var sheet = session.ActiveSheet.Id;
        return new GridRange(new CellAddress(sheet, 0, 0), new CellAddress(sheet, 4, 0));
    }
}
