using FluentAssertions;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class NormalizedOverwritePromptPlannerTests
{
    [Theory]
    [InlineData(
        NormalizedOverwriteTargetKind.Pdf,
        "NormalizedOverwrite_ReplacePdfTitle",
        "NormalizedOverwrite_PdfDetail",
        "PdfExportOverwriteReplaceButton",
        "PdfExportOverwriteCancelButton")]
    [InlineData(
        NormalizedOverwriteTargetKind.Workbook,
        "NormalizedOverwrite_ReplaceWorkbookTitle",
        "NormalizedOverwrite_WorkbookDetail",
        "WorkbookSaveOverwriteReplaceButton",
        "WorkbookSaveOverwriteCancelButton")]
    public void Build_MapsTargetToStableResourcesAndAutomation(
        NormalizedOverwriteTargetKind kind,
        string titleResourceKey,
        string detailResourceKey,
        string replaceAutomationId,
        string cancelAutomationId)
    {
        var plan = NormalizedOverwritePromptPlanner.Build(kind, Path.Combine("folder", "Budget.fxl"));

        plan.FileName.Should().Be("Budget.fxl");
        plan.WindowTitleResourceKey.Should().Be(titleResourceKey);
        plan.DetailResourceKey.Should().Be(detailResourceKey);
        plan.ReplaceButtonAutomationId.Should().Be(replaceAutomationId);
        plan.CancelButtonAutomationId.Should().Be(cancelAutomationId);
        plan.FileExistsFormatResourceKey.Should().Be("NormalizedOverwrite_FileAlreadyExistsFormat");
        plan.ReplaceButtonResourceKey.Should().Be("ShellLoc_ReplaceButton");
        plan.CancelButtonResourceKey.Should().Be("Common_Cancel");
    }

    [Fact]
    public void Build_UsesPathWhenNoFileNameCanBeExtracted()
    {
        var plan = NormalizedOverwritePromptPlanner.Build(
            NormalizedOverwriteTargetKind.Pdf,
            Path.DirectorySeparatorChar.ToString());

        plan.FileName.Should().Be(Path.DirectorySeparatorChar.ToString());
    }
}
