using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MacOsHumanValidationChecklistPreflightTests
{
    private const string AccessibilityKnownIssuesNoneRow = "| None | None | None | No keyboard-only or VoiceOver known issues found during this runtime validation | None | Release Owner | No | No known accessibility issues; public preview may proceed |";
    private const string AccessibilityKnownIssuesNonBlockingIssueRow = "| FX-A11Y-001 | VoiceOver Smoke - workbook grid focus | Medium | Active cell omits row number in announcement; transcript attached | Use Name Box and status text to confirm location | Accessibility Owner | No | Accepted as non-blocking for public preview; tracked in FX-A11Y-001 |";
    private const string AccessibilityKnownIssuesBlockingIssueRow = "| FX-A11Y-001 | Keyboard-Only Accessibility - dialogs | High | Save confirmation traps focus after keyboard-only close | Use pointer to cancel the dialog | Accessibility Owner | Yes | Blocks public preview until fixed |";

    [Fact]
    public void HumanValidationChecklistPreflight_DocumentsCompletedChecklistGate()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-MacOsHumanValidationChecklist.ps1");
        var checklist = WorkspaceFileLocator.ReadAllText("docs", "release", "macos-public-preview-checklist.md");

        script.Should().Contain("ExpectedRuntime");
        script.Should().Contain("ExpectedRunId");
        script.Should().Contain("ExpectedRunAttempt");
        script.Should().Contain("Public-Preview Decision");
        script.Should().Contain("Accessibility Known Issues");
        script.Should().Contain("Release owner accepts this runtime for public preview");
        script.Should().Contain("macOS human validation checklist passed");

        checklist.Should().Contain("tools/Test-MacOsHumanValidationChecklist.ps1");
        checklist.Should().Contain("-ExpectedRuntime osx-arm64");
        checklist.Should().Contain("-ExpectedRunId <run-id>");
        checklist.Should().Contain("-ExpectedRunAttempt <run-attempt>");
        checklist.Should().Contain("## Accessibility Known Issues");
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

    [Fact]
    public void HumanValidationChecklistPreflight_FailsWhenWorkflowIdentityIsMalformed()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "malformed-run-macos-human-checklist.md");
        var checklist = CreateCompletedChecklist()
            .Replace("| Workflow run id / attempt | 42 / 1 |", "| Workflow run id / attempt | run 42 / attempt 1 |");
        File.WriteAllText(checklistPath, checklist);

        var result = RunChecklistPreflight(checklistPath);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Candidate Summary 'Workflow run id / attempt' must be '<numeric run id> / <numeric run attempt>', but was 'run 42 / attempt 1'.");
        result.CombinedOutput.Should().Contain("macOS human validation checklist failed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_FailsWhenWrapperNamesDoNotMatchSummaryIdentityWithoutExpectedArgs()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "wrong-wrapper-run-macos-human-checklist.md");
        var checklist = CreateCompletedChecklist()
            .Replace("| Artifact wrapper name | freex-42-1-osx-arm64-macos-app |", "| Artifact wrapper name | freex-41-1-osx-arm64-macos-app |")
            .Replace("| Diagnostics artifact name | freex-42-1-osx-arm64-macos-diagnostics |", "| Diagnostics artifact name | freex-42-2-osx-arm64-macos-diagnostics |");
        File.WriteAllText(checklistPath, checklist);

        var result = RunChecklistPreflight(checklistPath);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Candidate Summary 'Artifact wrapper name' must be 'freex-42-1-osx-arm64-macos-app'");
        result.CombinedOutput.Should().Contain("Candidate Summary 'Diagnostics artifact name' must be 'freex-42-1-osx-arm64-macos-diagnostics'");
        result.CombinedOutput.Should().Contain("macOS human validation checklist failed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_FailsWhenChecksumEvidenceDoesNotCopyCandidateZipAndHash()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "wrong-checksum-evidence-macos-human-checklist.md");
        var zipHash = new string('a', 64);
        var checklist = CreateCompletedChecklist()
            .Replace(
                $"| Checksum | OK | freex-osx-arm64-macos-app.zip: OK; sha256 {zipHash} | Pass | checksum.txt |",
                "| Checksum | OK | shasum verified | Pass | checksum.txt |");
        File.WriteAllText(checklistPath, checklist);

        var result = RunChecklistPreflight(checklistPath);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Hosted Evidence Copy-Forward 'Checksum' evidence must include 'freex-osx-arm64-macos-app.zip'.");
        result.CombinedOutput.Should().Contain($"Hosted Evidence Copy-Forward 'Checksum' evidence must include '{zipHash}'.");
        result.CombinedOutput.Should().Contain("macOS human validation checklist failed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_FailsWhenLogCollectionRowsDoNotNameExpectedArtifacts()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "generic-log-artifacts-macos-human-checklist.md");
        var checklist = CreateCompletedChecklist()
            .Replace("| GitHub Actions app artifact wrapper | Yes | freex-42-1-osx-arm64-macos-app | retained |", "| GitHub Actions app artifact wrapper | Yes | app-wrapper | retained |")
            .Replace("| Inner app ZIP and .sha256 file | Yes | freex-osx-arm64-macos-app.zip; freex-osx-arm64-macos-app.zip.sha256 | retained |", "| Inner app ZIP and .sha256 file | Yes | zip bundle | retained |")
            .Replace("| freex-osx-arm64-macos-evidence.txt | Yes | freex-osx-arm64-macos-evidence.txt | retained |", "| freex-osx-arm64-macos-evidence.txt | Yes | evidence.txt | retained |")
            .Replace("| macOS release-assets wrapper | Yes | freex-42-1-macos-release-assets | retained |", "| macOS release-assets wrapper | Yes | release assets | retained |")
            .Replace("| FreeX-latest-macos-distribution-candidate-manifest.json | Yes | freex-42-1-macos-release-assets/FreeX-latest-macos-distribution-candidate-manifest.json | retained |", "| FreeX-latest-macos-distribution-candidate-manifest.json | Yes | manifest.json | retained |")
            .Replace("| Diagnostics artifact | Yes | freex-42-1-osx-arm64-macos-diagnostics | retained |", "| Diagnostics artifact | Yes | diagnostics.zip | retained |");
        File.WriteAllText(checklistPath, checklist);

        var result = RunChecklistPreflight(checklistPath);

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Log And Artifact Collection 'GitHub Actions app artifact wrapper' must include 'freex-42-1-osx-arm64-macos-app'.");
        result.CombinedOutput.Should().Contain("Log And Artifact Collection 'Inner app ZIP and .sha256 file' must include 'freex-osx-arm64-macos-app.zip'.");
        result.CombinedOutput.Should().Contain("Log And Artifact Collection 'Inner app ZIP and .sha256 file' must include 'freex-osx-arm64-macos-app.zip.sha256'.");
        result.CombinedOutput.Should().Contain("Log And Artifact Collection 'freex-osx-arm64-macos-evidence.txt' must include 'freex-osx-arm64-macos-evidence.txt'.");
        result.CombinedOutput.Should().Contain("Log And Artifact Collection 'macOS release-assets wrapper' must include 'macos-release-assets'.");
        result.CombinedOutput.Should().Contain("Log And Artifact Collection 'FreeX-latest-macos-distribution-candidate-manifest.json' must include 'FreeX-latest-macos-distribution-candidate-manifest.json'.");
        result.CombinedOutput.Should().Contain("Log And Artifact Collection 'Diagnostics artifact' must include 'freex-42-1-osx-arm64-macos-diagnostics'.");
        result.CombinedOutput.Should().Contain("macOS human validation checklist failed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_PassesWhenAccessibilityKnownIssuesListAcceptedNonBlockingIssue()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "non-blocking-accessibility-issue-macos-human-checklist.md");
        var checklist = CreateCompletedChecklist()
            .Replace(AccessibilityKnownIssuesNoneRow, AccessibilityKnownIssuesNonBlockingIssueRow)
            .Replace("See Accessibility Known Issues section; no issues found", "See Accessibility Known Issues section; one non-blocking issue accepted");
        File.WriteAllText(checklistPath, checklist);

        var result = RunChecklistPreflight(
            checklistPath,
            "-ExpectedRuntime osx-arm64 -ExpectedRunId 42 -ExpectedRunAttempt 1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("macOS human validation checklist passed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_FailsWhenAccessibilityKnownIssuesSectionIsMissing()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "missing-accessibility-known-issues-macos-human-checklist.md");
        File.WriteAllText(checklistPath, RemoveChecklistSection(CreateCompletedChecklist(), "Accessibility Known Issues"));

        var result = RunChecklistPreflight(
            checklistPath,
            "-ExpectedRuntime osx-arm64 -ExpectedRunId 42 -ExpectedRunAttempt 1");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Checklist must include the 'Accessibility Known Issues' table.");
        result.CombinedOutput.Should().Contain("macOS human validation checklist failed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_FailsWhenAccessibilityKnownIssuesMixNoneAndIssueRows()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "mixed-accessibility-known-issues-macos-human-checklist.md");
        File.WriteAllText(
            checklistPath,
            CreateCompletedChecklist().Replace(
                AccessibilityKnownIssuesNoneRow,
                AccessibilityKnownIssuesNoneRow + Environment.NewLine + AccessibilityKnownIssuesNonBlockingIssueRow));

        var result = RunChecklistPreflight(
            checklistPath,
            "-ExpectedRuntime osx-arm64 -ExpectedRunId 42 -ExpectedRunAttempt 1");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Accessibility Known Issues must contain either exactly one 'None' row or issue rows, not both.");
        result.CombinedOutput.Should().Contain("macOS human validation checklist failed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_FailsWhenAccessibilityKnownIssueRowsDoNotFollowSchema()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "malformed-accessibility-known-issues-macos-human-checklist.md");
        File.WriteAllText(
            checklistPath,
            CreateCompletedChecklist().Replace(
                AccessibilityKnownIssuesNoneRow,
                "| FX-A11Y-001 | VoiceOver Smoke - workbook grid focus | Needs triage |  |  |  | Maybe |  |"));

        var result = RunChecklistPreflight(
            checklistPath,
            "-ExpectedRuntime osx-arm64 -ExpectedRunId 42 -ExpectedRunAttempt 1");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Accessibility Known Issues 'FX-A11Y-001' severity must be one of: Critical, High, Medium, Low. Actual value: 'Needs triage'.");
        result.CombinedOutput.Should().Contain("Accessibility Known Issues 'FX-A11Y-001' user impact / evidence must be filled in.");
        result.CombinedOutput.Should().Contain("Accessibility Known Issues 'FX-A11Y-001' workaround must be filled in.");
        result.CombinedOutput.Should().Contain("Accessibility Known Issues 'FX-A11Y-001' owner must be filled in.");
        result.CombinedOutput.Should().Contain("Accessibility Known Issues 'FX-A11Y-001' public-preview blocking must be one of: Yes, No. Actual value: 'Maybe'.");
        result.CombinedOutput.Should().Contain("Accessibility Known Issues 'FX-A11Y-001' decision / rationale must be filled in.");
        result.CombinedOutput.Should().Contain("macOS human validation checklist failed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_FailsWhenAccessibilityKnownIssueBlocksPublicPreview()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "blocking-accessibility-known-issue-macos-human-checklist.md");
        File.WriteAllText(checklistPath, CreateCompletedChecklist().Replace(AccessibilityKnownIssuesNoneRow, AccessibilityKnownIssuesBlockingIssueRow));

        var result = RunChecklistPreflight(
            checklistPath,
            "-ExpectedRuntime osx-arm64 -ExpectedRunId 42 -ExpectedRunAttempt 1");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("Accessibility Known Issues 'FX-A11Y-001' is marked public-preview blocking; public-preview candidates must be Internal-only until it is resolved or accepted as non-blocking.");
        result.CombinedOutput.Should().Contain("Public-Preview Decision 'Known issues are listed with severity, workaround, owner, and blocking decision' cannot pass while Accessibility Known Issues has public-preview blocking issues.");
        result.CombinedOutput.Should().Contain("macOS human validation checklist failed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_FailsWhenVoiceOverKnownIssuesReviewDoesNotPointToAccessibilityKnownIssues()
    {
        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "untied-voiceover-known-issues-macos-human-checklist.md");
        File.WriteAllText(
            checklistPath,
            CreateCompletedChecklist().Replace(
                "| Known issues review | decisions recorded | See Accessibility Known Issues section; no issues found | Pass | Accessibility Known Issues table |",
                "| Known issues review | decisions recorded | No issues found | Pass | release notes |"));

        var result = RunChecklistPreflight(
            checklistPath,
            "-ExpectedRuntime osx-arm64 -ExpectedRunId 42 -ExpectedRunAttempt 1");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("VoiceOver Smoke 'Known issues review' must include 'Accessibility Known Issues'.");
        result.CombinedOutput.Should().Contain("macOS human validation checklist failed");
    }

    [Fact]
    public void HumanValidationChecklistPreflight_FailsWhenImportantTemplateRowsAreOmitted()
    {
        var missingRows = new (string Section, string Label)[]
        {
            ("Gatekeeper First Launch", "Confirm quarantine is still present before first launch, if the artifact was browser-downloaded"),
            ("Gatekeeper First Launch", "Record Gatekeeper prompt"),
            ("Command-Key Menu Behavior", "Cmd+Shift+S"),
            ("Command-Key Menu Behavior", "Cmd+A"),
            ("Command-Key Menu Behavior", "Cmd+F and Find Next menu route"),
            ("Command-Key Menu Behavior", "Cmd+B, Cmd+I, Cmd+U"),
            ("Keyboard-Only Accessibility", "Formula box edits"),
            ("Keyboard-Only Accessibility", "Sheet tabs"),
            ("Keyboard-Only Accessibility", "Context menus"),
            ("VoiceOver Smoke", "Status text"),
            ("VoiceOver Smoke", "Sheet tabs"),
        };

        using var temp = new TestTemporaryDirectory();
        var checklistPath = Path.Combine(temp.Path, "missing-rows-macos-human-checklist.md");
        var checklist = missingRows.Aggregate(
            CreateCompletedChecklist(),
            (current, row) => RemoveChecklistRow(current, row.Section, row.Label));
        File.WriteAllText(checklistPath, checklist);

        var result = RunChecklistPreflight(
            checklistPath,
            "-ExpectedRuntime osx-arm64 -ExpectedRunId 42 -ExpectedRunAttempt 1");

        result.ExitCode.Should().NotBe(0);
        foreach (var row in missingRows)
        {
            result.CombinedOutput.Should().Contain($"Checklist section '{row.Section}' must include '{row.Label}'.");
        }

        result.CombinedOutput.Should().Contain("macOS human validation checklist failed");
    }

    private static PowerShellResult RunChecklistPreflight(string checklistPath, string arguments = "")
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        return PowerShellScriptRunner.RunToolScript(
            "Test-MacOsHumanValidationChecklist.ps1",
            repoRoot,
            $"-ChecklistPath \"{checklistPath}\" {arguments}");
    }

    private static string RemoveChecklistRow(string checklist, string section, string rowLabel)
    {
        var rowPrefix = $"| {rowLabel} |";
        var lines = checklist.Split(["\r\n", "\n"], StringSplitOptions.None);
        var filtered = new List<string>(lines.Length);
        var currentSection = "";
        var removedCount = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                currentSection = line[3..].Trim();
            }

            if (string.Equals(currentSection, section, StringComparison.Ordinal) &&
                line.TrimStart().StartsWith(rowPrefix, StringComparison.Ordinal))
            {
                removedCount++;
                continue;
            }

            filtered.Add(line);
        }

        removedCount.Should().Be(1, $"the synthetic checklist should include exactly one '{rowLabel}' row in '{section}'");
        return string.Join(Environment.NewLine, filtered);
    }

    private static string RemoveChecklistSection(string checklist, string section)
    {
        var sectionHeader = $"## {section}";
        var lines = checklist.Split(["\r\n", "\n"], StringSplitOptions.None);
        var filtered = new List<string>(lines.Length);
        var isRemovingSection = false;
        var removed = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (isRemovingSection)
                {
                    isRemovingSection = false;
                }

                if (string.Equals(line.Trim(), sectionHeader, StringComparison.Ordinal))
                {
                    isRemovingSection = true;
                    removed = true;
                    continue;
                }
            }

            if (!isRemovingSection)
            {
                filtered.Add(line);
            }
        }

        removed.Should().BeTrue($"the synthetic checklist should include the '{section}' section");
        return string.Join(Environment.NewLine, filtered);
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
            | Checksum | OK | freex-osx-arm64-macos-app.zip: OK; sha256 {{zipHash}} | Pass | checksum.txt |
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
            | Confirm quarantine is still present before first launch, if the artifact was browser-downloaded | quarantine evidence or N/A note | browser path did not preserve quarantine | N/A | terminal transcript |
            | Double-click FreeX.app in Finder | launches | launched from Finder | Pass | screenshot |
            | Record Gatekeeper prompt | prompt allows opening or no prompt is shown | no Gatekeeper prompt was shown after notarized launch | Pass | screenshot |
            | App reaches first usable window | usable window | workbook window opened | Pass | screenshot |
            | Quit and relaunch from Finder | relaunch works | relaunch worked | Pass | screenshot |

            ## Finder And File Association

            | Step | Expected result | Actual result | Status | Evidence |
            | --- | --- | --- | --- | --- |
            | Verify .fxl appears as a FreeX-supported document type | listed | FreeX listed | Pass | screenshot |
            | Set default .fxl handler, if permitted | default handler set or constraint recorded | tester machine policy disallows Change All | Skipped | notes |
            | Double-click .fxl in Finder | opens workbook | opened selected workbook | Pass | screenshot |
            | Confirm workbook identity | identity matches | expected sheet visible | Pass | screenshot |
            | Right-click .fxl > Open With > FreeX | opens file | opened through Open With | Pass | screenshot |
            | Repeat while FreeX is already running | opens in session | opened in existing session | Pass | screenshot |
            | Optional spreadsheet file Open With | opens representative spreadsheet when in scope | spreadsheet Open With was out of candidate scope | N/A | notes |

            ## Workbook Smoke

            | Step | Expected result | Actual result | Status | Evidence |
            | --- | --- | --- | --- | --- |
            | Create a new workbook | blank workbook | created | Pass | screenshot |
            | Enter values and formulas | values and formulas commit | entered values and formula committed | Pass | screenshot |
            | Save and Save As | saves files | saved and save-as worked | Pass | screenshot |
            | Close dirty workbook | prompt choices clear | Save, Discard, and Cancel reachable | Pass | screenshot |
            | Reopen saved workbook | values survive | reopened expected values | Pass | screenshot |
            | Recent files | recent route updated | saved workbook appeared in recent files | Pass | screenshot |

            ## Command-Key Menu Behavior

            | Command | Expected result | Actual result | Status | Evidence |
            | --- | --- | --- | --- | --- |
            | Menu labels | labels present | labels present | Pass | screenshot |
            | Cmd+N | creates workbook | created | Pass | screenshot |
            | Cmd+O | opens picker | picker opened | Pass | screenshot |
            | Cmd+S | saves workbook | saved | Pass | screenshot |
            | Cmd+Shift+S | opens Save As | Save As opened | Pass | screenshot |
            | Cmd+W | closes workbook | closed with prompt | Pass | screenshot |
            | Cmd+Q | quits app | quit with prompt | Pass | screenshot |
            | Cmd+A | selects current region then sheet | selection expanded as expected | Pass | screenshot |
            | Cmd+F and Find Next menu route | Find opens and advances | Find opened and next match advanced | Pass | screenshot |
            | Cmd+B, Cmd+I, Cmd+U | format commands apply | formatting applied and survived reopen | Pass | screenshot |
            | Cmd+PageUp / Cmd+PageDown or hardware equivalent | switches sheets or limitation recorded | keyboard lacks Page keys; alternate route recorded | N/A | notes |

            ## Keyboard-Only Accessibility

            | Flow | Expected result | Actual result | Status | Evidence |
            | --- | --- | --- | --- | --- |
            | First launch and initial focus | focus visible | visible initial focus | Pass | screenshot |
            | Grid navigation and editing | keyboard works | navigation and edit worked | Pass | notes |
            | Formula box edits | keyboard reach/edit/commit/cancel works | formula box reached and edited by keyboard | Pass | notes |
            | Native menus | menus reachable | File, Edit, Format, View, Sheet, and Help invoked | Pass | notes |
            | Toolbar or command surface | commands reachable | primary command surface reached by keyboard | Pass | notes |
            | Sheet tabs | tab strip reachable | sheet add, rename, and context route reachable | Pass | notes |
            | Dialogs | predictable focus | dialogs navigated | Pass | notes |
            | Context menus | context menus keyboard reachable | grid and sheet-tab menus opened from keyboard | Pass | notes |
            | Help and feedback routes | help routes reachable | Help, About, and Legal Notices reached | Pass | notes |
            | Dirty close and Quit | choices reachable | choices reachable | Pass | notes |

            ## VoiceOver Smoke

            | Surface | Expected result | Actual announcement or issue | Status | Evidence |
            | --- | --- | --- | --- | --- |
            | First launch | app identified | FreeX window announced | Pass | transcript |
            | Workbook grid focus | location announced | active cell announced | Pass | transcript |
            | Visible cells | values discoverable | values announced | Pass | transcript |
            | Formula box | formula understandable | formula announced | Pass | transcript |
            | Status text | status discoverable | selection summary announced | Pass | transcript |
            | Sheet tabs | sheet state understandable | selected sheet and tab actions announced | Pass | transcript |
            | Drawing objects, if present | objects identifiable | no drawing objects included in candidate smoke workbook | N/A | notes |
            | Dialog titles and buttons | titles announced | dialog title and buttons announced | Pass | transcript |
            | Gatekeeper or accessibility prompts | prompts understandable | no system prompt appeared during VoiceOver pass | N/A | notes |
            | Known issues review | decisions recorded | See Accessibility Known Issues section; no issues found | Pass | Accessibility Known Issues table |

            ## Accessibility Known Issues

            | Issue ID | Affected workflow | Severity | User impact / evidence | Workaround | Owner | Public-preview blocking | Decision / rationale |
            | --- | --- | --- | --- | --- | --- | --- | --- |
            | None | None | None | No keyboard-only or VoiceOver known issues found during this runtime validation | None | Release Owner | No | No known accessibility issues; public preview may proceed |

            ## Log And Artifact Collection

            | Artifact | Required for public preview | Collected path or attachment | Notes |
            | --- | --- | --- | --- |
            | Completed checklist/report | Yes | checklist.md | retained |
            | GitHub Actions app artifact wrapper | Yes | freex-42-1-osx-arm64-macos-app | retained |
            | Inner app ZIP and .sha256 file | Yes | freex-osx-arm64-macos-app.zip; freex-osx-arm64-macos-app.zip.sha256 | retained |
            | freex-osx-arm64-macos-evidence.txt | Yes | freex-osx-arm64-macos-evidence.txt | retained |
            | macOS release-assets wrapper | Yes | freex-42-1-macos-release-assets | retained |
            | FreeX-latest-macos-distribution-candidate-manifest.json | Yes | freex-42-1-macos-release-assets/FreeX-latest-macos-distribution-candidate-manifest.json | retained |
            | Packaging smoke log | Yes | packaging.log | retained |
            | Launch smoke file | Yes | launch.txt | retained |
            | Notarization log | Yes | notarization.log | retained |
            | Tester instructions | Yes | tester-instructions.md | retained |
            | Diagnostics artifact | Yes | freex-42-1-osx-arm64-macos-diagnostics | retained |
            | Screenshots or recordings | Required for failures and Gatekeeper/default-handler proof | screenshots.zip | retained |
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
