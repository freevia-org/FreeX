using System.Diagnostics;
using System.Text;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class LinuxInteractionRunnerSessionBindingTests
{
    [Fact]
    public void DockerRunner_WritesAtomicPerInvocationMetadata_WhileKeepingInteractivePointer()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("tools", "Run-LinuxInteractiveDocker.ps1"));

        source.Should().Contain("[string]$SessionMetadataPath");
        source.Should().Contain("function Write-SessionMetadata");
        source.Should().Contain("[IO.File]::WriteAllText($temporaryPath");
        source.Should().Contain("Move-Item -LiteralPath $temporaryPath -Destination $Path -Force");
        source.Should().Contain("sessionId = [guid]::NewGuid().ToString(\"N\")");
        source.Should().Contain("currentSessionPath");
        source.Should().Contain("Session metadata: $sessionMetadataOutputPath");
    }

    [Fact]
    public void FreeXValidation_BindsEveryStartAndReadsExpectedBatchIdentity()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("tools", "Run-FreeXLinuxInteractionValidation.ps1"));

        source.Should().Contain("function Start-ValidationSession");
        source.Should().Contain("SessionMetadataPath = $metadataPath");
        source.Should().NotContain("$currentSessionPath");
        source.Should().Contain("manifest changed before it became stable");
        source.Should().Contain("Validate-InteractionManifest");
        source.Should().Contain("ExpectedSection");
        source.Should().Contain("ExpectedContextMenuDispatchStart");
        source.Should().Contain("Timed out waiting for a stable interaction manifest");
        source.Should().Contain("length=$lastLength");
        source.Should().Contain("parseError=$lastError");

        CountOccurrences(source, "-ReusePublishedPayload")
            .Should().Be(4);
    }

    [Fact]
    public void ManifestReader_AcceptsStableExpectedIdentity_AndReportsRejectedIdentityOnTimeout()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var powershell = ResolvePowerShellExecutable();
        powershell.Should().NotBeNull("the focused Windows runner test requires PowerShell");

        using var temporary = new TestTemporaryDirectory();
        var sourcePath = RepositoryFileLocator.Find("tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probePath = System.IO.Path.Combine(temporary.Path, "manifest-reader-probe.ps1");
        var manifestPath = System.IO.Path.Combine(temporary.Path, "interaction-validation.json");
        File.WriteAllText(probePath, ProbeSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(manifestPath, "{\"validationSection\":\"context-menus\",\"contextMenuDispatchStart\":5}");

        var accepted = RunPowerShellProbe(powershell!, probePath, sourcePath, manifestPath, "accept");
        accepted.ExitCode.Should().Be(0, accepted.Output);
        accepted.Output.Should().Contain("accepted=context-menus");

        var rejected = RunPowerShellProbe(powershell!, probePath, sourcePath, manifestPath, "reject");
        rejected.ExitCode.Should().Be(9, rejected.Output);
        rejected.Output.Should().Contain("Timed out waiting for a stable interaction manifest");
        rejected.Output.Should().Contain("length=");
        rejected.Output.Should().Contain("parseError=");
        rejected.Output.Should().Contain(manifestPath);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string? ResolvePowerShellExecutable()
    {
        foreach (var candidate in new[] { "pwsh.exe", "powershell.exe" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "-NoProfile -NonInteractive -Command \"exit 0\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process is not null)
                {
                    if (process.WaitForExit(5000) && process.HasExited)
                        return candidate;
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit();
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Try the next installed PowerShell host.
            }
        }
        return null;
    }

    private static ProbeResult RunPowerShellProbe(
        string executable,
        string probePath,
        string sourcePath,
        string manifestPath,
        string mode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(probePath);
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add(manifestPath);
        startInfo.ArgumentList.Add(mode);

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull();
        var output = process!.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        var completed = process.WaitForExit(10000);
        if (!completed && !process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        completed.Should().BeTrue("the PowerShell probe must remain bounded");
        return new ProbeResult(process.ExitCode, output + error);
    }

    private sealed record ProbeResult(int ExitCode, string Output);

    private const string ProbeSource = """
param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$Mode
)

$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($SourcePath, [ref]$tokens, [ref]$parseErrors)
$functionAst = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq "Read-CompletedJsonManifest"
}, $true)
if ($null -eq $functionAst) { throw "Read-CompletedJsonManifest was not found." }
. ([scriptblock]::Create($functionAst.Extent.Text))

function Validate-InteractionManifest {
    param(
        $Manifest,
        [string]$Section,
        [bool]$IncludeCoreResults,
        [bool]$RibbonOnly,
        [int]$DialogStart,
        [int]$DialogCount,
        [int]$RibbonCommandStart,
        [int]$RibbonCommandCount,
        [int]$ContextMenuDispatchStart,
        [int]$ContextMenuDispatchCount,
        [bool]$RequireRunnerMetadata
    )
    if ([string]$Manifest.validationSection -ne $Section -or
        [int]$Manifest.contextMenuDispatchStart -ne $ContextMenuDispatchStart) {
        throw "identity mismatch"
    }
}

$expectedStart = if ($Mode -eq "accept") { 5 } else { 4 }
try {
    $manifest = Read-CompletedJsonManifest -Path $ManifestPath -Deadline (Get-Date).AddSeconds(1) `
        -ExpectedSection "context-menus" -ExpectedContextMenuDispatchStart $expectedStart
    Write-Output "accepted=$($manifest.validationSection)"
    exit 0
}
catch {
    Write-Output $_.Exception.Message
    exit 9
}
""";
}
