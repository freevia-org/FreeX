using System;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class GitHubWorkflowPreflightTests
{
    [Fact]
    public void CiWorkflow_RunsPreflightBuildAndTestsWithReadOnlyPermissions()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "ci.yml");

        workflow.Should().Contain("push:");
        workflow.Should().Contain("pull_request:");
        workflow.Should().Contain("branches:");
        workflow.Should().Contain("- main");
        workflow.Should().Contain("permissions:");
        workflow.Should().Contain("contents: read");
        workflow.Should().NotContain("contents: write");
        workflow.Should().NotContain("pull_request_target");
        workflow.Should().Contain("runs-on: windows-latest");
        workflow.Should().Contain("timeout-minutes: 60");
        workflow.Should().Contain("actions/checkout@v6");
        workflow.Should().Contain("persist-credentials: false");
        workflow.Should().Contain("actions/setup-dotnet@v5");
        workflow.Should().Contain("dotnet-version: 10.0.x");
        workflow.Should().Contain("powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\\Test-RepositoryPreflight.ps1");
        workflow.Should().Contain("concurrency:");
        workflow.Should().Contain("group: ci-${{ github.ref }}");
        workflow.Should().Contain("cancel-in-progress: true");
        workflow.Should().Contain("name: Default test lane");
        workflow.Should().Contain("dotnet build FreeX.slnx --configuration Release");
        workflow.Should().Contain("dotnet test FreeX.DefaultTests.slnx --configuration Release --no-build");
        workflow.Should().Contain("name: macOS portable lane");
        workflow.Should().Contain("dotnet build src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj --configuration Release");
        workflow.Should().Contain("name: UI test lane");
        workflow.Should().Contain("dotnet build FreeX.UiTests.slnx --configuration Release");
        workflow.Should().Contain("dotnet test FreeX.UiTests.slnx --configuration Release --no-build");
        workflow.Should().NotContain("dotnet restore FreeX.DefaultTests.slnx");
        workflow.Should().NotContain("dotnet restore FreeX.UiTests.slnx");
        workflow.Should().NotContain("--disable-build-servers");
        workflow.Should().NotContain("-p:UseSharedCompilation=false");
        workflow.Should().NotContain("-p:NodeReuse=false");
        workflow.Should().NotContain("/nr:false");
        workflow.Should().NotContain("dotnet test FreeX.slnx --configuration Release --no-build");
    }

    [Fact]
    public void R121_FreeWWorkflow_RunsAutomaticallyOnPushAndPullRequestToMain()
    {
        // R121: freew-ci.yml was made workflow_dispatch-only on 2026-06-25 (to unblock a
        // failing merge push) and nothing ever re-enabled an automatic trigger or flagged
        // the gap -- FreeW.slnx silently stopped being built/tested by anything automated
        // for six weeks. This asserts the automatic push/pull_request gate is present, the
        // same contract CiWorkflow_RunsPreflightBuildAndTestsWithReadOnlyPermissions enforces
        // for the primary FreeX lane, so this specific regression can't recur unnoticed.
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "freew-ci.yml");

        workflow.Should().Contain("push:");
        workflow.Should().Contain("pull_request:");
        workflow.Should().Contain("branches:");
        workflow.Should().Contain("- main");
        workflow.Should().Contain("workflow_dispatch:");
        workflow.Should().Contain("permissions:");
        workflow.Should().Contain("contents: read");
        workflow.Should().NotContain("contents: write");
        workflow.Should().NotContain("pull_request_target");
        workflow.Should().Contain("runs-on: windows-latest");
        workflow.Should().Contain("dotnet build FreeW.slnx --configuration Release");
        workflow.Should().Contain("dotnet test FreeW.slnx --configuration Release --no-build");
    }

    [Fact]
    public void GlobalJson_PinsDotNetSdkBandWithFeatureRollForward()
    {
        var globalJson = WorkspaceFileLocator.ReadAllText("global.json");

        globalJson.Should().Contain("\"version\": \"10.0.100\"");
        globalJson.Should().Contain("\"rollForward\": \"latestFeature\"");
    }

    [Fact]
    public void GitHubWorkflowPreflight_ValidatesPinnedActionsAndPermissions()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-GitHubWorkflows.ps1");

        script.Should().Contain(".github\\workflows");
        script.Should().Contain("(?:-\\s*)?uses:");
        script.Should().Contain("pull_request_target");
        script.Should().Contain("self-hosted");
        script.Should().Contain("timeout-minutes");
        script.Should().Contain("persist-credentials: false");
        script.Should().Contain("if-no-files-found");
        script.Should().Contain("workflow must declare top-level permissions explicitly");
        script.Should().Contain("primary CI must run on direct pushes to main");
        script.Should().Contain("push path filters must include $requiredPushPath");
        script.Should().Contain("Directory.Build.props");
        script.Should().Contain("Directory.Packages.props");
        script.Should().Contain("workflow must not request write-all permissions");
        script.Should().Contain("must be pinned to an explicit major version");
        script.Should().Contain("\"actions/download-artifact\" = \"v7\"");
        script.Should().Contain("must declare an explicit shell");
        script.Should().Contain("must stay within the workflow workspace");
        script.Should().Contain("workflow YAML must use spaces for indentation");
        script.Should().Contain("$allowedActionMajors");
        script.Should().Contain("must use supported major");
        script.Should().Contain("publish-distribution-candidate");
        script.Should().Contain("distribution_candidate");
        script.Should().Contain("macOS release publication job must be gated to workflow_dispatch distribution-candidate runs");
        script.Should().Contain("macOS release publication job must declare actions: read");
        script.Should().Contain("macOS release publication must be the only workflow scope requesting contents: write");
        script.Should().Contain("cancel-in-progress: false");
        script.Should().Contain("macOS release publication checkout must use actions/checkout@v6 with persist-credentials: false");
        script.Should().Contain("macOS app hosted test command must use a focused --filter");
        script.Should().Contain("macOS app workflow focused test filter is missing");
        script.Should().Contain("PortablePdfTextCapabilityPlannerTests");
        script.Should().Contain("AppStoragePathPlannerTests");
        script.Should().Contain("AppOptionsStoreTests");
        script.Should().Contain("AtomicFileWriterTests");
        script.Should().Contain("MacOsLaunchSmokeReportKeyDriftGuardTests");
        script.Should().Contain("macOS release publication job must not run dotnet test");
        script.Should().Contain("github_run_id=${GITHUB_RUN_ID}");
        script.Should().Contain("github_run_attempt=${GITHUB_RUN_ATTEMPT}");
        script.Should().Contain("macOS app artifact upload name must include github.run_id");
        script.Should().Contain("macOS diagnostics artifact upload name must include github.run_id");
        script.Should().Contain("macOS app artifact upload must set retention-days: 14");
        script.Should().Contain("macOS diagnostics artifact upload must set retention-days: 14");
        script.Should().Contain("macOS release publication must download app artifacts using the current run id and run attempt");
        script.Should().Contain("macOS release publication must validate downloaded evidence run identity against the current run");
        script.Should().Contain("macOS app workflow must run focused hosted tests before package/upload step");
        script.Should().Contain("validate_macos_tfm");
        script.Should().Contain("FREEX_DOTNET_WORKLOAD_SET_VERSION: 10.0.300.3");
        script.Should().Contain("runner: macos-26");
        script.Should().Contain("dotnet workload install macos --version");
        script.Should().Contain("-p:EnableMacOsTargetFramework=true");
        script.Should().Contain("-p:ApplicationId=io.github.tony-xmelon.freex");
        script.Should().Contain("-p:ILLinkTreatWarningsAsErrors=false");
        script.Should().Contain("-p:NoWarn=IL2026");
        script.Should().Contain("--framework net10.0-macos");
        script.Should().Contain("--runtime");
        script.Should().Contain("macOS TFM validation job must be gated to workflow_dispatch validate_macos_tfm runs");
        script.Should().Contain("macOS TFM validation artifact upload must be evidence-only");
        script.Should().Contain("macOS TFM validation job must not run dotnet publish");
        script.Should().Contain("Validated $($workflows.Count) GitHub workflow file(s).");
    }

    [Fact]
    public void FreePWorkflow_IsManualOnlyOrWatchesCentralProps()
    {
        foreach (var workflowName in new[] { "freep-ci.yml" })
        {
            var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", workflowName);
            var onBlock = ExtractRequiredYamlBlock(workflow, "on:");

            // FreeP CI is intentionally manual-only (workflow_dispatch). If a push trigger
            // is ever reintroduced it must watch the central props files (also enforced by
            // tools/Test-GitHubWorkflows.ps1).
            onBlock.Should().Contain("workflow_dispatch:");
            if (onBlock.Contains("push:", StringComparison.Ordinal))
            {
                var pushBlock = ExtractRequiredYamlBlock(workflow, "push:");
                pushBlock.Should().Contain("branches:");
                pushBlock.Should().Contain("- main");
                pushBlock.Should().Contain("Directory.Build.props");
                pushBlock.Should().Contain("Directory.Packages.props");
            }
        }
    }

    [Fact]
    public void MacOsAppWorkflow_ReleasePublicationIsDistributionCandidateDispatchOnly()
    {
        var workflow = ReadMacOsAppWorkflow();

        workflow.Should().NotContain("push:");
        workflow.Should().Contain("pull_request:");
        var workflowDispatch = ExtractRequiredYamlBlock(workflow, "workflow_dispatch:");
        var distributionCandidateInput = ExtractRequiredYamlBlock(workflowDispatch, "distribution_candidate:");
        distributionCandidateInput.Should().Contain("type: boolean");
        distributionCandidateInput.Should().Contain("default: false");

        var releaseJob = ExtractRequiredYamlBlock(workflow, "publish-distribution-candidate:");
        releaseJob.Should().Contain("needs: [macos-app, macos-preview-readiness]");
        releaseJob.Should().Contain("if: ${{ github.event_name == 'workflow_dispatch' && inputs.distribution_candidate == true }}");
        releaseJob.Should().Contain("permissions:");
        releaseJob.Should().Contain("actions: read");
        releaseJob.Should().Contain("contents: write");
        releaseJob.Should().NotContain("write-all");
        releaseJob.Should().Contain("concurrency:");
        releaseJob.Should().Contain("group: macos-distribution-candidate-release");
        releaseJob.Should().Contain("cancel-in-progress: false");
        releaseJob.Should().Contain("uses: actions/checkout@v6");
        releaseJob.Should().Contain("persist-credentials: false");

        workflow.Replace(releaseJob, string.Empty, StringComparison.Ordinal)
            .Should().NotContain("contents: write");
    }

    [Fact]
    public void MacOsAppWorkflow_UsesFocusedHostedTestFiltersForAppJobOnly()
    {
        var workflow = ReadMacOsAppWorkflow();

        var appJob = ExtractRequiredYamlBlock(workflow, "macos-app:");
        appJob.Should().Contain("runs-on: ${{ matrix.runner }}");
        appJob.Should().Contain("dotnet test tests/FreeX.App.Services.Tests/FreeX.App.Services.Tests.csproj");
        appJob.Should().Contain("dotnet test tests/FreeX.Core.Model.Tests/FreeX.Core.Model.Tests.csproj");
        appJob.Should().Contain(
            "--filter 'FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfDocumentExporterTests|FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfExportPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfPageContentPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfTextCapabilityPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.WorkbookExportPrintPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.WorkbookShareActionPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.WorkbookViewportScrollPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.OpenRecentWorkbookMenuPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.AppDiagnosticsFileStoreTests|FullyQualifiedName~FreeX.App.Services.Tests.AppServicesPortabilityGuardTests|FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaProjectPortabilityGuardTests|FullyQualifiedName~FreeX.App.Services.Tests.ApplicationDataPathGuardTests|FullyQualifiedName~FreeX.App.Services.Tests.AppStoragePathPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.AppOptionsStoreTests|FullyQualifiedName~FreeX.App.Services.Tests.AtomicFileWriterTests|FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaShellSourceTests|FullyQualifiedName~FreeX.App.Services.Tests.MacOsLaunchSmokeReportKeyDriftGuardTests'");
        appJob.Should().Contain("--filter 'FullyQualifiedName~FreeX.Core.Model.Tests.ExportPathPlannerTests'");
        appJob.Should().NotContain("dotnet test FreeX.slnx");
        appJob.Should().NotContain("dotnet test FreeX.DefaultTests.slnx");
        appJob.Should().NotContain("dotnet test FreeX.UiTests.slnx");

        var focusedTestIndex = appJob.IndexOf("- name: Test portable PDF macOS route", StringComparison.Ordinal);
        focusedTestIndex.Should().BeGreaterThanOrEqualTo(0);
        foreach (var laterStep in new[]
        {
            "- name: Build app project",
            "- name: Publish app bundle",
            "- name: Require hosted smoke before app artifact upload",
            "- name: Upload app artifact",
            "- name: Upload app diagnostics"
        })
        {
            focusedTestIndex.Should().BeLessThan(appJob.IndexOf(laterStep, StringComparison.Ordinal));
        }

        var releaseJob = ExtractRequiredYamlBlock(workflow, "publish-distribution-candidate:");
        releaseJob.Should().NotContain("dotnet test");
    }

    [Fact]
    public void MacOsAppWorkflow_BlocksHostArchitectureMismatchBeforeAppArtifactUpload()
    {
        var workflow = ReadMacOsAppWorkflow();
        var appJob = ExtractRequiredYamlBlock(workflow, "macos-app:");
        var publishStep = ExtractRequiredYamlBlock(appJob, "- name: Publish app bundle");

        publishStep.Should().Contain("smoke_status=skipped_host_arch_mismatch");
        publishStep.Should().Contain("app_artifact_upload_blocked=host_arch_mismatch");
        publishStep.Should().Contain("rm -f \"$zip_path\" \"$zip_path.sha256\"");
        publishStep.Should().Contain("Host/runtime architecture mismatch for $runtime on $host_arch cannot publish a macOS app artifact.");
        publishStep.Should().Contain("echo \"Host/runtime architecture mismatch for $runtime on $host_arch cannot publish a macOS app artifact.\" | tee -a \"$smoke_log\" >&2\n            exit 1");
        publishStep.IndexOf("smoke_status=skipped_host_arch_mismatch", StringComparison.Ordinal)
            .Should()
            .BeLessThan(publishStep.IndexOf("Host/runtime architecture mismatch for $runtime on $host_arch cannot publish a macOS app artifact.", StringComparison.Ordinal));
        publishStep.IndexOf("rm -f \"$zip_path\" \"$zip_path.sha256\"", StringComparison.Ordinal)
            .Should()
            .BeLessThan(publishStep.IndexOf("Host/runtime architecture mismatch for $runtime on $host_arch cannot publish a macOS app artifact.", StringComparison.Ordinal));

        var hostedSmokeGate = ExtractRequiredYamlBlock(appJob, "- name: Require hosted smoke before app artifact upload");
        hostedSmokeGate.Should().Contain("smoke_status=skipped_host_arch_mismatch");
        hostedSmokeGate.Should().Contain("grep -q \"^smoke_status=passed$\" \"$evidence_path\"");
        hostedSmokeGate.Should().Contain("grep -q \"^macos_launch_smoke=passed$\" \"$launch_smoke_report\"");
        hostedSmokeGate.Should().Contain("grep -q \"^macos_launch_smoke=passed$\" \"$open_with_report\"");
        hostedSmokeGate.Should().Contain("grep -q \"^macos_launch_smoke=passed$\" \"$default_open_report\"");

        appJob.IndexOf("- name: Require hosted smoke before app artifact upload", StringComparison.Ordinal)
            .Should()
            .BeLessThan(appJob.IndexOf("- name: Upload app artifact", StringComparison.Ordinal));

        var releaseJob = ExtractRequiredYamlBlock(workflow, "publish-distribution-candidate:");
        releaseJob.Should().Contain("\"smoke_status=passed\"");
        releaseJob.Should().Contain("$packagingSmokeText = Get-Content -LiteralPath $packagingSmokePath -Raw");
        releaseJob.Should().Contain("Assert-ContainsRequiredText -Text $smokeReportText -Needle \"macos_launch_smoke=passed\"");
    }

    [Fact]
    public void MacOsAppWorkflow_BoundsHostedLaunchServicesSmokePaths()
    {
        var workflow = ReadMacOsAppWorkflow();

        workflow.Should().Contain("launchservices_smoke_timeout_seconds=60");
        workflow.Should().Contain("launchservices_cleanup_timeout_seconds=10");
        workflow.Should().Contain("append_launchservices_failure_diagnostics");
        workflow.Should().Contain("wait_for_bounded_launchservices_cleanup");
        workflow.Should().Contain("run_bounded_launchservices_smoke \"bundle_id\" \"$launch_smoke_report\"");
        workflow.Should().Contain("run_bounded_launchservices_smoke \"open_with\" \"$open_with_report\"");
        workflow.Should().Contain("run_bounded_launchservices_smoke \"default_open\" \"$default_open_report\"");
        workflow.Should().Contain("kill \"$launchservices_pid\" 2>/dev/null || true");
        workflow.Should().Contain("kill -9 \"$launchservices_pid\" 2>/dev/null || true");
        workflow.Should().Contain("cat \"$report_path\" >> \"$evidence_path\"");
        workflow.Should().NotContain("launch_pid=$!");
        workflow.Should().NotContain("open_with_pid=$!");
        workflow.Should().NotContain("default_open_pid=$!");

        var boundedLaunchSmokeCount = workflow.Split("run_bounded_launchservices_smoke \"", StringSplitOptions.None).Length - 1;
        boundedLaunchSmokeCount.Should().Be(3);
    }

    [Fact]
    public void MacOsAppWorkflow_WritesRunIdentityEvidenceAndUsesRunAttemptArtifactIdentity()
    {
        var workflow = ReadMacOsAppWorkflow();
        var appJob = ExtractRequiredYamlBlock(workflow, "macos-app:");

        var evidenceStep = ExtractRequiredYamlBlock(appJob, "- name: Capture runner toolchain evidence");
        evidenceStep.Should().Contain("echo \"github_run_id=${GITHUB_RUN_ID}\"");
        evidenceStep.Should().Contain("echo \"github_run_attempt=${GITHUB_RUN_ATTEMPT}\"");

        var appArtifactUpload = ExtractRequiredYamlBlock(appJob, "- name: Upload app artifact");
        appArtifactUpload.Should().Contain("name: freex-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.runtime }}-macos-app");
        appArtifactUpload.Should().Contain("retention-days: 14");

        var diagnosticsUpload = ExtractRequiredYamlBlock(appJob, "- name: Upload app diagnostics");
        diagnosticsUpload.Should().Contain("name: freex-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.runtime }}-macos-diagnostics");
        diagnosticsUpload.Should().Contain("retention-days: 14");

        var releaseJob = ExtractRequiredYamlBlock(workflow, "publish-distribution-candidate:");
        var artifactDownload = ExtractRequiredYamlBlock(releaseJob, "- name: Download macOS app artifacts");
        artifactDownload.Should().Contain("pattern: freex-${{ github.run_id }}-${{ github.run_attempt }}-*-macos-app");
        releaseJob.Should().Contain("\"github_run_id=$($env:GITHUB_RUN_ID)\"");
        releaseJob.Should().Contain("\"github_run_attempt=$($env:GITHUB_RUN_ATTEMPT)\"");
        releaseJob.Should().Contain("source_artifact_pattern = \"freex-$($env:GITHUB_RUN_ID)-$($env:GITHUB_RUN_ATTEMPT)-*-macos-app\"");
    }

    [Fact]
    public void MacOsAppWorkflow_AggregatesPreviewEvidenceFromCurrentRunArtifacts()
    {
        var workflow = ReadMacOsAppWorkflow();
        var aggregateJob = ExtractRequiredYamlBlock(workflow, "macos-preview-readiness:");

        aggregateJob.Should().Contain("needs: macos-app");
        aggregateJob.Should().Contain("runs-on: ubuntu-latest");
        aggregateJob.Should().Contain("timeout-minutes: 15");
        aggregateJob.Should().Contain("actions: read");
        aggregateJob.Should().Contain("contents: read");

        var checkoutStep = ExtractRequiredYamlBlock(aggregateJob, "- name: Checkout");
        checkoutStep.Should().Contain("uses: actions/checkout@v6");
        checkoutStep.Should().Contain("persist-credentials: false");

        var downloadStep = ExtractRequiredYamlBlock(aggregateJob, "- name: Download macOS preview artifacts");
        downloadStep.Should().Contain("uses: actions/download-artifact@v7");
        downloadStep.Should().Contain("pattern: \"freex-${{ github.run_id }}-${{ github.run_attempt }}-osx-*-macos-*\"");
        downloadStep.Should().NotContain("{app,diagnostics}");
        downloadStep.Should().Contain("path: artifacts/macos-preview-evidence");
        downloadStep.Should().Contain("merge-multiple: false");

        var readinessStep = ExtractRequiredYamlBlock(aggregateJob, "- name: Validate aggregate readiness");
        readinessStep.Should().Contain("expectedWrapperNames = @(");
        readinessStep.Should().Contain("\"freex-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT-osx-arm64-macos-app\"");
        readinessStep.Should().Contain("\"freex-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT-osx-arm64-macos-diagnostics\"");
        readinessStep.Should().Contain("\"freex-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT-osx-x64-macos-app\"");
        readinessStep.Should().Contain("\"freex-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT-osx-x64-macos-diagnostics\"");
        readinessStep.Should().Contain("Missing downloaded macOS preview artifact wrapper(s):");
        readinessStep.Should().Contain("Unexpected downloaded macOS preview artifact wrapper(s):");
        readinessStep.Should().Contain("tools/Test-MacOsPublicPreviewReadiness.ps1");
        readinessStep.Should().Contain("ExpectedRunId = $env:GITHUB_RUN_ID");
        readinessStep.Should().Contain("ExpectedRunAttempt = $env:GITHUB_RUN_ATTEMPT");
        readinessStep.Should().Contain("RequireSeparateDiagnosticsArtifact = $true");
        readinessStep.Should().Contain("$arguments.DistributionCandidate = $true");

        var manifestStep = ExtractRequiredYamlBlock(aggregateJob, "- name: Write aggregate manifest");
        manifestStep.Should().Contain("GH_TOKEN: ${{ github.token }}");
        manifestStep.Should().Contain("gh api \"repos/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID/artifacts?per_page=100\"");
        manifestStep.Should().Contain("function Find-DownloadedArtifactFile");
        manifestStep.Should().Contain("Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $FileName");
        manifestStep.Should().Contain("contains multiple '$FileName' files");
        manifestStep.Should().Contain("Find-DownloadedArtifactFile -Root $appDirectory -FileName \"freex-$runtime-macos-app.zip\" -ArtifactName $appArtifactName");
        manifestStep.Should().Contain("Find-DownloadedArtifactFile -Root $appDirectory -FileName \"freex-$runtime-macos-app.zip.sha256\" -ArtifactName $appArtifactName");
        manifestStep.Should().Contain("Find-DownloadedArtifactFile -Root $appDirectory -FileName \"freex-$runtime-macos-evidence.txt\" -ArtifactName $appArtifactName");
        manifestStep.Should().Contain("app_artifact_digest = $artifactDigestByName[$appArtifactName]");
        manifestStep.Should().Contain("diagnostics_artifact_digest = $artifactDigestByName[$diagnosticsArtifactName]");
        manifestStep.Should().Contain("schema = \"io.github.tony-xmelon.freex.macos-preview-readiness.v1\"");
        manifestStep.Should().Contain("source_artifact_pattern = \"freex-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT-osx-*-macos-*\"");
        manifestStep.Should().Contain("\"source_artifact_pattern=freex-$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT-osx-*-macos-*\"");
        manifestStep.Should().Contain("\"artifact_channel\"");
        manifestStep.Should().Contain("\"distribution_readiness\"");
        manifestStep.Should().Contain("\"smoke_status\"");
        manifestStep.Should().Contain("\"artifact_bundle_metadata_subject\"");
        manifestStep.Should().Contain("\"bundle_identifier\"");
        manifestStep.Should().Contain("\"bundle_package_type\"");
        manifestStep.Should().Contain("\"bundle_minimum_system_version\"");
        manifestStep.Should().Contain("\"bundle_high_resolution_capable\"");
        manifestStep.Should().Contain("\"artifact_document_extensions_subject\"");
        manifestStep.Should().Contain("\"native_document_extensions\"");
        manifestStep.Should().Contain("\"imported_document_extensions\"");
        manifestStep.Should().Contain("\"bundle_identifier=$($entry.evidence_markers.bundle_identifier)\"");
        manifestStep.Should().Contain("\"bundle_package_type=$($entry.evidence_markers.bundle_package_type)\"");

        var uploadStep = ExtractRequiredYamlBlock(aggregateJob, "- name: Upload aggregate readiness");
        uploadStep.Should().Contain("uses: actions/upload-artifact@v7");
        uploadStep.Should().Contain("name: freex-${{ github.run_id }}-${{ github.run_attempt }}-macos-preview-readiness");
        uploadStep.Should().Contain("path: artifacts/macos-preview-readiness/*");
        uploadStep.Should().Contain("if-no-files-found: error");
        uploadStep.Should().Contain("retention-days: 14");
    }

    [Fact]
    public void GitHubWorkflowPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory("Test-GitHubWorkflows.ps1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("GitHub workflow file(s).");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenJobOmitsTimeout()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            """
            name: Broken

            on:
              workflow_dispatch:

            permissions:
              contents: read

            jobs:
              build:
                runs-on: windows-latest
                steps:
                  - name: Safe shell
                    shell: pwsh
                    run: dotnet restore FreeX.slnx
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("must declare timeout-minutes");
        result.CombinedOutput.Should().Contain("broken.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenUploadArtifactOmitsMissingFilePolicy()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            """
            name: Broken

            on:
              workflow_dispatch:

            permissions:
              contents: read

            jobs:
              build:
                runs-on: windows-latest
                timeout-minutes: 5
                steps:
                  - name: Upload release artifact
                    uses: actions/upload-artifact@v7
                    with:
                      name: freex-release
                      path: artifacts/upload/*.exe
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("actions/upload-artifact steps must set if-no-files-found to error or warn");
        result.CombinedOutput.Should().Contain("broken.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenCheckoutPersistsCredentials()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            """
            name: Broken

            on:
              workflow_dispatch:

            permissions:
              contents: read

            jobs:
              build:
                runs-on: windows-latest
                steps:
                  - name: Checkout
                    uses: actions/checkout@v6
                    with:
                      fetch-depth: 0
                  - name: Safe shell
                    shell: pwsh
                    run: dotnet restore FreeX.slnx
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("actions/checkout steps must set persist-credentials: false");
        result.CombinedOutput.Should().Contain("broken.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenWorkflowUsesSelfHostedRunner()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            """
            name: Broken

            on:
              workflow_dispatch:

            permissions:
              contents: read

            jobs:
              build:
                runs-on: [self-hosted, windows]
                steps:
                  - name: Safe shell
                    shell: pwsh
                    run: dotnet restore FreeX.slnx
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("workflow must not use self-hosted runners");
        result.CombinedOutput.Should().Contain("broken.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenWorkflowUsesPullRequestTarget()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            """
            name: Broken

            on:
              pull_request_target:

            permissions:
              contents: read

            jobs:
              build:
                runs-on: windows-latest
                steps:
                  - name: Safe shell
                    shell: pwsh
                    run: dotnet restore FreeX.slnx
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("workflow must not use the privileged pull_request_target event");
        result.CombinedOutput.Should().Contain("broken.yml");
    }

    [Theory]
    [InlineData("\"pull_request_target\":")]
    [InlineData("'pull_request_target':")]
    public void GitHubWorkflowPreflight_FailsWhenWorkflowUsesQuotedBlockPullRequestTarget(string eventLine)
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            $$"""
            name: Broken

            "on":
              {{eventLine}}

            permissions:
              contents: read

            jobs:
              build:
                runs-on: windows-latest
                steps:
                  - name: Safe shell
                    shell: pwsh
                    run: dotnet restore FreeX.slnx
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("workflow must not use the privileged pull_request_target event");
        result.CombinedOutput.Should().Contain("broken.yml");
    }

    [Theory]
    [InlineData("- pull_request_target")]
    [InlineData("- \"pull_request_target\"")]
    [InlineData("- 'pull_request_target'")]
    public void GitHubWorkflowPreflight_FailsWhenWorkflowUsesBlockListPullRequestTarget(string eventLine)
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            $$"""
            name: Broken

            on:
              {{eventLine}}
              - push

            permissions:
              contents: read

            jobs:
              build:
                runs-on: windows-latest
                steps:
                  - name: Safe shell
                    shell: pwsh
                    run: dotnet restore FreeX.slnx
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("workflow must not use the privileged pull_request_target event");
        result.CombinedOutput.Should().Contain("broken.yml");
    }

    [Theory]
    [InlineData("on: pull_request_target")]
    [InlineData("on: \"pull_request_target\"")]
    [InlineData("on: 'pull_request_target'")]
    [InlineData("on: [push, pull_request_target]")]
    [InlineData("on: [push, \"pull_request_target\"]")]
    [InlineData("on: { pull_request_target: {} }")]
    [InlineData("\"on\": \"pull_request_target\"")]
    [InlineData("'on': ['push', 'pull_request_target']")]
    public void GitHubWorkflowPreflight_FailsWhenWorkflowUsesInlinePullRequestTarget(string onLine)
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            $$"""
            name: Broken

            {{onLine}}

            permissions:
              contents: read

            jobs:
              build:
                runs-on: windows-latest
                steps:
                  - name: Safe shell
                    shell: pwsh
                    run: dotnet restore FreeX.slnx
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("workflow must not use the privileged pull_request_target event");
        result.CombinedOutput.Should().Contain("broken.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenWorkflowRequestsWriteAllPermissions()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            """
            name: Broken

            on:
              workflow_dispatch:

            permissions: write-all

            jobs:
              build:
                runs-on: windows-latest
                steps:
                  - name: Safe shell
                    shell: pwsh
                    run: dotnet restore FreeX.slnx
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("workflow must not request write-all permissions");
        result.CombinedOutput.Should().Contain("broken.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenRunStepOmitsShell()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            """
            name: Broken

            on:
              workflow_dispatch:

            permissions:
              contents: read

            jobs:
              build:
                runs-on: windows-latest
                steps:
                  - name: Missing shell
                    run: dotnet restore FreeX.slnx
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("must declare an explicit shell");
        result.CombinedOutput.Should().Contain("Missing shell");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenLocalActionEscapesWorkspace()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            """
            name: Broken

            on:
              workflow_dispatch:

            permissions:
              contents: read

            jobs:
              build:
                runs-on: windows-latest
                steps:
                  - uses: ./../outside-action
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("must stay within the workflow workspace");
        result.CombinedOutput.Should().Contain("./../outside-action");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsForFloatingActionReference()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            """
            name: Broken

            on:
              workflow_dispatch:

            permissions:
              contents: read

            jobs:
              build:
                runs-on: windows-latest
                steps:
                  - uses: actions/checkout@main
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("GitHub workflow validation failed");
        result.CombinedOutput.Should().Contain("actions/checkout@main");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsForUnsupportedKnownActionMajor()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "broken.yml"),
            """
            name: Broken

            on:
              workflow_dispatch:

            permissions:
              contents: read

            jobs:
              build:
                runs-on: windows-latest
                timeout-minutes: 5
                steps:
                  - name: Checkout
                    uses: actions/checkout@v99
                    with:
                      persist-credentials: false
            """);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("actions/checkout@v99");
        result.CombinedOutput.Should().Contain("must use supported major v6");
    }

    [Fact]
    public void GitHubWorkflowPreflight_PassesWhenMacOsTfmValidationLaneIsManualEvidenceOnly()
    {
        using var temp = new TestTemporaryDirectory();

        WriteMacOsWorkflow(temp, AddValidMacOsTfmValidationLane(ReadMacOsAppWorkflow()));

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Validated 1 GitHub workflow file(s).");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsTfmValidationInputDefaultsOn()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            AddValidMacOsTfmValidationLane(ReadMacOsAppWorkflow()),
            "      validate_macos_tfm:\n        description: Compile the opt-in net10.0-macos target with the hosted macOS workload; evidence only, no app artifact.\n        required: false\n        type: boolean\n        default: false",
            "      validate_macos_tfm:\n        description: Compile the opt-in net10.0-macos target with the hosted macOS workload; evidence only, no app artifact.\n        required: false\n        type: boolean\n        default: true");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.NormalizedCombinedOutput.Should().Contain("macOS TFM validation must declare a workflow_dispatch validate_macos_tfm boolean input defaulting to false");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsTfmValidationJobIsNotManualDispatchOnly()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            AddValidMacOsTfmValidationLane(ReadMacOsAppWorkflow()),
            "if: ${{ github.event_name == 'workflow_dispatch' && inputs.validate_macos_tfm == true }}",
            "if: ${{ inputs.validate_macos_tfm == true }}");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS TFM validation job must be gated to workflow_dispatch validate_macos_tfm runs");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsTfmValidationBuildOrWorkloadDrifts()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            ReplaceRequiredText(
                AddValidMacOsTfmValidationLane(ReadMacOsAppWorkflow()),
                "dotnet workload install macos --version \"$FREEX_DOTNET_WORKLOAD_SET_VERSION\"",
                "dotnet workload install macos --skip-manifest-update"),
            "--framework \"$FREEX_MACOS_TFM\"",
            "--framework net10.0");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.NormalizedCombinedOutput.Should().Contain("macOS TFM validation job must install the pinned macOS workload set");
        result.NormalizedCombinedOutput.Should().Contain("macOS TFM validation job must build FreeX.App.Avalonia with -p:EnableMacOsTargetFramework=true, -p:ApplicationId=io.github.tony-xmelon.freex, -p:ILLinkTreatWarningsAsErrors=false, -p:NoWarn=IL2026, --framework net10.0-macos, and --runtime");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsTfmValidationLanePublishesOrUploadsReleaseArtifact()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            ReplaceRequiredText(
                ReplaceRequiredText(
                    AddValidMacOsTfmValidationLane(ReadMacOsAppWorkflow()),
                    "            echo \"macos_tfm_build=passed\"",
                    "            dotnet publish src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj --configuration Release --framework net10.0-macos\n            gh release create macos-tfm-validation artifacts/macos-release-assets/freex-macos-app.zip\n            echo \"macos_tfm_build=passed\""),
                "name: freex-${{ github.run_id }}-${{ github.run_attempt }}-macos-tfm-build-${{ matrix.arch }}-evidence",
                "name: freex-${{ github.run_id }}-${{ github.run_attempt }}-macos-app"),
            "path: artifacts/freex-${{ matrix.arch }}-macos-tfm-*-evidence.txt",
            "path: artifacts/macos-release-assets/*");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS TFM validation job must not run dotnet publish");
        result.CombinedOutput.Should().Contain("macOS TFM validation job must not invoke GitHub release publication");
        result.CombinedOutput.Should().Contain("macOS TFM validation artifact upload must be evidence-only");
        result.CombinedOutput.Should().Contain("macOS TFM validation job must not upload app or release artifacts");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsReleasePublicationIsNotDispatchCandidateOnly()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            ReadMacOsAppWorkflow(),
            "if: ${{ github.event_name == 'workflow_dispatch' && inputs.distribution_candidate == true }}",
            "if: ${{ github.event_name == 'workflow_dispatch' }}");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS release publication job must be gated to workflow_dispatch distribution-candidate runs");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsReleasePublicationPermissionsAreWidened()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            ReplaceRequiredText(
                ReadMacOsAppWorkflow(),
                "permissions:\n  contents: read",
                "permissions:\n  contents: write"),
            "      actions: read",
            "      actions: write");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS release publication job must declare actions: read");
        result.CombinedOutput.Should().Contain("macOS release publication must be the only workflow scope requesting contents: write");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsReleasePublicationConcurrencyCanCancel()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            ReadMacOsAppWorkflow(),
            "      cancel-in-progress: false",
            "      cancel-in-progress: true");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS release publication job must use non-canceling concurrency with cancel-in-progress: false");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsReleasePublicationCheckoutPersistsCredentials()
    {
        using var temp = new TestTemporaryDirectory();
        var workflow = ReadMacOsAppWorkflow();
        var releaseJob = ExtractRequiredYamlBlock(workflow, "publish-distribution-candidate:");
        var brokenReleaseJob = ReplaceRequiredText(
            releaseJob,
            "          persist-credentials: false",
            "          persist-credentials: true");
        var brokenWorkflow = ReplaceRequiredText(workflow, releaseJob, brokenReleaseJob);

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS release publication checkout must use actions/checkout@v6 with persist-credentials: false");
        result.CombinedOutput.Should().Contain("actions/checkout steps must set persist-credentials: false");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsHostedTestFilterDrifts()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            ReadMacOsAppWorkflow(),
            "FreeX.App.Services.Tests.MacOsLaunchSmokeReportKeyDriftGuardTests",
            "FreeX.App.Services.Tests.RenamedMacOsLaunchSmokeReportTests");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.NormalizedCombinedOutput.Should().Contain("macOS app workflow focused test filter is missing 'FreeX.App.Services.Tests.MacOsLaunchSmokeReportKeyDriftGuardTests'");
        result.NormalizedCombinedOutput.Should().Contain("macOS app workflow has unexpected focused test filter 'FreeX.App.Services.Tests.RenamedMacOsLaunchSmokeReportTests'");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsHostedTestUsesBroadLane()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            ReadMacOsAppWorkflow(),
            "dotnet test tests/FreeX.App.Services.Tests/FreeX.App.Services.Tests.csproj \\",
            "dotnet test FreeX.DefaultTests.slnx \\");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS app hosted test command must not run broad test target 'FreeX.DefaultTests.slnx'");
        result.NormalizedCombinedOutput.Should().Contain("macOS app workflow must run exactly one focused dotnet test command for tests/FreeX.App.Services.Tests/FreeX.App.Services.Tests.csproj");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsReleasePublicationRunsTests()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            ReadMacOsAppWorkflow(),
            "      - name: Download macOS app artifacts",
            """
                  - name: Broad release tests
                    shell: pwsh
                    run: dotnet test FreeX.DefaultTests.slnx --configuration Release --no-build

                  - name: Download macOS app artifacts
            """);

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS release publication job must not run dotnet test");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsEvidenceOmitsRunIdentity()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            ReadMacOsAppWorkflow(),
            "echo \"github_run_id=${GITHUB_RUN_ID}\"",
            "echo \"github_run_identifier=${GITHUB_RUN_ID}\"");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS app workflow is missing hosted runner/toolchain evidence marker");
        result.CombinedOutput.Should().Contain("github_run_id=${GITHUB_RUN_ID}");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsArtifactIdentityDropsRunAttempt()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            ReplaceRequiredText(
                ReplaceRequiredText(
                    ReadMacOsAppWorkflow(),
                    "name: freex-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.runtime }}-macos-app",
                    "name: freex-${{ github.run_id }}-${{ matrix.runtime }}-macos-app"),
                "name: freex-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.runtime }}-macos-diagnostics",
                "name: freex-${{ github.run_id }}-${{ matrix.runtime }}-macos-diagnostics"),
            "pattern: freex-${{ github.run_id }}-${{ github.run_attempt }}-*-macos-app",
            "pattern: freex-${{ github.run_id }}-*-macos-app");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS app artifact upload name must include github.run_id, github.run_attempt");
        result.CombinedOutput.Should().Contain("macOS diagnostics artifact upload name must include github.run_id, github.run_attempt");
        result.CombinedOutput.Should().Contain("macOS release publication must download app artifacts using the current run id and run attempt");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsArtifactRetentionDrifts()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            ReadMacOsAppWorkflow(),
            "          retention-days: 14",
            "          retention-days: 7");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS app artifact upload must set retention-days: 14");
        result.CombinedOutput.Should().Contain("macOS diagnostics artifact upload must set retention-days: 14");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenReleaseEvidenceIdentityValidationDrifts()
    {
        using var temp = new TestTemporaryDirectory();
        var brokenWorkflow = ReplaceRequiredText(
            ReadMacOsAppWorkflow(),
            "\"github_run_attempt=$($env:GITHUB_RUN_ATTEMPT)\"",
            "\"github_run_attempt=stale-attempt\"");

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS release publication must validate downloaded evidence run identity against the current run");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenMacOsFocusedTestsMoveAfterPackaging()
    {
        using var temp = new TestTemporaryDirectory();
        var workflow = ReadMacOsAppWorkflow();
        var appJob = ExtractRequiredYamlBlock(workflow, "macos-app:");
        var focusedTestStep = ExtractRequiredYamlBlock(appJob, "- name: Test portable PDF macOS route");
        var publishStep = ExtractRequiredYamlBlock(appJob, "- name: Publish app bundle");
        var appJobWithoutFocusedTests = ReplaceRequiredText(appJob, focusedTestStep, string.Empty);
        var brokenAppJob = ReplaceRequiredText(appJobWithoutFocusedTests, publishStep, publishStep + "\n\n" + focusedTestStep);
        var brokenWorkflow = ReplaceRequiredText(workflow, appJob, brokenAppJob);

        WriteMacOsWorkflow(temp, brokenWorkflow);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-GitHubWorkflows.ps1",
            $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("macOS app workflow must run focused hosted tests before package/upload step 'Publish app bundle'");
        result.CombinedOutput.Should().Contain("macos-app.yml");
    }

    private static string ReadMacOsAppWorkflow()
    {
        return NormalizeLineEndings(WorkspaceFileLocator.ReadAllText(".github", "workflows", "macos-app.yml"));
    }

    private static string AddValidMacOsTfmValidationLane(string workflow)
    {
        if (workflow.Contains("validate_macos_tfm:", StringComparison.Ordinal) &&
            workflow.Contains("macos-tfm-build:", StringComparison.Ordinal))
        {
            return workflow;
        }

        var withInput = ReplaceRequiredText(
            workflow,
            "        default: false\n  pull_request:",
            "        default: false\n      validate_macos_tfm:\n        description: Compile the opt-in net10.0-macos target with the hosted macOS workload; evidence only, no app artifact.\n        required: false\n        type: boolean\n        default: false\n  pull_request:");

        var releaseJob = ExtractRequiredYamlBlock(withInput, "publish-distribution-candidate:");
        const string validationJob =
            """
              macos-tfm-build:
                name: macOS TFM compile validation (${{ matrix.runtime }})
                if: ${{ github.event_name == 'workflow_dispatch' && inputs.validate_macos_tfm == true }}
                runs-on: ${{ matrix.runner }}
                timeout-minutes: 45

                strategy:
                  fail-fast: false
                  matrix:
                    include:
                      - runtime: osx-arm64
                        arch: arm64
                        runner: macos-26
                      - runtime: osx-x64
                        arch: x64
                        runner: macos-26-intel

                env:
                  DOTNET_CLI_TELEMETRY_OPTOUT: "1"
                  DOTNET_NOLOGO: "1"
                  FREEX_DOTNET_WORKLOAD_SET_VERSION: 10.0.300.3
                  FREEX_MACOS_ARCH: ${{ matrix.arch }}
                  FREEX_MACOS_TFM: net10.0-macos
                  FREEX_MACOS_TFM_EVIDENCE: artifacts/freex-${{ matrix.arch }}-macos-tfm-build-evidence.txt
                  FREEX_RUNTIME: ${{ matrix.runtime }}
                  FREEX_XCODE_PATH: /Applications/Xcode_26.5.app/Contents/Developer

                steps:
                  - name: Checkout
                    uses: actions/checkout@v6
                    with:
                      fetch-depth: 0
                      persist-credentials: false

                  - name: Setup .NET
                    uses: actions/setup-dotnet@v5
                    with:
                      dotnet-version: 10.0.300

                  - name: Capture macOS TFM toolchain evidence
                    shell: bash
                    run: |
                      set -euo pipefail
                      test -d "$FREEX_XCODE_PATH"
                      sudo xcode-select -s "$FREEX_XCODE_PATH"
                      mkdir -p "$(dirname "$FREEX_MACOS_TFM_EVIDENCE")"
                      {
                        echo "runtime=$FREEX_RUNTIME"
                        echo "arch=$FREEX_MACOS_ARCH"
                        echo "macos_tfm=$FREEX_MACOS_TFM"
                        echo "github_run_id=${GITHUB_RUN_ID}"
                        echo "github_run_attempt=${GITHUB_RUN_ATTEMPT}"
                        dotnet --info
                        xcodebuild -version
                      } | tee "$FREEX_MACOS_TFM_EVIDENCE"

                  - name: Install macOS workload
                    shell: bash
                    run: |
                      set -euo pipefail
                      {
                        dotnet workload install macos --version "$FREEX_DOTNET_WORKLOAD_SET_VERSION"
                        dotnet workload --info
                      } | tee -a "$FREEX_MACOS_TFM_EVIDENCE"

                  - name: Build opt-in macOS TFM
                    shell: bash
                    run: |
                      set -euo pipefail
                      dotnet build src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj \
                        --configuration Release \
                        --framework "$FREEX_MACOS_TFM" \
                        --runtime "$FREEX_RUNTIME" \
                        -p:EnableMacOsTargetFramework=true \
                        -p:ApplicationId=io.github.tony-xmelon.freex \
                        -p:ILLinkTreatWarningsAsErrors=false \
                        -p:NoWarn=IL2026
                      {
                        echo "macos_tfm_build=passed"
                        echo "macos_tfm=$FREEX_MACOS_TFM"
                        echo "runtime=$FREEX_RUNTIME"
                        echo "macos_tfm_artifact_channel=evidence-only"
                      } | tee -a "$FREEX_MACOS_TFM_EVIDENCE"

                  - name: Upload macOS TFM build evidence
                    uses: actions/upload-artifact@v7
                    with:
                      name: freex-${{ github.run_id }}-${{ github.run_attempt }}-macos-tfm-build-${{ matrix.arch }}-evidence
                      path: artifacts/freex-${{ matrix.arch }}-macos-tfm-*-evidence.txt
                      if-no-files-found: error
                      retention-days: 14
            """;

        return ReplaceRequiredText(withInput, releaseJob, validationJob + "\n\n" + releaseJob);
    }

    private static void WriteMacOsWorkflow(TestTemporaryDirectory temp, string workflow)
    {
        File.WriteAllText(Path.Combine(temp.Path, "macos-app.yml"), workflow);
    }

    private static string ExtractRequiredYamlBlock(string yaml, string key)
    {
        var lines = NormalizeLineEndings(yaml).Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex].TrimEnd();
            if (!string.Equals(line.TrimStart(' '), key, StringComparison.Ordinal))
            {
                continue;
            }

            var indentLength = line.Length - line.TrimStart(' ').Length;
            var blockLines = new System.Collections.Generic.List<string> { lines[lineIndex] };
            for (var nextLineIndex = lineIndex + 1; nextLineIndex < lines.Length; nextLineIndex++)
            {
                if (string.IsNullOrWhiteSpace(lines[nextLineIndex]))
                {
                    blockLines.Add(lines[nextLineIndex]);
                    continue;
                }

                var nextIndentLength = lines[nextLineIndex].Length - lines[nextLineIndex].TrimStart(' ').Length;
                if (nextIndentLength <= indentLength)
                {
                    break;
                }

                blockLines.Add(lines[nextLineIndex]);
            }

            return string.Join('\n', blockLines);
        }

        throw new InvalidOperationException($"YAML block was not found: {key}");
    }

    private static string ReplaceRequiredText(string text, string oldValue, string newValue)
    {
        var normalizedOldValue = NormalizeLineEndings(oldValue);
        var updated = text.Replace(normalizedOldValue, NormalizeLineEndings(newValue), StringComparison.Ordinal);
        if (string.Equals(updated, text, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Required workflow text was not found: {normalizedOldValue}");
        }

        return updated;
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
