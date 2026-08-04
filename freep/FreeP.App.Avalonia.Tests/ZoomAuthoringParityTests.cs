using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class ZoomAuthoringParityTests
{
    [Fact]
    public void Avalonia_zoom_format_dialog_uses_shared_transition_control_and_validation()
    {
        var source = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "ZoomObjectPropertiesDialog.cs"));

        source.Should().Contain("Use Zoom transition");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseTransitionDuration(");
        source.Should().Contain("_transitionEnabled.IsChecked == true");
        source.Should().Contain("_transitionDuration.IsEnabled");
    }

    [Fact]
    public void Avalonia_zoom_commands_keep_shared_property_persistence_and_undo_route()
    {
        var source = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));

        source.Should().Contain("ZoomObjectPropertiesPlanner.CommandId");
        source.Should().Contain("Editor.SetSelectedZoomObjectProperties(dialog.Properties)");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull();
        return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
    }
}
