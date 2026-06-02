using System.IO;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class ShareWorkbookPlannerTests
{
    [Fact]
    public void CreatePlan_UsesCurrentFilePath_WhenWorkbookIsSaved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(path, "workbook");

        try
        {
            var plan = ShareWorkbookPlanner.CreatePlan(path);

            plan.Kind.Should().Be(ShareWorkbookPlanKind.ShareExistingFile);
            plan.Path.Should().Be(path);
            plan.SaveAsReason.Should().Be(ShareWorkbookSaveAsReason.None);
            plan.CandidatePath.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreatePlan_RequiresSaveAs_WhenCurrentFilePathNoLongerExists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");

        var plan = ShareWorkbookPlanner.CreatePlan(path);

        plan.Kind.Should().Be(ShareWorkbookPlanKind.SaveAsBeforeShare);
        plan.Path.Should().BeNull();
        plan.SaveAsReason.Should().Be(ShareWorkbookSaveAsReason.MissingFile);
        plan.CandidatePath.Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public void CreatePlan_UsesInjectedFileProbe_ForDeterministicCallers()
    {
        var plan = ShareWorkbookPlanner.CreatePlan(
            @"C:\Work\Budget.xlsx",
            path => path == @"C:\Work\Budget.xlsx");

        plan.Kind.Should().Be(ShareWorkbookPlanKind.ShareExistingFile);
        plan.Path.Should().Be(@"C:\Work\Budget.xlsx");
    }

    [Fact]
    public void CreatePlan_TrimsAndNormalizesCurrentPathBeforeSharing()
    {
        var expectedPath = Path.GetFullPath("Budget.xlsx");

        var plan = ShareWorkbookPlanner.CreatePlan(
            "  Budget.xlsx  ",
            path => path == expectedPath);

        plan.Kind.Should().Be(ShareWorkbookPlanKind.ShareExistingFile);
        plan.Path.Should().Be(expectedPath);
    }

    [Fact]
    public void CreatePlan_RejectsInvalidPathWithoutProbingFileSystem()
    {
        var plan = ShareWorkbookPlanner.CreatePlan(
            "bad\0path.xlsx",
            _ => throw new InvalidOperationException("invalid paths must not be probed"));

        plan.Kind.Should().Be(ShareWorkbookPlanKind.SaveAsBeforeShare);
        plan.Path.Should().BeNull();
        plan.SaveAsReason.Should().Be(ShareWorkbookSaveAsReason.InvalidPath);
        plan.CandidatePath.Should().Be("bad\0path.xlsx");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePlan_RequiresSaveAs_WhenWorkbookHasNoFilePath(string? currentFilePath)
    {
        var plan = ShareWorkbookPlanner.CreatePlan(currentFilePath);

        plan.Kind.Should().Be(ShareWorkbookPlanKind.SaveAsBeforeShare);
        plan.Path.Should().BeNull();
        plan.SaveAsReason.Should().Be(ShareWorkbookSaveAsReason.UnsavedWorkbook);
        plan.CandidatePath.Should().BeNull();
    }

    [Fact]
    public void FormatStatus_ExplainsShareReadinessAndSaveAsReasons()
    {
        ShareWorkbookPlanner.FormatStatus(new ShareWorkbookPlan(
                ShareWorkbookPlanKind.ShareExistingFile,
                @"C:\Work\Budget.xlsx"))
            .Should()
            .Be(@"Ready for Windows Share from C:\Work\Budget.xlsx.");

        ShareWorkbookPlanner.FormatStatus(new ShareWorkbookPlan(
                ShareWorkbookPlanKind.SaveAsBeforeShare,
                null,
                ShareWorkbookSaveAsReason.MissingFile,
                @"C:\Missing\Budget.xlsx"))
            .Should()
            .Be(@"Save As is required before Windows Share can send the workbook because the saved path is missing: C:\Missing\Budget.xlsx.");

        ShareWorkbookPlanner.FormatStatus(new ShareWorkbookPlan(
                ShareWorkbookPlanKind.SaveAsBeforeShare,
                null,
                ShareWorkbookSaveAsReason.InvalidPath,
                "bad\0path.xlsx"))
            .Should()
            .Be("Save As is required before Windows Share can send the workbook because the saved path is not a valid local file path.");
    }
}
