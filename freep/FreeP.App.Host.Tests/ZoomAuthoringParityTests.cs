using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class ZoomAuthoringParityTests
{
    [Fact]
    public void Wpf_zoom_format_dialog_uses_shared_transition_control_and_validation()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "ZoomObjectPropertiesDialog.cs"));

        source.Should().Contain("Use Zoom transition");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseTransitionDuration(");
        source.Should().Contain("_transitionEnabled.IsChecked == true");
        source.Should().Contain("_transitionDuration.IsEnabled");
    }

    [Fact]
    public void Wpf_zoom_commands_keep_shared_property_persistence_and_undo_route()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        source.Should().Contain("onFormatZoom:       () => OpenZoomObjectPropertiesDialog()");
        source.Should().Contain("Editor.SetSelectedZoomObjectProperties(dialog.Properties)");
    }
}
