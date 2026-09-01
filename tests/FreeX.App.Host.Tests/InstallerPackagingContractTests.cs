using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class InstallerPackagingContractTests
{
    [Fact]
    public void CanonicalWorkflow_PreservesPortableAssetsAndBuildsIndividualAndSuiteInstallers()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "full-release.yml");

        workflow.Should().Contain("tools/Publish-WindowsVelopackPackage.ps1");
        workflow.Should().Contain("tools/packaging/New-AppInstallers.ps1");
        workflow.Should().Contain("tools/New-ReleaseArtifactManifest.ps1");
        workflow.Should().Contain("tools/New-ReleaseSbom.ps1");
        workflow.Should().Contain("artifacts/suite/FreeSuite-v${{ inputs.release_version }}-${{ matrix.runtime }}.spdx.json");
        workflow.Should().Contain("artifacts/suite/FreeSuite-v${{ inputs.release_version }}-${{ matrix.runtime }}.spdx.json.sha256");
        workflow.Should().Contain("tools/Test-ReleaseInstallation.ps1");
        workflow.Should().Contain("Microsoft.Sbom.DotNetTool --version 4.1.5");
        workflow.Should().Contain("tools/Test-ReleasePackageContents.ps1");
        workflow.Should().Contain("-Configuration Release");
        workflow.Should().Contain("-Apps \"${{ matrix.app }}\"");
        workflow.Should().Contain("-Apps FreeX,FreeW,FreeP");
        workflow.Should().Contain("-Suite");
        workflow.Should().Contain("free-suite-v$version");
        workflow.Should().Contain("signed/notarized/stapled `.app` bundle");
        workflow.Should().Contain("FreeFamilySentryDsn: ${{ secrets.FREE_FAMILY_SENTRY_DSN }}");
    }

    [Fact]
    public void WindowsVelopackPublisher_SignsPayloadBeforePackingAndVerifiesEveryExecutable()
    {
        var publisher = WorkspaceFileLocator.ReadAllText("tools", "Publish-WindowsVelopackPackage.ps1");

        var payloadSigning = publisher.IndexOf("-Files $standalonePath,$mainExecutable", StringComparison.Ordinal);
        var velopackPacking = publisher.IndexOf("& $VpkPath @vpkArguments", StringComparison.Ordinal);

        payloadSigning.Should().BeGreaterThan(-1);
        payloadSigning.Should().BeLessThan(velopackPacking,
            "the app-host executable must be signed before Velopack embeds it in release packages");
        publisher.Should().Contain("-Files $standalonePath,$mainExecutable,$setupPath");
        publisher.Should().Contain("-VerifyOnly");
    }

    [Fact]
    public void ReleaseInstallationSmoke_UsesVelopackCanonicalLaunchPath()
    {
        var smoke = WorkspaceFileLocator.ReadAllText("tools", "Test-ReleaseInstallation.ps1");

        smoke.Should().Contain("Join-Path (Join-Path (Join-Path $Root $App) 'current')");
        smoke.Should().NotContain("Get-ChildItem -LiteralPath $Root -Recurse -File -Filter \"$App.App.Host.exe\"");
    }

    [Fact]
    public void WindowsSuitePackager_ReturnsOnlyTheBootstrapperPath()
    {
        var packager = WorkspaceFileLocator.ReadAllText("tools", "packaging", "New-FreeSuiteWindowsBootstrapper.ps1");

        packager.Should().Contain("$publishOutput = @(& dotnet publish");
        packager.Should().Contain("$publishOutput | ForEach-Object { Write-Host $_ }");
        packager.Should().Contain("Write-Output $result");
    }

    [Fact]
    public void SuitePackages_DelegateToIndividualInstallerIdentities()
    {
        var packager = WorkspaceFileLocator.ReadAllText("tools", "packaging", "New-AppInstallers.ps1");

        packager.Should().Contain("The suite is deliberately a non-owning bootstrapper");
        packager.Should().Contain("$childName = \"$app-v$Version-$Runtime-setup.exe\"");
        packager.Should().Contain("ResultCode <> 0");
        packager.Should().Contain("RaiseException(DisplayName + '' installation failed with exit code ''");
        packager.Should().Contain("{param:TestInstallRoot|}");
        packager.Should().Contain("$inputName = if ($Suite) { \"$app-v$Version-$Runtime-installer.zip\"");
        packager.Should().Contain("Find-UniqueInput \"$app-v$Version-$Runtime-apps.zip\"");
        packager.Should().Contain("bash `\"`$temp_root/$app/install.sh`\"");
        packager.Should().Contain("bash `\"`$temp_root/$app/uninstall.sh`\"");
        packager.Should().Contain("Uninstallable=no");
    }

    [Fact]
    public void ReleaseWorkflow_EnforcesImmutableCommitAndFullInventory()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "full-release.yml");
        var manifest = WorkspaceFileLocator.ReadAllText("tools", "New-ReleaseArtifactManifest.ps1");

        workflow.Should().Contain("Tag '$tag' is immutable at $tagSha; refusing to replace it with assets from $env:GITHUB_SHA");
        workflow.Should().Contain("contents: write");
        workflow.Should().Contain("contents: read");
        manifest.Should().Contain("ValidatePattern('^[0-9a-fA-F]{40}$')");
        manifest.Should().Contain("RequireRuntimeManifests");
        manifest.Should().Contain("StageLegalBundle");
        manifest.Should().Contain("docs/legal/legal-notices.md");
        manifest.Should().Contain("kind = 'sbom'");
        manifest.Should().Contain("Checksum mismatch or non-canonical checksum content");
    }

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void ReleaseManifest_PrefersCanonicalRootArtifactsOverNestedWorkingCopies()
    {
        using var temp = new TestTemporaryDirectory();
        const string version = "1.2.3";
        const string prefix = "FreeW-v1.2.3-win-x64";
        var nestedStage = Path.Combine(temp.Path, ".sbom-FreeW-win-x64");
        Directory.CreateDirectory(nestedStage);

        WriteArtifactWithChecksum(temp.Path, $"{prefix}.exe", [0x4d, 0x5a, 0x01]);
        WriteArtifactWithChecksum(temp.Path, $"{prefix}-setup.exe", [0x4d, 0x5a, 0x02]);
        WriteArtifactWithChecksum(temp.Path, $"{prefix}.spdx.json", "{}"u8.ToArray());
        File.WriteAllBytes(Path.Combine(nestedStage, $"{prefix}.exe"), [0x00]);

        var outputPath = Path.Combine(temp.Path, $"{prefix}-manifest.json");
        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "New-ReleaseArtifactManifest.ps1",
            temp.Path,
            $"-Scope App -Apps FreeW -Version {version} -CommitSha {new string('a', 40)} " +
            $"-Runtimes win-x64 -InputRoot \"{temp.Path}\" -OutputPath \"{outputPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        using var manifest = JsonDocument.Parse(File.ReadAllText(outputPath));
        var portable = manifest.RootElement.GetProperty("artifacts").EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == $"{prefix}.exe");
        portable.GetProperty("size").GetInt64().Should().Be(3);
    }

    [RequiresExternalToolFact(ExternalToolPreconditions.PowerShell7)]
    public void ReleaseSbom_RemovesItsPayloadStagingDirectoryAfterGeneration()
    {
        using var temp = new TestTemporaryDirectory();
        var payloadPath = Path.Combine(temp.Path, "payload.bin");
        var outputPath = Path.Combine(temp.Path, "sample.spdx.json");
        File.WriteAllText(payloadPath, "payload");

        var fakeToolPath = Path.Combine(temp.Path, OperatingSystem.IsWindows() ? "fake-sbom.cmd" : "fake-sbom");
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(fakeToolPath,
                "@echo off\r\nset stage=\r\n:args\r\nif \"%~1\"==\"\" goto run\r\n" +
                "if \"%~1\"==\"-b\" (set \"stage=%~2\"& shift & shift & goto args)\r\n" +
                "shift\r\ngoto args\r\n:run\r\nmkdir \"%stage%\\_manifest\\spdx_2.2\"\r\n" +
                ">\"%stage%\\_manifest\\spdx_2.2\\manifest.spdx.json\" echo {}\r\n");
        }
        else
        {
            File.WriteAllText(fakeToolPath,
                "#!/bin/sh\nwhile [ \"$#\" -gt 0 ]; do\n  if [ \"$1\" = \"-b\" ]; then stage=$2; shift 2; else shift; fi\ndone\n" +
                "mkdir -p \"$stage/_manifest/spdx_2.2\"\nprintf '{}' > \"$stage/_manifest/spdx_2.2/manifest.spdx.json\"\n");
            File.SetUnixFileMode(fakeToolPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "New-ReleaseSbom.ps1",
            temp.Path,
            $"-Name Sample -Version 1.2.3 -CommitSha {new string('a', 40)} -Runtime win-x64 " +
            $"-InputRoot \"{temp.Path}\" -PayloadNames payload.bin -OutputPath \"{outputPath}\" " +
            $"-SbomToolPath \"{fakeToolPath}\" -RepositoryRoot \"{temp.Path}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        File.Exists(outputPath).Should().BeTrue();
        Directory.Exists(Path.Combine(temp.Path, ".sbom-Sample-win-x64")).Should().BeFalse();
    }

    [Fact]
    public void ReleaseArtifactLookup_RejectsAmbiguousNestedWrappersWhenNoRootArtifactExists()
    {
        using var temp = new TestTemporaryDirectory();
        var first = Path.Combine(temp.Path, "artifact-one");
        var second = Path.Combine(temp.Path, "artifact-two");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "sample.zip"), "one");
        File.WriteAllText(Path.Combine(second, "sample.zip"), "two");

        var supportPath = WorkspaceFileLocator.FindToolScript("ToolScriptSupport.ps1");
        var probePath = Path.Combine(temp.Path, "probe.ps1");
        File.WriteAllText(
            probePath,
            $"$ErrorActionPreference = 'Stop'\n. '{supportPath.Replace("'", "''")}'\n" +
            $"Find-ToolReleaseArtifact -InputRoot '{temp.Path.Replace("'", "''")}' -Name 'sample.zip'\n");

        var result = PowerShellScriptRunner.Run(probePath, temp.Path);

        result.ExitCode.Should().NotBe(0);
        result.NormalizedCombinedOutput.Should().Contain("found 2");
    }

    [Fact]
    public void PortablePublisher_EnforcesOptimizedReleasePayloadsWithoutDebugSidecars()
    {
        var publisher = WorkspaceFileLocator.ReadAllText("tools", "Publish-SisterAppTesterPackages.ps1");
        var contentGate = WorkspaceFileLocator.ReadAllText("tools", "Test-ReleasePackageContents.ps1");

        publisher.Should().Contain("[ValidateSet(\"Release\")]");
        publisher.Should().Contain("\"-p:DebugType=None\"");
        publisher.Should().Contain("\"-p:DebugSymbols=false\"");
        publisher.Should().Contain("\"-p:Optimize=true\"");
        contentGate.Should().Contain("Debug artifact");
        contentGate.Should().Contain("Standalone executable missing");
        contentGate.Should().Contain("Windows installer missing");
    }

    private static void WriteArtifactWithChecksum(string directory, string name, byte[] contents)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, contents);
        var hash = Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant();
        File.WriteAllText(path + ".sha256", $"{hash}  {name}");
    }
}
