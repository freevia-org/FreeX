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
}
