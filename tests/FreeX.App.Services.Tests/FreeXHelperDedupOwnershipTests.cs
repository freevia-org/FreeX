using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FreeXHelperDedupOwnershipTests
{
    [Fact]
    public void FormulaEditors_DelegateStructuredReferenceResolutionToCore()
    {
        var core = Read("src", "FreeX.Core.Formula", "StructuredReferenceResolver.cs");
        var wpf = Read("src", "FreeX.App.Host", "MainWindow.FormulaReferenceEditing.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var renderers = wpf + avalonia;

        core.Should().Contain("ResolveEditorReference(");
        wpf.Should().Contain("StructuredReferenceResolver.ResolveEditorReference(");
        avalonia.Should().Contain("StructuredReferenceResolver.ResolveEditorReference(");
        renderers.Should().NotContain("var trimmedSelector = selector.Trim()");
        renderers.Should().NotContain("StructuredReferenceResolver.ResolveCurrentRowColumn(");
    }

    [Fact]
    public void FindReplaceRenderers_DelegateOptionConstructionToServices()
    {
        var planner = Read("src", "FreeX.App.Services", "FindReplaceDialogPlanner.cs");
        var wpf = Read("src", "FreeX.App.Host", "FindReplaceDialog.xaml.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var renderers = wpf + avalonia;

        planner.Should().Contain("public static FindOptions CreateFindOptions(");
        wpf.Should().Contain("FindReplaceDialogPlanner.CreateFindOptions(");
        avalonia.Should().Contain("FindReplaceDialogPlanner.CreateFindOptions(");
        renderers.Should().NotContain("WithinCombo.SelectedIndex == 1");
        renderers.Should().NotContain("controls.WithinBox.SelectedIndex == 1");
        renderers.Should().NotContain("LookInCombo.SelectedIndex switch");
        renderers.Should().NotContain("controls.LookInBox.SelectedIndex switch");
    }

    [Fact]
    public void FormulaTextOverlays_DelegateSegmentationToPresentation()
    {
        var planner = Read("src", "FreeX.App.Presentation", "FormulaReferenceTextSegmentPlanner.cs");
        var wpf = Read("src", "FreeX.App.Host", "FormulaReferenceTextOverlay.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var renderers = wpf + avalonia;

        planner.Should().Contain("CreateSegments(");
        wpf.Should().Contain("FormulaReferenceTextSegmentPlanner.CreateSegments(");
        avalonia.Should().Contain("FormulaReferenceTextSegmentPlanner.CreateSegments(");
        renderers.Should().NotContain("foreach (var highlight in highlights.OrderBy");
        renderers.Should().NotContain("var highlightEnd = Math.Min");
    }

    [Fact]
    public void WheelHandlers_DelegateStepNormalizationToServices()
    {
        var planner = Read("src", "FreeX.App.Services", "WorkbookViewportScrollPlanner.cs");
        var wpf = Read("src", "FreeX.App.Host", "MainWindow.Viewport.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var renderers = wpf + avalonia;

        planner.Should().Contain("NormalizeWheelScrollStep(");
        wpf.Should().Contain("WorkbookViewportScrollPlanner.NormalizeWheelScrollStep(");
        avalonia.Should().Contain("WorkbookViewportScrollPlanner.NormalizeWheelScrollStep(");
        renderers.Should().NotContain("NormalizeWheelScrollLines(");
        renderers.Should().NotContain("MaxWheelScrollLinesPerNotch");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(RepositoryFileLocator.Find(parts));
}
