using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookShareReadinessPlannerTests
{
    [Fact]
    public void FormatStatus_PreservesDefaultWindowsShareWording()
    {
        WorkbookShareReadinessPlanner.FormatStatus(new WorkbookShareReadinessPlan(
                WorkbookShareReadinessPlanKind.ShareExistingFile,
                @"C:\Work\Budget.xlsx"))
            .Should()
            .Be(@"Ready for Windows Share from C:\Work\Budget.xlsx.");

        WorkbookShareReadinessPlanner.FormatStatus(new WorkbookShareReadinessPlan(
                WorkbookShareReadinessPlanKind.SaveAsBeforeShare,
                null,
                WorkbookShareReadinessSaveAsReason.MissingFile,
                @"C:\Missing\Budget.xlsx"))
            .Should()
            .Be(@"Save As is required before Windows Share can send the workbook because the saved path is missing: C:\Missing\Budget.xlsx.");
    }

    [Fact]
    public void CreatePlan_UsesInjectedMacOsShareSurfaceWithoutWinRt()
    {
        var surface = new WorkbookShareSurface("macOS Share");
        var expectedPath = Path.GetFullPath("Budget.xlsx");

        var plan = WorkbookShareReadinessPlanner.CreatePlan(
            "  Budget.xlsx  ",
            surface,
            path => path == expectedPath);

        plan.Kind.Should().Be(WorkbookShareReadinessPlanKind.ShareExistingFile);
        plan.Path.Should().Be(expectedPath);
        plan.SaveAsReason.Should().Be(WorkbookShareReadinessSaveAsReason.None);
        plan.EffectiveSurface.Should().Be(surface);
        WorkbookShareReadinessPlanner.FormatStatus(plan)
            .Should()
            .Be($"Ready for macOS Share from {expectedPath}.");
    }

    [Fact]
    public void CreatePlan_HonorsInjectedSurfaceCapabilityBeforeFileProbe()
    {
        var surface = new WorkbookShareSurface("macOS Share", CanShareLocalFiles: false);

        var plan = WorkbookShareReadinessPlanner.CreatePlan(
            "Budget.xlsx",
            surface,
            _ => throw new InvalidOperationException("unavailable surfaces must not probe the file system"));

        plan.Kind.Should().Be(WorkbookShareReadinessPlanKind.ShareSurfaceUnavailable);
        plan.Path.Should().BeNull();
        plan.SaveAsReason.Should().Be(WorkbookShareReadinessSaveAsReason.None);
        plan.CandidatePath.Should().BeNull();
        plan.EffectiveSurface.Should().Be(surface);
        WorkbookShareReadinessPlanner.FormatStatus(plan)
            .Should()
            .Be("macOS Share cannot send local workbook files from this build.");
    }
}
