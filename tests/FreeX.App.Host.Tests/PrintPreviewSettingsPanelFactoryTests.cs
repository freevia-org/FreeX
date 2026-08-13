using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PrintPreviewSettingsPanelFactoryTests
{
    // Panel ComboBox layout (by index):
    //   0 printerBox  1 printWhatBox  2 sidesBox  3 collatedBox
    //   4 orientBox   5 paperBox      6 marginsBox 7 scaleBox
    // Panel CheckBox layout (by index):
    //   0 ignorePrintAreaBox  1 gridlinesBox  2 headingsBox

    [Fact]
    public void Build_DelegatesPageLayoutCommandConstructionToSharedPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PrintPreviewSettingsPanelFactory.cs");

        source.Should().Contain("PrintPreviewSettingsPanelPlanner.CreateOrientationAction(");
        source.Should().Contain("PrintPreviewSettingsPanelPlanner.CreatePaperSizeAction(");
        source.Should().Contain("PrintPreviewSettingsPanelPlanner.CreateMarginsAction(");
        source.Should().Contain("PrintPreviewSettingsPanelPlanner.CreateScalingAction(");
        source.Should().Contain("PrintPreviewSettingsPanelPlanner.CreatePrintOptionsAction(");
        source.Should().NotContain("PageLayoutRibbonCommandPlanner.Build");
        source.Should().NotContain("new SetPageOrientationCommand(");
        source.Should().NotContain("new SetPaperSizeCommand(");
        source.Should().NotContain("new SetPageMarginsCommand(");
        source.Should().NotContain("new SetScaleToFitCommand(");
        source.Should().NotContain("new SetPrintOptionsCommand(");
    }

    [Fact]
    public void Build_DelegatesVisibleRailTextToSharedSurfacePlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PrintPreviewSettingsPanelFactory.cs");

        source.Should().Contain("PrintPreviewSurfacePlanner.CreateSettingsRailPlan(");
        source.Should().Contain("AddLabel(railPlan.CopiesSectionText, copiesUpDown);");
        source.Should().Contain("AddLabel(railPlan.PrintWhatLabelText, printWhatBox);");
        source.Should().Contain("AddLabel(railPlan.OrientationLabelText, orientBox);");
        source.Should().Contain("Content = railPlan.PrintGridlinesText");
        source.Should().Contain("Content = railPlan.PrintHeadingsText");
        source.Should().Contain("Content = railPlan.PageSetupLinkText");

        source.Should().NotContain("AddLabel(UiText.Get(\"PrintPreview_CopiesSectionLabel\")");
        source.Should().NotContain("AddLabel(UiText.Get(\"PrintPreview_PrintWhatLabel\")");
        source.Should().NotContain("Content = UiText.Get(\"PageSetup_PrintGridlines\")");
        source.Should().NotContain("Content = UiText.Get(\"PageSetup_PrintRowAndColumnHeadings\")");
    }

    [Fact]
    public void Build_InitializesOrientationPaperMarginScaleFromSheetPrintSettings()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = CreateSheet();
            sheet.PageOrientation = WorksheetPageOrientation.Landscape;
            sheet.PaperSize = WorksheetPaperSize.Legal;
            sheet.PageMargins = WorksheetPageMargins.Wide;
            sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, null); // FitColumns → index 2
            sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
            sheet.PrintGridlines = true;
            sheet.PrintHeadings = false;

            var panel = PrintPreviewSettingsPanelFactory.Build(sheet.Id, sheet, _ => { }, () => { }, _ => { });
            var combos = ComboBoxes(panel);

            // orientBox (index 4): Landscape → 1
            combos[4].SelectedIndex.Should().Be(1, "orientation should be Landscape");
            // paperBox (index 5): Legal → 2
            combos[5].SelectedIndex.Should().Be(2, "paper size should be Legal");
            // marginsBox (index 6): Wide → 2
            combos[6].SelectedIndex.Should().Be(2, "margins should be Wide");
            // scaleBox (index 7): FitColumns → 2
            combos[7].SelectedIndex.Should().Be(2, "scaling should be Fit All Columns on One Page");

            // CheckBoxes: ignorePrintArea enabled (has print area), gridlines true, headings false
            var checkboxes = CheckBoxes(panel);
            checkboxes[0].IsEnabled.Should().BeTrue("ignore print area should be enabled when sheet has a print area");
            checkboxes[1].IsChecked.Should().BeTrue("gridlines should be checked");
            checkboxes[2].IsChecked.Should().BeFalse("headings should be unchecked");
        });
    }

    [Fact]
    public void Build_InitializesDefaultPrintJobSettings()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = CreateSheet();
            var panel = PrintPreviewSettingsPanelFactory.Build(sheet.Id, sheet, _ => { }, () => { }, _ => { });
            var combos = ComboBoxes(panel);

            // printWhatBox (index 1): default = ActiveSheets (0)
            combos[1].SelectedIndex.Should().Be(0, "print what should default to Active Sheets");
            // sidesBox (index 2): default = OneSided (0)
            combos[2].SelectedIndex.Should().Be(0, "sides should default to one sided");
            // collatedBox (index 3): default = Collated (0)
            combos[3].SelectedIndex.Should().Be(0, "collation should default to collated");
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

            // orientBox is at index 4
            ComboBoxes(panel)[4].SelectedIndex = 1;

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

            // marginsBox is at index 6
            var marginsBox = ComboBoxes(panel)[6];
            marginsBox.SelectedIndex.Should().Be(1, "a new sheet starts with Normal margins");
            marginsBox.SelectedIndex = 2;

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

            // scaleBox is at index 7
            ComboBoxes(panel)[7].SelectedIndex = 1;

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

    [Fact]
    public void PrintWhatSelection_ReportsUpdatedSettings()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = CreateSheet();
            var settings = new List<PrintPreviewSettings>();
            var panel = PrintPreviewSettingsPanelFactory.Build(
                sheet.Id, sheet, _ => { }, () => { }, settings.Add, hasSelection: true);

            // printWhatBox is at index 1; select EntireWorkbook (1)
            ComboBoxes(panel)[1].SelectedIndex = 1;

            settings.Should().ContainSingle().Which.PrintWhat.Should().Be(PrintWhat.EntireWorkbook);
        });
    }

    [Fact]
    public void SidesSelection_ReportsUpdatedSettings()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = CreateSheet();
            var settings = new List<PrintPreviewSettings>();
            var panel = PrintPreviewSettingsPanelFactory.Build(
                sheet.Id, sheet, _ => { }, () => { }, settings.Add);

            // sidesBox is at index 2; select TwoSidedLongEdge (1)
            ComboBoxes(panel)[2].SelectedIndex = 1;

            settings.Should().ContainSingle().Which.Sides.Should().Be(PrintPreviewSidesMode.TwoSidedLongEdge);
        });
    }

    [Fact]
    public void CollationSelection_ReportsUpdatedSettings()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = CreateSheet();
            var settings = new List<PrintPreviewSettings>();
            var panel = PrintPreviewSettingsPanelFactory.Build(
                sheet.Id, sheet, _ => { }, () => { }, settings.Add);

            // collatedBox is at index 3; select Uncollated (1)
            ComboBoxes(panel)[3].SelectedIndex = 1;

            settings.Should().ContainSingle().Which.Collated.Should().BeFalse();
        });
    }

    [Fact]
    public void PageSetupLink_IsPresent()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = CreateSheet();
            var pageSetupCalls = 0;
            var panel = PrintPreviewSettingsPanelFactory.Build(
                sheet.Id, sheet, _ => { }, () => { },
                showPageSetup: () => pageSetupCalls++);

            // There should be at least one Button with "Page Setup" automation name.
            var buttons = panel.Children.OfType<Button>().ToList();
            buttons.Should().NotBeEmpty("panel should contain the Page Setup link button");
        });
    }

    private static Sheet CreateSheet() =>
        new Workbook("Book1").AddSheet("Sheet1");

    private static IReadOnlyList<ComboBox> ComboBoxes(Panel panel) =>
        panel.Children.OfType<ComboBox>().ToList();

    private static IReadOnlyList<CheckBox> CheckBoxes(Panel panel) =>
        panel.Children.OfType<CheckBox>().ToList();
}
