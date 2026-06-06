using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeNumberFormatCommandSourceTests
{
    [Theory]
    [InlineData("CurrencyBtn_Click", "Accounting Number Format", "AN")]
    [InlineData("PercentBtn_Click", "Percent Style", "P")]
    [InlineData("CommaStyleBtn_Click", "Comma Style", "K")]
    [InlineData("IncDecimalBtn_Click", "Increase Decimal Places", "QI")]
    [InlineData("DecDecimalBtn_Click", "Decrease Decimal Places", "QD")]
    public void HomeNumberRibbonButtons_KeepExpectedHandlersAndKeyTips(
        string clickHandler,
        string tooltipTitle,
        string keyTip)
    {
        var xaml = LocalizedXamlTestSupport.ReadMainWindowXaml();
        var button = xaml.ExtractButtonElementByClickHandler(clickHandler);

        button.Should().Contain($"Click=\"{clickHandler}\"");
        button.ShouldContainInvariantCommandName(tooltipTitle);
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
    }

    [Fact]
    public void HomeNumberFormatHandlers_ApplyExpectedStyleDiffs()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().Contain("private void CurrencyBtn_Click(object sender, RoutedEventArgs e)    => ApplyStyleDiff(new StyleDiff(NumberFormat: \"$#,##0.00\"));");
        source.Should().Contain("private void PercentBtn_Click(object sender, RoutedEventArgs e)     => ApplyStyleDiff(new StyleDiff(NumberFormat: \"0%\"));");
        source.Should().Contain("private void CommaStyleBtn_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(NumberFormat: \"#,##0.00\"));");
    }

    [Fact]
    public void HomeNumberFormatDropdown_ProjectsFormatCellsCatalogAndMoreNumberFormatsAction()
    {
        HomeNumberFormatDropdownPlanner.Options
            .Where(option => !option.OpensFormatCellsDialog)
            .Select(option => option.Label)
            .Should()
            .Contain(FormatCellsNumberFormatPlanner.Options.Select(option => option.Label).Distinct(StringComparer.OrdinalIgnoreCase));

        HomeNumberFormatDropdownPlanner.Options.Should().ContainSingle(option =>
            option.Label == HomeNumberFormatDropdownPlanner.MoreNumberFormatsLabel
            && option.Code == null
            && option.OpensFormatCellsDialog);
        HomeNumberFormatDropdownPlanner.Options.Last().OpensFormatCellsDialog.Should().BeTrue();
    }

    [Fact]
    public void HomeNumberFormatDropdown_SourceUsesProjectionPlannerAndOpensFormatCellsNumberTab()
    {
        var startupSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Startup.cs");
        var formattingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        startupSource.Should().Contain("HomeNumberFormatDropdownPlanner.Options.Select(option => option.Label)");
        formattingSource.Should().Contain("HomeNumberFormatDropdownPlanner.Options[selectedIndex]");
        formattingSource.Should().Contain("OpenFormatCellsDialog(FormatCellsDialogTab.Number)");
    }

    [Fact]
    public void DecimalPlaceHandlers_UseDecimalAdjusterThroughRepeatableStyleDiff()
    {
        var formattingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");
        var workbookUiStateSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookUiState.cs");

        formattingSource.Should().Contain("NumberFormatDecimalAdjuster.AddDecimalPlace(style.NumberFormat)");
        formattingSource.Should().Contain("NumberFormatDecimalAdjuster.RemoveDecimalPlace(style.NumberFormat)");
        formattingSource.Should().Contain("ApplyStyleDiff(new StyleDiff(NumberFormat:");
        workbookUiStateSource.Should().Contain("private void ApplyStyleDiff(StyleDiff diff)");
        workbookUiStateSource.Should().Contain("TryExecuteRepeatableApplyStyle(diff, \"Apply Style\")");
    }
}
