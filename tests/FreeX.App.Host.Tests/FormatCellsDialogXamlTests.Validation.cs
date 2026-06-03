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

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidTextRotationMessage\"), DlgTextRotationBox);");
        source.Should().Contain("Tabs.SelectedIndex = (int)FormatCellsDialogTab.Alignment;");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void FormatCellsDialog_RejectsInvalidFontSizeWithOwnedWarning()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidFontSizeMessage\"), DlgFontSizeBox);");
        source.Should().Contain("Tabs.SelectedIndex = (int)FormatCellsDialogTab.Font;");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, ComboBox target)");
    }

    [Fact]
    public void FormatCellsDialog_RejectsInvalidIndentWithOwnedWarning()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidIndentLevelMessage\"), DlgIndentLevelBox);");
        source.Should().Contain("Tabs.SelectedIndex = (int)FormatCellsDialogTab.Alignment;");
    }

    [Fact]
    public void FormatCellsDialog_RejectsInvalidDecimalPlacesWithOwnedWarning()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("if (!ValidateNumberInputs())");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidDecimalPlacesMessage\"), NumberDecimalPlacesBox);");
        source.Should().Contain("FormatCellsInputParser.IsSupportedCustomNumberFormat(NumberFormatCombo.Text)");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidCustomNumberFormatMessage\"), NumberFormatCombo);");
        source.Should().Contain("Tabs.SelectedIndex = (int)FormatCellsDialogTab.Number;");
    }

    [Fact]
    public void FormatCellsDialog_RejectsInvalidFontColorWithOwnedWarning()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("if (!TryParseRequiredColor(DlgFontColorBox.Text, out var fontColor))");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidFontColorMessage\"), DlgFontColorBox);");
        source.Should().Contain("Tabs.SelectedIndex = (int)FormatCellsDialogTab.Font;");
    }

    [Fact]
    public void FormatCellsDialog_RejectsInvalidFillColorsWithOwnedWarnings()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("if (!TryParseOptionalColor(DlgFillColorBox.Text, out var fillColor))");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidFillColorMessage\"), DlgFillColorBox);");
        source.Should().Contain("if (!TryParseOptionalColor(DlgFillPatternColorBox.Text, out var fillPatternColor))");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidPatternColorMessage\"), DlgFillPatternColorBox);");
        source.Should().Contain("Tabs.SelectedIndex = (int)FormatCellsDialogTab.Fill;");
    }

    [Fact]
    public void FormatCellsDialog_RejectsInvalidBorderColorsWithOwnedWarnings()
    {
        var source = ReadFormatCellsDialogSource();

        source.Should().Contain("if (!ValidateBorderInputs())");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidBorderColorMessage\"), DlgBorderLineColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidTopBorderColorMessage\"), DlgBorderTopColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidRightBorderColorMessage\"), DlgBorderRightColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidBottomBorderColorMessage\"), DlgBorderBottomColorBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"FormatCells_InvalidLeftBorderColorMessage\"), DlgBorderLeftColorBox);");
        source.Should().Contain("Tabs.SelectedIndex = (int)FormatCellsDialogTab.Border;");
    }
}
