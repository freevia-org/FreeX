namespace FreeW.App.Presentation.Tests;

public sealed class CoreDocumentViewResidualOwnershipSourceTests
{
    [Fact]
    public void NoteFormattingBelongsToCore()
    {
        var formatter = ReadSource("freew", "FreeW.Core.Model", "NoteNumberFormatter.cs");
        var crossReferences = ReadSource("freew", "FreeW.Core.Model", "CrossReferences.cs");
        var notePlanner = ReadSource(
            "freew", "FreeW.App.Presentation", "DocumentView", "DocumentNoteRegionPlanner.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        formatter.Should().Contain("public static class NoteNumberFormatter");
        crossReferences.Should().Contain("NoteNumberFormatter.Format(");
        notePlanner.Should().Contain("NoteNumberFormatter.Format(");
        avalonia.Should().Contain("NoteNumberFormatter.Format(");
        crossReferences.Should().NotContain("private static string FormatNoteNumber");
        notePlanner.Should().NotContain("private static string ToChicago");
        avalonia.Should().NotContain("NoteNumberFormat.LowerRoman => ToRoman(n");
    }

    [Fact]
    public void TableColumnGeometryBelongsToPresentationPlanner()
    {
        var planner = ReadSource(
            "freew", "FreeW.App.Presentation", "DocumentView", "TableColumnLayoutPlanner.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        planner.Should().Contain("public static class TableColumnLayoutPlanner");
        wpf.Should().Contain("TableColumnLayoutPlanner.BuildContentAutoFitWidths(");
        wpf.Should().Contain("TableColumnLayoutPlanner.ResolveTableWidthDip(");
        avalonia.Should().Contain("TableColumnLayoutPlanner.AllocateColumnWidths(");
        wpf.Should().NotContain("private static double ResolveTableWidthDip");
        avalonia.Should().NotContain("var declaredCount = 0;");
    }

    [Fact]
    public void HeaderFooterSemanticTextBelongsToVisualPlanner()
    {
        var planner = ReadSource(
            "freew", "FreeW.App.Presentation", "DocumentView", "HeaderFooterVisualPlanner.cs");
        var printPreview = ReadSource("freew", "FreeW.App.Host", "PrintPreviewWindow.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        planner.Should().Contain("public static string? ResolveFieldText(");
        planner.Should().Contain("public static string ResolveLineText(");
        printPreview.Should().Contain("HeaderFooterVisualPlanner.ResolveLineText(");
        avalonia.Should().Contain("HeaderFooterVisualPlanner.ResolveFieldText(");
        printPreview.Should().NotContain("r.FieldKind == RunFieldKind.PageNumber");
        avalonia.Should().NotContain("private string? ResolveHfField(");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
