using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class VisualEvidenceProtocolOwnershipTests
{
    [Fact]
    public void Runtime_shared_project_owns_the_cross_app_evidence_protocol()
    {
        var protocol = TestWorkspaceFiles.ReadRepoText(
            "shared", "Free.Shared.AppServices", "VisualEvidence", "VisualEvidenceProtocol.cs");
        var freeP = TestWorkspaceFiles.ReadRepoText(
            "tools", "FreeP.RenderCompare", "VisualEvidenceCaptureOrchestration.cs");
        var freeXWpf = TestWorkspaceFiles.ReadRepoText(
            "src", "FreeX.App.Host", "ParityCapture.cs");
        var freeXAvalonia = TestWorkspaceFiles.ReadRepoText(
            "src", "FreeX.App.Avalonia", "ParityCapture.cs");
        var freeXParityCore = TestWorkspaceFiles.ReadRepoText(
            "tools", "FreeX.ParityCompare.Core", "ParityComparison.cs");
        var freeW = TestWorkspaceFiles.ReadRepoText(
            "freew", "FreeW.App.Presentation", "DocumentView", "VisualEvidenceManifestNormalizer.cs");
        var freeWRibbonShot = TestWorkspaceFiles.ReadRepoText(
            "freew", "tools", "FreeW.RibbonShot", "Program.cs");
        var freeWDialogHarness = TestWorkspaceFiles.ReadRepoText(
            "freew", "tools", "FreeW.DialogVisualHarness", "Program.cs");
        var freeWDialogCatalog = TestWorkspaceFiles.ReadRepoText(
            "freew", "tools", "FreeW.DialogVisualHarness", "FreeWDialogEvidenceCatalog.cs");
        var productProjects = new[]
        {
            TestWorkspaceFiles.ReadRepoText("src", "FreeX.App.Host", "FreeX.App.Host.csproj"),
            TestWorkspaceFiles.ReadRepoText("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj"),
            TestWorkspaceFiles.ReadRepoText("freew", "FreeW.App.Presentation", "FreeW.App.Presentation.csproj"),
            TestWorkspaceFiles.ReadRepoText("freep", "FreeP.App.Host", "FreeP.App.Host.csproj"),
            TestWorkspaceFiles.ReadRepoText("freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj"),
        };

        protocol.Should().Contain("public static class VisualEvidenceArgumentParser");
        protocol.Should().Contain("public static class VisualEvidencePathPolicy");
        protocol.Should().Contain("public static class VisualEvidenceManifestIO");
        protocol.Should().Contain("public static class VisualEvidenceProgressLog");
        protocol.Should().Contain("public static class VisualEvidenceHash");
        protocol.Should().Contain("public static class VisualEvidenceNormalization");
        protocol.Should().Contain("namespace Free.Shared.AppServices;");
        productProjects.Should().OnlyContain(project =>
            !project.Contains(@"tools\Free.ToolsShared", StringComparison.OrdinalIgnoreCase));

        freeP.Should().Contain("VisualEvidenceArgumentParser.ReadFirst(");
        freeP.Should().Contain("VisualEvidencePathPolicy.ResolveContainedPath(");
        freeP.Should().Contain("VisualEvidenceProgressLog.Append(");
        freeP.Should().Contain("VisualEvidenceManifestIO.Write(");
        freeP.Should().NotContain("SHA256.HashData(");

        freeXWpf.Should().Contain("VisualEvidenceArgumentParser.ReadFirst(");
        freeXWpf.Should().Contain("VisualEvidenceManifestIO.Write(");
        freeXAvalonia.Should().Contain("VisualEvidenceArgumentParser.Parse(");
        freeXAvalonia.Should().Contain("VisualEvidenceManifestIO.Write(");
        freeXParityCore.Should().Contain("VisualEvidenceTextPolicy.ToAlphaNumericSafeArtifactName(");
        freeXParityCore.Should().NotContain("var chars = id.Select(");

        freeW.Should().Contain("VisualEvidenceManifestIO.Read<FreeWVisualEvidenceManifest>(");
        freeW.Should().Contain("VisualEvidencePathPolicy.IsContained(");
        freeW.Should().Contain("VisualEvidenceHash.Sha256File(");
        freeW.Should().NotContain("private static string ComputeSha256(");
        freeW.Should().NotContain("private static bool IsSubPathOf(");

        freeWRibbonShot.Should().Contain("VisualEvidenceTextPolicy.ToLowerSafeArtifactName(");
        freeWRibbonShot.Should().Contain("VisualEvidenceManifestIO.Write(");
        freeWRibbonShot.Should().NotContain("static string SanitizeFileName(");
        freeWDialogHarness.Should().Contain("VisualEvidenceTextPolicy.ToAsciiSafeArtifactName(");
        freeWDialogHarness.Should().Contain("VisualEvidenceHash.Sha256Text(");
        freeWDialogHarness.Should().Contain("VisualEvidenceManifestIO.Read<T>(");
        freeWDialogCatalog.Should().Contain("VisualEvidenceTextPolicy.ToAsciiSafeArtifactName(");
        freeWDialogCatalog.Should().NotContain("Regex.Replace(value, \"[^A-Za-z0-9._-]\", \"-\")");
    }
}
