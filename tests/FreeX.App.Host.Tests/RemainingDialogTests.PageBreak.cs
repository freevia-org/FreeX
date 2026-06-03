using FluentAssertions;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void PageBreakDialog_CreateClearResult_RepresentsClearAll()
    {
        PageBreakDialog.CreateClearResult().Should().Be(new PageBreakDialogResult(PageBreakDialogAction.Clear, null, null));
    }

    [Fact]
    public void PageBreakDialog_TryCreateResult_ParsesRowAndColumnBreaks()
    {
        PageBreakDialog.TryCreateResult(" row 12 ", out var rowResult).Should().BeTrue();
        PageBreakDialog.TryCreateResult(" column 5 ", out var columnResult).Should().BeTrue();
        PageBreakDialog.TryCreateResult(" column C ", out var letterColumnResult).Should().BeTrue();

        rowResult.Should().Be(new PageBreakDialogResult(PageBreakDialogAction.AddRow, 12, null));
        columnResult.Should().Be(new PageBreakDialogResult(PageBreakDialogAction.AddColumn, null, 5));
        letterColumnResult.Should().Be(new PageBreakDialogResult(PageBreakDialogAction.AddColumn, null, 3));
    }

    [Theory]
    [InlineData("row 0")]
    [InlineData("row 1048577")]
    [InlineData("col 0")]
    [InlineData("col 16385")]
    [InlineData("column 0")]
    [InlineData("column XFE")]
    public void PageBreakDialog_TryCreateResult_RejectsOutOfWorksheetBreakEntries(string input)
    {
        PageBreakDialog.TryCreateResult(input, out _).Should().BeFalse();
    }

    [Fact]
    public void PageBreakDialog_ExposesExplicitExcelStyleActionsInsteadOfCommandText()
    {
        var source = ReadRemainingDialogSources();
        var pageBreakSource = source[source.IndexOf("public sealed class PageBreakDialog", StringComparison.Ordinal)..];

        pageBreakSource.Should().Contain("UiText.Get(\"PageBreak_InsertRowPageBreak\")");
        pageBreakSource.Should().Contain("UiText.Get(\"PageBreak_InsertColumnPageBreak\")");
        pageBreakSource.Should().Contain("UiText.Get(\"PageBreak_ResetAllPageBreaks\")");
        pageBreakSource.Should().Contain("_rowBreakBox");
        pageBreakSource.Should().Contain("_columnBreakBox");
        pageBreakSource.Should().NotContain("ObjectSizeDialog.CreateSingleInputContent(\"Page break:\"");
    }

    [Fact]
    public void PageBreakDialogOpenedFromKeyboard_FocusesSelectedBreakEntry()
    {
        var source = ReadClassSource("PageBreakDialog.cs", "public sealed class PageBreakDialog", "public sealed record __NoNextPageBreakDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rowBreakBox);");
        source.Should().Contain("DialogFocus.FocusAndSelect(_columnBreakBox);");
        source.Should().Contain("_resetAllButton.Focus();");
    }

    [Fact]
    public void PageBreakDialog_NumberInputsExposeAutomationMetadata()
    {
        var source = ReadClassSource("PageBreakDialog.cs", "public sealed class PageBreakDialog", "public sealed record __NoNextPageBreakDialog");

        source.Should().Contain("AutomationProperties.SetName(_rowBreakBox, UiText.Get(\"PageBreak_RowPageBreak\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_rowBreakBox, \"PageBreakRowBreakBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_rowBreakBox, UiText.Get(\"PageBreak_EnterTheRowNumberWhereTheHorizontalPageBreakShouldBeInserted\"));");
        source.Should().Contain("AutomationProperties.SetName(_columnBreakBox, UiText.Get(\"PageBreak_ColumnPageBreak\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_columnBreakBox, \"PageBreakColumnBreakBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_columnBreakBox, UiText.Get(\"PageBreak_EnterTheColumnNumberOrLetterWhereTheVerticalPageBreakShouldBeInserted\"));");
    }

    [Fact]
    public void PageBreakDialogInvalidBreakEntry_ShowsOwnedWarningAndRefocusesEntry()
    {
        var source = ReadClassSource("PageBreakDialog.cs", "public sealed class PageBreakDialog", "public sealed record __NoNextPageBreakDialog");

        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("UiText.Get(\"PageBreak_EnterARowNumberWithinTheWorksheetForThePageBreak\")");
        source.Should().Contain("UiText.Get(\"PageBreak_EnterAColumnNumberOrLetterWithinTheWorksheetForThePageBreak\")");
        source.Should().Contain("PageLayoutInputParser.IsValidRowBreak(rowBreak)");
        source.Should().Contain("FocusInvalidBreakInput(_rowBreakBox);");
        source.Should().Contain("FocusInvalidBreakInput(_columnBreakBox);");
        source.Should().Contain("private static void FocusInvalidBreakInput(TextBox textBox)");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void PageBreakDialog_EnablesOnlyTheSelectedBreakEntry()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PageBreakDialog("row 12");

            var rowButton = GetField<RadioButton>(dialog, "_insertRowButton");
            var columnButton = GetField<RadioButton>(dialog, "_insertColumnButton");
            var resetButton = GetField<RadioButton>(dialog, "_resetAllButton");
            var rowBox = GetField<TextBox>(dialog, "_rowBreakBox");
            var columnBox = GetField<TextBox>(dialog, "_columnBreakBox");

            rowButton.IsChecked.Should().BeTrue();
            rowBox.IsEnabled.Should().BeTrue();
            columnBox.IsEnabled.Should().BeFalse();

            columnButton.IsChecked = true;
            rowBox.IsEnabled.Should().BeFalse();
            columnBox.IsEnabled.Should().BeTrue();

            resetButton.IsChecked = true;
            rowBox.IsEnabled.Should().BeFalse();
            columnBox.IsEnabled.Should().BeFalse();
        });
    }

    [Fact]
    public void PageBreakDialog_UpdateBreakInputAvailabilityTracksExcelActionChoice()
    {
        var source = ReadClassSource("PageBreakDialog.cs", "public sealed class PageBreakDialog", "public sealed record __NoNextPageBreakDialog");

        source.Should().Contain("_insertRowButton.Checked += (_, _) => UpdateBreakInputAvailability();");
        source.Should().Contain("_insertColumnButton.Checked += (_, _) => UpdateBreakInputAvailability();");
        source.Should().Contain("_resetAllButton.Checked += (_, _) => UpdateBreakInputAvailability();");
        source.Should().Contain("private void UpdateBreakInputAvailability()");
        source.Should().Contain("_rowBreakBox.IsEnabled = _insertRowButton.IsChecked == true;");
        source.Should().Contain("_columnBreakBox.IsEnabled = _insertColumnButton.IsChecked == true;");
    }
}
