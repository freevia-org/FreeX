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
    public void FormulaSelectionRenderers_DelegateCapturePivotAndApplyPolicyToPresentation()
    {
        var session = Read(
            "src",
            "FreeX.App.Presentation",
            "FormulaBar",
            "FormulaRangeEditingSession.cs");
        var wpf = Read("src", "FreeX.App.Host", "MainWindow.FormulaReferenceEditing.cs") +
            Read("src", "FreeX.App.Host", "MainWindow.Selection.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var renderers = wpf + avalonia;

        session.Should().Contain("public static FormulaRangeEditorSnapshot Capture(");
        session.Should().Contain("public bool TryApplyPointRangeSelectionEdit(");
        session.Should().Contain("GetPivotDataFormulaPlanner.CreatePointModeFunctionCall(");
        session.Should().Contain("applyEditorEdit(plan.Edit.TextEdit);");
        wpf.Should().Contain("_formulaRangeEditingSession.TryApplyPointRangeSelectionEdit(");
        avalonia.Should().Contain("_formulaRangeEditingSession.TryApplyPointRangeSelectionEdit(");
        renderers.Should().NotContain("new FormulaRangeEditorSnapshot(");
        renderers.Should().NotContain("GetPivotDataFormulaPlanner.CreatePointModeFunctionCall(");
        renderers.Should().NotContain("_formulaRangeEditingSession.ApplySelectionEdit(plan);");
    }

    [Fact]
    public void FormulaAutocompleteRenderers_DelegateActionDispatchToPresentation()
    {
        var session = Read(
            "src",
            "FreeX.App.Presentation",
            "FormulaBar",
            "FormulaRangeEditingSession.cs");
        var wpf = Read("src", "FreeX.App.Host", "MainWindow.Editing.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var renderers = wpf + avalonia;

        session.Should().Contain("public bool ExecuteFunctionAutocompleteKey(");
        wpf.Should().Contain("_formulaRangeEditingSession.ExecuteFunctionAutocompleteKey(");
        avalonia.Should().Contain("_formulaRangeEditingSession.ExecuteFunctionAutocompleteKey(");
        renderers.Should().NotContain("switch (plan.Action)");
        renderers.Should().NotContain("case FormulaFunctionAutocompleteKeyAction.");
    }

    [Fact]
    public void ShellFocusRenderers_DelegateBoundedRetryPolicyToPresentation()
    {
        var planner = Read("src", "FreeX.App.Presentation", "Shell", "ShellFocusCyclePlanner.cs");
        var wpf = Read("src", "FreeX.App.Host", "MainWindow.KeyboardFocus.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var renderers = wpf + avalonia;

        planner.Should().Contain("public static bool TryFocusNextAvailable(");
        wpf.Should().Contain("ShellFocusCyclePlanner.TryFocusNextAvailable(");
        avalonia.Should().Contain("ShellFocusCyclePlanner.TryFocusNextAvailable(");
        renderers.Should().NotContain("Enum.GetValues<ShellFocusTarget>()");
        renderers.Should().NotContain("ShellFocusCyclePlanner.GetNextAvailable(current");
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
