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

    [Fact]
    public void FieldUpdateCoordinationBelongsSolelyToTheReferenceEditingCoordinator()
    {
        // DocumentFieldUpdateCoordinator was a second, unwired copy of the F9 field-update pass: every
        // shipping call site (both shells) went through DocumentEditingSession.References
        // (DocumentReferenceEditingCoordinator) instead, so a fix applied only to the dead copy changed
        // nothing a user could observe. Guard the deletion so a future "share this logic" pass cannot
        // reintroduce an orphaned duplicate coordinator that a fixer might mistake for the live path.
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var deadCoordinatorPath = Path.Combine(
            root, "freew", "FreeW.App.Presentation", "DocumentView", "DocumentFieldUpdateCoordinator.cs");
        File.Exists(deadCoordinatorPath).Should().BeFalse(
            "DocumentFieldUpdateCoordinator has no production caller; field-update coordination lives in " +
            "DocumentReferenceEditingCoordinator, reached via DocumentEditingSession.References");

        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        wpf.Should().NotContain("DocumentFieldUpdateCoordinator");
        avalonia.Should().NotContain("DocumentFieldUpdateCoordinator");
        wpf.Should().Contain("DocumentReferenceEditingCoordinator ReferenceEdits");
        avalonia.Should().Contain("DocumentReferenceEditingCoordinator ReferenceEdits");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
