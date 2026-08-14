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
        var wpfPasteNames = Read(repoRoot, "src", "FreeX.App.Host", "PasteNamesDialog.cs");
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
        wpfDefinition.Should().Contain("DefinedNameValidator.Validate(name).Error == DefinedNameError.Blank");
        wpfDefinition.Should().NotContain("if (string.IsNullOrWhiteSpace(name))");
        wpfPasteNames.Should().Contain("DefinedNameUiPolicy.PlanPasteNamesSelection(_items, _namesList.SelectedIndex)");
        wpfPasteNames.Should().NotContain("_namesList.SelectedItem is not PasteNamesItem");
        wpfPasteNames.Should().NotContain("_okButton.IsEnabled = _namesList.SelectedItem");
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
        avaloniaNames.Should().Contain("CreateNamesFromSelectionPlanner.TryCreateOptions(");
        avaloniaNames.Should().NotContain("if (!options.HasAnyEdge)");
        avaloniaPasteNames.Should().Contain("DefinedNameUiProfile.Avalonia");
        avaloniaPasteNames.Should().Contain("DefinedNameUiPolicy.PlanPasteNamesSelection");
        avaloniaMain.Should().Contain("DefinedNameUiPolicy.PlanNameBoxDefinition(");
        avaloniaMain.Should().NotContain("WorkbookReferenceNavigator.NameExistsAsFormula(");
        avaloniaMain.Should().NotContain("StructuredTableSelectionPlanner.ContainsTableName(");
    }

    /// <summary>
    /// Both renderers must seed the Create Names from Selection checkboxes from the shared
    /// <c>CreateNamesFromSelectionPlanner.DetectOptions</c> auto-detection (which mirrors real Excel 16.0), and
    /// neither may hardcode a checkbox default of its own — that divergence is exactly the bug this guards.
    /// </summary>
    [Fact]
    public void Renderers_SeedCreateNamesFromSelectionCheckBoxesFromDetectOptions()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var planner = Read(repoRoot, "src", "FreeX.App.Presentation", "DefinedNames", "CreateNamesFromSelectionPlanner.cs");
        var wpfDialog = Read(repoRoot, "src", "FreeX.App.Host", "CreateNamesFromSelectionDialog.cs");
        var wpfFormula = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.FormulaCommands.cs");
        var avaloniaNames = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.DefinedNames.cs");

        planner.Should().Contain("public static CreateNamesFromSelectionOptions DetectOptions(");
        planner.Should().NotContain("DefaultOptions");

        wpfFormula.Should().Contain("CreateNamesFromSelectionPlanner.DetectOptions(");
        wpfFormula.Should().Contain("new CreateNamesFromSelectionDialog(detected)");
        wpfDialog.Should().Contain("CreateNamesFromSelectionDialog(CreateNamesFromSelectionOptions detectedOptions)");
        wpfDialog.Should().Contain("IsChecked = detectedOptions.UseTopRow");
        wpfDialog.Should().Contain("IsChecked = detectedOptions.UseLeftColumn");
        wpfDialog.Should().Contain("IsChecked = detectedOptions.UseBottomRow");
        wpfDialog.Should().Contain("IsChecked = detectedOptions.UseRightColumn");
        wpfDialog.Should().NotContain("IsChecked = true");
        wpfDialog.Should().NotContain("DefaultOptions");

        avaloniaNames.Should().Contain("CreateNamesFromSelectionPlanner.DetectOptions(");
        avaloniaNames.Should().Contain("IsChecked = detected.UseTopRow");
        avaloniaNames.Should().Contain("IsChecked = detected.UseLeftColumn");
        avaloniaNames.Should().Contain("IsChecked = detected.UseBottomRow");
        avaloniaNames.Should().Contain("IsChecked = detected.UseRightColumn");
        avaloniaNames.Should().NotContain("CreateNamesTopRow\"), IsChecked = true");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(parts.Prepend(root).ToArray()));
}
