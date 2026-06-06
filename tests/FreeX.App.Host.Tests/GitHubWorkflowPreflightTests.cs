using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class GitHubWorkflowPreflightTests
{
    [Fact]
    public void CiWorkflow_RunsPreflightBuildAndTestsWithReadOnlyPermissions()
    {
        var workflow = File.ReadAllText(WorkspaceFileLocator.Find(".github", "workflows", "ci.yml"));

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
        var globalJson = File.ReadAllText(WorkspaceFileLocator.Find("global.json"));

        globalJson.Should().Contain("\"version\": \"10.0.100\"");
        globalJson.Should().Contain("\"rollForward\": \"latestFeature\"");
    }

    [Fact]
    public void GitHubWorkflowPreflight_ValidatesPinnedActionsAndPermissions()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1"));

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
        script.Should().Contain("must declare an explicit shell");
        script.Should().Contain("must stay within the workflow workspace");
        script.Should().Contain("workflow YAML must use spaces for indentation");
        script.Should().Contain("$allowedActionMajors");
        script.Should().Contain("must use supported major");
        script.Should().Contain("Validated $($workflows.Count) GitHub workflow file(s).");
    }

    [Fact]
    public void GitHubWorkflowPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, "");

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
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("must declare timeout-minutes");
        (result.Output + result.Error).Should().Contain("broken.yml");
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
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("actions/upload-artifact steps must set if-no-files-found to error or warn");
        (result.Output + result.Error).Should().Contain("broken.yml");
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
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("actions/checkout steps must set persist-credentials: false");
        (result.Output + result.Error).Should().Contain("broken.yml");
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
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("workflow must not use self-hosted runners");
        (result.Output + result.Error).Should().Contain("broken.yml");
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
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("workflow must not use the privileged pull_request_target event");
        (result.Output + result.Error).Should().Contain("broken.yml");
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
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("workflow must not request write-all permissions");
        (result.Output + result.Error).Should().Contain("broken.yml");
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
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("must declare an explicit shell");
        (result.Output + result.Error).Should().Contain("Missing shell");
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
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("must stay within the workflow workspace");
        (result.Output + result.Error).Should().Contain("./../outside-action");
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
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("GitHub workflow validation failed");
        (result.Output + result.Error).Should().Contain("actions/checkout@main");
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
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-WorkflowDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("actions/checkout@v99");
        (result.Output + result.Error).Should().Contain("must use supported major v6");
    }

    private static PowerShellResult RunScriptFromTemporaryWorkingDirectory(string scriptPath, string arguments)
    {
        using var workingDirectory = new TestTemporaryDirectory();
        return PowerShellScriptRunner.Run(scriptPath, workingDirectory.Path, arguments);
    }

}
