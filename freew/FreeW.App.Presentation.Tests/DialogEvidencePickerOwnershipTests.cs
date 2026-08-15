namespace FreeW.App.Presentation.Tests;

public sealed class DialogEvidencePickerOwnershipTests
{
    [Fact]
    public void Draw_table_proofing_and_table_conversion_are_thin_paired_dialogs()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var commandRegistry = Read(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var drawTable = Read(root, "freew", "FreeW.App.Host", "DrawTableDimensionDialog.cs");
        var proofing = Read(root, "freew", "FreeW.App.Host", "ProofingLanguageDialog.cs");
        var avaloniaProofing = Read(root, "freew", "FreeW.App.Avalonia", "ProofingDialogs.cs");
        var conversion = Read(root, "freew", "FreeW.App.Host", "TableTextConversionDialog.cs");
        var catalog = Read(root, "freew", "tools", "FreeW.DialogVisualHarness", "FreeWDialogEvidenceCatalog.cs");

        drawTable.Should().Contain(": Free.Shared.Ribbon.Wpf.DialogWindow")
            .And.Contain("DrawTableCommandPlanner.BuildDialog(")
            .And.Contain("DrawTableCommandPlanner.Normalize(");
        proofing.Should().Contain(": Free.Shared.Ribbon.Wpf.DialogWindow")
            .And.Contain("ProofingLanguageDialogPlanner.Build(");
        avaloniaProofing.Should().Contain("private readonly ListBox _languages")
            .And.Contain("ProofingLanguageDialogPlanner.Build(")
            .And.Contain("Width = 320")
            .And.Contain("Height = 420")
            .And.Contain("Text = plan.Text.Instruction")
            .And.NotContain("private readonly ComboBox _languages");
        conversion.Should().Contain(": Free.Shared.Ribbon.Wpf.DialogWindow")
            .And.Contain("TableTextConversionDialogPlanner.ResolveText(")
            .And.Contain("TableTextConversionDialogPlanner.DelimiterAt(");

        commandRegistry.Should().Contain("DrawTableDimensionDialog.Ask(")
            .And.Contain("ProofingLanguageDialog.Choose(")
            .And.Contain("TableTextConversionDialog.Ask(")
            .And.NotContain("private static class DrawTableDimensionPicker")
            .And.NotContain("private static class DelimiterDialog")
            .And.NotContain("private static string? ShowDialog(Window? owner, string? current)");

        foreach (var route in new[] { "draw-table-dimension", "proofing-language", "table-text-conversion" })
        {
            catalog.Should().Contain($"Pair(\"{route}\"")
                .And.NotContain($"AvaloniaOnly(\"{route}\"");
        }
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine([root, .. relativeParts]));
}
