using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class ShapeGradientColorPickerParityTests
{
    [Fact]
    public void GradientColorSwatches_AreKeyboardAndAutomationReachableButtons()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.DrawingFormatDialogs.cs"));

        source.Should().Contain("CreateGradientColorButton(");
        source.Should().Contain("startSwatch.Click += async");
        source.Should().Contain("endSwatch.Click += async");
        source.Should().Contain("AutomationProperties.SetAutomationId(button, automationId);");
        source.Should().Contain("AutomationProperties.SetName(button, UiText.Get(automationNameKey));");
        source.Should().Contain("AutomationProperties.SetHelpText(button, UiText.Get(helpTextKey));");
        source.Should().Contain("ShapeGradientStartColorButton");
        source.Should().Contain("ShapeGradientEndColorButton");
        source.Should().NotContain("startSwatch.PointerPressed");
        source.Should().NotContain("endSwatch.PointerPressed");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
