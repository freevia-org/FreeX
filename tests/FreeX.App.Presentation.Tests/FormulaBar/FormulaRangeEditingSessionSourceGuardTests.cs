using FluentAssertions;

namespace FreeX.App.Presentation.Tests.FormulaBar;

public sealed class FormulaRangeEditingSessionSourceGuardTests
{
    private static readonly string[] RemovedHostFields =
    [
        "_formulaRangeSelectionAnchor",
        "_formulaRangeSelectionCursor",
        "_formulaSheetSpanEntryState",
        "_formulaRangeEntryMode",
        "_formulaRangeEntrySelectionMode",
        "_formulaReferenceStart",
        "_formulaReferenceLength",
        "_formulaReferenceDragActive",
        "_formulaReferenceDragHighlight",
        "_functionAutocompleteCandidates",
        "_functionAutocompleteTokenStart",
        "_functionAutocompleteTokenLength",
        "_suppressNextCellValueAutoCompleteSuggestion",
        "_suppressNextInlineCellValueAutoCompleteSuggestion"
    ];

    private static readonly string[] SessionOwnedPlannerCalls =
    [
        "FormulaEditInteractionPlanner.BuildTextChangePlan(",
        "FormulaEditInteractionPlanner.BuildPointModeTogglePlan(",
        "FormulaEditInteractionPlanner.ShouldStartPointModeFromTypedText(",
        "FormulaEditInteractionPlanner.IsFormulaText(",
        "FormulaEditInteractionPlanner.ShouldCommitInlineArrows(",
        "FormulaEditInteractionPlanner.BuildEditStatusBarPlan(",
        "FormulaRangeEntryPlanner.TryToggleKeyboardSelectionMode(",
        "FormulaRangeEntryPlanner.GetKeyboardCursor(",
        "FormulaRangeEntryPlanner.GetKeyboardSelectionTarget(",
        "FormulaRangeEntryPlanner.GetKeyboardDisjointRange(",
        "FormulaRangeEntryPlanner.TryAppendKeyboardRangeSelection(",
        "FormulaRangeEntryPlanner.TryAppendDisjointRangeSelection(",
        "FormulaRangeEntryPlanner.TryGetReferenceSpanForPointEntry(",
        "FormulaRangeEntryPlanner.TryApplyRangeSelection(",
        "FormulaRangeEntryPlanner.TryApplySelectionText(",
        "ExcelEditKeyPlanner.GetIntent(",
        "ExcelEditKeyPlanner.ShouldCycleFormulaReference(",
        "ExcelTextEditorPlanner.TryCycleFormulaReference(",
        "FormulaReferenceDragResizePlanner.",
        "FormulaFunctionAutocompletePlanner.",
        "CellValueAutoCompleteSuggester.",
        "FormulaSheetSpanEntryPlanner.PlanTabSelection("
    ];

    [Fact]
    public void Session_RemainsRendererNeutral()
    {
        var formulaBarRoot = RepositoryFileLocator.FindDirectory(
            "src",
            "FreeX.App.Presentation",
            "FormulaBar");
        var source = File.ReadAllText(Path.Combine(formulaBarRoot, "FormulaRangeEditingSession.cs"));

        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia.");
        source.Should().NotContain("FreeX.App.Host");
        source.Should().NotContain("FreeX.App.Avalonia");
    }

    [Fact]
    public void Controller_RemainsRendererNeutralAndOwnsCrossRendererOrchestration()
    {
        var formulaBarRoot = RepositoryFileLocator.FindDirectory(
            "src",
            "FreeX.App.Presentation",
            "FormulaBar");
        var source = File.ReadAllText(Path.Combine(formulaBarRoot, "FormulaReferenceEditingController.cs"));

        source.Should().Contain("TryApplyKeyboardSelection(");
        source.Should().Contain("FormulaReferenceHighlightPlanner.GetHighlights(");
        source.Should().Contain("StructuredReferenceResolver.ResolveEditorReference(");
        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia.");
        source.Should().NotContain("FreeX.App.Host");
        source.Should().NotContain("FreeX.App.Avalonia");
    }

    [Theory]
    [InlineData("FreeX.App.Host")]
    [InlineData("FreeX.App.Avalonia")]
    public void Hosts_DelegateFormulaRangeStateAndTransitionsToPortableSession(string projectName)
    {
        var hostRoot = RepositoryFileLocator.FindDirectory("src", projectName);
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(hostRoot, "MainWindow*.cs")
                .Select(File.ReadAllText));

        source.Should().Contain("FormulaRangeEditingSession _formulaRangeEditingSession = new();");
        foreach (var removedField in RemovedHostFields)
            source.Should().NotContain(removedField);
        foreach (var plannerCall in SessionOwnedPlannerCalls)
            source.Should().NotContain(plannerCall);
        source.Should().NotContain("selection.Mode == FormulaPointModeSelectionMode");
        source.Should().Contain("FormulaReferenceEditingController.TryApplyKeyboardSelection(");
        source.Should().Contain("FormulaReferenceEditingController.BuildHighlights(");
        source.Should().NotContain("StructuredReferenceResolver.ResolveEditorReference(");
    }
}
