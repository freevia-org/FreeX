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
            "freew", "FreeW.Core.Model", "TableGridProjection.cs");
        var coreCommands = ReadSource("freew", "FreeW.Core.Model", "EditCommands.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var presentationFiles = ReadSourcesUnder(
            "freew", "FreeW.App.Presentation");

        projection.Should().Contain("public static class TableGridProjection");
        coreCommands.Should().Contain("TableGridProjection.At(");
        coreCommands.Should().Contain("TableGridProjection.ProjectRow(");
        coreCommands.Should().Contain("TableGridProjection.RowWidth(");
        coreCommands.Should().NotContain("TableColumnHelpers");
        wpf.Should().Contain("TableGridProjection.ProjectRow(");
        avalonia.Should().Contain("TableGridProjection.ProjectRow(");
        presentationFiles.Should().Contain("TableGridProjection.At(");
        presentationFiles.Should().Contain("TableGridProjection.ProjectRow(");
        presentationFiles.Should().Contain("TableGridProjection.TableWidth(");
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
        var wpfView = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        ribbon.Should().Contain("ReviewAuthorIdentityPlanner.BuildCommentStamp(");
        ribbon.Should().NotContain("private static class CommentAuthor");
        wpfView.Should().Contain("ReviewAuthorIdentityPlanner.ResolveAuthor(");
        wpfView.Should().NotContain("return string.IsNullOrWhiteSpace(author) ? \"FreeW User\"");
        ribbon.Should().Contain("StyleDialogPlanner.BuildStyleNamesById(editor.Model)");
        ribbon.Should().NotContain("private static IReadOnlyDictionary<string, string> StyleNamesById");
        avalonia.Should().Contain("CommentInitialsPolicy.ResolveBadge(");
        avalonia.Should().Contain("ReviewAuthorIdentityPlanner.BuildCommentStamp(");
        avalonia.Should().Contain("ReviewAuthorIdentityPlanner.ResolveAuthor(");
        avalonia.Should().NotContain("CommentInitialsPolicy.FirstAndLastWords");
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
