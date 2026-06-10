using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class HelpAboutLegalScreenshotTourTests
{
    private static readonly string[] ExpectedCaptureFileNames =
    [
        "freex_help_ribbon_command_context.png",
        "freex_help_online_guarded_message.png",
        "freex_feedback_guarded_message.png",
        "freex_updates_guarded_message.png",
        "freex_about_dialog.png",
        "freex_legal_notices_dialog.png",
        "freex_help_focus_return_status.png"
    ];

    [Fact]
    public void Manifest_ClosesHelpAboutLegalFreeXVisualEvidenceSlice()
    {
        var manifest = ReadManifestRoot();
        var captures = manifest.GetProperty("Captures").EnumerateArray().ToArray();

        manifest.GetProperty("Tool").GetString().Should().Be("FREEX_HELP_ABOUT_LEGAL_TOUR");
        manifest.GetProperty("EvidenceFamily").GetString().Should().Be("help-about-legal");
        manifest.GetProperty("EvidenceSubject").GetString().Should().Be("freex");
        manifest.GetProperty("CaptureStatus").GetString().Should().Be("complete");
        manifest.GetProperty("ExternalBrowserLaunched").GetBoolean().Should().BeFalse();
        manifest.GetProperty("PlannedCaptureCount").GetInt32().Should().Be(ExpectedCaptureFileNames.Length);
        manifest.GetProperty("ActualCaptureCount").GetInt32().Should().Be(ExpectedCaptureFileNames.Length);
        manifest.GetProperty("FocusGuard").GetProperty("Required").GetBoolean().Should().BeFalse();

        manifest.GetProperty("EntryPaths").EnumerateArray().Select(item => item.GetString()).Should().Equal(
        [
            "Help tab",
            "Help > Help Online",
            "Help > Feedback",
            "Help > Check for Updates",
            "Help > About FreeX",
            "Help > Legal Notices",
            "Help tab focus return / Ready status"
        ]);

        captures.Select(capture => capture.GetProperty("OutputFileName").GetString())
            .Should()
            .Equal(ExpectedCaptureFileNames);

        captures.Where(capture => capture.GetProperty("ScenarioId").GetString() == "help-about-legal:external-link-guard")
            .Should()
            .HaveCount(3)
            .And.OnlyContain(capture =>
                capture.GetProperty("CaptureMethod").GetString() == "PrintWindow-owned-native-dialog" &&
                capture.GetProperty("EvidenceSummary").GetString()!.Contains("guarded", StringComparison.Ordinal) &&
                capture.GetProperty("Url").GetString()!.StartsWith("https://", StringComparison.Ordinal));

        CaptureByKey(captures, "help:about-dialog:opened").Should().Match<JsonElement>(capture =>
            capture.GetProperty("CaptureMethod").GetString() == "RenderTargetBitmap-about-dialog-window" &&
            capture.GetProperty("FocusedElementAutomationId").GetString() == "AboutFreeXText");

        CaptureByKey(captures, "help:legal-notices-dialog:opened").Should().Match<JsonElement>(capture =>
            capture.GetProperty("CaptureMethod").GetString() == "RenderTargetBitmap-legal-notices-dialog-window" &&
            capture.GetProperty("EvidenceSummary").GetString()!.Contains("packaged legal/privacy/third-party tabs", StringComparison.Ordinal) &&
            capture.GetProperty("FocusedElementAutomationId").GetString() == "LegalNoticesProjectLicenseText");

        CaptureByKey(captures, "help:focus-return-status").Should().Match<JsonElement>(capture =>
            capture.GetProperty("ScenarioId").GetString() == "help-about-legal:focus-return" &&
            capture.GetProperty("CaptureMethod").GetString() == "RenderTargetBitmap-main-window-full" &&
            capture.GetProperty("FocusedElementAutomationId").GetString() == "HelpOnlineButton" &&
            capture.GetProperty("EvidenceSummary").GetString()!.Contains("Ready status bar", StringComparison.Ordinal));

        manifest.GetProperty("Limitations").EnumerateArray().Select(item => item.GetString()).Should().Contain(
            "No Microsoft Excel counterpart capture is produced by this tool.");
    }

    [Fact]
    public void EvidenceDirectory_ContainsOnlyReferencedNontrivialPngCaptures()
    {
        var directory = Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(),
            "screenshots",
            "help-about-legal-tour");
        var pngFiles = Directory
            .EnumerateFiles(directory, "*.png")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        pngFiles.Should().Equal(ExpectedCaptureFileNames.Order(StringComparer.Ordinal));

        foreach (var fileName in ExpectedCaptureFileNames)
        {
            var path = Path.Combine(directory, fileName);
            var bytes = File.ReadAllBytes(path);

            bytes.Length.Should().BeGreaterThan(8_000, fileName);
            bytes.Take(8).Should().Equal([137, 80, 78, 71, 13, 10, 26, 10], fileName);
        }
    }

    private static JsonElement ReadManifestRoot()
    {
        using var document = JsonDocument.Parse(WorkspaceFileLocator.ReadAllText(
            "screenshots",
            "help-about-legal-tour",
            "help_about_legal_tour_manifest.json"));
        return document.RootElement.Clone();
    }

    private static JsonElement CaptureByKey(IEnumerable<JsonElement> captures, string captureKey) =>
        captures.Single(capture => capture.GetProperty("CaptureKey").GetString() == captureKey);
}
