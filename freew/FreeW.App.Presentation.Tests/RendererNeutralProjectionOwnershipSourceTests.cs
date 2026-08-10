namespace FreeW.App.Presentation.Tests;

public sealed class RendererNeutralProjectionOwnershipSourceTests
{
    [Fact]
    public void EquationFactoriesAndCommandIdsBelongToThePortableCatalog()
    {
        var catalog = ReadSource("freew", "FreeW.App.Presentation", "Ribbon", "EquationPresetCatalog.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");
        var avaloniaDocumentView = ReadSource(
            "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        catalog.Should().Contain("public static class EquationPresetCatalog");
        wpf.Should().Contain("foreach (var preset in EquationPresetCatalog.Presets)");
        avalonia.Should().Contain("foreach (var preset in EquationPresetCatalog.Presets)");
        avaloniaDocumentView.Should().Contain("EquationPresetCatalog.CreateDefaultEquation()");
        wpf.Should().NotContain("MathRun.Fraction(\"a\", \"b\")");
        avalonia.Should().NotContain("MathRun.Fraction(\"a\", \"b\")");
        avaloniaDocumentView.Should().NotContain("DefaultSampleEquation");
    }

    [Fact]
    public void LogicalTableGridArithmeticBelongsToThePortableProjection()
    {
        var projection = ReadSource(
            "freew", "FreeW.App.Presentation", "Editing", "TableGridProjection.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var presentationFiles = ReadSourcesUnder(
            "freew", "FreeW.App.Presentation");

        projection.Should().Contain("public static class TableGridProjection");
        wpf.Should().Contain("TableGridProjection.ProjectRow(");
        avalonia.Should().Contain("TableGridProjection.ProjectRow(");
        presentationFiles.Should().Contain("TableGridProjection.At(");
        wpf.Should().NotContain("Math.Max(1, modelCell.GridSpan)");
        avalonia.Should().NotContain("Math.Max(1, cell.GridSpan)");
        presentationFiles.Should().NotContain("Math.Max(1, cell.GridSpan)");
    }

    [Fact]
    public void ListHeadingAndTextRangeProjectionBelongToPresentation()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        wpf.Should().Contain("new DocumentListMarkerSequencePlanner(");
        avalonia.Should().Contain("new DocumentListMarkerSequencePlanner(");
        wpf.Should().NotContain("new MultiLevelListMarkerState(");
        avalonia.Should().NotContain("new MultiLevelListMarkerState(");
        wpf.Should().Contain("OutlineViewController.HeadingStyleIdForLevel(level)");
        avalonia.Should().Contain("OutlineViewController.HeadingStyleIdForLevel(level)");
        wpf.Should().Contain("DocumentTextRangeProjection.TryProject(");
        avalonia.Should().Contain("DocumentTextRangeProjection.TryProject(");
    }

    [Fact]
    public void RemainingOwnedCommentAndStyleCallSitesUsePortablePolicies()
    {
        var ribbon = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        ribbon.Should().Contain("CommentInitialsPolicy.Derive(");
        ribbon.Should().Contain("StyleDialogPlanner.BuildStyleNamesById(editor.Model)");
        ribbon.Should().NotContain("private static IReadOnlyDictionary<string, string> StyleNamesById");
        avalonia.Should().Contain("CommentInitialsPolicy.ResolveBadge(");
        avalonia.Should().Contain("CommentInitialsPolicy.FirstAndLastWords");
        avalonia.Should().NotContain("private static string DeriveInitials");
    }

    private static string ReadSourcesUnder(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var directory = Path.Combine([root, .. parts]);
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
