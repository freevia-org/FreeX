using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Presentation.Editing;

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

        choices.Select(choice => UiText.Get(choice.LabelKey)).Should().Equal(
            UiText.Get("CellShift_Insert_ShiftCellsRight"),
            UiText.Get("CellShift_Insert_ShiftCellsDown"),
            UiText.Get("CellShift_Insert_EntireRow"),
            UiText.Get("CellShift_Insert_EntireColumn"));
    }

    [Fact]
    public void GetAvailableChoices_UsesExcelDeleteLabels()
    {
        var choices = CellShiftDialog.GetAvailableChoices(CellShiftDialogMode.Delete);

        choices.Select(choice => UiText.Get(choice.LabelKey)).Should().Equal(
            UiText.Get("CellShift_Delete_ShiftCellsLeft"),
            UiText.Get("CellShift_Delete_ShiftCellsUp"),
            UiText.Get("CellShift_Delete_EntireRow"),
            UiText.Get("CellShift_Delete_EntireColumn"));
    }

    [Fact]
    public void DialogButtons_ExposeKeyboardAccessKeys()
    {
        var source = ReadCellShiftDialogSource();

        source.Should().Contain("CellShiftDialogPlanner.GetSurface");
        source.Should().Contain("CellShiftDialogPlanner.ToKeyboardChoice");
        source.Should().Contain("Content = UiText.Get(option.LabelKey)");
        source.Should().Contain("AutomationProperties.SetAutomationId(button, option.AutomationId)");
        source.Should().NotContain("GetChoiceAutomationName");
        source.Should().NotContain("GetChoiceHelpText");
        source.Should().NotContain("Choose how Excel should make room");
        source.Should().NotContain("Choose how Excel should close the gap");
        source.Should().Contain("DialogButtonRowFactory.Create");
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesDefaultShiftChoice()
    {
        var source = ReadCellShiftDialogSource();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("var firstButton = FindFirstButton();");
        source.Should().Contain("firstButton?.Focus();");
        source.Should().Contain("private RadioButton? FindFirstButton()");
        source.Should().Contain("_buttons.Count > 0 ? _buttons[0] : null");
        source.Should().Contain("Keyboard.Focus(firstButton);");
    }

    [Theory]
    [InlineData(CellShiftDialogMode.Insert)]
    [InlineData(CellShiftDialogMode.Delete)]
    public void DialogChoiceButtons_ExposeAutomationMetadata(CellShiftDialogMode mode)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new CellShiftDialog(mode);
            try
            {
                var buttons = GetButtons(dialog);
                (CellShiftDialogChoice Choice, string Name, string HelpText)[] expected = mode == CellShiftDialogMode.Insert
                    ?
                    [
                        (CellShiftDialogChoice.ShiftCellsRight, "Shift cells right", "Insert cells and shift existing cells to the right."),
                        (CellShiftDialogChoice.ShiftCellsDown, "Shift cells down", "Insert cells and shift existing cells down."),
                        (CellShiftDialogChoice.EntireRow, "Entire row", "Apply the operation to the entire selected row."),
                        (CellShiftDialogChoice.EntireColumn, "Entire column", "Apply the operation to the entire selected column.")
                    ]
                    :
                    [
                        (CellShiftDialogChoice.ShiftCellsLeft, "Shift cells left", "Delete cells and shift remaining cells left."),
                        (CellShiftDialogChoice.ShiftCellsUp, "Shift cells up", "Delete cells and shift remaining cells up."),
                        (CellShiftDialogChoice.EntireRow, "Entire row", "Apply the operation to the entire selected row."),
                        (CellShiftDialogChoice.EntireColumn, "Entire column", "Apply the operation to the entire selected column.")
                    ];

                buttons.Should().HaveCount(expected.Length);
                for (var index = 0; index < expected.Length; index++)
                {
                    var (choice, name, helpText) = expected[index];
                    var button = buttons[index];
                    button.Tag.Should().Be(choice);
                    AutomationProperties.GetName(button).Should().Be(name);
                    AutomationProperties.GetAutomationId(button).Should().Be($"CellShift{choice}Option");
                    AutomationProperties.GetHelpText(button).Should().Be(helpText);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static string ReadCellShiftDialogSource() =>
        DialogSourceTestSupport.ReadHostSources("CellShiftDialog.cs");

    private static IReadOnlyList<RadioButton> GetButtons(CellShiftDialog dialog)
        => DialogSourceTestSupport.GetPrivateField<List<RadioButton>>(dialog, "_buttons");
}
