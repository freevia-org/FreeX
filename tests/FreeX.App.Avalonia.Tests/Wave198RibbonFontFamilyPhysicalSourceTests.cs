using FluentAssertions;
using System.Security.Cryptography;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave198RibbonFontFamilyPhysicalSourceTests
{
    [Fact]
    public void FontFamilyPhysicalLane_UsesProductionFixtureFocusAndPackageEvidence()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var fixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave198RibbonFontFamilyFixture.ps1");

        runner.Should().Contain("ribbon-font-family");
        runner.Should().Contain("New-FreeXWave198RibbonFontFamilyFixture.ps1");
        probe.Should().Contain("probe_ribbon_home_font_family_combo");
        probe.Should().Contain("xdotool_mousemove_sync 323 96 click 1");
        probe.Should().Contain("xdotool_mousemove_sync 280 149 click 1");
        probe.Should().Contain("select_cell 0 0 A1");
        probe.Should().Contain("ribbon-home-font-family-combo-focus-reselect.png");
        probe.Should().Contain("send_key Escape || true");
        probe.Should().Contain("send_key Right");
        probe.Should().Contain("focus_clipboard");
        probe.Should().Contain("font-name");
        probe.Should().Contain("name.lower() == 'arial'");
        probe.Should().Contain("save-clean=$save_clean");
        probe.Should().Contain("ribbon-home-font-family-combo-focus-auto.png");
        probe.Should().Contain("automatic-focus-after-combo=$automatic_focus");
        probe.Should().Contain("automatic-focus-status=$automatic_focus_status");
        probe.Should().Contain("automatic-focus-clipboard=$automatic_focus_clipboard");
        probe.Should().Contain("worksheet-focus-after-reselect=$worksheet_focus");
        fixture.Should().Contain("Wave198 Font Family Target");
        fixture.Should().Contain("<name val=\"Calibri\"/>");
        fixture.Should().Contain("Unchanged");
    }

    [Fact]
    public void FontFamilyPhysicalLane_MatchesAvaloniaProductionRouteAndWpfHandler()
    {
        var avalonia = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.cs");
        var host = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "Ribbon", "AvaloniaRibbonHost.cs");
        var shared = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Services", "WorkbookSession.cs");
        var wpf = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs");
        var definition = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.Ribbon.Definitions", "HomeRibbonDefinition.cs");

        avalonia.Should().Contain("SetFontName = ApplyRibbonFontName");
        avalonia.Should().Contain("_session.SetSelectedRangeFontName(fontName)");
        avalonia.Should().Contain("ScheduleWorksheetFocusAfterRibbonComboClosed(combo.IsKeyboardFocusWithin)");
        host.Should().Contain("Register(registry, \"Font\", new ValueRibbonCommand(setFontName))");
        shared.Should().Contain("new StyleDiff(FontName: fontName.Trim())");
        wpf.Should().Contain("ApplyStyleDiff(new StyleDiff(FontName: name))");
        definition.Should().Contain(".ComboBox(\"Font\", \"Font\"");
    }

    [Fact]
    public void FontFamilyPhysicalLane_TracksFinalProvenancePackageAndResult()
    {
        var report = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "docs", "parity", "freex-wave198-ribbon-font-family", "evidence", "interaction-validation.json");
        var postcondition = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "docs", "parity", "freex-wave198-ribbon-font-family", "evidence",
            "ribbon-home-font-family-combo-postcondition.txt");
        var provenance = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "docs", "parity", "freex-wave198-ribbon-font-family", "evidence", "resume-provenance.json");
        var manifest = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "docs", "parity", "freex-wave198-ribbon-font-family", "evidence", "x11-input-results.json");
        var packageProof = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "docs", "parity", "freex-wave198-ribbon-font-family", "evidence", "package-proof.txt");
        var hashes = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "docs", "parity", "freex-wave198-ribbon-font-family", "evidence", "SHA256SUMS.txt");

        report.Should().Contain("\"id\": \"ribbon-home-font-family-combo-physical\"");
        report.Should().Contain("\"status\": \"passed\"");
        report.Should().Contain("\"passed\": 1");
        postcondition.Should().Contain("automatic-focus-after-combo=not-measured");
        postcondition.Should().Contain("automatic-focus-status=unresolved-not-measured");
        postcondition.Should().Contain("worksheet-focus-after-reselect=true");
        postcondition.Should().Contain("focus-clipboard=Unchanged");
        postcondition.Should().Contain("save-clean=true");
        postcondition.Should().Contain("style-id=1|font-id=1|font-name=Arial|font-family=true");
        provenance.Should().Contain("\"sourceCommit\": \"11bff13a7c79d3d63b8aae4aa04e3652f4411667\"");
        provenance.Should().Contain("\"payloadFingerprint\": \"8e98855334aa681317ea5658a60ad7049315a8d076d03a15bb834de143a9c315\"");
        provenance.Should().Contain("\"payloadFileCount\": 778");
        provenance.Should().Contain("\"appImageId\": \"sha256:82cedc8a29edda2963cba8c948e5cd7f65e5390553320761c015dbd2a7aa65d3\"");
        manifest.Should().Contain("\"id\":\"ribbon-home-font-family-combo-physical\"");
        manifest.Should().Contain("\"status\":\"passed\"");
        packageProof.Should().Contain("saved-package-retained=false");
        packageProof.Should().Contain("style-id=1|font-id=1|font-name=Arial|font-family=true");
        var evidenceDirectory = Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
            "docs", "parity", "freex-wave198-ribbon-font-family", "evidence");
        var recordedHashes = hashes.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Split("  ", 2, StringSplitOptions.None))
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);
        var promotedFiles = Directory.EnumerateFiles(evidenceDirectory)
            .Select(Path.GetFileName)
            .Where(name => name != "SHA256SUMS.txt")
            .Order(StringComparer.Ordinal)
            .ToArray();

        recordedHashes.Keys.Order(StringComparer.Ordinal).Should().Equal(promotedFiles);
        foreach (var (name, expectedHash) in recordedHashes)
        {
            var actualHash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(evidenceDirectory, name))))
                .ToLowerInvariant();
            actualHash.Should().Be(expectedHash, $"the promoted evidence hash must match {name}");
        }
    }
}
