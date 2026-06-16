using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class LinuxHumanValidationToolTests
{
    private static readonly string[] RequiredGates =
    [
        "install_tarball", "appimage_launch", "desktop_association", "file_open", "file_dialogs",
        "clipboard", "drag_drop", "x11_session", "wayland_session", "keyboard_only",
        "screen_reader_orca", "external_links", "known_issues_reviewed"
    ];

    [Fact]
    public void ValidatorScript_RequiresEveryHumanGateAndEmitsManifest()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find("tools", "Test-LinuxHumanValidationChecklist.ps1"));

        script.Should().Contain("freex-linux-validation");
        script.Should().Contain("linux-human-validation.v1");
        foreach (var gate in RequiredGates)
            script.Should().Contain($"\"{gate}\"");
        // Gates must be pass/na, not pending/fail.
        script.Should().Contain("'pass'").And.Contain("'na'");
        script.Should().Contain("ConvertTo-Json");
    }

    [Fact]
    public void ChecklistDoc_DeclaresMachineReadableRecordWithEveryGate()
    {
        var doc = File.ReadAllText(RepositoryFileLocator.Find("docs", "release", "linux-human-validation-checklist.md"));

        doc.Should().Contain("<!-- freex-linux-validation -->");
        foreach (var gate in RequiredGates)
            doc.Should().Contain($"{gate}: pending");
        doc.Should().Contain("Test-LinuxHumanValidationChecklist.ps1");
    }

    [Fact]
    public void PromotionTool_CanRequireCompletedChecklistForCandidate()
    {
        var script = File.ReadAllText(RepositoryFileLocator.Find("tools", "Test-LinuxPublicPreviewPromotion.ps1"));

        script.Should().Contain("[string]$ChecklistPath");
        script.Should().Contain("Test-LinuxHumanValidationChecklist.ps1");
        script.Should().Contain("Human-validation checklist failed");
    }
}
