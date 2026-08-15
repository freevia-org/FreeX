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
        var applicationSource = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "PivotUI",
            "PivotApplicationSession.Configuration.cs"));

        source.Should().Contain("PivotFieldFilterSummary.CreateState(");
        source.Should().Contain("PivotApplication.PlanFieldItemSelection(");
        source.Should().NotContain(".CreateFieldSelectionState(");
        applicationSource.Should().Contain(".CreateFieldSelectionState(pivot, area, sourceFieldIndex)");
        applicationSource.Should().Contain(".WithSelectedItems(selectedItems)");
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
        source.Should().Contain("PivotUiPlanner.ResolvePivotChartFieldArea(");
        source.Should().NotContain("pivot.LabelFilters.Any(");
        source.Should().NotContain("pivot.ValueFilters.Any(");
        source.Should().NotContain("pivot.PageFields.Any(field => field.SourceFieldIndex");
        source.Should().NotContain("pivot.ColumnFields.Any(field => field.SourceFieldIndex");
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
        // The three-state check-box painting is deliberately not local: "Share WPF-aligned compact
        // checkbox chrome" moved it into Free.Shared.Shell.Avalonia. It came back briefly as a local
        // template to work around the shared tick staying visible in the indeterminate state; that
        // was a defect in the shared binding, now fixed there and covered by
        // CompactCheckBoxThreeStateTests, so this dialog delegates again.
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCompactCheckBox(checkBox, PivotDialogChromeStyle);");
        source.Should().NotContain("IsVisible = checkBox.IsChecked is null");
        source.Should().Contain("Content = labelFilter is null ? \"Add Label Filter...\" : \"Edit Label Filter...\"");
        source.Should().Contain("Content = valueFilter is null ? \"Add Value Filter...\" : \"Edit Value Filter...\"");
        source.Should().NotContain("PlaceholderText = StripDisplayMnemonic(UiText.Get(\"PivotFieldFilter_Search\"))");
    }

    [Fact]
    public void ParityPivotFixture_UsesWpfPartialSelectionAndMemberOrder()
    {
        var captureSource = File.ReadAllText(RepoFile("tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ParityCapture.cs"));
        var filterSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFilters.cs"));
        var itemReaderSource = File.ReadAllText(RepoFile(
            "src",
            "FreeX.App.Presentation",
            "SlicerTimeline",
            "PivotFieldItemsReader.cs"));

        captureSource.Should().Contain("new PivotFieldModel(0, SelectedItems: [\"North\", \"South\"])");
        captureSource.Should().Contain("exposeActiveFilterActions: false");
        filterSource.Should().Contain("ResolveSelectAllState(");
        filterSource.Should().Contain("PivotApplication.ReadSourceItems(");
        itemReaderSource.Should().Contain("new HashSet<string>(StringComparer.CurrentCultureIgnoreCase)");
        itemReaderSource.Should().Contain("values.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeX.slnx", parts);
}
