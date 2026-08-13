namespace FreeW.Core.IO.Tests;

public sealed class DocumentFileDialogRequestPlannerTests
{
    [Fact]
    public void BuildSaveDialogPlanFromSourceName_PreservesCurrentExtensionAndFilterIndex()
    {
        var adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();

        var plan = DocumentFileDialogRequestPlanner.BuildSaveDialogPlanFromSourceName(
            adapters,
            sourceName: "Letter.rtf",
            fallbackDisplayName: "Document",
            defaultExtensionWithDot: ".rtf");

        plan.SuggestedFileName.Should().Be("Letter.rtf");
        plan.DefaultExtensionWithDot.Should().Be(".rtf");
        plan.DefaultExtensionWithoutDot.Should().Be("rtf");
        plan.FilterIndex.Should().Be(
            DocumentFileDialogRequestPlanner.BuildSaveDialogPlan(adapters, "", ".rtf").FilterIndex);
        plan.Filter.Should().Contain("Rich Text Format (*.rtf)|*.rtf");
    }

    [Fact]
    public void BuildSavePickerPlan_UsesSharedPickerPlanShape()
    {
        var plan = DocumentFileDialogRequestPlanner.BuildSavePickerPlan(
            DocumentFileAdapterCatalog.CreateDefaultAdapters(),
            sourceName: null,
            fallbackDisplayName: "Document",
            defaultExtensionWithDot: ".docx");

        plan.SuggestedFileName.Should().Be("Document.docx");
        plan.DefaultExtensionWithoutDot.Should().Be("docx");
        plan.FileTypes[0].DisplayName.Should().Be("Word Document");
        plan.FileTypes[0].Patterns.Should().Equal("*.docx");
    }
}
