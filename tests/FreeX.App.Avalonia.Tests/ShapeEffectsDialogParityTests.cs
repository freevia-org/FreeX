using System.IO;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

public sealed class ShapeEffectsDialogParityTests
{
    [Fact]
    public void Parity_fixture_seeds_the_Wpf_Shadow_state_through_the_shared_command()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));

        source.Should().Contain("new SetDrawingShapeEffectCommand(");
        source.Should().Contain("DrawingShapeEffectPreset.Shadow));");
        source.Should().Contain("await OpenShapeEffectsDialogAsync();");
    }

    [Fact]
    public void Dialog_uses_the_Wpf_label_and_shared_Shadow_description()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.DrawingFormatDialogs.cs"));
        var plan = ShapeEffectsPlanner.CreatePlan(DrawingShapeEffectPreset.Shadow);

        source.Should().Contain("StripDisplayMnemonic(UiText.Get(\"ShapeEffects_EffectLabel\"))");
        source.Should().Contain("HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch");
        plan.SelectedPreset.Should().Be(DrawingShapeEffectPreset.Shadow);
        plan.Options.Single(option => option.Preset == DrawingShapeEffectPreset.Shadow)
            .DescriptionKey.Should().Be("ShapeEffects_ShadowDescription");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
