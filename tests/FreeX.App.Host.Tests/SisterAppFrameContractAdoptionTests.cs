using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SisterAppFrameContractAdoptionTests
{
    [Fact]
    public void FreeXWpfMainWindow_AppliesSharedClientFrameContractToExistingWorkbookRows()
    {
        var constructorSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var shellSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Shell.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        constructorSource.Should().Contain("ApplySisterAppClientFrameContractRows();");
        shellSource.Should().Contain("SisterAppClientFrameContractPlanner.Plan(");
        shellSource.Should().Contain("topPanelsBelowChrome: 2");
        shellSource.Should().Contain("bottomPanelsAboveStatus: 1");
        shellSource.Should().Contain("ApplyRootFrameSlot(RibbonTabs, slotRow, GridLength.Auto);");
        shellSource.Should().Contain("ApplyRootFrameSlot(BelowRibbonQatRoot, slotRow, GridLength.Auto);");
        shellSource.Should().Contain("ApplyRootFrameSlot(FormulaBarBorder, slotRow, GridLength.Auto);");
        shellSource.Should().Contain("ApplyRootFrameSlot(WorkbookWorkAreaRoot, slotRow, new GridLength(1, GridUnitType.Star));");
        shellSource.Should().Contain("ApplyRootFrameSlot(SheetTabsPanelRoot, slotRow, GridLength.Auto);");
        shellSource.Should().Contain("ApplyRootFrameSlot(StatusBarRoot, slotRow, GridLength.Auto);");

        xaml.Should().Contain("x:Name=\"WorkbookWorkAreaRoot\" Grid.Row=\"4\"");
        xaml.Should().Contain("x:Name=\"SheetTabsPanelRoot\" Grid.Row=\"5\"");
        shellSource.Should().NotContain("SisterAppStatusBarChrome.Build(");
    }

}
