using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HomeNumberFormatCommandSourceTests
{

    [Fact]
    public void HomeNumberFormatHandlers_ApplyExpectedStyleDiffs()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");

        source.Should().Contain("private void CurrencyBtn_Click(object sender, RoutedEventArgs e)    => ApplyStyleDiff(new StyleDiff(NumberFormat: HomeNumberFormatDropdownPlanner.AccountingNumberFormatCode));");
        source.Should().Contain("private void PercentBtn_Click(object sender, RoutedEventArgs e)     => ApplyStyleDiff(new StyleDiff(NumberFormat: \"0%\"));");
        source.Should().Contain("private void CommaStyleBtn_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(NumberFormat: HomeNumberFormatDropdownPlanner.CommaStyleNumberFormatCode));");
        source.Should().Contain("HomeNumberFormatDropdownPlanner.ResolveAccountingNumberFormatCode(symbol)");
    }

    [Fact]
    public void HomeNumberFormatDropdown_UsesExcelLikeCompactCatalogAndMoreNumberFormatsAction()
    {
        HomeNumberFormatDropdownPlanner.Options
            .Where(option => !option.OpensFormatCellsDialog)
            .Select(option => option.Label)
            .Should()
            .Equal(
                "General",
                "Number",
                "Currency",
                "Accounting",
                "Short Date",
                "Long Date",
                "Time",
                "Percentage",
                "Fraction",
                "Scientific",
                "Text");

        HomeNumberFormatDropdownPlanner.Options.Should().ContainSingle(option =>
            option.Label == HomeNumberFormatDropdownPlanner.MoreNumberFormatsLabel
            && option.Code == null
            && option.OpensFormatCellsDialog);
        HomeNumberFormatDropdownPlanner.Options.Last().OpensFormatCellsDialog.Should().BeTrue();
        HomeNumberFormatDropdownPlanner.Options.Single(option => option.Label == "Accounting").Code
            .Should()
            .Be(HomeNumberFormatDropdownPlanner.AccountingNumberFormatCode);
    }

    [Fact]
    public void AccountingSymbolDropdown_UsesSharedCatalogAndFormatBuilder()
    {
        HomeNumberFormatDropdownPlanner.AccountingSymbolOptions
            .Select(option => (option.CommandId, option.Label, option.Symbol))
            .Should()
            .Equal(
                ("Accounting Number Format US Dollar", "US Dollar ($)", "$"),
                ("Accounting Number Format Euro", "Euro (EUR)", "\u20AC"),
                ("Accounting Number Format British Pound", "British Pound (GBP)", "\u00A3"),
                ("Accounting Number Format Japanese Yen", "Japanese Yen (JPY)", "\u00A5"));

        HomeNumberFormatDropdownPlanner.AccountingSymbolOptions
            .Should()
            .OnlyContain(option => option.NumberFormatCode == FormatCellsNumberFormatPlanner.BuildAccountingFormatFor(2, option.Symbol));

        HomeNumberFormatDropdownPlanner.ResolveAccountingNumberFormatCode(null)
            .Should()
            .Be(HomeNumberFormatDropdownPlanner.AccountingSymbolOptions.Single(option => option.Symbol == "$").NumberFormatCode);
        HomeNumberFormatDropdownPlanner.ResolveAccountingNumberFormatCode("CHF")
            .Should()
            .Be(FormatCellsNumberFormatPlanner.BuildAccountingFormatFor(2, "CHF"));
    }

    [Fact]
    public void HomeNumberFormatDropdown_SourceUsesPlannerAndOpensFormatCellsNumberTab()
    {
        var declarativeSource = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonDeclarative.cs");
        var formattingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");
        var definition = FreeXRibbonCompositionPlanner.Compose(FreeXRibbon.Build(), key => key);
        var numberFormatCombo = definition.FindTab(FreeXRibbonTabIds.Home)!
            .Groups.SelectMany(group => group.Controls)
            .OfType<RibbonComboBox>()
            .Single(control => control.CommandId.Value == "Number Format");

        declarativeSource.Should().Contain("FreeXRibbonCompositionPlanner.Compose(FreeXRibbon.Build(), UiText.Get)");
        numberFormatCombo.PresentationKind.Should().Be(RibbonComboBoxPresentationKind.Gallery);
        numberFormatCombo.Choices.Select(choice => (choice.Value, choice.Label)).Should().Equal(
            HomeNumberFormatDropdownPlanner.Options.Select(option => (option.Value, option.Label)));
        numberFormatCombo.Choices.Select(choice => choice.PreviewKind).Should().Equal(
            RibbonComboBoxGalleryPreviewKind.General,
            RibbonComboBoxGalleryPreviewKind.Number,
            RibbonComboBoxGalleryPreviewKind.Currency,
            RibbonComboBoxGalleryPreviewKind.Accounting,
            RibbonComboBoxGalleryPreviewKind.ShortDate,
            RibbonComboBoxGalleryPreviewKind.LongDate,
            RibbonComboBoxGalleryPreviewKind.Time,
            RibbonComboBoxGalleryPreviewKind.Percentage,
            RibbonComboBoxGalleryPreviewKind.Fraction,
            RibbonComboBoxGalleryPreviewKind.Scientific,
            RibbonComboBoxGalleryPreviewKind.Text,
            RibbonComboBoxGalleryPreviewKind.More);
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
