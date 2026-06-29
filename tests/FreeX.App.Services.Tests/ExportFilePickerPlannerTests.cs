using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ExportFilePickerPlannerTests
{
    [Fact]
    public void BuildPortablePdfPickerPlan_UsesPdfDescriptorAndSuggestedName()
    {
        var plan = ExportFilePickerPlanner.BuildPortablePdfPickerPlan(
            sourceName: "Quarterly Budget.xlsx",
            fallbackDisplayName: "FreeX");

        plan.SuggestedFileName.Should().Be("Quarterly Budget.pdf");
        plan.DefaultExtensionWithoutDot.Should().Be("pdf");
        plan.FileTypes.Should().ContainSingle();
        plan.FileTypes[0].DisplayName.Should().Be(ExportFilePickerPlanner.PdfPickerDisplayName);
        plan.FileTypes[0].Patterns.Should().Equal("*.pdf");
    }

    [Fact]
    public void BuildPortablePdfPickerPlan_FallsBackWhenSourceNameIsBlank()
    {
        var plan = ExportFilePickerPlanner.BuildPortablePdfPickerPlan(
            sourceName: "   ",
            fallbackDisplayName: "   ");

        plan.SuggestedFileName.Should().Be("FreeX.pdf");
    }

    [Fact]
    public void BuildPortablePdfSaveTargetPlan_NormalizesExtensionAndRequestsOverwritePrompt()
    {
        var plan = ExportFilePickerPlanner.BuildPortablePdfSaveTargetPlan(
            @"C:\temp\report.txt",
            path => path == @"C:\temp\report.pdf");

        plan.Path.Should().Be(@"C:\temp\report.pdf");
        plan.ShouldConfirmNormalizedOverwrite.Should().BeTrue();
    }

    [Fact]
    public void BuildPortablePdfSaveTargetPlan_SkipsPromptWhenNormalizedTargetDoesNotExist()
    {
        var plan = ExportFilePickerPlanner.BuildPortablePdfSaveTargetPlan(
            @"C:\temp\report",
            _ => false);

        plan.Path.Should().Be(@"C:\temp\report.pdf");
        plan.ShouldConfirmNormalizedOverwrite.Should().BeFalse();
    }

    [Fact]
    public void BuildPdfXpsDialogPlan_UsesPdfDefaultFilterAndBaseName()
    {
        var plan = ExportFilePickerPlanner.BuildPdfXpsDialogPlan(
            sourceName: "Quarterly Budget.xlsx",
            fallbackDisplayName: "FreeX");

        plan.SuggestedFileName.Should().Be("Quarterly Budget");
        plan.DefaultExtensionWithDot.Should().Be(".pdf");
        plan.DefaultFilterIndex.Should().Be(ExportFilePickerPlanner.PdfXpsDialogPdfFilterIndex);
    }

    [Theory]
    [InlineData(ExportFilePickerPlanner.PdfXpsDialogPdfFilterIndex, ExportFileFormat.Pdf)]
    [InlineData(ExportFilePickerPlanner.PdfXpsDialogXpsFilterIndex, ExportFileFormat.Xps)]
    [InlineData(99, ExportFileFormat.Pdf)]
    public void FormatFromPdfXpsFilterIndex_DefaultsToPdfUnlessXpsIsSelected(
        int filterIndex,
        ExportFileFormat expected)
    {
        var format = ExportFilePickerPlanner.FormatFromPdfXpsFilterIndex(filterIndex);

        format.Should().Be(expected);
    }

    [Fact]
    public void BuildPickerType_UsesStableXpsDescriptor()
    {
        var descriptor = ExportFilePickerPlanner.BuildPickerType(ExportFileFormat.Xps);

        descriptor.DisplayName.Should().Be(ExportFilePickerPlanner.XpsPickerDisplayName);
        descriptor.Patterns.Should().Equal("*.xps");
    }
}
