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
        workflow.Should().Contain("name: Default test lane");
        workflow.Should().Contain("dotnet build FreeX.DefaultTests.slnx --configuration Release");
        workflow.Should().Contain("dotnet test FreeX.DefaultTests.slnx --configuration Release --no-build");
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
        script.Should().Contain("MacOsLaunchSmokeReportKeyDriftGuardTests");
        script.Should().Contain("macOS release publication job must not run dotnet test");
        script.Should().Contain("github_run_id=${GITHUB_RUN_ID}");
        script.Should().Contain("github_run_attempt=${GITHUB_RUN_ATTEMPT}");
        script.Should().Contain("macOS app artifact upload name must include github.run_id");
        script.Should().Contain("macOS diagnostics artifact upload name must include github.run_id");
        script.Should().Contain("macOS release publication must download app artifacts using the current run id and run attempt");
        script.Should().Contain("macOS release publication must validate downloaded evidence run identity against the current run");
        script.Should().Contain("macOS app workflow must run focused hosted tests before package/upload step");
        script.Should().Contain("Validated $($workflows.Count) GitHub workflow file(s).");
    }

    [Fact]
    public void MacOsAppWorkflow_ReleasePublicationIsDistributionCandidateDispatchOnly()
    {
        var workflow = ReadMacOsAppWorkflow();

        var workflowDispatch = ExtractRequiredYamlBlock(workflow, "workflow_dispatch:");
        var distributionCandidateInput = ExtractRequiredYamlBlock(workflowDispatch, "distribution_candidate:");
        distributionCandidateInput.Should().Contain("type: boolean");
        distributionCandidateInput.Should().Contain("default: false");

        var releaseJob = ExtractRequiredYamlBlock(workflow, "publish-distribution-candidate:");
        releaseJob.Should().Contain("needs: macos-app");
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
            "--filter 'FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfDocumentExporterTests|FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfExportPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfPageContentPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.WorkbookExportPrintPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.WorkbookShareActionPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.AppServicesPortabilityGuardTests|FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaProjectPortabilityGuardTests|FullyQualifiedName~FreeX.App.Services.Tests.ApplicationDataPathGuardTests|FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaShellSourceTests|FullyQualifiedName~FreeX.App.Services.Tests.MacOsLaunchSmokeReportKeyDriftGuardTests'");
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
    public void MacOsAppWorkflow_WritesRunIdentityEvidenceAndUsesRunAttemptArtifactIdentity()
    {
        var workflow = ReadMacOsAppWorkflow();
        var appJob = ExtractRequiredYamlBlock(workflow, "macos-app:");

        var evidenceStep = ExtractRequiredYamlBlock(appJob, "- name: Capture runner toolchain evidence");
        evidenceStep.Should().Contain("echo \"github_run_id=${GITHUB_RUN_ID}\"");
        evidenceStep.Should().Contain("echo \"github_run_attempt=${GITHUB_RUN_ATTEMPT}\"");

        var appArtifactUpload = ExtractRequiredYamlBlock(appJob, "- name: Upload app artifact");
        appArtifactUpload.Should().Contain("name: freex-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.runtime }}-macos-app");

        var diagnosticsUpload = ExtractRequiredYamlBlock(appJob, "- name: Upload app diagnostics");
        diagnosticsUpload.Should().Contain("name: freex-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.runtime }}-macos-diagnostics");

        var releaseJob = ExtractRequiredYamlBlock(workflow, "publish-distribution-candidate:");
        var artifactDownload = ExtractRequiredYamlBlock(releaseJob, "- name: Download macOS app artifacts");
        artifactDownload.Should().Contain("pattern: freex-${{ github.run_id }}-${{ github.run_attempt }}-*-macos-app");
        releaseJob.Should().Contain("\"github_run_id=$($env:GITHUB_RUN_ID)\"");
        releaseJob.Should().Contain("\"github_run_attempt=$($env:GITHUB_RUN_ATTEMPT)\"");
        releaseJob.Should().Contain("source_artifact_pattern = \"freex-$($env:GITHUB_RUN_ID)-$($env:GITHUB_RUN_ATTEMPT)-*-macos-app\"");
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
