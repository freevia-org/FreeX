namespace FreeX.App.Avalonia.Tests;

public sealed class PivotFieldFilterSourceTests
{
    [Fact]
    public void ItemFilterDialog_UsesDistinctCancelSelectionAndRemovalRoutes()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFilters.cs"));

        source.Should().Contain("cancel.Click += (_, _) => dialog.Close(0);");
        source.Should().Contain("ok.Click += (_, _) => dialog.Close(1);");
        source.Should().Contain("clearItemFilterBtn.Click += (_, _) => dialog.Close(4);");
        source.Should().Contain("clearFiltersBtn.Click += (_, _) => dialog.Close(5);");
        source.Should().Contain("removeLabelFilterBtn.Click += (_, _) => dialog.Close(6);");
        source.Should().Contain("removeValueFilterBtn.Click += (_, _) => dialog.Close(7);");
        source.Should().Contain("case 4:");
        source.Should().Contain("case 5:");
        source.Should().Contain("case 6:");
        source.Should().Contain("case 7:");
    }

    [Fact]
    public void ItemFilterDialog_DelegatesSelectionStateToPortablePivotUi()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFilters.cs"));

        source.Should().Contain(".CreateFieldSelectionState(");
        source.Should().Contain("PivotFieldFilterSummary.CreateState(");
        source.Should().NotContain("CloneFieldsWithSelection");
        source.Should().NotContain("FindFieldSelection");
        source.Should().NotContain("field with { SelectedItem");
    }

    [Fact]
    public void ItemFilterDialog_UsesLocalizedNoFilterAndExactValueFieldOwnership()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFilters.cs"));

        source.Should().Contain("UiText.Get(\"PivotFieldFilter_NoItemFilter\")");
        source.Should().Contain("UiText.Get(\"PivotFieldFilter_NoLabelFilter\")");
        source.Should().Contain("UiText.Get(\"PivotFieldFilter_NoValueFilter\")");
        source.Should().Contain("filter.SourceFieldIndex == target.SourceFieldIndex");
        source.Should().NotContain("filter.SourceFieldIndex is null || filter.SourceFieldIndex == target.SourceFieldIndex");
    }

    [Fact]
    public void PivotChartContextMenu_ConsumesSharedFilterStateWithoutChangingVisibleHeaders()
    {
        var source = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.PivotChartContextMenus.cs"));

        source.Should().Contain("PivotFieldFilterSummary.CreateState(");
        source.Should().Contain("var hasFilter = filterState.HasStoredFilter;");
        source.Should().Contain("SelectItemsHeader: \"Select Items...\"");
        source.Should().Contain("ClearFilterHeader: $\"Clear Filters from {target.FieldCaption}\"");
        source.Should().NotContain("pivot.LabelFilters.Any(");
        source.Should().NotContain("pivot.ValueFilters.Any(");
    }

    [Fact]
    public void ItemFilterDialog_EncodesWpfClientGeometryAndCompactControlsLocally()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFilters.cs"));

        source.Should().Contain("PivotFieldFilterWindowWidth = 380");
        source.Should().Contain("PivotFieldFilterWindowHeight = 470");
        source.Should().Contain("PivotFieldFilterClientWidth = 364");
        source.Should().Contain("PivotFieldFilterClientHeight = 431");
        source.Should().Contain("new Thickness(12)");
        source.Should().Contain("new Thickness(10)");
        source.Should().Contain("new Thickness(0, 10, 0, 0)");
        source.Should().Contain("ApplyPivotFilterButtonChrome(ok, 74, isDefault: true)");
        source.Should().Contain("ApplyPivotFilterButtonChrome(cancel, 74)");
        source.Should().Contain("button.CornerRadius = new CornerRadius(0)");
        source.Should().Contain("textBox.CornerRadius = new CornerRadius(0)");
        source.Should().Contain("IsVisible = checkBox.IsChecked is null");
        source.Should().Contain("Content = labelFilter is null ? \"Add Label Filter...\" : \"Edit Label Filter...\"");
        source.Should().Contain("Content = valueFilter is null ? \"Add Value Filter...\" : \"Edit Value Filter...\"");
        source.Should().NotContain("PlaceholderText = StripDisplayMnemonic(UiText.Get(\"PivotFieldFilter_Search\"))");
    }

    [Fact]
    public void ParityPivotFixture_UsesWpfPartialSelectionAndMemberOrder()
    {
        var captureSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));
        var filterSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFilters.cs"));

        captureSource.Should().Contain("new PivotFieldModel(0, SelectedItems: [\"North\", \"South\"])");
        captureSource.Should().Contain("exposeActiveFilterActions: false");
        filterSource.Should().Contain("ResolveSelectAllState(");
        filterSource.Should().Contain("members.OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeX.slnx", parts);
}
