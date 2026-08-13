using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host.Tests;

public sealed class WpfDialogSurfaceSemanticsTests
{
    private enum Field
    {
        Value,
    }

    [StaFact]
    public void Apply_ProjectsWindowAndFieldAutomationMetadata()
    {
        var field = new DialogFieldSurfaceSpec<Field>(Field.Value, "Value", "ValueField", "Value field");
        var surface = new DialogSurfaceSpec<Field>(
            "Dialog",
            "DialogWindow",
            "Dialog window",
            [field]);
        var window = new Window();
        var textBox = new TextBox();

        WpfDialogSurfaceSemantics.Apply(window, surface);
        WpfDialogSurfaceSemantics.Apply(textBox, field);

        AutomationProperties.GetAutomationId(window).Should().Be("DialogWindow");
        AutomationProperties.GetName(window).Should().Be("Dialog window");
        AutomationProperties.GetAutomationId(textBox).Should().Be("ValueField");
        AutomationProperties.GetName(textBox).Should().Be("Value field");
    }

    [Fact]
    public void WpfDialogsUseOneSurfaceSemanticsOwner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var host = Path.Combine(root, "freew", "FreeW.App.Host");

        File.Exists(Path.Combine(host, "ImageChartDialogSurfaceSemantics.cs")).Should().BeFalse();
        File.Exists(Path.Combine(host, "PageLayoutDialogSurfaceSemantics.cs")).Should().BeFalse();
        File.ReadAllText(Path.Combine(host, "ImageSizeDialog.cs"))
            .Should().Contain("WpfDialogSurfaceSemantics.Apply(");
        File.ReadAllText(Path.Combine(host, "ParagraphIndentDialog.cs"))
            .Should().Contain("WpfDialogSurfaceSemantics.Apply(");
    }
}
