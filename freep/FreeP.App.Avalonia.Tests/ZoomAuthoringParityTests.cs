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
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderWidth(");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderDash(");
        source.Should().Contain("Use gradient border");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderGradient(");
        source.Should().Contain("Use pattern border");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderPattern(");
        source.Should().Contain("Use no-fill border");
        source.Should().Contain("IsFrameBorderNoFillEnabled");
        source.Should().Contain("Use theme border color");
        source.Should().Contain("FrameBorderThemeColor");
        source.Should().Contain("Use outer border shadow");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderShadow(");
        source.Should().Contain("Use border glow");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderGlow(");
        source.Should().Contain("Use border soft edge");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameBorderSoftEdge(");
        source.Should().Contain("ZoomObjectPropertiesPlanner.TryParseFrameGeometry(");
        source.Should().Contain("Frame shape:");
        source.Should().Contain("_frameBorderColor.IsEnabled");
        source.Should().Contain("Apply format to all Summary Zoom tiles");
        source.Should().Contain("ApplySummaryPropertiesToAllTiles");
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
        source.Should().Contain("dialog.ApplySummaryPropertiesToAllTiles");
    }

    [Fact]
    public void Avalonia_summary_zoom_dialog_exposes_target_reordering()
    {
        var source = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "SummaryZoomDialog.cs"));

        source.Should().Contain("Move Up");
        source.Should().Contain("Move Down");
        source.Should().Contain("SelectOrderedTargets");
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
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderWidthMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderDashMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderGradientMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderPatternMessage");
        source.Should().Contain("Use no-fill border");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderShadowMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderGlowMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameBorderSoftEdgeMessage");
        source.Should().Contain("ZoomObjectPropertiesPlanner.InvalidFrameGeometryMessage");
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
