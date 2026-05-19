using FluentAssertions;

namespace Freexcel.App.Host.Tests;

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
    }

    [Fact]
    public void GetAvailableChoices_UsesExcelInsertLabels()
    {
        var choices = CellShiftDialog.GetAvailableChoices(CellShiftDialogMode.Insert);

        choices.Select(choice => choice.Label).Should().Equal(
            "Shift cells right",
            "Shift cells down",
            "Entire row",
            "Entire column");
    }

    [Fact]
    public void GetAvailableChoices_UsesExcelDeleteLabels()
    {
        var choices = CellShiftDialog.GetAvailableChoices(CellShiftDialogMode.Delete);

        choices.Select(choice => choice.Label).Should().Equal(
            "Shift cells left",
            "Shift cells up",
            "Entire row",
            "Entire column");
    }
}
