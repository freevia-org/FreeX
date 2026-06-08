using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MacOsHumanValidationChecklistPreflightTests
{
    [Fact]
    public void HumanValidationChecklistPreflight_DocumentsCompletedChecklistGate()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-MacOsHumanValidationChecklist.ps1");
        var checklist = WorkspaceFileLocator.ReadAllText("docs", "release", "macos-public-preview-checklist.md");

        script.Should().Contain("ExpectedRuntime");
        script.Should().Contain("ExpectedRunId");
        script.Should().Contain("ExpectedRunAttempt");
        script.Should().Contain("Public-Preview Decision");
        script.Should().Contain("Release owner accepts this runtime for public preview");
        script.Should().Contain("macOS human validation checklist passed");

        checklist.Should().Contain("tools/Test-MacOsHumanValidationChecklist.ps1");
        checklist.Should().Contain("-ExpectedRuntime osx-arm64");
        checklist.Should().Contain("-ExpectedRunId <run-id>");
        checklist.Should().Contain("-ExpectedRunAttempt <run-attempt>");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_PassesForCompletedSyntheticChecklist()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "completed-macos-human-checklist.md");
        File.WriteAllText(checklistPath, CreateCompletedChecklist());

        var result = RunChecklistPreflight(
            checklistPath,
            "-ExpectedRuntime osx-arm64 -ExpectedRunId 42 -ExpectedRunAttempt 1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("macOS human validation checklist passed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_FailsForRepositoryTemplateUntilCompleted()
    {
        var checklistPath = WorkspaceFileLocator.Find("docs", "release", "macos-public-preview-checklist.md");

        var result = RunChecklistPreflight(checklistPath);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Candidate Summary 'Validation date' must be filled in.");
        result.CombinedOutput.Should().Contain("Candidate Summary 'Runtime under test' still contains template placeholder text");
        result.CombinedOutput.Should().Contain("macOS human validation checklist failed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_FailsWhenFinalDecisionIsInternalOnly()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "internal-only-macos-human-checklist.md");
        File.WriteAllText(
            checklistPath,
            CreateCompletedChecklist().Replace("| Final decision | Pass |", "| Final decision | Internal-only |"));

        var result = RunChecklistPreflight(
            checklistPath,
            "-ExpectedRuntime osx-arm64 -ExpectedRunId 42 -ExpectedRunAttempt 1");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Candidate Summary 'Final decision' must be 'Pass'");
        result.CombinedOutput.Should().Contain("macOS human validation checklist failed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_FailsWhenRunIdentityDoesNotMatch()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "wrong-run-macos-human-checklist.md");
        File.WriteAllText(checklistPath, CreateCompletedChecklist());

        var result = RunChecklistPreflight(
            checklistPath,
            "-ExpectedRuntime osx-arm64 -ExpectedRunId 41 -ExpectedRunAttempt 1");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Candidate Summary 'Workflow run id / attempt' has unexpected value '42 / 1'");
        result.CombinedOutput.Should().Contain("Candidate Summary 'Artifact wrapper name' must be 'freex-41-1-osx-arm64-macos-app'");
    }

    private static PowerShellResult RunChecklistPreflight(string checklistPath, string arguments = "")
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        return PowerShellScriptRunner.RunToolScript(
            "Test-MacOsHumanValidationChecklist.ps1",
            repoRoot,
            $"-ChecklistPath \"{checklistPath}\" {arguments}");
    }

    private static string CreateCompletedChecklist()
    {
        var zipHash = new string('a', 64);
        return $$"""
            # macOS Public Preview Human Validation Checklist

            ## Candidate Summary

            | Field | Value |
            | --- | --- |
            | Validation date | 2026-06-08 |
            | Tester | QA Tester |
            | Mac model | Mac mini |
            | Processor family | Apple Silicon |
            | macOS version and build | macOS 15.5 24F74 |
            | Runtime under test | osx-arm64 |
            | Workflow run id / attempt | 42 / 1 |
            | Source branch or commit | 849434284 |
            | Artifact wrapper name | freex-42-1-osx-arm64-macos-app |
            | Diagnostics artifact name | freex-42-1-osx-arm64-macos-diagnostics |
            | Inner app ZIP | freex-osx-arm64-macos-app.zip |
            | ZIP SHA-256 | {{zipHash}} |
            | Evidence file | freex-osx-arm64-macos-evidence.txt |
            | Signing mode | developer-id |
            | Notarization status | accepted |
            | Stapler status | validated |
            | Final decision | Pass |
            | Decision owner | Release Owner |

            ## Hosted Evidence Copy-Forward

            | Required check | Expected public-preview evidence | Actual evidence | Status | Attachment |
            | --- | --- | --- | --- | --- |
            | Checksum | OK | shasum verified | Pass | checksum.txt |
            | Artifact channel | distribution candidate | artifact_channel=distribution-candidate | Pass | evidence.txt |
            | Distribution readiness | ready | distribution_readiness=distribution_candidate_ready | Pass | evidence.txt |
            | Signing | Developer ID | codesign_mode=developer-id | Pass | codesign.txt |
            | Notarization | accepted | notarization_status=accepted | Pass | notary.log |
            | Stapling | validated | stapler_validated=true | Pass | stapler.txt |
            | Gatekeeper assessment | accepted | spctl accepted | Pass | spctl.txt |
            | Hosted launch smoke | passed | macos_launch_smoke=passed | Pass | launch.txt |
            | LaunchServices/Open-With smoke | passed | open-with and default-open passed | Pass | open-with.txt |
            | Command-key smoke | passed | command_key_smoke=passed | Pass | command.txt |
            | Diagnostics artifact | retained | diagnostics artifact present | Pass | diagnostics.zip |

            ## Gatekeeper First Launch

            | Step | Expected result | Actual result | Status | Evidence |
            | --- | --- | --- | --- | --- |
            | Double-click FreeX.app in Finder | launches | launched from Finder | Pass | screenshot |
            | App reaches first usable window | usable window | workbook window opened | Pass | screenshot |
            | Quit and relaunch from Finder | relaunch works | relaunch worked | Pass | screenshot |

            ## Finder And File Association

            | Step | Expected result | Actual result | Status | Evidence |
            | --- | --- | --- | --- | --- |
            | Verify .fxl appears as a FreeX-supported document type | listed | FreeX listed | Pass | screenshot |
            | Double-click .fxl in Finder | opens workbook | opened selected workbook | Pass | screenshot |
            | Confirm workbook identity | identity matches | expected sheet visible | Pass | screenshot |
            | Right-click .fxl > Open With > FreeX | opens file | opened through Open With | Pass | screenshot |
            | Repeat while FreeX is already running | opens in session | opened in existing session | Pass | screenshot |

            ## Workbook Smoke

            | Step | Expected result | Actual result | Status | Evidence |
            | --- | --- | --- | --- | --- |
            | Create a new workbook | blank workbook | created | Pass | screenshot |
            | Save and Save As | saves files | saved and save-as worked | Pass | screenshot |
            | Reopen saved workbook | values survive | reopened expected values | Pass | screenshot |

            ## Command-Key Menu Behavior

            | Command | Expected result | Actual result | Status | Evidence |
            | --- | --- | --- | --- | --- |
            | Menu labels | labels present | labels present | Pass | screenshot |
            | Cmd+N | creates workbook | created | Pass | screenshot |
            | Cmd+O | opens picker | picker opened | Pass | screenshot |
            | Cmd+S | saves workbook | saved | Pass | screenshot |
            | Cmd+W | closes workbook | closed with prompt | Pass | screenshot |
            | Cmd+Q | quits app | quit with prompt | Pass | screenshot |

            ## Keyboard-Only Accessibility

            | Flow | Expected result | Actual result | Status | Evidence |
            | --- | --- | --- | --- | --- |
            | First launch and initial focus | focus visible | visible initial focus | Pass | screenshot |
            | Grid navigation and editing | keyboard works | navigation and edit worked | Pass | notes |
            | Dialogs | predictable focus | dialogs navigated | Pass | notes |
            | Dirty close and Quit | choices reachable | choices reachable | Pass | notes |

            ## VoiceOver Smoke

            | Surface | Expected result | Actual announcement or issue | Status | Evidence |
            | --- | --- | --- | --- | --- |
            | First launch | app identified | FreeX window announced | Pass | transcript |
            | Workbook grid focus | location announced | active cell announced | Pass | transcript |
            | Visible cells | values discoverable | values announced | Pass | transcript |
            | Formula box | formula understandable | formula announced | Pass | transcript |
            | Dialog titles and buttons | titles announced | dialog title and buttons announced | Pass | transcript |
            | Known issues review | decisions recorded | no blocking issues | Pass | release notes |

            ## Log And Artifact Collection

            | Artifact | Required for public preview | Collected path or attachment | Notes |
            | --- | --- | --- | --- |
            | Completed checklist/report | Yes | checklist.md | retained |
            | GitHub Actions app artifact wrapper | Yes | freex-42-1-osx-arm64-macos-app | retained |
            | Inner app ZIP and .sha256 file | Yes | freex-osx-arm64-macos-app.zip | retained |
            | freex-osx-arm64-macos-evidence.txt | Yes | evidence.txt | retained |
            | Packaging smoke log | Yes | packaging.log | retained |
            | Launch smoke file | Yes | launch.txt | retained |
            | Notarization log | Yes | notarization.log | retained |
            | Tester instructions | Yes | tester-instructions.md | retained |
            | Diagnostics artifact | Yes | diagnostics.zip | retained |
            | Terminal transcript | Required for checksum/signing/stapler commands | terminal.txt | retained |

            ## Public-Preview Decision

            | Decision item | Result |
            | --- | --- |
            | Hosted public-preview preflight passed for both runtimes | Pass |
            | This runtime passed human Finder/Gatekeeper validation | Pass |
            | This runtime passed keyboard-only validation | Pass |
            | This runtime passed VoiceOver validation | Pass |
            | Known issues are listed with severity, workaround, owner, and blocking decision | Pass |
            | Release owner accepts this runtime for public preview | Yes |
            """;
    }
}
