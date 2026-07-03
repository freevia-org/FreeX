using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;

namespace FreeX.App.Services.Tests;

public sealed class GoToDialogPlannerTests
{
    [Fact]
    public void GoToSpecialDialogSize_MatchesSharedWpfLogicalEvidenceTarget()
    {
        GoToSpecialDialogPlanner.Width.Should().Be(430);
        GoToSpecialDialogPlanner.Height.Should().Be(438);
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
