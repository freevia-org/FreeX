using FluentAssertions;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host.Tests;

public sealed class PasteSpecialPlannerTests
{
    [Theory]
    [InlineData("Add", PasteSpecialOperation.Add)]
    [InlineData("subtract", PasteSpecialOperation.Subtract)]
    [InlineData("Multiply", PasteSpecialOperation.Multiply)]
    [InlineData("divide", PasteSpecialOperation.Divide)]
    [InlineData("None", PasteSpecialOperation.None)]
    [InlineData("unknown", PasteSpecialOperation.None)]
    public void CreatePlan_MapsOperationText(string operationText, PasteSpecialOperation expected)
    {
        var plan = PasteSpecialPlanner.CreatePlan(
            new PasteSpecialDialogSelection(PasteSpecialDialogMode.All, operationText, SkipBlanks: true, Transpose: true));

        plan.Options.Operation.Should().Be(expected);
        plan.Options.SkipBlanks.Should().BeTrue();
        plan.Options.Transpose.Should().BeTrue();
    }

    [Theory]
    [InlineData(PasteSpecialDialogMode.AllExceptBorders, PasteSpecialContentKind.AllExceptBorders)]
    [InlineData(PasteSpecialDialogMode.AllUsingSourceTheme, PasteSpecialContentKind.AllUsingSourceTheme)]
    [InlineData(PasteSpecialDialogMode.AllMergingConditionalFormats, PasteSpecialContentKind.AllMergingConditionalFormats)]
    [InlineData(PasteSpecialDialogMode.FormulasAndNumberFormats, PasteSpecialContentKind.FormulasAndNumberFormats)]
    [InlineData(PasteSpecialDialogMode.ValuesAndNumberFormats, PasteSpecialContentKind.ValuesAndNumberFormats)]
    [InlineData(PasteSpecialDialogMode.ValuesAndSourceFormatting, PasteSpecialContentKind.ValuesAndSourceFormatting)]
    [InlineData(PasteSpecialDialogMode.All, PasteSpecialContentKind.Default)]
    public void CreatePlan_MapsContentKind(PasteSpecialDialogMode mode, PasteSpecialContentKind expected)
    {
        var plan = PasteSpecialPlanner.CreatePlan(new PasteSpecialDialogSelection(mode, "None"));

        plan.Options.ContentKind.Should().Be(expected);
    }

    [Theory]
    [InlineData(PasteSpecialDialogMode.ColumnWidths, PasteSpecialAction.ColumnWidths, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.Comments, PasteSpecialAction.Comments, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.Validation, PasteSpecialAction.Validation, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.Picture, PasteSpecialAction.Picture, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.LinkedPicture, PasteSpecialAction.LinkedPicture, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.Values, PasteSpecialAction.Paste, PasteMode.Values)]
    [InlineData(PasteSpecialDialogMode.Formulas, PasteSpecialAction.Paste, PasteMode.Formulas)]
    [InlineData(PasteSpecialDialogMode.Formats, PasteSpecialAction.Paste, PasteMode.Formats)]
    [InlineData(PasteSpecialDialogMode.All, PasteSpecialAction.Paste, PasteMode.All)]
    public void CreatePlan_SelectsExecutionActionAndPasteMode(
        PasteSpecialDialogMode mode,
        PasteSpecialAction expectedAction,
        PasteMode expectedPasteMode)
    {
        var plan = PasteSpecialPlanner.CreatePlan(new PasteSpecialDialogSelection(mode, "None"));

        plan.Action.Should().Be(expectedAction);
        plan.PasteMode.Should().Be(expectedPasteMode);
    }

    [Fact]
    public void CreatePlan_RoutesPasteLinkAfterNonPictureModes()
    {
        var plan = PasteSpecialPlanner.CreatePlan(new PasteSpecialDialogSelection(
            PasteSpecialDialogMode.Values,
            "None",
            PasteLink: true,
            KeepColumnWidths: true));

        plan.Action.Should().Be(PasteSpecialAction.Link);
        plan.KeepColumnWidths.Should().BeTrue();
    }
}
