using System.Text.Json;
using FluentAssertions;
using FreeX.ParityCompare.Core;

namespace FreeX.ParityCompare.Tests;

/// <summary>
/// End-to-end on synthetic fixtures: writes tiny PNGs (identical, slightly-different,
/// missing-on-one) into temp capture dirs + manifests, runs the engine, and asserts the
/// pairing, diff%, missing flags, and report generation.
/// </summary>
public sealed class ParityEngineReportTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("parity-cmp-test-");
    private readonly string _winDir;
    private readonly string _linDir;
    private readonly string _imagesDir;
    private readonly string _reportDir;
    private string _root => _temporaryDirectory.Path;

    public ParityEngineReportTests()
    {
        _winDir = Path.Combine(_root, "win");
        _linDir = Path.Combine(_root, "lin");
        _imagesDir = Path.Combine(_root, "images");
        _reportDir = Path.Combine(_root, "report");
        foreach (var d in new[] { _winDir, _linDir, _imagesDir, _reportDir })
            Directory.CreateDirectory(d);
    }

    public void Dispose() => _temporaryDirectory.Dispose();

    private void WritePng(string dir, string name, PixelImage img) =>
        PngCodec.EncodeFile(img, Path.Combine(dir, name));

    private (CaptureManifest win, CaptureManifest lin) BuildFixture()
    {
        // grid.demo — identical on both (hard, should be 0% diff, no regression)
        var grid = PixelImage.Solid(60, 40, 200, 210, 220, 255);
        WritePng(_winDir, "grid.demo.png", grid);
        WritePng(_linDir, "grid.demo.png", grid);

        // grid.big — clearly different on linux (hard, exceeds threshold -> regression).
        // 4:3 source fills the 800x600 canvas with no letterbox bars => clean 100% diff.
        WritePng(_winDir, "grid.big.png", PixelImage.Solid(80, 60, 0, 0, 0, 255));
        WritePng(_linDir, "grid.big.png", PixelImage.Solid(80, 60, 255, 255, 255, 255));

        // tab.Home — slightly different chrome (informational, never a regression)
        WritePng(_winDir, "tab.Home.png", PixelImage.Solid(50, 50, 100, 100, 100, 255));
        WritePng(_linDir, "tab.Home.png", PixelImage.Solid(50, 50, 120, 120, 120, 255));

        // dialog.WinOnly — present on Windows only
        WritePng(_winDir, "dialog.WinOnly.png", PixelImage.Solid(30, 30, 1, 2, 3, 255));

        // backstage.LinuxOnly — present on Linux only
        WritePng(_linDir, "backstage.LinuxOnly.png", PixelImage.Solid(30, 30, 9, 8, 7, 255));

        var win = new CaptureManifest
        {
            Platform = "windows", Shell = "wpf",
            Surfaces =
            {
                new() { Id = "grid.demo", Kind = "grid", Png = "grid.demo.png", Captured = true },
                new() { Id = "grid.big", Kind = "grid", Png = "grid.big.png", Captured = true },
                new() { Id = "tab.Home", Kind = "tab", Png = "tab.Home.png", Captured = true },
                new() { Id = "dialog.WinOnly", Kind = "dialog", Png = "dialog.WinOnly.png", Captured = true },
            },
        };
        var lin = new CaptureManifest
        {
            Platform = "linux", Shell = "avalonia",
            Surfaces =
            {
                new() { Id = "grid.demo", Kind = "grid", Png = "grid.demo.png", Captured = true },
                new() { Id = "grid.big", Kind = "grid", Png = "grid.big.png", Captured = true },
                new() { Id = "tab.Home", Kind = "tab", Png = "tab.Home.png", Captured = true },
                new() { Id = "backstage.LinuxOnly", Kind = "backstage", Png = "backstage.LinuxOnly.png", Captured = true },
            },
        };
        return (win, lin);
    }

    [Fact]
    public void Engine_pairs_diffs_and_flags_correctly()
    {
        var (win, lin) = BuildFixture();
        var c = new ParityComparisonEngine().Compare(win, lin, _winDir, _linDir, _imagesDir, hardThreshold: 2.0);

        c.TotalSurfaces.Should().Be(5);
        c.BothCount.Should().Be(3);              // grid.demo, grid.big, tab.Home
        c.WindowsOnlyCount.Should().Be(1);       // dialog.WinOnly
        c.LinuxOnlyCount.Should().Be(1);         // backstage.LinuxOnly

        var demo = c.Surfaces.Single(s => s.Id == "grid.demo");
        demo.Severity.Should().Be(DiffSeverity.Hard);
        demo.DiffPercent.Should().Be(0.0);
        demo.IsHardRegression(2.0).Should().BeFalse();

        var big = c.Surfaces.Single(s => s.Id == "grid.big");
        big.Severity.Should().Be(DiffSeverity.Hard);
        big.DiffPercent.Should().BeApproximately(100.0, 0.001);
        big.IsHardRegression(2.0).Should().BeTrue();

        var home = c.Surfaces.Single(s => s.Id == "tab.Home");
        home.Severity.Should().Be(DiffSeverity.Chrome);
        home.DiffPercent.Should().BeGreaterThan(0);
        home.IsHardRegression(2.0).Should().BeFalse(); // chrome never gate-fails

        c.Surfaces.Single(s => s.Id == "dialog.WinOnly").DiffPercent.Should().BeNull();
        c.Surfaces.Single(s => s.Id == "backstage.LinuxOnly").DiffPercent.Should().BeNull();

        c.HardRegressions.Should().ContainSingle().Which.Id.Should().Be("grid.big");
        c.Passed.Should().BeFalse();
    }

    [Fact]
    public void Surfaces_sorted_worst_diff_first()
    {
        var (win, lin) = BuildFixture();
        var c = new ParityComparisonEngine().Compare(win, lin, _winDir, _linDir, _imagesDir);
        // grid.big (100%) must come before grid.demo (0%) and tab.Home (small).
        var ids = c.Surfaces.Select(s => s.Id).ToList();
        ids.IndexOf("grid.big").Should().BeLessThan(ids.IndexOf("grid.demo"));
        ids.IndexOf("tab.Home").Should().BeLessThan(ids.IndexOf("grid.demo"));
    }

    [Fact]
    public void Engine_copies_paired_pngs_into_images_dir()
    {
        var (win, lin) = BuildFixture();
        var c = new ParityComparisonEngine().Compare(win, lin, _winDir, _linDir, _imagesDir);

        var demo = c.Surfaces.Single(s => s.Id == "grid.demo");
        demo.WindowsImage.Should().NotBeNull();
        demo.LinuxImage.Should().NotBeNull();
        File.Exists(demo.WindowsImage!).Should().BeTrue();
        File.Exists(demo.LinuxImage!).Should().BeTrue();

        var winOnly = c.Surfaces.Single(s => s.Id == "dialog.WinOnly");
        winOnly.WindowsImage.Should().NotBeNull();
        winOnly.LinuxImage.Should().BeNull();
    }

    [Fact]
    public void WriteAll_emits_html_json_md_and_json_is_wellformed()
    {
        var (win, lin) = BuildFixture();
        var c = new ParityComparisonEngine().Compare(win, lin, _winDir, _linDir, _imagesDir);

        var html = ParityReport.WriteAll(c, _reportDir);

        File.Exists(html).Should().BeTrue();
        File.Exists(Path.Combine(_reportDir, ParityReport.JsonName)).Should().BeTrue();
        File.Exists(Path.Combine(_reportDir, ParityReport.MarkdownName)).Should().BeTrue();

        var htmlText = File.ReadAllText(html);
        htmlText.Should().Contain("grid.big").And.Contain("REGRESSION");
        // win-only / linux-only surfaces render a "no X capture" placeholder cell
        htmlText.Should().Contain("no Linux capture");
        htmlText.Should().Contain("no Windows capture");

        var jsonText = File.ReadAllText(Path.Combine(_reportDir, ParityReport.JsonName));
        using var doc = JsonDocument.Parse(jsonText); // throws if malformed
        doc.RootElement.GetProperty("passed").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("summary").GetProperty("hardRegressions").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("surfaces").GetArrayLength().Should().Be(5);

        var mdText = File.ReadAllText(Path.Combine(_reportDir, ParityReport.MarkdownName));
        mdText.Should().Contain("FreeX cross-platform visual parity report");
        mdText.Should().Contain("functional-parity.md");
    }

    [Fact]
    public void All_identical_and_no_missing_passes()
    {
        var img = PixelImage.Solid(40, 40, 50, 60, 70, 255);
        WritePng(_winDir, "grid.demo.png", img);
        WritePng(_linDir, "grid.demo.png", img);
        var win = new CaptureManifest { Platform = "windows", Shell = "wpf", Surfaces = { new() { Id = "grid.demo", Kind = "grid", Png = "grid.demo.png", Captured = true } } };
        var lin = new CaptureManifest { Platform = "linux", Shell = "avalonia", Surfaces = { new() { Id = "grid.demo", Kind = "grid", Png = "grid.demo.png", Captured = true } } };

        var c = new ParityComparisonEngine().Compare(win, lin, _winDir, _linDir, _imagesDir);
        c.Passed.Should().BeTrue();
        c.HardRegressions.Should().BeEmpty();
    }
}
