using FluentAssertions;

namespace FreeX.App.Presentation.Tests.DefinedNames;

public sealed class DefinedNameCrossRendererSourceGuardTests
{
    [Fact]
    public void Renderers_DelegateNamedRangeDescriptorsAndCommandPolicyToPresentation()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var policy = Read(repoRoot, "src", "FreeX.App.Presentation", "DefinedNames", "DefinedNameUiPolicy.cs");
        var wpfManager = Read(repoRoot, "src", "FreeX.App.Host", "NamedRangeDialog.xaml.cs");
        var wpfDefinition = Read(repoRoot, "src", "FreeX.App.Host", "NameDefinitionDialog.cs");
        var wpfFormula = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.FormulaCommands.cs");
        var wpfNameBox = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.Editing.cs");
        var avaloniaNames = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.DefinedNames.cs");
        var avaloniaPasteNames = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.PasteNames.cs");
        var avaloniaMain = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs");

        policy.Should().Contain("public static IReadOnlyList<DefinedNameFilterDescriptor> Filters");
        policy.Should().Contain("PlanNameBoxDefinition(");
        policy.Should().Contain("PlanUseInFormula(");
        policy.Should().Contain("PlanManagerSelection(");
        policy.Should().Contain("CreateRangeSelectionRequest(");
        policy.Should().Contain("ClearManagerRefersToOnDeselection");
        policy.Should().Contain("DefinedNameIdentifierCatalog");

        wpfManager.Should().Contain("FilterBox.ItemsSource = DefinedNameUiPolicy.Filters");
        wpfManager.Should().Contain("DefinedNameUiPolicy.ResolveFilter(FilterBox.SelectedIndex)");
        wpfManager.Should().Contain("_definedNames.ProjectRows(_items, selected)");
        wpfManager.Should().Contain("DefinedNameUiPolicy.PlanManagerSelection");
        wpfManager.Should().Contain("DefinedNameUiProfile.Wpf");
        wpfManager.Should().NotContain("1 => DefinedNameFilter.Workbook");
        wpfDefinition.Should().Contain("DefinedNameUiPolicy.CreateDraft(");
        wpfDefinition.Should().NotContain("record struct NamedRangeScopeOption");
        wpfFormula.Should().Contain("DefinedNameUiProfile.Wpf");
        wpfFormula.Should().Contain("definedNames.PlanSave(draft)");
        wpfFormula.Should().NotContain("NameConflictsWithExistingDefinition");
        wpfFormula.Should().NotContain("_workbook.NamedRanges.Keys.OrderBy");
        wpfNameBox.Should().Contain("DefinedNameUiPolicy.PlanNameBoxDefinition(");
        wpfNameBox.Should().NotContain("WorkbookReferenceNavigator.NameExistsAsFormula(");
        wpfNameBox.Should().NotContain("StructuredTableSelectionPlanner.ContainsTableName(");

        avaloniaNames.Should().Contain("DefinedNameUiPolicy.Filters");
        avaloniaNames.Should().Contain("definedNames.ProjectRows(filter)");
        avaloniaNames.Should().Contain("DefinedNameUiPolicy.CreateDraft(");
        avaloniaNames.Should().Contain("DefinedNameUiPolicy.PlanManagerSelection");
        avaloniaNames.Should().Contain("DefinedNameUiProfile.Avalonia");
        avaloniaNames.Should().NotContain("NameManagerFilterChoices");
        avaloniaNames.Should().NotContain("new(\"All names\", DefinedNameFilter.All)");
        avaloniaPasteNames.Should().Contain("DefinedNameUiProfile.Avalonia");
        avaloniaPasteNames.Should().Contain("DefinedNameUiPolicy.PlanPasteNamesSelection");
        avaloniaMain.Should().Contain("DefinedNameUiPolicy.PlanNameBoxDefinition(");
        avaloniaMain.Should().NotContain("WorkbookReferenceNavigator.NameExistsAsFormula(");
        avaloniaMain.Should().NotContain("StructuredTableSelectionPlanner.ContainsTableName(");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(parts.Prepend(root).ToArray()));
}
