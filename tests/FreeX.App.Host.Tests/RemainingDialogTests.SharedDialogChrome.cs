using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void StatusDialogs_UseSharedExcelStyleButtonRows()
    {
        var source = ReadStatusDialogSources();

        source.Should().Contain("DialogButtonRowFactory.Create");
        source.Should().NotContain("InsertChartDialog.CreateButtonRow");
    }

    [Fact]
    public void RemainingNonChartDialogs_UseSharedExcelStyleButtonRows()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("DialogButtonRowFactory.Create(Accept, 72)");
        source.Should().NotContain("InsertChartDialog.CreateButtonRow");
    }

    [Fact]
    public void SingleInputMiniDialogs_UseAccessKeyedLabelsAndSharedButtonRows()
    {
        var remainingSource = ReadRemainingDialogSources();
        var objectSource = ReadObjectDialogSources();

        remainingSource.Should().Contain("UiText.Get(\"Remaining_FormatCellsGreaterThan\")");
        remainingSource.Should().Contain("UiText.Get(\"Remaining_RowHeight2\")");
        remainingSource.Should().Contain("UiText.Get(\"Remaining_ColumnWidth2\")");
        remainingSource.Should().Contain("UiText.Get(\"ForecastSheet_PeriodsLabel\")");
        remainingSource.Should().Contain("UiText.Get(\"SheetName_SheetName\")");
        remainingSource.Should().Contain("AutomationProperties.SetName(_thresholdBox, UiText.Get(\"Remaining_ConditionalFormatThreshold\"));");
        remainingSource.Should().Contain("AutomationProperties.SetName(_heightBox, UiText.Get(\"Remaining_RowHeight\"));");
        remainingSource.Should().Contain("AutomationProperties.SetName(_widthBox, UiText.Get(\"Remaining_ColumnWidth\"));");
        remainingSource.Should().Contain("AutomationProperties.SetName(_periodsBox, UiText.Get(\"ForecastSheet_PeriodsAutomationName\"));");
        remainingSource.Should().Contain("AutomationProperties.SetName(_nameBox, UiText.Get(\"SheetName_SheetName\"));");
        objectSource.Should().Contain("Target = box");
        objectSource.Should().Contain("DialogButtonRowFactory.Create(accept, 72)");
    }

    [Fact]
    public void MiniDialogs_UseSharedDialogChromeButtonRowsAndFocusHelpers()
    {
        var source = ReadMiniDialogSources();

        source.Should().Contain("using Free.Shared.Shell.Wpf;");
        source.Should().Contain("public sealed class ActivateSheetDialog : DialogWindow");
        source.Should().Contain("public sealed class UnhideSheetDialog : DialogWindow");
        source.Should().Contain("public sealed class UnhideWindowDialog : DialogWindow");
        source.Should().Contain("public sealed class AddWatchDialog : DialogWindow");
        source.Should().Contain("DialogButtonRowFactory.Create(_okButton, _cancelButton)");
        source.Should().Contain("var buttons = DialogButtonRowFactory.Create(");
        source.Should().Contain("new Thickness(0, AddWatchDialogPlanner.ActionRowTopMargin, 0, 0));");
        source.Should().Contain("DialogFocus.Focus(_sheetList);");
        source.Should().Contain("DialogFocus.Focus(_sheetBox);");
        source.Should().Contain("DialogFocus.Focus(_windowBox);");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rangeBox);");
        source.Should().NotContain("WindowStartupLocation = WindowStartupLocation.CenterOwner;");
        source.Should().NotContain("ShowInTaskbar = false;");
        source.Should().NotContain("Keyboard.Focus(");
    }

    [Fact]
    public void SharedDialogResources_UseDirectDynamicThemeKeysForBrushSetters()
    {
        var source = DialogSourceTestSupport.ReadShellSources("DialogResources.xaml");

        source.Should().Contain("{DynamicResource ThemeNeutralTextBrush}");
        source.Should().Contain("{DynamicResource ThemeAccentBrush}");
        source.Should().Contain("{DynamicResource ThemeAccentSoftBrush}");
        source.Should().Contain("{DynamicResource ThemeAccentPressedBrush}");
        source.Should().Contain("{DynamicResource ThemeAccentDarkBrush}");
        source.Should().NotContain("{StaticResource DialogText}");
        source.Should().NotContain("{StaticResource DialogAccent}");
        source.Should().NotContain("{StaticResource DialogHover}");
        source.Should().NotContain("{StaticResource DialogPressed}");
        source.Should().NotContain("{StaticResource DialogAccentDark}");
        source.Should().NotContain("{DynamicResource DialogText}");
        source.Should().NotContain("{DynamicResource DialogAccent}");
        source.Should().NotContain("{DynamicResource DialogHover}");
        source.Should().NotContain("{DynamicResource DialogPressed}");
        source.Should().NotContain("{DynamicResource DialogAccentDark}");
    }
}
