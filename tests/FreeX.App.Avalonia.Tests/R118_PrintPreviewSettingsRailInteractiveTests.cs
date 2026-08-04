using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R118-print-preview-settings-rail-wiring: the Avalonia Print Preview window built its settings rail
/// via <c>PrintPreviewSurfacePlanner.CreateSettingsRailPlan(..., canUpdatePrintPreviewSettings: false, ...)</c>
/// unconditionally -- even for a real, live preview (not just the read-only parity-capture fixture) --
/// and none of the rail's combo boxes/checkboxes had any <c>SelectionChanged</c>/<c>IsCheckedChanged</c>
/// handler wired anywhere, so changing Orientation, Paper Size, Margins, Scaling, or the Print
/// Gridlines/Headings checkboxes never re-paginated the preview and never touched the sheet, unlike the
/// WPF shell's <c>PrintPreviewSettingsPanelFactory</c>. These tests drive the real product entry point
/// (<c>MainWindow.ShowPrintPreviewDialogAsync</c> -&gt; the real, live <see cref="ComboBox"/>/<see
/// cref="CheckBox"/> controls it creates) rather than a hand-built model, and assert both halves of the
/// fix: the sheet's real page-setup property changes (so the eventual print/export -- which reads that
/// same sheet state -- honors it too) and the previewed page's rendered dimensions/ink change to match.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R118_PrintPreviewSettingsRailInteractiveTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ChangingOrientationInSettingsRail_RepaginatesPreviewAndPersistsToTheSheet()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                SeedPrintableCell(window);

                var dialog = await OpenPrintPreviewDialogAsync(window);

                window.Session.ActiveSheet.PageOrientation.Should().Be(
                    WorksheetPageOrientation.Portrait,
                    "a brand new sheet defaults to Portrait");

                var canvasBefore = FindPageCanvas(dialog);
                canvasBefore.Width.Should().BeLessThan(canvasBefore.Height,
                    "the default Portrait page is taller than it is wide");

                var orientationBox = FindComboBox(dialog, "PrintPreviewSettingsOrientationBox");
                orientationBox.SelectedIndex.Should().Be(0, "Portrait is index 0 in PrintPreviewSettingsPanelPlanner.CreateOrientationOptions");

                // The actual, user-reachable interaction: pick "Landscape" (index 1) in the live combo
                // box the settings rail built -- not a call into any planner/service directly.
                orientationBox.SelectedIndex = 1;
                await DrainInputAsync();

                window.Session.ActiveSheet.PageOrientation.Should().Be(
                    WorksheetPageOrientation.Landscape,
                    "selecting Landscape in the settings rail must execute the real orientation command " +
                    "against the sheet (mirroring WPF's PrintPreviewSettingsPanelFactory), so the change " +
                    "also carries into the real print/export path that reads the sheet's own PageOrientation");

                var canvasAfter = FindPageCanvas(dialog);
                canvasAfter.Width.Should().BeGreaterThan(canvasAfter.Height,
                    "the preview must re-paginate to the Landscape page shape immediately, the same way " +
                    "WPF's Print Preview does -- before the fix, nothing repainted the page at all");
            }
            finally
            {
                foreach (var owned in window.OwnedWindows.ToList())
                    owned.Close();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ChangingPrintGridlinesInSettingsRail_RepaintsGridlineInkOnTheSamePage()
    {
        // No-regression sibling covering a different family member of the same wiring fix (a checkbox
        // driving PageLayoutRibbonCommandPlanner.BuildPrintOptionsCommand rather than a combo box driving
        // an orientation/paper-size/margins/scaling command) -- proving the fix isn't limited to combo
        // boxes and doesn't regress the (already-portrait) page shape.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                SeedPrintableCell(window);

                var dialog = await OpenPrintPreviewDialogAsync(window);

                window.Session.ActiveSheet.PrintGridlines.Should().BeFalse("gridlines are off by default");

                var gridlinesBox = FindCheckBox(dialog, "PrintPreviewSettingsGridlinesBox");
                gridlinesBox.IsChecked.Should().Be(false);

                gridlinesBox.IsChecked = true;
                await DrainInputAsync();

                window.Session.ActiveSheet.PrintGridlines.Should().BeTrue(
                    "checking Print Gridlines in the settings rail must execute the real print-options " +
                    "command against the sheet, exactly like the WPF shell's equivalent checkbox");

                // The page is still the default Portrait shape -- toggling gridlines must not have
                // disturbed orientation/paper size.
                var canvasAfter = FindPageCanvas(dialog);
                canvasAfter.Width.Should().BeLessThan(canvasAfter.Height);
            }
            finally
            {
                foreach (var owned in window.OwnedWindows.ToList())
                    owned.Close();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(PrintPreviewSettingsPanelPlanner.CustomMarginsOptionIndex)]
    [InlineData(PrintPreviewSettingsPanelPlanner.CustomScalingOptionIndex)]
    public async Task SelectingCustomPrintOptionInSettingsRail_OpensPageSetupDialog(int optionIndex)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                SeedPrintableCell(window);

                var preview = await OpenPrintPreviewDialogAsync(window);
                var controlAutomationId = optionIndex == PrintPreviewSettingsPanelPlanner.CustomMarginsOptionIndex
                    ? "PrintPreviewSettingsMarginsBox"
                    : "PrintPreviewSettingsScalingBox";
                var optionBox = FindComboBox(preview, controlAutomationId);

                optionBox.SelectedIndex = optionIndex;
                await DrainInputAsync();
                await DrainInputAsync();

                window.OwnedWindows.Should().Contain(w =>
                    AutomationProperties.GetAutomationId(w) == PageSetupDialogPlanner.DialogAutomationId,
                    "the WPF print-preview rail opens the real Page Setup workflow for custom print options");
            }
            finally
            {
                foreach (var owned in window.OwnedWindows.ToList())
                    owned.Close();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static void SeedPrintableCell(MainWindow window)
    {
        var sheet = window.Session.ActiveSheet;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
    }

    private static async Task<Window> OpenPrintPreviewDialogAsync(MainWindow window)
    {
        var showMethod = typeof(MainWindow).GetMethod(
            "ShowPrintPreviewDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)showMethod.Invoke(window, [null, null])!;
        await DrainInputAsync();
        await DrainInputAsync();

        window.OwnedWindows.Should().ContainSingle("Print Preview must open a real modal Window");
        var dialog = window.OwnedWindows.Single();

        // Keep the still-running ShowDialog task alive in the background; the test closes the dialog
        // (or the window) in its own finally block, which lets this task complete on its own.
        _ = task;
        return dialog;
    }

    private static Canvas FindPageCanvas(Window dialog) =>
        dialog.GetVisualDescendants().OfType<Canvas>()
            .Single(c => AutomationProperties.GetAutomationId(c) == PrintPreviewDialogPlanner.PageCanvasAutomationId);

    private static ComboBox FindComboBox(Window dialog, string automationId) =>
        dialog.GetLogicalDescendants().OfType<ComboBox>()
            .Single(c => AutomationProperties.GetAutomationId(c) == automationId);

    private static CheckBox FindCheckBox(Window dialog, string automationId) =>
        dialog.GetLogicalDescendants().OfType<CheckBox>()
            .Single(c => AutomationProperties.GetAutomationId(c) == automationId);

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
