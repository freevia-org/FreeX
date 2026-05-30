using System.IO;
using System.Reflection;
using System.Windows.Automation;
using System.Windows.Controls;
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
            "Shift cells _right",
            "Shift cells _down",
            "Entire _row",
            "Entire _column");
    }

    [Fact]
    public void GetAvailableChoices_UsesExcelDeleteLabels()
    {
        var choices = CellShiftDialog.GetAvailableChoices(CellShiftDialogMode.Delete);

        choices.Select(choice => choice.Label).Should().Equal(
            "Shift cells _left",
            "Shift cells _up",
            "Entire _row",
            "Entire _column");
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

    private static IReadOnlyList<RadioButton> GetButtons(CellShiftDialog dialog)
    {
        var field = typeof(CellShiftDialog).GetField("_buttons", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeAssignableTo<IReadOnlyList<RadioButton>>().Subject;
    }
}
