using System.Text.RegularExpressions;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ReleaseAutomationWorkflowTests
{
    [Fact]
    public void TesterReleaseWorkflow_BuildsTestsPublishesAndUploadsLatestExe()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "tester-release.yml");

        workflow.Should().Contain("workflow_dispatch:");
        workflow.Should().Contain("release_notes:");
        workflow.Should().Contain("include_macos_preview:");
        workflow.Should().Contain("Optionally attach matching macOS internal-preview artifacts");
        workflow.Should().Contain("default: false");
        workflow.Should().Contain("macos_preview_run_id:");
        workflow.Should().Contain("public_preview_candidate:");
        workflow.Should().Contain("accessibility_keyboard_only:");
        workflow.Should().Contain("accessibility_screen_reader:");
        workflow.Should().Contain("accessibility_uia_catalog:");
        workflow.Should().Contain("accessibility_known_issues:");
        workflow.Should().Contain("permissions:");
        workflow.Should().Contain("contents: write");
        workflow.Should().Contain("concurrency:");
        workflow.Should().Contain("group: tester-release");
        workflow.Should().Contain("cancel-in-progress: false");
        workflow.Should().NotContain("FORCE_JAVASCRIPT_ACTIONS_TO_NODE24");
        workflow.Should().Contain("actions/checkout@v6");
        workflow.Should().Contain("persist-credentials: false");
        workflow.Should().Contain("name: Validate latest release source");
        workflow.Should().Contain("$isMainRelease = $env:GITHUB_REF -eq \"refs/heads/main\"");
        workflow.Should().Contain("$isDailyReleaseBranch = $env:GITHUB_REF -like \"refs/heads/codex/daily-tester-release-*\"");
        workflow.Should().Contain("-not ($isMainRelease -or $isDailyReleaseBranch)");
        workflow.Should().Contain("Tester releases publish stable latest assets and must run from refs/heads/main or a codex/daily-tester-release-* branch.");
        workflow.Should().Contain("git fetch origin main:refs/remotes/origin/main --no-tags");
        workflow.Should().Contain("git merge-base --is-ancestor origin/main HEAD");
        workflow.Should().Contain("Daily tester release branches must contain the current origin/main commit.");
        workflow.Should().Contain("actions/setup-dotnet@v5");
        workflow.Should().Contain("timeout-minutes: 180");
        workflow.Should().Contain("name: Repository preflight");
        workflow.Should().Contain("powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\\Test-RepositoryPreflight.ps1");
        workflow.Should().Contain("dotnet build FreeX.slnx --configuration Release");
        workflow.Should().Contain("dotnet test FreeX.DefaultTests.slnx --configuration Release --no-build --logger \"trx;LogFileName=default-tests.trx\"");
        workflow.Should().Contain("dotnet test FreeX.UiTests.slnx --configuration Release --no-build --logger \"trx;LogFileName=ui-tests.trx\"");
        workflow.Should().NotContain("dotnet restore FreeX.slnx");
        workflow.Should().NotContain("--disable-build-servers");
        workflow.Should().NotContain("-p:UseSharedCompilation=false");
        workflow.Should().NotContain("-p:NodeReuse=false");
        workflow.Should().NotContain("/nr:false");
        workflow.Should().Contain("if: always()");
        workflow.Should().Contain("name: freex-${{ github.run_id }}-${{ github.run_attempt }}-test-results");
        workflow.Should().Contain("path: \"**/TestResults/*.trx\"");
        workflow.Should().Contain("if-no-files-found: warn");
        workflow.Should().Contain("tools/Publish-UserTestBuild.ps1");
        workflow.Should().Contain("-RuntimeIdentifier win-x64");
        workflow.Should().Contain("-PublishMode SingleFile");
        workflow.Should().Contain("Publish MSIX package");
        workflow.Should().Contain("secrets.FREEX_MSIX_CERTIFICATE_BASE64");
        workflow.Should().Contain("secrets.FREEX_MSIX_CERTIFICATE_PASSWORD");
        workflow.Should().Contain("vars.FREEX_MSIX_TIMESTAMP_URL");
        workflow.Should().Contain("$env:FREEX_MSIX_CERTIFICATE_PASSWORD = \"${{ secrets.FREEX_MSIX_CERTIFICATE_PASSWORD }}\"");
        workflow.Should().Contain("if (-not [string]::IsNullOrWhiteSpace($certificateBase64))");
        workflow.Should().Contain("$signParameters = @{}");
        workflow.Should().Contain("$signParameters.MsixCertificatePath = $certificatePath");
        workflow.Should().Contain("$signParameters.MsixTimestampUrl = $timestampUrl");
        workflow.Should().Contain("$signParameters.AllowUnsignedMsix = $true");
        workflow.Should().Contain("@signParameters");
        workflow.Should().NotContain("@signArgs");
        workflow.Should().NotContain("-MsixCertificatePassword\", $certificatePassword");
        workflow.Should().Contain("-PublishMode Msix");
        workflow.Should().Contain("FreeX-latest-win-x64.exe");
        workflow.Should().Contain("FreeX-latest-win-x64.exe.sha256");
        workflow.Should().Contain("FreeX-latest-win-x64.msix");
        workflow.Should().Contain("FreeX-latest-macos-arm64.zip");
        workflow.Should().Contain("FreeX-latest-macos-x64.zip");
        workflow.Should().Contain("include_macos_preview=true requires a successful macOS App Preview run");
        workflow.Should().Contain("$encodedBranchName = [System.Uri]::EscapeDataString($env:GITHUB_REF_NAME)");
        workflow.Should().Contain("gh api \"repos/$env:GITHUB_REPOSITORY/actions/workflows/macos-app.yml/runs?branch=$encodedBranchName&status=success&per_page=50\"");
        workflow.Should().Contain("gh run download $($macOsRun.id) --name $artifactName --dir $runtimeRoot");
        workflow.Should().Contain("attempt ${macOsRunAttempt}: $macOsRunUrl");
        workflow.Should().NotContain("attempt $macOsRunAttempt: $macOsRunUrl");
        workflow.Should().Contain("function Find-DownloadedArtifactFile");
        workflow.Should().Contain("Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $FileName");
        workflow.Should().Contain("Find-DownloadedArtifactFile -Root $runtimeRoot -FileName \"freex-$runtime-macos-app.zip\" -ArtifactName $artifactName");
        workflow.Should().Contain("FreeX-latest-macos-$assetLabel.zip");
        workflow.Should().Contain("FreeX-latest-macos-$assetLabel-instructions.md");
        workflow.Should().Contain("FreeX-latest-macos-$assetLabel-evidence.txt");
        workflow.Should().Contain("actions/upload-artifact@v7");
        workflow.Should().Contain("gh release create");
        workflow.Should().Contain("gh release upload");
        workflow.Should().Contain("$runNumber = [int]$env:GITHUB_RUN_NUMBER");
        workflow.Should().Contain("$runAttempt = [int]$env:GITHUB_RUN_ATTEMPT");
        workflow.Should().Contain("$progressPath = \"release/progress.json\"");
        workflow.Should().Contain("$releaseProgress = Get-Content -LiteralPath $progressPath -Raw | ConvertFrom-Json");
        workflow.Should().Contain("$overallCompletion = [int]$releaseProgress.overallCompletion");
        workflow.Should().Contain("$releasePatchBase = [int]$releaseProgress.releasePatchBase");
        workflow.Should().Contain("$channel = [string]$releaseProgress.channel");
        workflow.Should().Contain("release/progress.json major must be non-negative.");
        workflow.Should().Contain("release/progress.json overallCompletion must be between 0 and 100.");
        workflow.Should().Contain("release/progress.json releasePatchBase must be non-negative.");
        workflow.Should().Contain("Unsupported release channel '$channel'.");
        workflow.Should().Contain("elseif ($overallCompletion -ge 93) { $minor = 7 }");
        workflow.Should().Contain("elseif ($overallCompletion -ge 90) { $minor = 6 }");
        workflow.Should().Contain("$versionLabel = \"$major.$minor.$releasePatch\"");
        workflow.Should().Contain("Release version must be a single line.");
        workflow.Should().Contain("$releaseStamp = Get-Date -AsUTC -Format \"yyyy-MM-dd-HH-mm-ss\"");
        workflow.Should().Contain("$releaseId = \"$versionSlug-$releaseStamp-run$runNumber-attempt$runAttempt\"");
        workflow.Should().Contain("$tag = \"v$releaseId+$shortSha\"");
        workflow.Should().Contain("$displayVersion = $versionLabel.Trim()");
        workflow.Should().Contain("$releaseName = \"FreeX (Test Release) $displayVersion ($releaseStamp) Run $runNumber Attempt $runAttempt ($shortSha)\"");
        workflow.Should().Contain("\"release_id=$releaseId\" >> $env:GITHUB_OUTPUT");
        workflow.Should().Contain("name: freex-${{ steps.meta.outputs.release_id }}-${{ steps.meta.outputs.short_sha }}-win-x64-singlefile");
        workflow.Should().Contain("name: freex-${{ steps.meta.outputs.release_id }}-${{ steps.meta.outputs.short_sha }}-win-x64-msix");
        workflow.Should().Contain("name: freex-${{ steps.meta.outputs.release_id }}-${{ steps.meta.outputs.short_sha }}-win-x64-singlefile-sha256");
        workflow.Should().Contain("path: artifacts/upload/freex-*-win-x64-msix.msix");
        workflow.Should().Contain("path: artifacts/upload/*.exe.sha256");
        workflow.Should().Contain("path: artifacts/upload/*.msix.sha256");
        workflow.Should().Contain("$assetPaths = @(");
        workflow.Should().Contain("\"artifacts/upload/*.exe.sha256\"");
        workflow.Should().Contain("\"artifacts/upload/*.msix.sha256\"");
        workflow.Should().Contain("\"artifacts/upload/*.zip\"");
        workflow.Should().Contain("\"artifacts/upload/*.zip.sha256\"");
        workflow.Should().Contain("\"artifacts/upload/FreeX-latest-macos-*-instructions.md\"");
        workflow.Should().Contain("\"artifacts/upload/FreeX-latest-macos-*-evidence.txt\"");
        workflow.Should().Contain("gh release create $tag @assetPaths --target $env:GITHUB_SHA --title $title --notes $notes --draft @prereleaseArgs");
        workflow.Should().Contain("gh release edit $tag --draft=false @latestArgs");
        workflow.Should().Contain("gh release upload $tag @assetPaths --clobber");
        workflow.Should().NotContain("gh release create $tag --target $env:GITHUB_SHA --title $title --notes $notes @prereleaseArgs");
        workflow.Should().Contain("$latestArgs += \"--latest\"");
        workflow.Should().Contain("Additional tester notes:");
        workflow.Should().Contain("FREEX_RELEASE_NOTES: ${{ inputs.release_notes }}");
        workflow.Should().Contain("$extraNotes = $env:FREEX_RELEASE_NOTES");
        workflow.Should().Contain("Windows tester steps:");
        workflow.Should().Contain("Get-FileHash .\\FreeX-latest-win-x64.exe -Algorithm SHA256");
        workflow.Should().Contain("Windows SmartScreen warns about an unknown publisher");
        workflow.Should().Contain("macOS tester downloads:");
        workflow.Should().Contain("shasum -a 256 -c FreeX-latest-macos-arm64.zip.sha256");
        workflow.Should().Contain("Control-click or right-click > Open");
        workflow.Should().Contain("System Settings > Privacy & Security");
        workflow.Should().Contain("Do not disable Gatekeeper globally");
        workflow.Should().Contain("This is an internal preview while signing certificates are pending");
        workflow.Should().Contain("Public-preview accessibility gate:");
        workflow.Should().Contain("$publicPreviewCandidate = \"${{ inputs.public_preview_candidate }}\" -eq \"true\"");
        workflow.Should().Contain("\"Keyboard-only smoke validation\" = \"${{ inputs.accessibility_keyboard_only }}\" -eq \"true\"");
        workflow.Should().Contain("\"Screen-reader smoke validation\" = \"${{ inputs.accessibility_screen_reader }}\" -eq \"true\"");
        workflow.Should().Contain("\"UI Automation catalog review\" = \"${{ inputs.accessibility_uia_catalog }}\" -eq \"true\"");
        workflow.Should().Contain("\"Known accessibility issues reviewed/listed\" = \"${{ inputs.accessibility_known_issues }}\" -eq \"true\"");
        workflow.Should().Contain("Public-preview promotion requires completed accessibility gate inputs");
        workflow.Should().Contain("Keyboard-only smoke validation: $keyboardOnlyStatus.");
        workflow.Should().Contain("Screen-reader smoke validation: $screenReaderStatus.");
        workflow.Should().Contain("UI Automation catalog review: $uiaCatalogStatus.");
        workflow.Should().Contain("Known accessibility issues reviewed/listed: $knownIssuesStatus.");
        workflow.Should().Contain("this build is public-preview eligible");
        workflow.Should().Contain("This build is internal-only unless release notes separately document a completed public-preview accessibility gate.");
    }

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
    public void TesterReleaseWorkflow_DefaultsToStableReleaseWhenAdvertisingLatestDownload()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "tester-release.yml");

        workflow.Should().Contain("Download the stable latest asset: FreeX-latest-win-x64.exe");
        workflow.Should().Contain("Checksum for the latest single-file asset: FreeX-latest-win-x64.exe.sha256");
        workflow.Should().Contain("MSIX package: FreeX-latest-win-x64.msix");
        workflow.Should().Contain("signed when the release certificate secret is configured");
        workflow.Should().Contain("published unsigned for tester continuity");
        workflow.Should().Contain("macOS internal-preview assets are attached from macOS App Preview run");

        var prereleaseInput = Regex.Match(workflow, @"(?ms)^\s+prerelease:\s*$.*?^\s+type:\s+boolean\s*$");
        prereleaseInput.Success.Should().BeTrue("the workflow should expose a prerelease dispatch input");
        prereleaseInput.Value.Should().Contain(
            "default: false",
            "GitHub releases/latest excludes prereleases, so the advertised stable latest asset must be backed by stable releases by default");
    }

    [Fact]
    public void TesterReleaseWorkflow_RefreshesReleaseNotesWhenReleaseAlreadyExists()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "tester-release.yml");
        var existingReleaseBlock = Regex.Match(workflow, @"(?ms)if \(\$releaseExists\) \{.*?\} else \{");

        existingReleaseBlock.Success.Should().BeTrue("the rerun path should be explicit and guarded separately from first release creation");
        existingReleaseBlock.Value.Should().Contain("gh release upload $tag @assetPaths --clobber");
        existingReleaseBlock.Value.Should().Contain("gh release edit $tag --title $title --notes $notes @prereleaseArgs @latestArgs");
        existingReleaseBlock.Value.Should().NotContain("gh release edit $tag --title $title @prereleaseArgs @latestArgs");
    }

    [Fact]
    public void AppTesterReleaseWorkflow_UsesOneAppVersionTagAndIndependentPlatformPackages()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "app-tester-release.yml");
        var publisher = WorkspaceFileLocator.ReadAllText("tools", "Publish-SisterAppTesterPackages.ps1");
        var expectedLanes = new[]
        {
            """@{ app = "FreeX"; platform = "windows"; runtime = "win-x64"; runner = "windows-latest" }""",
            """@{ app = "FreeX"; platform = "linux"; runtime = "linux-x64"; runner = "ubuntu-latest" }""",
            """@{ app = "FreeX"; platform = "linux"; runtime = "linux-arm64"; runner = "ubuntu-24.04-arm" }""",
            """@{ app = "FreeX"; platform = "macos"; runtime = "osx-x64"; runner = "macos-15-intel" }""",
            """@{ app = "FreeX"; platform = "macos"; runtime = "osx-arm64"; runner = "macos-15" }""",
            """@{ app = "FreeW"; platform = "windows"; runtime = "win-x64"; runner = "windows-latest" }""",
            """@{ app = "FreeW"; platform = "linux"; runtime = "linux-x64"; runner = "ubuntu-latest" }""",
            """@{ app = "FreeW"; platform = "linux"; runtime = "linux-arm64"; runner = "ubuntu-24.04-arm" }""",
            """@{ app = "FreeW"; platform = "macos"; runtime = "osx-x64"; runner = "macos-15-intel" }""",
            """@{ app = "FreeW"; platform = "macos"; runtime = "osx-arm64"; runner = "macos-15" }""",
            """@{ app = "FreeP"; platform = "windows"; runtime = "win-x64"; runner = "windows-latest" }""",
            """@{ app = "FreeP"; platform = "linux"; runtime = "linux-x64"; runner = "ubuntu-latest" }""",
            """@{ app = "FreeP"; platform = "linux"; runtime = "linux-arm64"; runner = "ubuntu-24.04-arm" }""",
            """@{ app = "FreeP"; platform = "macos"; runtime = "osx-x64"; runner = "macos-15-intel" }""",
            """@{ app = "FreeP"; platform = "macos"; runtime = "osx-arm64"; runner = "macos-15" }"""
        };

        workflow.Should().Contain("name: App Tester Release");
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

        workflow.Should().Contain("needs: [prepare, verify]");
        workflow.Should().Contain("fromJSON(needs.prepare.outputs.package_matrix)");
        workflow.Should().Contain("$isFullReleaseBranch = $env:GITHUB_REF -like \"refs/heads/codex/full-release-*\"");
        workflow.Should().Contain("git fetch origin main:refs/remotes/origin/main --no-tags");
        workflow.Should().Contain("git merge-base --is-ancestor origin/main HEAD");
        workflow.Should().Contain("Full release branches must contain the current origin/main commit.");
        workflow.Should().Contain("function Invoke-Dotnet");
        workflow.Should().Contain("throw \"dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE.\"");
        workflow.Should().Contain("Invoke-Dotnet build FreeX.slnx --configuration Release");
        workflow.Should().Contain("FullyQualifiedName~ReleaseAutomationWorkflowTests");
        workflow.Should().Contain("FullyQualifiedName~TesterReleaseSmokeTests");
        workflow.Should().Contain("FullyQualifiedName~RibbonNativeRegistryTests");
        workflow.Should().Contain("DeclarativeHomeMenuChoices_AreEnabledAcrossFormattingFamilies");
        workflow.Should().Contain("FullyQualifiedName~BorderRenderTests");
        workflow.Should().Contain("FullyQualifiedName~R74_SlantDashDotBorderThicknessTests");
        workflow.Should().Contain("FullyQualifiedName~BorderStrokePixelSnapperTests");
        workflow.Should().Contain("FullyQualifiedName~CellBorderPanelNeighborResolutionTests");
        workflow.Should().Contain("Invoke-Dotnet build FreeW.slnx --configuration Release");
        workflow.Should().Contain("freew/FreeW.Core.Model.Tests/FreeW.Core.Model.Tests.csproj");
        workflow.Should().Contain("freew/FreeW.Core.IO.Tests/FreeW.Core.IO.Tests.csproj");
        workflow.Should().Contain("freew/FreeW.Ribbon.Definitions.Tests/FreeW.Ribbon.Definitions.Tests.csproj");
        workflow.Should().Contain("freew/FreeW.App.Localization.Tests/FreeW.App.Localization.Tests.csproj");
        workflow.Should().Contain("FullyQualifiedName~PackagingSmokeTests|FullyQualifiedName~SharedLaunchSmokeBootstrapTests");
        workflow.Should().Contain("Invoke-Dotnet build FreeP.slnx --configuration Release");
        workflow.Should().Contain("Invoke-Dotnet test FreeP.slnx");
        workflow.Should().Contain("-Runtimes \"${{ matrix.runtime }}\"");
        workflow.Should().Contain("-WindowsPackageMode SingleFile");
        publisher.Should().Contain("AvaloniaValidationProject = \"tools\\FreeX.Validation.Avalonia\\FreeX.Validation.Avalonia.csproj\"");
        publisher.Should().Contain("AvaloniaValidationHost = \"FreeX.Validation.Avalonia\"");
        publisher.Should().Contain("if (-not $isWindowsRuntime) {");
        workflow.Should().Contain("$tag = \"$($app.ToLowerInvariant())-v$version\"");
        workflow.Should().Contain("## Install and deploy");
        workflow.Should().Contain("### Windows x64");
        workflow.Should().Contain("### Linux x64 and ARM64");
        workflow.Should().Contain("### macOS Intel and Apple silicon");
        workflow.Should().Contain("unsigned portable archives, not signed or notarized `.app` bundles");
        workflow.Should().Contain("$notes = @'");
        workflow.Should().Contain("sha256sum -c {{APP}}-v{{VERSION}}-linux-<architecture>.zip.sha256");
        workflow.Should().Contain("shasum -a 256 -c {{APP}}-v{{VERSION}}-osx-<architecture>.zip.sha256");
        workflow.Should().Contain("Replace(\"{{SHA}}\", $env:GITHUB_SHA)");
        workflow.Should().Contain("$notes | gh release edit $tag --title $title --notes-file -");
        workflow.Should().Contain("$notes | gh release create $tag @assets --target $env:GITHUB_SHA --title $title --notes-file - @releaseArgs");
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
        publisher.Should().Contain("has no packaged smoke entry point; the release gate uses its compiled test suite.");
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
        plan.Should().Contain("https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.exe");
        plan.Should().Contain("https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-macos-arm64.zip");
        plan.Should().Contain("https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-macos-x64.zip");
        plan.Should().Contain("GitHub's `releases/latest` redirect remains on the latest non-prerelease tester build");
        plan.Should().Contain("FREEX_MSIX_CERTIFICATE_BASE64");
        plan.Should().Contain("publishes an unsigned MSIX for tester continuity");
        plan.Should().Contain("Installer trust validation and Store-style submission remain release-gate work.");
    }
}
