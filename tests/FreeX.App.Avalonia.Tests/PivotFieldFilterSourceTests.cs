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
    public void ItemFilterDialog_UsesLocalizedNoFilterAndExactValueFieldOwnership()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFilters.cs"));

        source.Should().Contain("UiText.Get(\"PivotFieldFilter_NoItemFilter\")");
        source.Should().Contain("UiText.Get(\"PivotFieldFilter_NoLabelFilter\")");
        source.Should().Contain("UiText.Get(\"PivotFieldFilter_NoValueFilter\")");
        source.Should().Contain("filter.SourceFieldIndex == target.SourceFieldIndex");
        source.Should().NotContain("filter.SourceFieldIndex is null || filter.SourceFieldIndex == target.SourceFieldIndex");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory) && !File.Exists(Path.Combine(directory, "FreeX.slnx")))
            directory = Directory.GetParent(directory)?.FullName;

        return Path.Combine(directory ?? throw new DirectoryNotFoundException("Repository root not found."), Path.Combine(parts));
    }
}
