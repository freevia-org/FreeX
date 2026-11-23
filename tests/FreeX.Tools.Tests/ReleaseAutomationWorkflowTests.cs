using System.Text.RegularExpressions;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ReleaseAutomationWorkflowTests
{
    [Fact]
    public void UserTestPublishScript_PublishesFrameworkDependentRuntimeSpecificBuild()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Publish-UserTestBuild.ps1");

        script.Should().Contain("[string]$RuntimeIdentifier = \"win-x64\"");
        script.Should().Contain("\"-r\", $RuntimeIdentifier");
        script.Should().Contain("\"--self-contained\", \"false\"");
        script.Should().Contain("$artifactExeHashPath = \"$artifactExePath.sha256\"");
        script.Should().Contain("Set-Content -LiteralPath $artifactExeHashPath");
        script.Should().NotContain("--disable-build-servers");
        script.Should().NotContain("-p:UseSharedCompilation=false");
        script.Should().NotContain("-p:NodeReuse=false");
        script.Should().NotContain("/nr:false");
        script.Should().Contain("FreeX is not affiliated with, endorsed by, or sponsored by Microsoft.");
        script.Should().Contain("Microsoft Excel is a trademark of Microsoft Corporation.");
        script.Should().Contain("docs/legal/privacy.md");
        script.Should().Contain("THIRD_PARTY_NOTICES.md");
    }

    [Fact]
    public void UserTestPublishScript_CanPackageAndOptionallySignLocalMsix()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Publish-UserTestBuild.ps1");

        script.Should().Contain("[ValidateSet(\"SingleFile\", \"Folder\", \"Msix\", \"Velopack\")]");
        script.Should().Contain("[string]$MsixCertificatePath = $env:FREEX_MSIX_CERTIFICATE_PATH");
        script.Should().Contain("[string]$MsixCertificatePassword = $env:FREEX_MSIX_CERTIFICATE_PASSWORD");
        script.Should().Contain("[string]$MsixTimestampUrl = $env:FREEX_MSIX_TIMESTAMP_URL");
        script.Should().Contain("[switch]$AllowUnsignedMsix");
        script.Should().Contain("$artifactMsixPath = Join-Path $artifactRoot \"$artifactName.msix\"");
        script.Should().Contain("function ConvertTo-MsixPackageVersion");
        script.Should().Contain("function Import-MsixSigningCertificate");
        script.Should().Contain("function Get-MsixManifestPublisher");
        script.Should().Contain("ConvertTo-ToolXmlAttribute");
        script.Should().Contain("MSIX packages require MsixCertificatePath; pass -AllowUnsignedMsix only for local packaging validation.");
        script.Should().Contain("ConvertTo-SecureString -String $CertificatePassword -AsPlainText -Force");
        script.Should().Contain("Cert:\\CurrentUser\\My");
        script.Should().Contain("function Remove-MsixSigningCertificate");
        script.Should().Contain("$msixVersion = ConvertTo-MsixPackageVersion -DisplayVersion $Version");
        script.Should().Contain("$msixParts[$i] = $msixParts[$i] % 65536");
        script.Should().Contain("$msixPublisher = Get-MsixManifestPublisher -Certificate $importedSigningCertificate");
        script.Should().Contain("$msixPublisherAttribute = ConvertTo-ToolXmlAttribute -Value $msixPublisher");
        script.Should().Contain("<Identity Name=\"FreeX.Tester\" Publisher=\"$msixPublisherAttribute\" Version=\"$msixVersion\" />");
        script.Should().Contain("EntryPoint=\"Windows.FullTrustApplication\"");
        script.Should().Contain("<rescap:Capability Name=\"runFullTrust\" />");
        script.Should().Contain("Get-Command makeappx.exe");
        script.Should().Contain("makeappx.exe was not found. Install the Windows SDK");
        script.Should().Contain("pack /d $publishDir /p $artifactMsixPath /o");
        script.Should().Contain("Get-Command signtool.exe");
        script.Should().Contain("signtool.exe was not found. Install the Windows SDK to sign MSIX packages.");
        script.Should().Contain("$signArgs = @(\"sign\", \"/fd\", \"SHA256\", \"/sha1\", $importedSigningCertificate.Thumbprint, \"/s\", \"My\")");
        script.Should().NotContain("\"/p\", $MsixCertificatePassword");
        script.Should().Contain("Created unsigned local MSIX; pass -MsixCertificatePath to sign it.");
        script.Should().Contain("$artifactMsixHashPath = \"$artifactMsixPath.sha256\"");
        script.Should().Contain("Set-Content -LiteralPath $artifactMsixHashPath");
    }

    [Fact]
    public void FullReleaseWorkflow_GatesOnlyWindowsSigningLegsBehindThePublicPreviewEnvironment()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "full-release.yml");

        // The Azure federated credential trusts only environment:public-preview and the AZURE_* values live
        // only in that environment, so exactly the Windows legs of the two signing job families need it.
        // Linux and macOS legs run without the approval wait. The non-empty value sits on the true side
        // because an empty string is falsy in && / || chains; a dry run still resolves to no environment.
        const string scoped = "environment: ${{ !inputs.dry_run && matrix.platform == 'windows' && 'public-preview' || '' }}";
        Regex.Matches(workflow, Regex.Escape(scoped)).Count.Should().Be(2,
            "only the package and suite-package matrix jobs scope the approval to Windows signing legs");
        workflow.Should().NotContain("environment: ${{ !inputs.dry_run && 'public-preview' || '' }}");

        // Publication stays behind the protected environment on the job that creates the release.
        Regex.Matches(workflow, @"(?m)^    environment: public-preview\r?$").Count.Should().Be(2);
        workflow.Should().Contain("needs: [resolve, prepare, publication-gate]");
        // Every Azure login step is Windows-only, so no ungated leg needs the environment secrets.
        Regex.Matches(workflow, @"(?s)if: matrix\.platform == 'windows' && !inputs\.dry_run\s+uses: azure/login").Count.Should().Be(2);
    }
    private static string ExtractWorkflowJob(string workflow, string jobId)
    {
        var match = Regex.Match(workflow, $@"(?ms)^  {Regex.Escape(jobId)}:\r?\n.*?(?=^  [A-Za-z][A-Za-z0-9_-]*:\r?$|\z)");
        match.Success.Should().BeTrue($"the workflow should define the '{jobId}' job");
        return match.Value;
    }

    [Fact]
    public void ReleaseReadinessWorkflow_SkipsRepeatsOfARedShaButStillReportsRed()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "release-readiness.yml");
        var guard = ExtractWorkflowJob(workflow, "guard");
        var summary = ExtractWorkflowJob(workflow, "summary");

        // Re-testing an unchanged SHA that already failed its full gates twice teaches nothing, so the
        // scheduled run skips it. Only failed runs whose README check passed count (that check reads
        // state outside the repository), and only the schedule is ever skipped.
        guard.Should().Contain("known_red: ${{ steps.decide.outputs.known_red }}");
        // A run whose every failed job is infrastructure (the classifier release-auto-retry.yml uses) never counts as failed,
        // and an unrunnable classifier throws so the guard fails open and runs in full.
        guard.Should().Contain("sparse-checkout: tools/Get-ReleaseRunFailureClass.ps1");
        guard.Should().Contain("tools/Get-ReleaseRunFailureClass.ps1 -Repository $env:REPOSITORY -RunId $failedParts[0]");
        guard.Should().Contain("if (-not $classState.retryable) { $failedFull++ }");
        guard.Should().Contain("the failure classifier could not judge run");
        guard.Should().Contain("contents: read", "the sparse checkout needs the contents scope");
        guard.Should().NotContain("contents: write");
        guard.Should().Contain("-f status=failure");
        guard.Should().Contain("-contains \"README download links`tsuccess\"");
        guard.Should().Contain("$failedFull -ge 2");
        guard.Should().Contain("if ($env:EVENT_NAME -eq 'schedule')");
        guard.Should().Contain("\"known_red=$knownRed\"");

        // The skip must never turn a red SHA green: the summary fails for a known-red skip.
        summary.Should().Contain("$guardKnownRed = \"${{ needs.guard.outputs.known_red }}\"");
        summary.Should().MatchRegex(@"(?s)\$guardSkip -eq 'true' -and \$guardKnownRed -eq 'true'\)\s*\{\s*throw");
    }

    [Fact]
    public void FullReleaseWorkflow_SkipsReleaseTestsOnlyForDryRunsAndStillRequiresThemForRealReleases()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "full-release.yml");
        var releaseTests = ExtractWorkflowJob(workflow, "release-tests");
        var releaseGate = ExtractWorkflowJob(workflow, "release-gate");
        var publicationGate = ExtractWorkflowJob(workflow, "publication-gate");

        // The dry run and the weekly rehearsal (which dispatches dry_run=true) rehearse packaging;
        // release-readiness.yml already runs the release-only gates, so re-running them held macOS
        // slots the package legs needed. The condition must be exactly the dry_run input: nothing
        // else may switch the real-release tests off.
        // The only other way the tests are skipped is verified readiness evidence on this exact
        // commit, and only the literal 'true' from the candidate job counts (empty runs everything).
        releaseTests.Should().Contain(
            "if: ${{ !inputs.dry_run && needs.candidate.outputs.gate_evidence_reuse != 'true' }}");
        var candidate = ExtractWorkflowJob(workflow, "candidate");
        candidate.Should().Contain("gate_evidence_reuse: ${{ steps.gate_evidence.outputs.reuse }}");
        candidate.Should().Contain("pwsh -NoProfile -File tools/Resolve-ReleaseGateEvidence.ps1");
        candidate.Should().Contain("-CommitSha $env:RELEASE_SHA");

        // 'skipped' is accepted only together with the dry_run input or verified evidence; a real
        // release without either needs 'success'.
        releaseGate.Should().Contain("$isDryRun = \"${{ inputs.dry_run }}\" -eq 'true'");
        releaseGate.Should().Contain("$evidenceReused = \"${{ needs.candidate.outputs.gate_evidence_reuse }}\" -eq 'true'");
        releaseGate.Should().Contain(
            "$releaseTestsAcceptable = $releaseTestResult -eq 'success' -or (($isDryRun -or $evidenceReused) -and $releaseTestResult -eq 'skipped')");
        releaseGate.Should().Contain("-not $releaseTestsAcceptable");
        releaseGate.Should().NotContain("$releaseTestResult -ne 'success'",
            "the old unconditional check would reject the skipped tests of a dry run");
        releaseGate.Should().Contain("if: always()");

        // Everything downstream still needs the gate, and the gate must still be green to publish.
        publicationGate.Should().Contain("needs: [resolve, prepare, release-gate, package, suite-package]");
        publicationGate.Should().Contain("$releaseGateResult -ne 'success'");
        ExtractWorkflowJob(workflow, "publish").Should().Contain("if: ${{ !inputs.dry_run }}");
        ExtractWorkflowJob(workflow, "publish").Should().Contain("needs: [resolve, prepare, publication-gate]");
        ExtractWorkflowJob(workflow, "publish-suite").Should().Contain("needs: [resolve, suite-package, publication-gate]");

        // Packaging never waited on the tests, so dropping them cannot starve the package legs.
        ExtractWorkflowJob(workflow, "package").Should().Contain("needs: [resolve, prepare, candidate]");
    }

    [Fact]
    public void FullReleaseWorkflow_UsesOneAppVersionTagAndIndependentPlatformPackages()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "full-release.yml");
        var publisher = WorkspaceFileLocator.ReadAllText("tools", "Publish-SisterAppTesterPackages.ps1");
        var expectedLanes = new[]
        {
            """@{ app = "FreeX"; platform = "windows"; runtime = "win-x64"; runner = "windows-latest""",
            """@{ app = "FreeX"; platform = "linux"; runtime = "linux-x64"; runner = "ubuntu-latest""",
            """@{ app = "FreeX"; platform = "linux"; runtime = "linux-arm64"; runner = "ubuntu-24.04-arm""",
            """@{ app = "FreeX"; platform = "macos"; runtime = "osx-x64"; runner = "macos-15-intel""",
            """@{ app = "FreeX"; platform = "macos"; runtime = "osx-arm64"; runner = "macos-15""",
            """@{ app = "FreeW"; platform = "windows"; runtime = "win-x64"; runner = "windows-latest""",
            """@{ app = "FreeW"; platform = "linux"; runtime = "linux-x64"; runner = "ubuntu-latest""",
            """@{ app = "FreeW"; platform = "linux"; runtime = "linux-arm64"; runner = "ubuntu-24.04-arm""",
            """@{ app = "FreeW"; platform = "macos"; runtime = "osx-x64"; runner = "macos-15-intel""",
            """@{ app = "FreeW"; platform = "macos"; runtime = "osx-arm64"; runner = "macos-15""",
            """@{ app = "FreeP"; platform = "windows"; runtime = "win-x64"; runner = "windows-latest""",
            """@{ app = "FreeP"; platform = "linux"; runtime = "linux-x64"; runner = "ubuntu-latest""",
            """@{ app = "FreeP"; platform = "linux"; runtime = "linux-arm64"; runner = "ubuntu-24.04-arm""",
            """@{ app = "FreeP"; platform = "macos"; runtime = "osx-x64"; runner = "macos-15-intel""",
            """@{ app = "FreeP"; platform = "macos"; runtime = "osx-arm64"; runner = "macos-15"""
        };

        workflow.Should().Contain("name: Full Signed Release");
        workflow.Should().Contain("- all");
        workflow.Should().Contain("- FreeX");
        workflow.Should().Contain("- FreeW");
        workflow.Should().Contain("- FreeP");
        workflow.Should().Contain("- windows");
        workflow.Should().Contain("- linux");
        workflow.Should().Contain("- macos");
        Regex.Matches(workflow, @"@\{ app = ""(?:FreeX|FreeW|FreeP)""; platform = ").Count.Should().Be(15);
        foreach (var lane in expectedLanes)
        {
            workflow.Should().Contain(lane);
        }

        workflow.Should().Contain("needs: [resolve, prepare, candidate]");
        workflow.Should().Contain("fromJSON(needs.prepare.outputs.package_matrix)");
        workflow.Should().Contain("fromJSON(needs.prepare.outputs.suite_matrix)");
        workflow.Should().Contain("max-parallel: 15");
        workflow.Should().NotContain("fetch-depth: 0");
        workflow.Should().NotContain("inputs.platform == 'all' || inputs.platform == matrix.platform");
        workflow.Should().Contain("group: full-signed-release-${{ inputs.release_version }}");
        workflow.Should().Contain("$isMainRelease = $env:GITHUB_REF -eq \"refs/heads/main\"");
        workflow.Should().Contain("$isTaggedContinuation = $allowedTagRefs -ccontains $env:GITHUB_REF");
        workflow.Should().Contain("-not ($isMainRelease -or $isTaggedContinuation)");
        workflow.Should().Contain("Full app releases must start from refs/heads/main or continue from an immutable tag for the requested version.");
        workflow.Should().Contain("refs/tags/free-suite-v$version");
        workflow.Should().Contain("Later main commits do not invalidate this run.");
        workflow.Should().Contain("name: Validate version and reject conflicting immutable tags");
        workflow.Should().Contain("release_version must be a filesystem-safe semantic version such as 0.8.182.");
        workflow.Should().Contain("choose a new version before running full release tests");
        workflow.Should().NotContain("codex/full-release-");
        workflow.Should().Contain("name: Verify immutable release candidate");
        workflow.Should().Contain("actions: read");
        // The workflow invokes the resolver, which applies the unchanged attestation to the newest
        // attested ancestor of the dispatch head. Assert the invocation and the delegation
        // separately, so this cannot pass on a mere mention of the old path in a comment.
        workflow.Should().Contain("pwsh -NoProfile -File tools/Resolve-GitHubReleaseCandidate.ps1");
        var resolver = WorkspaceFileLocator.ReadAllText("tools", "Resolve-GitHubReleaseCandidate.ps1");
        resolver.Should().Contain("Test-GitHubReleaseCandidate.ps1");
        workflow.Should().NotContain("preflight_matrix");
        workflow.Should().NotContain("verify_matrix");
        workflow.Should().NotContain("name: Preflight ${{ matrix.platform }}");
        workflow.Should().NotContain("name: Verify ${{ matrix.app }} ${{ matrix.platform }}");
        workflow.Should().NotContain("pwsh -NoProfile -File tools/Test-RepositoryPreflight.ps1");
        workflow.Should().Contain("actions/cache@55cc8345863c7cc4c66a329aec7e433d2d1c52a9");
        workflow.Should().Contain("runs-on: ${{ matrix.runner }}");
        workflow.Should().Contain("tools/Get-TestGateMatrix.ps1 -Gate release");
        workflow.Should().Contain("fromJSON(needs.prepare.outputs.release_matrix)");
        workflow.Should().Contain("tools/Invoke-TestGate.ps1");
        workflow.Should().Contain("-Gate release");
        workflow.Should().Contain("-RetryFailedProjectCount 1");
        workflow.Should().Contain("-HangTimeout 30m");
        workflow.Should().Contain("name: FreeX full release gate");
        workflow.Should().Contain("needs: [resolve, prepare, candidate]");
        workflow.Should().Contain("name: Validate complete release inventory");
        workflow.Should().Contain("needs: [resolve, prepare, release-gate, package, suite-package]");
        workflow.Should().Contain("$releaseGateResult = \"${{ needs.release-gate.result }}\"");
        workflow.Should().Contain("nuget-v2-${{ runner.os }}");
        workflow.Should().NotContain("'**/*.csproj'");
        workflow.Should().Contain("name: Verify published release inventory");
        workflow.Should().Contain("Published tag '$Tag' targets");
        workflow.Should().NotContain("function Invoke-Dotnet");
        workflow.Should().NotContain("dotnet test FreeP.slnx");
        workflow.Should().Contain("-Runtimes \"${{ matrix.runtime }}\"");
        workflow.Should().Contain("tools/Publish-WindowsVelopackPackage.ps1");
        publisher.Should().Contain("AvaloniaValidationProject = \"tools/FreeX.Validation.Avalonia/FreeX.Validation.Avalonia.csproj\"");
        publisher.Should().Contain("AvaloniaValidationHost = \"FreeX.Validation.Avalonia\"");
        publisher.Should().Contain("if (-not $isWindowsRuntime) {");
        workflow.Should().Contain("$tag = \"$($app.ToLowerInvariant())-v$version\"");
        workflow.Should().Contain("## Install and deploy");
        workflow.Should().Contain("### Windows x64");
        workflow.Should().Contain("### Linux x64 and ARM64");
        workflow.Should().Contain("### macOS Intel and Apple silicon");
        // The macOS signing claim is conditional on the macos_unsigned input, because an unsigned
        // build must not be published describing itself as signed. Pin both arms: the signed
        // wording for a normal release, and the explicit NOT-signed wording for an unsigned one.
        workflow.Should().Contain("inputs.macos_unsigned");
        workflow.Should().Contain("'signed, notarized, and stapled'");
        workflow.Should().Contain("'NOT signed or notarized'");
        // -AllowUnsigned must never be passed unconditionally: every occurrence is gated on the
        // input, so a normal release still fails closed when a signing secret is missing.
        Regex.Matches(workflow, @"-AllowUnsigned").Count
            .Should().Be(Regex.Matches(workflow, @"inputs\.macos_unsigned && '-AllowUnsigned'").Count);
        workflow.Should().Contain("$notes = @'");
        workflow.Should().Contain("sha256sum -c {{APP}}-v{{VERSION}}-linux-<architecture>.zip.sha256");
        workflow.Should().Contain("shasum -a 256 -c {{APP}}-v{{VERSION}}-osx-<architecture>.zip.sha256");
        workflow.Should().Contain("Replace(\"{{SHA}}\", $env:RELEASE_SHA)");
        workflow.Should().Contain("$notes | gh release edit $tag --title $title --notes-file - \"--prerelease=$prerelease\"");
        Regex.Matches(workflow, "gh release edit .*--prerelease=").Count.Should().Be(2);
        workflow.Should().Contain("$notes | gh release create $tag @assets --target $env:RELEASE_SHA --title $title --notes-file - @releaseArgs");
        workflow.Should().NotContain("## Assets");

        publisher.Should().Contain("[ValidateSet(\"FreeX\", \"FreeW\", \"FreeP\")]");
        publisher.Should().Contain("[ValidateSet(\"SingleFile\", \"FolderZip\")]");
        publisher.Should().Contain("-p:IncludeNativeLibrariesForSelfExtract=true");
        publisher.Should().Contain("-p:IncludeAllContentForSelfExtract=true");
        publisher.Should().Contain("-p:FreePWindowsBuild=false");
        publisher.Should().Contain("$testerReleaseSmokeProjectPath");
        publisher.Should().Contain("$smokeToolPath");
        publisher.Should().Contain("@(\"--tester-release-smoke\", $smokeReportPath)");
        publisher.Should().Contain("$smokeArguments = @(\"--packaging-smoke\")");
        publisher.Should().Contain("freep_packaging_smoke=passed");
        publisher.Should().Contain("Packaged smoke passed for $App $runtime.");
        publisher.Should().Contain("has no packaged smoke entry point; exact-SHA CI provides its compiled test coverage.");
        publisher.Should().Contain("Single-file Windows publish produced runtime sidecars");
        publisher.Should().Contain("$packageName = \"$App-v$Version-$runtime$packageExtension\"");
    }

    [Fact]
    public void ReleaseProgressJson_DefinesAutomaticTesterVersionBand()
    {
        using var document = JsonDocument.Parse(WorkspaceFileLocator.ReadAllText("release", "progress.json"));
        var root = document.RootElement;

        root.GetProperty("major").GetInt32().Should().Be(0);
        root.GetProperty("overallCompletion").GetInt32().Should().BeInRange(0, 100);
        root.GetProperty("releasePatchBase").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        root.GetProperty("releasePatchSource").GetString().Should().Be("github_run_number");
        root.GetProperty("channel").GetString().Should().Be("test");
    }

    [Fact]
    public void TestDistributionPlan_LinksToLatestTesterDownload()
    {
        var plan = WorkspaceFileLocator.ReadAllText("docs", "release/test-distribution.md");

        plan.Should().Contain("Stable latest non-prerelease tester downloads");
        plan.Should().Contain("FreeX-latest-win-x64.exe");
        plan.Should().Contain("https://github.com/freevia-org/FreeX/releases/latest/download/FreeX-latest-win-x64.exe");
        plan.Should().Contain("https://github.com/freevia-org/FreeX/releases/latest/download/FreeX-latest-macos-arm64.zip");
        plan.Should().Contain("https://github.com/freevia-org/FreeX/releases/latest/download/FreeX-latest-macos-x64.zip");
        plan.Should().Contain("GitHub's `releases/latest` redirect remains on the latest non-prerelease tester build");
        plan.Should().Contain("GitHub OIDC authenticates the dedicated release identity");
        plan.Should().Contain("Store-bound MSIX packages are submitted with the Partner Center identity");
        plan.Should().Contain("Windows publication additionally requires Azure Artifact Signing");
    }
}
