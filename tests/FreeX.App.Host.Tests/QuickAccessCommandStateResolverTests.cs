using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class QuickAccessCommandStateResolverTests
{
    [Theory]
    [InlineData(QuickAccessToolbarCommandIds.Cut)]
    [InlineData(QuickAccessToolbarCommandIds.Copy)]
    [InlineData(QuickAccessToolbarCommandIds.Paste)]
    [InlineData(QuickAccessToolbarCommandIds.Bold)]
    [InlineData(QuickAccessToolbarCommandIds.DataValidation)]
    [InlineData(QuickAccessToolbarCommandIds.SortAscending)]
    [InlineData(QuickAccessToolbarCommandIds.ZoomSelection)]
    [InlineData(QuickAccessToolbarCommandIds.FreezePanes)]
    public void CanExecute_DisablesSelectionCommandsWithoutSelection(string commandId)
    {
        var state = new QuickAccessCommandState(
            CanUndo: true,
            CanRedo: true,
            HasActiveWorksheet: true,
            HasSelection: false);

        QuickAccessCommandStateResolver.CanExecute(commandId, state).Should().BeFalse();
    }

    [Theory]
    [InlineData(QuickAccessToolbarCommandIds.Cut)]
    [InlineData(QuickAccessToolbarCommandIds.Copy)]
    [InlineData(QuickAccessToolbarCommandIds.Paste)]
    [InlineData(QuickAccessToolbarCommandIds.Bold)]
    [InlineData(QuickAccessToolbarCommandIds.DataValidation)]
    [InlineData(QuickAccessToolbarCommandIds.SortAscending)]
    [InlineData(QuickAccessToolbarCommandIds.ZoomSelection)]
    [InlineData(QuickAccessToolbarCommandIds.FreezePanes)]
    public void CanExecute_EnablesSelectionCommandsWithActiveWorksheetAndSelection(string commandId)
    {
        var state = new QuickAccessCommandState(
            CanUndo: false,
            CanRedo: false,
            HasActiveWorksheet: true,
            HasSelection: true);

        QuickAccessCommandStateResolver.CanExecute(commandId, state).Should().BeTrue();
    }

    [Fact]
    public void CanExecute_UsesCommandStackStateForUndoRedo()
    {
        var state = new QuickAccessCommandState(
            CanUndo: true,
            CanRedo: false,
            HasActiveWorksheet: true,
            HasSelection: true);

        QuickAccessCommandStateResolver.CanExecute(QuickAccessToolbarCommandIds.Undo, state).Should().BeTrue();
        QuickAccessCommandStateResolver.CanExecute(QuickAccessToolbarCommandIds.Redo, state).Should().BeFalse();
    }

    [Fact]
    public void WithSelectionContext_PreservesUndoRedoState()
    {
        var state = new QuickAccessCommandState(
            CanUndo: true,
            CanRedo: false,
            HasActiveWorksheet: false,
            HasSelection: false);

        var refreshed = state.WithSelectionContext(hasActiveWorksheet: true, hasSelection: true);

        QuickAccessCommandStateResolver.CanExecute(QuickAccessToolbarCommandIds.Undo, refreshed).Should().BeTrue();
        QuickAccessCommandStateResolver.CanExecute(QuickAccessToolbarCommandIds.Redo, refreshed).Should().BeFalse();
        QuickAccessCommandStateResolver.CanExecute(QuickAccessToolbarCommandIds.Bold, refreshed).Should().BeTrue();
    }

    [Theory]
    [InlineData(QuickAccessToolbarCommandIds.New)]
    [InlineData(QuickAccessToolbarCommandIds.Open)]
    [InlineData(QuickAccessToolbarCommandIds.Save)]
    [InlineData(QuickAccessToolbarCommandIds.SaveAs)]
    public void CanExecute_KeepsFileCommandsAvailableWithoutWorksheetOrSelection(string commandId)
    {
        var state = new QuickAccessCommandState(
            CanUndo: false,
            CanRedo: false,
            HasActiveWorksheet: false,
            HasSelection: false);

        QuickAccessCommandStateResolver.CanExecute(commandId, state).Should().BeTrue();
    }

    [Fact]
    public void CanExecute_DisablesUnknownCommandsConservatively()
    {
        var state = new QuickAccessCommandState(
            CanUndo: true,
            CanRedo: true,
            HasActiveWorksheet: true,
            HasSelection: true);

        QuickAccessCommandStateResolver.CanExecute("MissingCommand", state).Should().BeFalse();
    }

    [Theory]
    [InlineData(QuickAccessToolbarCommandIds.Save, (int)QuickAccessCommandAvailability.Always)]
    [InlineData(QuickAccessToolbarCommandIds.Undo, (int)QuickAccessCommandAvailability.Undo)]
    [InlineData(QuickAccessToolbarCommandIds.Redo, (int)QuickAccessCommandAvailability.Redo)]
    [InlineData(QuickAccessToolbarCommandIds.Print, (int)QuickAccessCommandAvailability.Worksheet)]
    [InlineData(QuickAccessToolbarCommandIds.Bold, (int)QuickAccessCommandAvailability.Selection)]
    [InlineData("MissingCommand", (int)QuickAccessCommandAvailability.Never)]
    public void GetAvailability_ClassifiesCommandsForCachedToolbarState(
        string commandId,
        int expected)
    {
        QuickAccessCommandStateResolver.GetAvailability(commandId)
            .Should()
            .Be((QuickAccessCommandAvailability)expected);
    }
}
