using FluentAssertions;
using System.Windows.Controls;

using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
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
        pageBreakSource.Should().Contain("PageBreakDialogPlanner.CreateClearResult()");
        pageBreakSource.Should().Contain("PageBreakDialogPlanner.TryCreateResult(defaultValue, out var result)");
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
        source.Should().Contain("PageBreakDialogPlanner.TryCreateResult(action, _rowBreakBox.Text, _columnBreakBox.Text, out var result)");
        source.Should().Contain("FocusInvalidBreakInput(_rowBreakBox);");
        source.Should().Contain("FocusInvalidBreakInput(_columnBreakBox);");
        source.Should().Contain("private static void FocusInvalidBreakInput(TextBox textBox)");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
        source.Should().NotContain("uint.TryParse(_rowBreakBox.Text");
        source.Should().NotContain("PageLayoutInputParser.TryParseColumnBreakValue");
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
