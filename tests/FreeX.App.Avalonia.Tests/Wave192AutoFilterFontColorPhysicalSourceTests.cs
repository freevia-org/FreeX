using FluentAssertions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave192AutoFilterFontColorPhysicalSourceTests
{
    [Fact]
    public void FontColorPhysicalSelector_RequiresRenderedFontSwatchAndExactFontDxfPostconditions()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var fixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave192AutoFilterFontColorFixture.ps1");

        runner.Should().Contain("autofilter-font-color-persistence");
        runner.Should().Contain("autofilter-color-font-save-reopen-physical");
        runner.Should().Contain("New-FreeXWave192AutoFilterFontColorFixture.ps1");
        runner.Should().Contain("Assert-AutoFilterFontColorPostcondition");
        runner.Should().Contain("sample-rgb=#00B050");
        probe.Should().Contain("probe_autofilter_color_persistence_physical font");
        probe.Should().Contain("verify_rendered_color_swatch");
        probe.Should().Contain("mode=${swatch_mode}");
        probe.Should().Contain("%[hex:p{${sample_x},${sample_y}}]");
        probe.Should().Contain("${prefix}-swatch-gate.txt");
        probe.Should().Contain("criteria=font:#00B050");
        probe.Should().Contain("cellColor=0");
        probe.Should().Contain("font=FF00B050");
        probe.Should().Contain("expected_package_mode");
        probe.Should().Contain("expected_package_color");
        probe.Should().Contain("copy_cell_formula_by_address A4");
        probe.Should().Contain("click_autofilter_control 110 220");
        probe.Should().Contain("swatch-gate=$swatch_gate");
        probe.Should().Contain("status\":\"failed\"");
        fixture.Should().Contain("<fonts count=\"2\">");
        fixture.Should().Contain("<color rgb=\"FF00B050\"/>");
        fixture.Should().Contain("<autoFilter ref=`\"A1:B5`\"");
    }

    [Fact]
    public void FontColorPhysicalSurface_UsesSharedColorWorkflowAndProductionReopenRoute()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs");
        var workflow = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Presentation", "Filtering", "WorksheetFilterWorkflowSession.cs");
        var command = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.Core.Commands", "FilterCommand.cs");

        source.Should().Contain("CreateAutoFilterColorPanel(model.ColorOptions");
        source.Should().Contain("new AutoFilterColorFilter(option.Kind, option.Color)");
        workflow.Should().Contain("AutoFilterColorFilterKind.FontColor");
        workflow.Should().Contain("new CellFontColorFilterCommand");
        command.Should().Contain("ColorFilter: new WorksheetAutoFilterColorFilterModel(CellColor: false, Color: _fontColor)");
    }

    [Fact]
    public void FontColorEvidenceManifest_HashesMatchDeclaredCrossPlatformPolicy()
    {
        var manifestPath = TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "docs", "parity", "evidence", "wave192-freex-autofilter-font-color-20260823", "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var evidenceDirectory = Path.GetDirectoryName(manifestPath)!;
        var hashPolicy = manifest.RootElement.GetProperty("hashPolicy");
        hashPolicy.GetProperty("canonical-lf").GetString().Should().Contain("strict UTF-8");
        hashPolicy.GetProperty("raw").GetString().Should().Contain("exact file bytes");

        VerifyHashes(manifest.RootElement.GetProperty("files"), path => Path.Combine(evidenceDirectory, path));
        VerifyHashes(manifest.RootElement.GetProperty("provenanceFiles"), path =>
            TestWorkspaceFileLocator.FindFromWorkspaceRoot(path.Split('/')));
        foreach (var audit in manifest.RootElement.GetProperty("gitBlobAudit").EnumerateArray())
        {
            audit.GetProperty("hashMode").GetString().Should().Be("canonical-lf");
            audit.GetProperty("match").GetBoolean().Should().BeTrue();
            audit.GetProperty("worktreeSha256").GetString().Should().Be(audit.GetProperty("gitBlobContentSha256").GetString());
        }

        var attributes = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(".gitattributes");
        attributes.Should().Contain("tools/LinuxInteractiveDocker/New-FreeXWave192AutoFilterFontColorFixture.ps1 text eol=lf");
        attributes.Should().Contain("docs/parity/evidence/wave192-freex-autofilter-font-color-20260823/*.txt text eol=lf");
        attributes.Should().Contain("docs/parity/evidence/wave192-freex-autofilter-font-color-20260823/*.json text eol=lf");
    }

    [Fact]
    public void FontColorEvidenceCanonicalLfHash_IsInvariantAcrossExistingWindowsCheckouts()
    {
        var lf = Encoding.UTF8.GetBytes("first\nsecond\n");
        var crlf = Encoding.UTF8.GetBytes("first\r\nsecond\r\n");
        var cr = Encoding.UTF8.GetBytes("first\rsecond\r");

        ComputeHash(lf, "canonical-lf").Should().Be(ComputeHash(crlf, "canonical-lf"));
        ComputeHash(lf, "canonical-lf").Should().Be(ComputeHash(cr, "canonical-lf"));
        ComputeHash(lf, "raw").Should().NotBe(ComputeHash(crlf, "raw"));
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
                hashMode.Should().Be("raw");
            else
                hashMode.Should().Be("canonical-lf");
            ComputeHash(File.ReadAllBytes(resolvePath(relativePath)), hashMode)
                .Should().Be(expected, $"the {hashMode} bytes for {relativePath} must match the manifest");
        }
    }

    private static string ComputeHash(byte[] bytes, string hashMode)
    {
        var hashBytes = hashMode switch
        {
            "raw" => bytes,
            "canonical-lf" => Encoding.UTF8.GetBytes(
                new UTF8Encoding(false, true).GetString(bytes)
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace("\r", "\n", StringComparison.Ordinal)),
            _ => throw new InvalidDataException($"Unknown Wave192 hash mode '{hashMode}'."),
        };
        return Convert.ToHexString(SHA256.HashData(hashBytes)).ToLowerInvariant();
    }
}
