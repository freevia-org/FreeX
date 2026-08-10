using FluentAssertions;
using FreeX.App.Presentation.Editing;

namespace FreeX.App.Presentation.Tests.Editing;

public sealed class CellShiftDialogPlannerTests
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
    public void ToKeyboardChoice_MapsDialogChoicesToCommandChoices(
        CellShiftDialogMode mode,
        CellShiftDialogChoice choice,
        KeyboardInsertDeleteDialogChoice expected)
    {
        CellShiftDialogPlanner.ToKeyboardChoice(mode, choice).Should().Be(expected);
    }

    [Fact]
    public void GetAvailableChoices_ReturnsInsertChoicesWithLocalizationKeys()
    {
        CellShiftDialogPlanner.GetAvailableChoices(CellShiftDialogMode.Insert)
            .Should()
            .Equal(
                new CellShiftDialogOption(CellShiftDialogChoice.ShiftCellsRight, "CellShift_Insert_ShiftCellsRight"),
                new CellShiftDialogOption(CellShiftDialogChoice.ShiftCellsDown, "CellShift_Insert_ShiftCellsDown"),
                new CellShiftDialogOption(CellShiftDialogChoice.EntireRow, "CellShift_Insert_EntireRow"),
                new CellShiftDialogOption(CellShiftDialogChoice.EntireColumn, "CellShift_Insert_EntireColumn"));
    }

    [Fact]
    public void GetAvailableChoices_ReturnsDeleteChoicesWithLocalizationKeys()
    {
        CellShiftDialogPlanner.GetAvailableChoices(CellShiftDialogMode.Delete)
            .Should()
            .Equal(
                new CellShiftDialogOption(CellShiftDialogChoice.ShiftCellsLeft, "CellShift_Delete_ShiftCellsLeft"),
                new CellShiftDialogOption(CellShiftDialogChoice.ShiftCellsUp, "CellShift_Delete_ShiftCellsUp"),
                new CellShiftDialogOption(CellShiftDialogChoice.EntireRow, "CellShift_Delete_EntireRow"),
                new CellShiftDialogOption(CellShiftDialogChoice.EntireColumn, "CellShift_Delete_EntireColumn"));
    }

    [Theory]
    [InlineData(CellShiftDialogMode.Insert, "CellShift_InsertTitle", CellShiftDialogChoice.ShiftCellsRight, CellShiftDialogChoice.ShiftCellsDown)]
    [InlineData(CellShiftDialogMode.Delete, "CellShift_DeleteTitle", CellShiftDialogChoice.ShiftCellsLeft, CellShiftDialogChoice.ShiftCellsUp)]
    public void Surface_OwnsRendererTextAutomationAndCellSelectionChoices(
        CellShiftDialogMode mode,
        string expectedTitleKey,
        CellShiftDialogChoice firstChoice,
        CellShiftDialogChoice secondChoice)
    {
        var surface = CellShiftDialogPlanner.GetSurface(mode);
        surface.TitleKey.Should().Be(expectedTitleKey);
        surface.Options.Should().OnlyContain(option =>
            option.AutomationId == $"CellShift{option.Choice}Option" &&
            !string.IsNullOrWhiteSpace(option.AutomationName) &&
            !string.IsNullOrWhiteSpace(option.HelpText));
        CellShiftDialogPlanner.GetCellSelectionChoices(mode)
            .Select(option => option.Choice)
            .Should().Equal(firstChoice, secondChoice);
    }
}
