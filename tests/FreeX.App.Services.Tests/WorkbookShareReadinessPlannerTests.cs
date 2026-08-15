using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookShareReadinessPlannerTests
{
    private static string FormatStatus(DocumentShareReadinessPlan plan) =>
        DocumentShareReadinessPlanner.FormatStatus(
            plan,
            DocumentShareReadinessTextSpec.WorkbookEnglish);

    [Fact]
    public void FormatStatus_PreservesDefaultWindowsShareWording()
    {
        FormatStatus(new DocumentShareReadinessPlan(
                DocumentShareReadinessPlanKind.ShareExistingFile,
                @"C:\Work\Budget.xlsx"))
            .Should()
            .Be(@"Ready for Windows Share from C:\Work\Budget.xlsx.");

        FormatStatus(new DocumentShareReadinessPlan(
                DocumentShareReadinessPlanKind.SaveAsBeforeShare,
                null,
                DocumentShareSaveAsReason.MissingFile,
                @"C:\Missing\Budget.xlsx"))
            .Should()
            .Be(@"Save As is required before Windows Share can send the workbook because the saved path is missing: C:\Missing\Budget.xlsx.");
    }

    [Fact]
    public void CreatePlan_UsesInjectedMacOsShareSurfaceWithoutWinRt()
    {
        var surface = new DocumentShareSurface("macOS Share");
        var expectedPath = Path.GetFullPath("Budget.xlsx");

        var plan = DocumentShareReadinessPlanner.CreatePlan(
            "  Budget.xlsx  ",
            surface,
            path => path == expectedPath);

        plan.Kind.Should().Be(DocumentShareReadinessPlanKind.ShareExistingFile);
        plan.Path.Should().Be(expectedPath);
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.None);
        plan.EffectiveSurface.Should().Be(surface);
        FormatStatus(plan)
            .Should()
            .Be($"Ready for macOS Share from {expectedPath}.");
    }

    [Fact]
    public void CreatePlan_AcceptsLocalFileUriForMacOsShare()
    {
        var surface = new DocumentShareSurface("macOS Share");
        var expectedPath = Path.GetFullPath("Budget.xlsx");

        var plan = DocumentShareReadinessPlanner.CreatePlan(
            new Uri(expectedPath).AbsoluteUri,
            surface,
            path => path == expectedPath);

        plan.Kind.Should().Be(DocumentShareReadinessPlanKind.ShareExistingFile);
        plan.Path.Should().Be(expectedPath);
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.None);
    }

    [Fact]
    public void CreatePlan_RejectsNonFileUriBeforeFileProbe()
    {
        var surface = new DocumentShareSurface("macOS Share");

        var plan = DocumentShareReadinessPlanner.CreatePlan(
            "https://example.test/Budget.xlsx",
            surface,
            _ => throw new InvalidOperationException("non-file URIs must not probe the file system"));

        plan.Kind.Should().Be(DocumentShareReadinessPlanKind.SaveAsBeforeShare);
        plan.Path.Should().BeNull();
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.InvalidPath);
        plan.CandidatePath.Should().Be("https://example.test/Budget.xlsx");
        FormatStatus(plan)
            .Should()
            .Be("Save As is required before macOS Share can send the workbook because cloud or web links are not supported; save the workbook to a local file first.");
    }

    [Fact]
    public void FormatStatus_PreservesInvalidLocalPathWording()
    {
        var surface = new DocumentShareSurface("macOS Share");

        var plan = new DocumentShareReadinessPlan(
            DocumentShareReadinessPlanKind.SaveAsBeforeShare,
            null,
            DocumentShareSaveAsReason.InvalidPath,
            "bad\0path.xlsx",
            surface);

        FormatStatus(plan)
            .Should()
            .Be("Save As is required before macOS Share can send the workbook because the saved path is not a valid local file path.");
    }

    [Fact]
    public void CreatePlan_HonorsInjectedSurfaceCapabilityBeforeFileProbe()
    {
        var surface = new DocumentShareSurface("macOS Share", CanShareLocalFiles: false);

        var plan = DocumentShareReadinessPlanner.CreatePlan(
            "Budget.xlsx",
            surface,
            _ => throw new InvalidOperationException("unavailable surfaces must not probe the file system"));

        plan.Kind.Should().Be(DocumentShareReadinessPlanKind.ShareSurfaceUnavailable);
        plan.Path.Should().BeNull();
        plan.SaveAsReason.Should().Be(DocumentShareSaveAsReason.None);
        plan.CandidatePath.Should().BeNull();
        plan.EffectiveSurface.Should().Be(surface);
        FormatStatus(plan)
            .Should()
            .Be("macOS Share cannot send local workbook files from this build.");
    }
}
