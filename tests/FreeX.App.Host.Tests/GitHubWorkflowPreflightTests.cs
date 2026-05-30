using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class GitHubWorkflowPreflightTests
{
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
        script.Should().Contain("Validated $($workflows.Count) GitHub workflow file(s).");
    }

    [Fact]
    public void GitHubWorkflowPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GitHubWorkflows.ps1");

        var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), "");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("GitHub workflow file(s).");
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenJobOmitsTimeout()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-workflow-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "broken.yml"),
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

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-WorkflowDirectory \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("must declare timeout-minutes");
            (result.Output + result.Error).Should().Contain("broken.yml");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenUploadArtifactOmitsMissingFilePolicy()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-workflow-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "broken.yml"),
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

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-WorkflowDirectory \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("actions/upload-artifact steps must set if-no-files-found to error or warn");
            (result.Output + result.Error).Should().Contain("broken.yml");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenCheckoutPersistsCredentials()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-workflow-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "broken.yml"),
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

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-WorkflowDirectory \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("actions/checkout steps must set persist-credentials: false");
            (result.Output + result.Error).Should().Contain("broken.yml");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenWorkflowUsesSelfHostedRunner()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-workflow-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "broken.yml"),
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

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-WorkflowDirectory \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("workflow must not use self-hosted runners");
            (result.Output + result.Error).Should().Contain("broken.yml");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenWorkflowUsesPullRequestTarget()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-workflow-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "broken.yml"),
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

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-WorkflowDirectory \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("workflow must not use the privileged pull_request_target event");
            (result.Output + result.Error).Should().Contain("broken.yml");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenWorkflowRequestsWriteAllPermissions()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-workflow-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "broken.yml"),
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

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-WorkflowDirectory \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("workflow must not request write-all permissions");
            (result.Output + result.Error).Should().Contain("broken.yml");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenRunStepOmitsShell()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-workflow-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "broken.yml"),
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

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-WorkflowDirectory \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("must declare an explicit shell");
            (result.Output + result.Error).Should().Contain("Missing shell");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsWhenLocalActionEscapesWorkspace()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-workflow-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "broken.yml"),
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

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-WorkflowDirectory \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("must stay within the workflow workspace");
            (result.Output + result.Error).Should().Contain("./../outside-action");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void GitHubWorkflowPreflight_FailsForFloatingActionReference()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-workflow-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "broken.yml"),
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

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-WorkflowDirectory \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("GitHub workflow validation failed");
            (result.Output + result.Error).Should().Contain("actions/checkout@main");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static PowerShellResult RunPowerShellScript(string scriptPath, string workingDirectory, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start().Should().BeTrue();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new PowerShellResult(process.ExitCode, output, error);
    }

    private sealed record PowerShellResult(int ExitCode, string Output, string Error);
}
