using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MacOsPublicPreviewHumanChecklistTests
{
    [Fact]
    public void PublicPreviewChecklist_PreservesRealMacHumanValidationContract()
    {
        var source = WorkspaceFileLocator.ReadAllText("docs", "release", "macos-public-preview-checklist.md");

        source.Should().Contain("# macOS Public Preview Human Validation Checklist");
        source.Should().Contain("Use a real macOS user session.");
        source.Should().Contain("Hosted smoke logs are supporting evidence, not a replacement for Finder, Gatekeeper, keyboard-only, or VoiceOver observations.");
        source.Should().Contain("tools/Test-MacOsHumanValidationChecklist.ps1");
        source.Should().Contain("-ExpectedRuntime osx-arm64");
        source.Should().Contain("-ExpectedRunId <run-id>");
        source.Should().Contain("-ExpectedRunAttempt <run-attempt>");
        source.Should().Contain("If Developer ID signing, accepted notarization, stapling, Gatekeeper launch, Finder `.fxl` open, keyboard-only validation, or VoiceOver validation is missing, mark the build `Internal-only`.");

        source.Should().Contain("## Candidate Summary");
        source.Should().Contain("| Validation date |  |");
        source.Should().Contain("| Tester |  |");
        source.Should().Contain("| Mac model |  |");
        source.Should().Contain("| Processor family | Apple Silicon / Intel |");
        source.Should().Contain("| macOS version and build |  |");
        source.Should().Contain("| Runtime under test | `osx-arm64` / `osx-x64` |");
        source.Should().Contain("| Workflow run id / attempt |  |");
        source.Should().Contain("| Source branch or commit |  |");
        source.Should().Contain("| Artifact wrapper name | `freex-<run-id>-<run-attempt>-<runtime>-macos-app` |");
        source.Should().Contain("| Diagnostics artifact name | `freex-<run-id>-<run-attempt>-<runtime>-macos-diagnostics` |");
        source.Should().Contain("| Inner app ZIP | `freex-<runtime>-macos-app.zip` |");
        source.Should().Contain("| ZIP SHA-256 |  |");
        source.Should().Contain("| Evidence file | `freex-<runtime>-macos-evidence.txt` |");
        source.Should().Contain("| Signing mode |  |");
        source.Should().Contain("| Notarization status |  |");
        source.Should().Contain("| Stapler status |  |");
        source.Should().Contain("| Final decision | Pass / Fail / Internal-only |");
        source.Should().Contain("| Decision owner |  |");

        source.Should().Contain("## Hosted Evidence Copy-Forward");
        source.Should().Contain("shasum -a 256 -c freex-<runtime>-macos-app.zip.sha256");
        source.Should().Contain("| Checksum | `<zip-name>: OK`; hash matches `zip_sha256` in evidence |");
        source.Should().Contain("| Gatekeeper assessment | `spctl` accepts `FreeX.app` as Developer ID software |");
        source.Should().Contain("| Hosted launch smoke | Native runtime reports `macos_launch_smoke=passed`");
        source.Should().Contain("| LaunchServices/Open-With smoke | Evidence contains the hosted LaunchServices and Open-With smoke pass markers |");
        source.Should().Contain("| Diagnostics artifact | Matching `macos-diagnostics` artifact is present and retained |");

        source.Should().Contain("## Gatekeeper First Launch");
        source.Should().Contain("Finder double-click on `FreeX.app`");
        source.Should().Contain("| Confirm quarantine is still present before first launch");
        source.Should().Contain("`xattr -p com.apple.quarantine FreeX.app`");
        source.Should().Contain("| Double-click `FreeX.app` in Finder |");
        source.Should().Contain("| Record Gatekeeper prompt |");
        source.Should().Contain("xattr -d");
        source.Should().Contain("Control-click/right-click Open as a trust workaround");

        source.Should().Contain("## Finder And File Association");
        source.Should().Contain("| Set default `.fxl` handler, if permitted | Finder Get Info > Open with > FreeX > Change All succeeds |");
        source.Should().Contain("| Double-click `.fxl` in Finder | FreeX launches or activates and opens the selected workbook");
        source.Should().Contain("| Right-click `.fxl` > Open With > FreeX | FreeX opens the file when it is not already running |");
        source.Should().Contain("| Repeat while FreeX is already running | The selected file opens in the existing app session without losing unsaved work |");
        source.Should().Contain("missing default-handler proof blocks promotion");

        source.Should().Contain("## Keyboard-Only Accessibility");
        source.Should().Contain("Complete this section with the mouse or trackpad set aside after launch.");
        source.Should().Contain("| First launch and initial focus | App opens with a visible, usable keyboard focus target |");
        source.Should().Contain("| Grid navigation and editing | Arrow keys, Tab, Shift+Tab, Enter, Escape, and F2/direct entry work without pointer input |");
        source.Should().Contain("| Dialogs | Open, Save As, Find, Replace, Go To, Format Cells, warnings, and confirmations have predictable focus order and default/cancel behavior |");
        source.Should().Contain("| Dirty close and Quit | Save/Discard/Cancel choices are reachable and do not trap focus |");

        source.Should().Contain("## VoiceOver Smoke");
        source.Should().Contain("Turn on VoiceOver in the tester session");
        source.Should().Contain("| First launch | VoiceOver identifies the app/window and initial focus |");
        source.Should().Contain("| Workbook grid focus | Active cell or grid location is announced with useful context |");
        source.Should().Contain("| Dialog titles and buttons | Find, Replace, Go To, Format Cells, warnings, About, and Legal Notices announce titles, fields, default buttons, and destructive actions |");
        source.Should().Contain("| Known issues review | `Accessibility Known Issues` is complete; every confusing announcement or missing name has severity, workaround, owner, and public-preview blocking decision |");

        source.Should().Contain("## Accessibility Known Issues");
        source.Should().Contain("If no issues are known for this runtime, keep exactly one explicit `None` row.");
        source.Should().Contain("The VoiceOver `Known issues review` row and the Public-Preview Decision known-issues row must both reflect this table.");
        source.Should().Contain("| Issue ID | Affected workflow | Severity | User impact / evidence | Workaround | Owner | Public-preview blocking | Decision / rationale |");
        source.Should().Contain("| None | None | None | No keyboard-only or VoiceOver known issues found during this runtime validation | None | Release owner | No | No known accessibility issues; public preview may proceed |");

        source.Should().Contain("## Log And Artifact Collection");
        source.Should().Contain("| macOS release-assets wrapper | Yes |");
        source.Should().Contain("| `FreeX-latest-macos-distribution-candidate-manifest.json` | Yes |");

        source.Should().Contain("## Public-Preview Decision");
        source.Should().Contain("| This runtime passed human Finder/Gatekeeper validation | Pass / Fail |");
        source.Should().Contain("| This runtime passed keyboard-only validation | Pass / Fail |");
        source.Should().Contain("| This runtime passed VoiceOver validation | Pass / Fail |");
        source.Should().Contain("| Known issues are listed with severity, workaround, owner, and blocking decision | Pass / Fail |");
        source.Should().Contain("| Release owner accepts this runtime for public preview | Yes / No |");
        source.Should().Contain("macOS human validation decision: Pass / Fail / Internal-only");
        source.Should().Contain("Decision owner:");
    }
}
