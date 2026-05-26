using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class PrintPreviewSettingsPanelFactoryTests
{
    [Fact]
    public void Build_InitializesControlsFromSheetPrintSettings()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = CreateSheet();
            sheet.PageOrientation = WorksheetPageOrientation.Landscape;
            sheet.PaperSize = WorksheetPaperSize.Legal;
            sheet.PageMargins = WorksheetPageMargins.Wide;
            sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, null);
            sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
            sheet.PrintGridlines = true;
            sheet.PrintHeadings = false;

            var panel = PrintPreviewSettingsPanelFactory.Build(sheet.Id, sheet, _ => { }, () => { }, _ => { });

            ComboBoxes(panel).Select(box => box.SelectedIndex).Should().Equal(1, 2, 2, 2);
            CheckBoxes(panel).Select(box => box.IsChecked).Should().Equal(false, true, false);
            CheckBoxes(panel)[0].IsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void OrientationSelection_DispatchesSetPageOrientationCommandAndRefreshes()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = CreateSheet();
            var commands = new List<IWorkbookCommand>();
            var refreshes = 0;
            var panel = PrintPreviewSettingsPanelFactory.Build(sheet.Id, sheet, commands.Add, () => refreshes++);

            ComboBoxes(panel)[0].SelectedIndex = 1;

            commands.Should().ContainSingle().Which.Should().BeOfType<SetPageOrientationCommand>();
            commands[0].Label.Should().Be("Page Orientation");
            refreshes.Should().Be(1);
        });
    }

    [Fact]
    public void MarginsSelection_DispatchesSetPageMarginsCommandAndRefreshes()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = CreateSheet();
            var commands = new List<IWorkbookCommand>();
            var refreshes = 0;
            var panel = PrintPreviewSettingsPanelFactory.Build(sheet.Id, sheet, commands.Add, () => refreshes++);

            ComboBoxes(panel)[2].SelectedIndex = 1;

            commands.Should().ContainSingle().Which.Should().BeOfType<SetPageMarginsCommand>();
            commands[0].Label.Should().Be("Page Margins");
            refreshes.Should().Be(1);
        });
    }

    [Fact]
    public void ScalingSelection_DispatchesSetScaleToFitCommandAndRefreshes()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = CreateSheet();
            var commands = new List<IWorkbookCommand>();
            var refreshes = 0;
            var panel = PrintPreviewSettingsPanelFactory.Build(sheet.Id, sheet, commands.Add, () => refreshes++);

            ComboBoxes(panel)[3].SelectedIndex = 1;

            commands.Should().ContainSingle().Which.Should().BeOfType<SetScaleToFitCommand>();
            commands[0].Label.Should().Be("Scale To Fit");
            refreshes.Should().Be(1);
        });
    }

    [Fact]
    public void IgnorePrintAreaToggle_ReportsPreviewSettingsOnlyWhenCallbackProvided()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = CreateSheet();
            sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
            var settings = new List<PrintPreviewSettings>();
            var refreshes = 0;
            var disabledPanel = PrintPreviewSettingsPanelFactory.Build(sheet.Id, sheet, _ => { }, () => { });
            var enabledPanel = PrintPreviewSettingsPanelFactory.Build(sheet.Id, sheet, _ => { }, () => refreshes++, settings.Add);

            CheckBoxes(disabledPanel)[0].IsEnabled.Should().BeFalse();
            var ignorePrintArea = CheckBoxes(enabledPanel)[0];
            ignorePrintArea.IsEnabled.Should().BeTrue();
            ignorePrintArea.IsChecked = true;

            settings.Should().ContainSingle().Which.IgnorePrintArea.Should().BeTrue();
            refreshes.Should().Be(1);
        });
    }

    [Fact]
    public void PrintOptionsToggles_DispatchSetPrintOptionsCommandWithCombinedCheckboxState()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = CreateSheet();
            sheet.PrintHeadings = true;
            var commands = new List<IWorkbookCommand>();
            var refreshes = 0;
            var panel = PrintPreviewSettingsPanelFactory.Build(sheet.Id, sheet, commands.Add, () => refreshes++);

            CheckBoxes(panel)[1].IsChecked = true;
            CheckBoxes(panel)[2].IsChecked = false;

            commands.Should().HaveCount(2);
            commands.Should().OnlyContain(command => command is SetPrintOptionsCommand);
            refreshes.Should().Be(2);
        });
    }

    private static Sheet CreateSheet() =>
        new Workbook("Book1").AddSheet("Sheet1");

    private static IReadOnlyList<ComboBox> ComboBoxes(Panel panel) =>
        panel.Children.OfType<ComboBox>().ToList();

    private static IReadOnlyList<CheckBox> CheckBoxes(Panel panel) =>
        panel.Children.OfType<CheckBox>().ToList();
}
