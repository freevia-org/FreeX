using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MacOsAccessibilityEvidencePlanTests
{
    [Fact]
    public void MacOsAccessibilityEvidencePlan_DefinesHostedHumanAndPreviewGate()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("docs", "planning/macos-accessibility-evidence.md"));

        source.Should().Contain("Hosted checks");
        source.Should().Contain("Human macOS validation");
        source.Should().Contain("Initial Controls And Surfaces");
        source.Should().Contain("Public-Preview Blockers");
        source.Should().Contain("osx-arm64");
        source.Should().Contain("osx-x64");
        source.Should().Contain("LaunchServices");
        source.Should().Contain("keyboard-only");
        source.Should().Contain("VoiceOver");
        source.Should().Contain("known accessibility issues");
        source.Should().Contain("internal-only");
        source.Should().Contain("tools/Test-MacOsHumanValidationChecklist.ps1");
        source.Should().Contain("stale-run human evidence");
    }

    [Fact]
    public void MacOsDocs_KeepAccessibilityEvidenceRequirementLinked()
    {
        var signing = File.ReadAllText(WorkspaceFileLocator.Find("docs", "release/macos-signing-notarization.md"));
        var multiplatform = File.ReadAllText(WorkspaceFileLocator.Find("docs", "planning/multiplatform-macos-port.md"));

        signing.Should().Contain("[planning/macos-accessibility-evidence.md](../planning/macos-accessibility-evidence.md)");
        signing.Should().Contain("macOS/Avalonia accessibility evidence requirement");
        signing.Should().Contain("public-preview");
        signing.Should().Contain("keyboard-only");
        signing.Should().Contain("VoiceOver");
        signing.Should().Contain("known accessibility issues");
        signing.Should().Contain("tools/Test-MacOsHumanValidationChecklist.ps1");

        multiplatform.Should().Contain("[macos-accessibility-evidence.md](macos-accessibility-evidence.md)");
        multiplatform.Should().Contain("macOS/Avalonia accessibility evidence requirement");
        multiplatform.Should().Contain("public-preview");
        multiplatform.Should().Contain("keyboard-only");
        multiplatform.Should().Contain("VoiceOver");
        multiplatform.Should().Contain("known accessibility issues");
        multiplatform.Should().Contain("tools/Test-MacOsHumanValidationChecklist.ps1");
    }
}
