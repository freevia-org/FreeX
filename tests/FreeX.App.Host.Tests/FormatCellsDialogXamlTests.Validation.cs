using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using FreeX.App.Host;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

public sealed partial class FormatCellsDialogXamlTests
{
    [Fact]
    public void FormatCellsDialog_RejectsUnsupportedTextRotationWithOwnedWarning()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("FormatCells_InvalidTextRotationMessage");
        source.Should().Contain("FormatCellsDialogValidationTarget.TextRotation");
        source.Should().Contain("ShowInvalidInputWarning(message, DlgTextRotationBox);");
        source.Should().Contain("FormatCellsDialogPlannerTab.Alignment => (int)FormatCellsDialogTab.Alignment");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void FormatCellsDialog_RejectsInvalidFontSizeWithOwnedWarning()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("FormatCells_InvalidFontSizeMessage");
        source.Should().Contain("FormatCellsDialogValidationTarget.FontSize");
        source.Should().Contain("ShowInvalidInputWarning(message, DlgFontSizeBox);");
        source.Should().Contain("FormatCellsDialogPlannerTab.Font => (int)FormatCellsDialogTab.Font");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, ComboBox target)");
    }

    [Fact]
    public void FormatCellsDialog_RejectsInvalidIndentWithOwnedWarning()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("FormatCells_InvalidIndentLevelMessage");
        source.Should().Contain("FormatCellsDialogValidationTarget.IndentLevel");
        source.Should().Contain("ShowInvalidInputWarning(message, DlgIndentLevelBox);");
        source.Should().Contain("FormatCellsDialogPlannerTab.Alignment => (int)FormatCellsDialogTab.Alignment");
    }

    [Fact]
    public void FormatCellsDialog_RejectsInvalidDecimalPlacesWithOwnedWarning()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("FormatCellsDialogPlanner.TryCreateResult(");
        source.Should().Contain("FormatCells_InvalidDecimalPlacesMessage");
        source.Should().Contain("FormatCells_InvalidCustomNumberFormatMessage");
        source.Should().Contain("FormatCellsDialogValidationTarget.NumberDecimalPlaces");
        source.Should().Contain("FormatCellsDialogValidationTarget.NumberFormat");
        source.Should().Contain("ShowInvalidInputWarning(message, NumberFormatCombo);");
        source.Should().Contain("ShowInvalidInputWarning(message, NumberDecimalPlacesBox);");
    }

    [Fact]
    public void FormatCellsDialog_RejectsInvalidFontColorWithOwnedWarning()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("FormatCells_InvalidFontColorMessage");
        source.Should().Contain("FormatCellsDialogValidationTarget.FontColor");
        source.Should().Contain("ShowInvalidInputWarning(message, DlgFontColorBox);");
        source.Should().Contain("FormatCellsDialogPlannerTab.Font => (int)FormatCellsDialogTab.Font");
    }

    [Fact]
    public void FormatCellsDialog_RejectsInvalidFillColorsWithOwnedWarnings()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("FormatCells_InvalidFillColorMessage");
        source.Should().Contain("FormatCells_InvalidPatternColorMessage");
        source.Should().Contain("FormatCellsDialogValidationTarget.FillColor");
        source.Should().Contain("FormatCellsDialogValidationTarget.FillPatternColor");
        source.Should().Contain("ShowInvalidInputWarning(message, DlgFillColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(message, DlgFillPatternColorBox);");
        source.Should().Contain("FormatCellsDialogPlannerTab.Fill => (int)FormatCellsDialogTab.Fill");
    }

    [Fact]
    public void FormatCellsDialog_RejectsInvalidBorderColorsWithOwnedWarnings()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("FormatCells_InvalidBorderColorMessage");
        source.Should().Contain("FormatCells_InvalidTopBorderColorMessage");
        source.Should().Contain("FormatCells_InvalidRightBorderColorMessage");
        source.Should().Contain("FormatCells_InvalidBottomBorderColorMessage");
        source.Should().Contain("FormatCells_InvalidLeftBorderColorMessage");
        source.Should().Contain("FormatCellsDialogValidationTarget.BorderLineColor");
        source.Should().Contain("FormatCellsDialogValidationTarget.BorderTopColor");
        source.Should().Contain("FormatCellsDialogValidationTarget.BorderRightColor");
        source.Should().Contain("FormatCellsDialogValidationTarget.BorderBottomColor");
        source.Should().Contain("FormatCellsDialogValidationTarget.BorderLeftColor");
        source.Should().Contain("ShowInvalidInputWarning(message, DlgBorderLineColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(message, DlgBorderTopColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(message, DlgBorderRightColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(message, DlgBorderBottomColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(message, DlgBorderLeftColorBox);");
        source.Should().Contain("FormatCellsDialogPlannerTab.Border => (int)FormatCellsDialogTab.Border");
    }
}
