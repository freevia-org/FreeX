using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;

namespace FreeX.App.Presentation.Tests.Editing;

public sealed class PasteSpecialPlannerTests
{
    [Fact]
    public void Surface_DefinesEveryModeOnce()
    {
        var choices = PasteSpecialPlanner.Surface.Choices;

        choices.Select(choice => choice.Mode).Should().BeEquivalentTo(Enum.GetValues<PasteSpecialDialogMode>());
        choices.Select(choice => choice.Mode).Should().OnlyHaveUniqueItems();
        choices.Should().ContainSingle(choice => choice.IsDefault && choice.Mode == PasteSpecialDialogMode.All);
    }

    [Fact]
    public void Surface_PreservesWpfChoiceOrderAndPlacement()
    {
        var choices = PasteSpecialPlanner.Surface.WpfChoices;

        choices.Select(choice => choice.Mode).Should().Equal(
            PasteSpecialDialogMode.All,
            PasteSpecialDialogMode.Formulas,
            PasteSpecialDialogMode.Values,
            PasteSpecialDialogMode.Formats,
            PasteSpecialDialogMode.Comments,
            PasteSpecialDialogMode.Validation,
            PasteSpecialDialogMode.AllUsingSourceTheme,
            PasteSpecialDialogMode.AllExceptBorders,
            PasteSpecialDialogMode.ColumnWidths,
            PasteSpecialDialogMode.FormulasAndNumberFormats,
            PasteSpecialDialogMode.ValuesAndNumberFormats,
            PasteSpecialDialogMode.AllMergingConditionalFormats,
            PasteSpecialDialogMode.ValuesAndSourceFormatting,
            PasteSpecialDialogMode.Text,
            PasteSpecialDialogMode.UnicodeText,
            PasteSpecialDialogMode.Picture,
            PasteSpecialDialogMode.LinkedPicture);
        choices.Select(choice => choice.WpfPlacement).Should().Equal(
            new PasteSpecialGridPosition(0, 0),
            new PasteSpecialGridPosition(1, 0),
            new PasteSpecialGridPosition(2, 0),
            new PasteSpecialGridPosition(3, 0),
            new PasteSpecialGridPosition(4, 0),
            new PasteSpecialGridPosition(5, 0),
            new PasteSpecialGridPosition(6, 0),
            new PasteSpecialGridPosition(7, 0),
            new PasteSpecialGridPosition(8, 0),
            new PasteSpecialGridPosition(0, 1),
            new PasteSpecialGridPosition(1, 1),
            new PasteSpecialGridPosition(2, 1),
            new PasteSpecialGridPosition(3, 1),
            new PasteSpecialGridPosition(4, 1),
            new PasteSpecialGridPosition(5, 1),
            new PasteSpecialGridPosition(6, 1),
            new PasteSpecialGridPosition(7, 1));
    }

    [Fact]
    public void Surface_PreservesAvaloniaChoiceOrderAndLabels()
    {
        var choices = PasteSpecialPlanner.Surface.AvaloniaChoices;

        choices.Select(choice => choice.Mode).Should().Equal(
            PasteSpecialDialogMode.All,
            PasteSpecialDialogMode.Values,
            PasteSpecialDialogMode.Formulas,
            PasteSpecialDialogMode.Formats,
            PasteSpecialDialogMode.Comments,
            PasteSpecialDialogMode.Validation,
            PasteSpecialDialogMode.AllExceptBorders,
            PasteSpecialDialogMode.AllMergingConditionalFormats,
            PasteSpecialDialogMode.ColumnWidths,
            PasteSpecialDialogMode.FormulasAndNumberFormats,
            PasteSpecialDialogMode.ValuesAndNumberFormats,
            PasteSpecialDialogMode.ValuesAndSourceFormatting,
            PasteSpecialDialogMode.Text,
            PasteSpecialDialogMode.UnicodeText,
            PasteSpecialDialogMode.Picture,
            PasteSpecialDialogMode.LinkedPicture);
        choices.Select(choice => choice.AvaloniaLabel).Should().Equal(
            "All",
            "Values",
            "Formulas",
            "Formats",
            "Comments and Notes",
            "Validation",
            "All Except Borders",
            "All Merging Conditional Formats",
            "Column Widths",
            "Formulas and Number Formats",
            "Values and Number Formats",
            "Values and Source Formatting",
            "Text",
            "Unicode Text",
            "Picture",
            "Linked Picture");
    }

    [Fact]
    public void Surface_PreservesOperationToggleAndFooterDefaults()
    {
        var surface = PasteSpecialPlanner.Surface;

        surface.Operations.Select(operation => operation.Operation).Should().Equal(
            PasteSpecialOperation.None,
            PasteSpecialOperation.Add,
            PasteSpecialOperation.Subtract,
            PasteSpecialOperation.Multiply,
            PasteSpecialOperation.Divide);
        surface.Operations.Should().ContainSingle(operation => operation.IsDefault && operation.Operation == PasteSpecialOperation.None);
        surface.Toggles.Select(toggle => toggle.Kind).Should().Equal(
            PasteSpecialToggleKind.SkipBlanks,
            PasteSpecialToggleKind.Transpose,
            PasteSpecialToggleKind.KeepColumnWidths);
        surface.Toggles.Should().OnlyContain(toggle => !toggle.IsCheckedByDefault && toggle.IsEnabled);
        surface.GetAction(PasteSpecialDialogActionKind.Accept).IsDefault.Should().BeTrue();
        surface.GetAction(PasteSpecialDialogActionKind.Cancel).IsCancel.Should().BeTrue();
        surface.GetAction(PasteSpecialDialogActionKind.PasteLink).AvaloniaLabel.Should().Be("Paste Link");
    }

    [Theory]
    [InlineData(PasteSpecialDialogMode.Values, PasteSpecialAction.Paste, PasteMode.Values, PasteSpecialContentKind.Default)]
    [InlineData(PasteSpecialDialogMode.Formulas, PasteSpecialAction.Paste, PasteMode.Formulas, PasteSpecialContentKind.Default)]
    [InlineData(PasteSpecialDialogMode.Formats, PasteSpecialAction.Paste, PasteMode.Formats, PasteSpecialContentKind.Default)]
    [InlineData(PasteSpecialDialogMode.AllUsingSourceTheme, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.AllUsingSourceTheme)]
    [InlineData(PasteSpecialDialogMode.AllExceptBorders, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.AllExceptBorders)]
    [InlineData(PasteSpecialDialogMode.AllMergingConditionalFormats, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.AllMergingConditionalFormats)]
    [InlineData(PasteSpecialDialogMode.FormulasAndNumberFormats, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.FormulasAndNumberFormats)]
    [InlineData(PasteSpecialDialogMode.ValuesAndNumberFormats, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.ValuesAndNumberFormats)]
    [InlineData(PasteSpecialDialogMode.ValuesAndSourceFormatting, PasteSpecialAction.Paste, PasteMode.All, PasteSpecialContentKind.ValuesAndSourceFormatting)]
    [InlineData(PasteSpecialDialogMode.ColumnWidths, PasteSpecialAction.ColumnWidths, PasteMode.All, PasteSpecialContentKind.Default)]
    [InlineData(PasteSpecialDialogMode.Comments, PasteSpecialAction.Comments, PasteMode.All, PasteSpecialContentKind.Default)]
    [InlineData(PasteSpecialDialogMode.Validation, PasteSpecialAction.Validation, PasteMode.All, PasteSpecialContentKind.Default)]
    [InlineData(PasteSpecialDialogMode.Picture, PasteSpecialAction.Picture, PasteMode.All, PasteSpecialContentKind.Default)]
    [InlineData(PasteSpecialDialogMode.LinkedPicture, PasteSpecialAction.LinkedPicture, PasteMode.All, PasteSpecialContentKind.Default)]
    [InlineData(PasteSpecialDialogMode.Text, PasteSpecialAction.ExternalText, PasteMode.All, PasteSpecialContentKind.Default)]
    [InlineData(PasteSpecialDialogMode.UnicodeText, PasteSpecialAction.ExternalText, PasteMode.All, PasteSpecialContentKind.Default)]
    public void CreatePlan_MapsChoicePolicy(
        PasteSpecialDialogMode mode,
        PasteSpecialAction expectedAction,
        PasteMode expectedPasteMode,
        PasteSpecialContentKind expectedContentKind)
    {
        var plan = PasteSpecialPlanner.CreatePlan(
            PasteSpecialPlanner.CreateSelection(mode, PasteSpecialOperation.Multiply, skipBlanks: true, transpose: true, keepColumnWidths: true));

        plan.Action.Should().Be(expectedAction);
        plan.PasteMode.Should().Be(expectedPasteMode);
        plan.Options.Should().Be(new PasteSpecialOptions(
            Transpose: true,
            Operation: PasteSpecialOperation.Multiply,
            SkipBlanks: true,
            ContentKind: expectedContentKind));
        plan.KeepColumnWidths.Should().BeTrue();
    }

    [Fact]
    public void CreatePasteLinkSelection_PreservesAvaloniaFooterResultPolicy()
    {
        var selection = PasteSpecialPlanner.CreatePasteLinkSelection();
        var plan = PasteSpecialPlanner.CreatePlan(selection);

        selection.Should().Be(new PasteSpecialDialogSelection(
            PasteSpecialDialogMode.All,
            PasteSpecialOperation.None,
            SkipBlanks: false,
            Transpose: false,
            KeepColumnWidths: false,
            PasteLink: true));
        plan.Action.Should().Be(PasteSpecialAction.Link);
        plan.Label.Should().Be("Paste Link");
    }

    [Fact]
    public void CreatePlan_LinkedPictureTakesPrecedenceOverLegacyWpfPasteLinkFlag()
    {
        var selection = PasteSpecialPlanner.CreateSelection(
            PasteSpecialDialogMode.LinkedPicture,
            PasteSpecialOperation.None,
            pasteLink: true);

        PasteSpecialPlanner.CreatePlan(selection).Action.Should().Be(PasteSpecialAction.LinkedPicture);
    }
}
