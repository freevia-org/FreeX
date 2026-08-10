using System.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class BackstageAndLifecycleOwnershipSourceTests
{
    [Fact]
    public void Renderers_use_the_portable_lifecycle_adapter_and_keep_native_ports_only()
    {
        var wpf = Read("freep", "FreeP.App.Host", "FileCommands.cs");
        var avalonia = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var avaloniaPorts = Read("freep", "FreeP.App.Avalonia", "MainWindow.FileCommandPorts.cs");
        var adapter = Read("freep", "FreeP.App.Presentation", "PresentationFileLifecycleAdapter.cs");

        wpf.Should().Contain("new PresentationFileLifecycleAdapter(workflow.Workflow)");
        avalonia.Should().Contain("new PresentationFileLifecycleAdapter(")
            .And.Contain("_fileWorkflow.Workflow")
            .And.Contain("_fileWorkflow.ConfirmCloseAllowedAsync");
        wpf.Should().NotContain("WpfPresentationFileLifecyclePort");
        avaloniaPorts.Should().NotContain("AvaloniaPresentationFileLifecyclePort")
            .And.NotContain(": IPresentationFileLifecyclePort");
        adapter.Should().Contain("FileCommandWorkflow _workflow")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia");
    }

    [Fact]
    public void Backstage_renderers_consume_shared_dispatch_text_and_automation_contracts()
    {
        var wpf = Read("freep", "FreeP.App.Host", "Backstage", "BackstageView.cs");
        var avalonia = Read("freep", "FreeP.App.Avalonia", "Backstage", "BackstageView.cs");
        var avaloniaMain = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var panePlanner = Read("freep", "FreeP.App.Presentation", "Backstage", "PresentationBackstagePanePlanner.cs");
        var printPlanner = Read("freep", "FreeP.App.Presentation", "Backstage", "PresentationBackstagePrintSurfacePlanner.cs");
        var sharedComposer = Read("shared", "Free.Shared.Shell.Avalonia", "AvaloniaBackstagePaneComposer.cs");
        var sharedFrame = Read("shared", "Free.Shared.Shell.Avalonia", "AvaloniaBackstageFrame.cs");
        var sharedFrameSession = Read("shared", "Free.Shared.Shell", "BackstageFrameSession.cs");

        wpf.Should().Contain("surface.SettingsHeading").And.Contain("choice.DisplayText");
        avalonia.Should().Contain("BackstageActionBinder.DismissBefore(Hide)")
            .And.Contain("surface.SettingsHeading")
            .And.Contain("choice.DisplayText")
            .And.NotContain("private Action DismissThen")
            .And.NotContain("private static string AutomationToken");
        avaloniaMain.Should().Contain("surface.SettingsHeading")
            .And.Contain("var row = choice.DisplayText")
            .And.Contain("AddPrintOptionsPaneRenderedChoice(group.Kind, row)")
            .And.NotContain("BuildPrintOptionsPaneChoiceSummary")
            .And.NotContain("PrintOptionsPaneSectionHeading")
            .And.NotContain("case \"Output Options\"")
            .And.NotContain("case \"Slide Range\"");
        printPlanner.Should().Contain("string SettingsHeading")
            .And.Contain("string DisplayText")
            .And.Contain("string StableId")
            .And.Contain("PresentationBackstagePrintChoiceGroupKind Kind")
            .And.Contain("AutomationIdToken.KeepLettersAndDigits(");

        foreach (var source in new[] { panePlanner, printPlanner, avalonia, sharedComposer, sharedFrameSession })
        {
            source.Should().Contain("AutomationIdToken.KeepLettersAndDigits(");
            source.Should().NotContain("private static string AutomationToken");
            source.Should().NotContain("string.Concat(value.Where(char.IsLetterOrDigit))");
        }

        sharedFrame.Should().Contain("BackstageFrameEntryIdentity.From(entry).ResolveAutomationId()")
            .And.NotContain("AutomationIdToken.KeepLettersAndDigits(");
    }

    private static string Read(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
