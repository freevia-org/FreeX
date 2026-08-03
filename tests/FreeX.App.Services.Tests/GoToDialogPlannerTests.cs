using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;

namespace FreeX.App.Services.Tests;

public sealed class GoToDialogPlannerTests
{
    [Fact]
    public void FindReplaceDialogSize_MatchesSharedWpfLogicalEvidenceTarget()
    {
        FindReplaceDialogPlanner.Width.Should().Be(720);
        FindReplaceDialogPlanner.Height.Should().Be(430);
        FindReplaceDialogPlanner.MinWidth.Should().Be(520);
        FindReplaceDialogPlanner.MinHeight.Should().Be(360);
    }

    [Fact]
    public void FindReplaceDialogLayout_UsesSharedWpfAuthorityMetrics()
    {
        FindReplaceDialogPlanner.RootMargin.Should().Be(12);
        FindReplaceDialogPlanner.TabContentMargin.Should().Be(10);
        FindReplaceDialogPlanner.FindTabHeight.Should().Be(74);
        FindReplaceDialogPlanner.ReplaceTabHeight.Should().Be(108);
        FindReplaceDialogPlanner.FieldLabelColumnWidth.Should().Be(88);
        FindReplaceDialogPlanner.FieldMinWidth.Should().Be(260);
        FindReplaceDialogPlanner.FormatButtonWidth.Should().Be(84);
        FindReplaceDialogPlanner.ClearFormatButtonWidth.Should().Be(52);
        FindReplaceDialogPlanner.ChooseFormatButtonWidth.Should().Be(136);
        FindReplaceDialogPlanner.ResultsMinimumHeight.Should().Be(120);
        FindReplaceDialogPlanner.ResultsHeaderHeight.Should().Be(24);
        FindReplaceDialogPlanner.ActionButtonSpacing.Should().Be(8);
        FindReplaceDialogPlanner.ActionButtonHeight.Should().Be(20);
        FindReplaceDialogPlanner.OptionsHeaderMinimumWidth.Should().Be(112);
        FindReplaceDialogPlanner.AvaloniaOptionsBottomMargin.Should().Be(13);
        FindReplaceDialogPlanner.ResultBookColumnWidth.Should().Be(110);
        FindReplaceDialogPlanner.ResultSheetColumnWidth.Should().Be(100);
        FindReplaceDialogPlanner.ResultNameColumnWidth.Should().Be(90);
        FindReplaceDialogPlanner.ResultCellColumnWidth.Should().Be(70);
    }

    [Fact]
    public void GoToSpecialDialogSize_MatchesSharedWpfLogicalEvidenceTarget()
    {
        GoToSpecialDialogPlanner.Width.Should().Be(430);
        GoToSpecialDialogPlanner.Height.Should().Be(438);
    }

    [Fact]
    public void GoToSpecialDialogLayout_UsesSharedWpfEvidenceMetrics()
    {
        GoToSpecialDialogPlanner.ContentMargin.Should().Be(12);
        GoToSpecialDialogPlanner.AvaloniaContentLeftMargin.Should().Be(13);
        GoToSpecialDialogPlanner.AvaloniaContentTopMargin.Should().Be(12);
        GoToSpecialDialogPlanner.AvaloniaContentRightMargin.Should().Be(29);
        GoToSpecialDialogPlanner.ActionRowTopMargin.Should().Be(10);
        GoToSpecialDialogPlanner.ActionRowRightMargin.Should().Be(28);
        GoToSpecialDialogPlanner.ActionRowBottomMargin.Should().Be(49);
        GoToSpecialDialogPlanner.ActionButtonHeight.Should().Be(20);
        GoToSpecialDialogPlanner.AvaloniaChoiceGroupTopMargin.Should().Be(3);
        GoToSpecialDialogPlanner.AvaloniaChoiceGroupBottomMargin.Should().Be(13);
        GoToSpecialDialogPlanner.AvaloniaChoiceGroupHorizontalPadding.Should().Be(8);
        GoToSpecialDialogPlanner.AvaloniaChoiceGroupBottomPadding.Should().Be(9);
        GoToSpecialDialogPlanner.AvaloniaValueTypeGroupBottomPadding.Should().Be(4);
        GoToSpecialDialogPlanner.AvaloniaValueTypeSpacing.Should().Be(16);
        GoToSpecialDialogPlanner.AvaloniaChoiceButtonRightMargin.Should().Be(12);
        GoToSpecialDialogPlanner.AvaloniaChoiceButtonBottomMargin.Should().Be(1);
    }

    [Fact]
    public void BuildChoices_UsesSharedExcelOrder()
    {
        GoToSpecialDialogPlanner.BuildChoices().Select(choice => choice.Kind).Should().Equal(
            GoToSpecialKind.Blanks,
            GoToSpecialKind.Constants,
            GoToSpecialKind.Formulas,
            GoToSpecialKind.Comments,
            GoToSpecialKind.CurrentRegion,
            GoToSpecialKind.RowDifferences,
            GoToSpecialKind.ColumnDifferences,
            GoToSpecialKind.LastCell,
            GoToSpecialKind.ConditionalFormats,
            GoToSpecialKind.Objects,
            GoToSpecialKind.Precedents,
            GoToSpecialKind.Dependents,
            GoToSpecialKind.DataValidation,
            GoToSpecialKind.VisibleCellsOnly);
    }

    [Theory]
    [InlineData("blanks", GoToSpecialKind.Blanks)]
    [InlineData("constant", GoToSpecialKind.Constants)]
    [InlineData("constants", GoToSpecialKind.Constants)]
    [InlineData("formula", GoToSpecialKind.Formulas)]
    [InlineData("formulas", GoToSpecialKind.Formulas)]
    [InlineData("comment", GoToSpecialKind.Comments)]
    [InlineData("comments", GoToSpecialKind.Comments)]
    [InlineData("validation", GoToSpecialKind.DataValidation)]
    [InlineData("data validation", GoToSpecialKind.DataValidation)]
    [InlineData("Data validation", GoToSpecialKind.DataValidation)]
    [InlineData("Data valid_ation", GoToSpecialKind.DataValidation)]
    [InlineData("visible", GoToSpecialKind.VisibleCellsOnly)]
    [InlineData("visible cells", GoToSpecialKind.VisibleCellsOnly)]
    [InlineData("visible cells only", GoToSpecialKind.VisibleCellsOnly)]
    [InlineData("row differences", GoToSpecialKind.RowDifferences)]
    [InlineData("column differences", GoToSpecialKind.ColumnDifferences)]
    [InlineData("current region", GoToSpecialKind.CurrentRegion)]
    [InlineData("last cell", GoToSpecialKind.LastCell)]
    [InlineData("conditional formats", GoToSpecialKind.ConditionalFormats)]
    [InlineData("Conditional forma_ts", GoToSpecialKind.ConditionalFormats)]
    [InlineData("object", GoToSpecialKind.Objects)]
    [InlineData("objects", GoToSpecialKind.Objects)]
    [InlineData("precedent", GoToSpecialKind.Precedents)]
    [InlineData("precedents", GoToSpecialKind.Precedents)]
    [InlineData("dependent", GoToSpecialKind.Dependents)]
    [InlineData("dependents", GoToSpecialKind.Dependents)]
    [InlineData("unknown", GoToSpecialKind.Blanks)]
    public void TryParseChoice_MapsPromptTextToGoToSpecialKind(string input, GoToSpecialKind expected)
    {
        GoToSpecialDialogPlanner.TryParseChoice(input, out var kind).Should().BeTrue();
        kind.Should().Be(expected);
    }

    [Fact]
    public void BuildOptions_UsesValueTypesOnlyForConstantsAndFormulas()
    {
        var selectedTypes = GoToSpecialValueTypes.Numbers | GoToSpecialValueTypes.Errors;

        GoToSpecialDialogPlanner.BuildOptions(GoToSpecialKind.Constants, selectedTypes).ValueTypes
            .Should().Be(selectedTypes);
        GoToSpecialDialogPlanner.BuildOptions(GoToSpecialKind.Formulas, selectedTypes).ValueTypes
            .Should().Be(selectedTypes);
        GoToSpecialDialogPlanner.BuildOptions(GoToSpecialKind.Blanks, selectedTypes).ValueTypes
            .Should().Be(GoToSpecialValueTypes.All);
    }
}
