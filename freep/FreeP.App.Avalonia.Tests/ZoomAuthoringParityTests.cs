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
        source.Should().Contain("Use Zoom border");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderColor(");
        source.Should().Contain("_frameBorderColor.IsEnabled");
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

    [Fact]
    public void Avalonia_zoom_validation_matches_wpf_with_the_shared_modal_warning_surface()
    {
        var source = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "ZoomObjectPropertiesDialog.cs"));

        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidTransitionDurationMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderColorMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidCropEdgesMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidSummaryTileLayoutMessage");
        source.Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync(");
        source.Should().NotContain("_validation");
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
