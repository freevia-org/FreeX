using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class KeyboardFileBackstageParityTests
{
    [Fact]
    public void MouseOpenRailEntry_NavigatesToOpenPaneWithoutLaunchingPicker()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            var sheet = harness.Workbook.Sheets.Single();
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("keep"));
            harness.OpenBackstage();

            var open = harness.RailButton("BackstageOpenButton");
            open.Should().NotBeNull();
            harness.Click(open!);

            harness.CurrentEntryId.Should().Be("freex.backstage.pane.open");
            harness.HasAutomationId("BackstageOpenBrowseButton").Should().BeTrue();
            harness.Workbook.Sheets.Single().GetCell(address)!.Value.Should().Be(new TextValue("keep"));
        });
    }

    [Fact]
    public void KeyboardOpen_RoutesToOpenPaneWithoutChangingWorkbook()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            var sheet = harness.Workbook.Sheets.Single();
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("keep"));
            var activeCellBefore = harness.ActiveCell;

            harness.ExecuteOpenShortcut();

            harness.IsBackstageVisible.Should().BeTrue();
            harness.CurrentEntryId.Should().Be("freex.backstage.pane.open");
            harness.HasAutomationId("BackstageOpenBrowseButton").Should().BeTrue();
            harness.HasAutomationId("BackstageOpenRecentLocation").Should().BeTrue();
            sheet.GetCell(address)!.Value.Should().Be(new TextValue("keep"));

            harness.PressKeyOnFrame(Key.Escape);

            harness.IsBackstageVisible.Should().BeFalse();
            harness.CurrentEntryId.Should().Be("freex.backstage.pane.open");
            harness.ActiveCell.Should().Be(activeCellBefore);
            harness.Workbook.Sheets.Single().GetCell(address)!.Value.Should().Be(new TextValue("keep"));
        });

        var routing = DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs");
        routing.Should().Contain("invocation.Route.Source == WorkbookApplicationCommandSource.KeyboardShortcut")
            .And.Contain("OpenWorkbookBackstageForKeyboard();")
            .And.Contain("OpenButton_Click(NativeSource(invocation), RoutedArgs(invocation));");

        var backstage = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs", "MainWindow.BackstageFrame.cs");
        backstage.Should().Contain("GetPaneStableId(FreeXBackstagePaneId.Open)")
            .And.Contain("browse.Click += (_, _) => RunGuardedUiCommand(\"Browse for Workbook\", OpenFromBackstageAsync);");
    }

    [Fact]
    public void SaveAsKeyTip_FocusesExistingActionAndEscapeDoesNotChangeWorkbook()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            var sheet = harness.Workbook.Sheets.Single();
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("keep"));
            harness.OpenBackstage();

            harness.FocusSaveAsFromKeyTip().Should().BeTrue();
            harness.IsRailButtonFocused("BackstageSaveAsButton").Should().BeTrue();
            harness.IsBackstageVisible.Should().BeTrue("the key tip enters the backstage destination without launching the picker");

            harness.PressKeyOnFrame(Key.Escape);

            harness.IsBackstageVisible.Should().BeFalse();
            harness.Workbook.Sheets.Single().GetCell(address)!.Value.Should().Be(new TextValue("keep"));
        });

        var keyTips = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyTips.cs", "MainWindow.BackstageFrame.cs");
        keyTips.Should().Contain("TryFocusBackstageSaveAsKeyTip(button)")
            .And.Contain("BackstageSaveAsButton")
            .And.Contain("SaveWorkbookAsAsync: () => RunBackstageCommand(() => SaveAsButton_Click(this, new RoutedEventArgs()))");
    }
}
