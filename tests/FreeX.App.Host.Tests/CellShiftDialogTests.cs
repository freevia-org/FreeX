using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class CellShiftDialogTests
{
    [Theory]
    [InlineData(CellShiftDialogMode.Insert, CellShiftDialogChoice.ShiftCellsRight, KeyboardInsertDeleteDialogChoice.ShiftRight)]
    [InlineData(CellShiftDialogMode.Insert, CellShiftDialogChoice.ShiftCellsDown, KeyboardInsertDeleteDialogChoice.ShiftDown)]
    [InlineData(CellShiftDialogMode.Insert, CellShiftDialogChoice.EntireRow, KeyboardInsertDeleteDialogChoice.EntireRow)]
    [InlineData(CellShiftDialogMode.Insert, CellShiftDialogChoice.EntireColumn, KeyboardInsertDeleteDialogChoice.EntireColumn)]
    [InlineData(CellShiftDialogMode.Delete, CellShiftDialogChoice.ShiftCellsLeft, KeyboardInsertDeleteDialogChoice.ShiftLeft)]
    [InlineData(CellShiftDialogMode.Delete, CellShiftDialogChoice.ShiftCellsUp, KeyboardInsertDeleteDialogChoice.ShiftUp)]
    [InlineData(CellShiftDialogMode.Delete, CellShiftDialogChoice.EntireRow, KeyboardInsertDeleteDialogChoice.EntireRow)]
    [InlineData(CellShiftDialogMode.Delete, CellShiftDialogChoice.EntireColumn, KeyboardInsertDeleteDialogChoice.EntireColumn)]
    public void ToKeyboardChoice_MapsDialogChoicesToExistingPlannerChoices(
        CellShiftDialogMode mode,
        CellShiftDialogChoice choice,
        KeyboardInsertDeleteDialogChoice expected)
    {
        CellShiftDialog.ToKeyboardChoice(mode, choice).Should().Be(expected);
        CellShiftDialogPlanner.ToKeyboardChoice(mode, choice).Should().Be(expected);
    }

    [Fact]
    public void GetAvailableChoices_UsesExcelInsertLabels()
    {
        var choices = CellShiftDialog.GetAvailableChoices(CellShiftDialogMode.Insert);

        choices.Select(choice => choice.Label).Should().Equal(
            UiText.Get("CellShift_Insert_ShiftCellsRight"),
            UiText.Get("CellShift_Insert_ShiftCellsDown"),
            UiText.Get("CellShift_Insert_EntireRow"),
            UiText.Get("CellShift_Insert_EntireColumn"));
    }

    [Fact]
    public void GetAvailableChoices_UsesExcelDeleteLabels()
    {
        var choices = CellShiftDialog.GetAvailableChoices(CellShiftDialogMode.Delete);

        choices.Select(choice => choice.Label).Should().Equal(
            UiText.Get("CellShift_Delete_ShiftCellsLeft"),
            UiText.Get("CellShift_Delete_ShiftCellsUp"),
            UiText.Get("CellShift_Delete_EntireRow"),
            UiText.Get("CellShift_Delete_EntireColumn"));
    }

    [Fact]
    public void DialogButtons_ExposeKeyboardAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CellShiftDialog.cs"));

        source.Should().Contain("CellShiftDialogPlanner.GetAvailableChoices");
        source.Should().Contain("CellShiftDialogPlanner.ToKeyboardChoice");
        source.Should().NotContain("Choose how Excel should make room");
        source.Should().NotContain("Choose how Excel should close the gap");
        source.Should().Contain("DialogButtonRowFactory.Create");
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesDefaultShiftChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CellShiftDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_buttons.FirstOrDefault()?.Focus();");
        source.Should().Contain("Keyboard.Focus(firstButton);");
    }
}
