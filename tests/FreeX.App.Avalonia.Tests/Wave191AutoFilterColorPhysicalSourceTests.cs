using FluentAssertions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave191AutoFilterColorPhysicalSourceTests
{
    [Fact]
    public void ColorPhysicalSelector_RequiresRenderedFillSwatchAndExactDxfPostconditions()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var fixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave191AutoFilterColorFixture.ps1");

        runner.Should().Contain("autofilter-color-persistence");
        runner.Should().Contain("autofilter-color-fill-save-reopen-physical");
        runner.Should().Contain("New-FreeXWave191AutoFilterColorFixture.ps1");
        runner.Should().Contain("Assert-AutoFilterColorPostcondition");
        runner.Should().Contain("before-rgb=#FFFFFF");
        runner.Should().Contain("sample-rgb=#00B050");
        probe.Should().Contain("probe_autofilter_color_persistence_physical");
        probe.Should().Contain("verify_rendered_fill_swatch");
        probe.Should().Contain("%[hex:p{${sample_x},${sample_y}}]");
        probe.Should().Contain("autofilter-color-swatch-gate.txt");
        probe.Should().Contain("criteria=\"$(verify_rendered_fill_swatch");
        probe.Should().Contain("click_autofilter_control 110 220");
        probe.Should().Contain("swatch-gate=$swatch_gate");
        probe.Should().NotContain("criteria=\"fill:#00B050\"");
        probe.Should().Contain("fill:#00B050");
        probe.Should().Contain("North,East,");
        probe.Should().Contain("ref=A1:B5|colId=0|cellColor=1");
        probe.Should().Contain("FF00B050");
        probe.Should().Contain("copy_cell_formula_by_address A4");
        probe.Should().Contain("status\":\"failed\"");
        fixture.Should().Contain("00B050");
        fixture.Should().Contain("FFC000");
        fixture.Should().Contain("<autoFilter ref=`\"A1:B5`\"");
    }

    [Fact]
    public void ColorPhysicalSurface_UsesSharedColorWorkflowAndRealAvaloniaFlyout()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs");
        var workflow = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Presentation", "Filtering", "WorksheetFilterWorkflowSession.cs");

        source.Should().Contain("CreateAutoFilterColorPanel(model.ColorOptions");
        source.Should().Contain("new AutoFilterColorFilter(option.Kind, option.Color)");
        source.Should().Contain("_filterWorkflowSession.PlanDialogResult(");
        source.Should().Contain("_session.ExecuteWorksheetFilterMutationPlan(plan)");
        source.Should().NotContain("new CellFillColorFilterCommand(");
        workflow.Should().Contain("CellFillColorFilterCommand");
        workflow.Should().Contain("AutoFilterColorFilterKind.CellFillColor");
    }

    [Fact]
    public void ColorEvidenceManifest_HashesMatchDeclaredCrossPlatformPolicy()
    {
        var manifestPath = TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "docs", "parity", "evidence", "wave191-freex-autofilter-color-20260823", "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var evidenceDirectory = Path.GetDirectoryName(manifestPath)!;
        var hashPolicy = manifest.RootElement.GetProperty("hashPolicy");
        hashPolicy.GetProperty("canonical-lf").GetString().Should().Contain("strict UTF-8");
        hashPolicy.GetProperty("raw").GetString().Should().Contain("exact file bytes");

        VerifyHashes(
            manifest.RootElement.GetProperty("files"),
            path => Path.Combine(evidenceDirectory, path));
        VerifyHashes(
            manifest.RootElement.GetProperty("provenanceFiles"),
            path => TestWorkspaceFileLocator.FindFromWorkspaceRoot(path.Split('/')));

        var attributes = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(".gitattributes");
        attributes.Should().Contain("tools/Run-FreeXLinuxInteractionValidation.ps1 text eol=lf");
        attributes.Should().Contain("docs/parity/evidence/wave191-freex-autofilter-color-20260823/*.txt text eol=lf");
        attributes.Should().Contain("docs/parity/evidence/wave191-freex-autofilter-color-20260823/*.json text eol=lf");
    }

    [Fact]
    public void ColorEvidenceCanonicalLfHash_IsInvariantAcrossExistingWindowsCheckouts()
    {
        var lf = Encoding.UTF8.GetBytes("first\nsecond\n");
        var crlf = Encoding.UTF8.GetBytes("first\r\nsecond\r\n");
        var cr = Encoding.UTF8.GetBytes("first\rsecond\r");

        ComputeHash(lf, "canonical-lf").Should().Be(ComputeHash(crlf, "canonical-lf"));
        ComputeHash(lf, "canonical-lf").Should().Be(ComputeHash(cr, "canonical-lf"));
        ComputeHash(lf, "raw").Should().NotBe(ComputeHash(crlf, "raw"),
            "raw binary hashing must never normalize file bytes");
    }

    private static void VerifyHashes(JsonElement entries, Func<string, string> resolvePath)
    {
        foreach (var entry in entries.EnumerateArray())
        {
            var relativePath = entry.GetProperty("path").GetString()!;
            var expected = entry.GetProperty("sha256").GetString()!;
            var hashMode = entry.GetProperty("hashMode").GetString()!;
            var extension = Path.GetExtension(relativePath);
            if (extension is ".png" or ".xlsx")
                hashMode.Should().Be("raw", $"binary evidence {relativePath} must remain byte-exact");
            else
                hashMode.Should().Be("canonical-lf", $"text evidence/provenance {relativePath} must be checkout-independent");

            var actual = ComputeHash(File.ReadAllBytes(resolvePath(relativePath)), hashMode);
            actual.Should().Be(expected, $"the {hashMode} bytes for {relativePath} must match the manifest");
        }
    }

    private static string ComputeHash(byte[] bytes, string hashMode)
    {
        var hashBytes = hashMode switch
        {
            "raw" => bytes,
            "canonical-lf" => Encoding.UTF8.GetBytes(
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetString(bytes)
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace("\r", "\n", StringComparison.Ordinal)),
            _ => throw new InvalidDataException($"Unknown Wave191 hash mode '{hashMode}'."),
        };
        return Convert.ToHexString(SHA256.HashData(hashBytes)).ToLowerInvariant();
    }
}
