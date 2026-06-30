using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Host.Tests;

public sealed class SisterAppFrameContractAdoptionTests
{
    [Fact]
    public void ClientFrameContract_DescribesSharedChromeWorkareaAndStatusSlots()
    {
        var contract = SisterAppClientFrameContractPlanner.Plan(
            topPanelsBelowChrome: 2,
            bottomPanelsAboveStatus: 1);

        contract.Slots.Should().Equal(
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.Chrome, 0),
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.TopPanelBelowChrome, 0),
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.TopPanelBelowChrome, 1),
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.WorkArea, 0),
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.BottomPanelAboveStatus, 0),
            new SisterAppClientFrameSlotPlan(SisterAppClientFrameSlotRole.StatusBar, 0));
    }

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

    [Theory]
    [InlineData("src", "FreeX.App.Avalonia", "MainWindow.cs", "SisterAppClientFrameBuilder.Build(new SisterAppClientFrameSpec(", "WorkArea:", "StatusBar:")]
    [InlineData("freew", "FreeW.App.Host", "MainWindow.cs", "SisterAppClientFrameBuilder.Build(", "WorkArea:", "StatusBar:")]
    [InlineData("freew", "FreeW.App.Avalonia", "MainWindow.cs", "SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(", "workArea:", "statusBar:")]
    [InlineData("freep", "FreeP.App.Host", "MainWindow.cs", "SisterAppClientFrameBuilder.Build(", "WorkArea:", "StatusBar:")]
    [InlineData("freep", "FreeP.App.Avalonia", "MainWindow.cs", "SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(", "workArea:", "statusBar:")]
    public void SisterAppMainWindows_UseSharedClientFrameBuilders(
        string appRoot,
        string appProject,
        string fileName,
        string expectedBuilder,
        string expectedWorkAreaToken,
        string expectedStatusBarToken)
    {
        var source = WorkspaceFileLocator.ReadAllText(appRoot, appProject, fileName);

        source.Should().Contain(expectedBuilder);
        source.Should().Contain(expectedWorkAreaToken);
        source.Should().Contain(expectedStatusBarToken);
    }
}
