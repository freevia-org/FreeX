using System.Text.RegularExpressions;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// Guards the single autosave recovery contract. FreeW and FreeP bind the shared
/// <c>AutosaveApplicationProfile</c> layer with a profile instance; neither may re-declare the
/// recovery prompt-mode / restore-exception-policy enums, the ports record, the plan/offer/workflow
/// mirrors, or bridge them to the shared enums with an integer cast.
/// </summary>
public sealed class AutosaveRecoveryProfileDedupSourceTests
{
    private static readonly string[] SisterAppRoots = ["freew", "freep"];

    private static readonly Regex MirrorEnum = new(
        @"\benum\s+\w*(RecoveryPromptMode|RestoreExceptionPolicy|PromptMode)\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex MirrorType = new(
        @"\b(record|class|struct|record\s+struct)\s+(\w*AutosavePorts|\w*RecoveryOffer|\w*RecoveryWorkflowResult|AutosaveRecoveryPlan|AutosaveRecoveryText|AutosaveRecoveryPlanner|\w*RecoveryWorkflow)\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex IntBridgeCast = new(
        @"\(\s*Autosave\w*(PromptMode|ExceptionPolicy)\s*\)\s*\(\s*int\s*\)",
        RegexOptions.CultureInvariant);

    [Fact]
    public void FreeWAndFreePDoNotRedeclareRecoveryEnumsOrMirrorTypes()
    {
        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in ProductSourceFiles())
        {
            scanned++;
            var text = File.ReadAllText(file);
            foreach (var regex in new[] { MirrorEnum, MirrorType, IntBridgeCast })
            {
                foreach (Match match in regex.Matches(text))
                    offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        scanned.Should().BeGreaterThan(100, "the scan must actually reach the sister-app sources");
        offenders.Should().BeEmpty(
            "recovery prompt modes, exception policies, ports, plans, offers, and the sequencer live in "
            + "Free.Shared.AppServices; the apps only supply an AutosaveApplicationProfile");
    }

    [Fact]
    public void SisterAppAutosaveBindingsAreProfileInstancesOverTheSharedSession()
    {
        var freeW = Read("freew", "FreeW.App.Presentation", "Shell", "FreeWAutosaveSession.cs");
        var freeP = Read("freep", "FreeP.App.Presentation", "FreePAutosaveSession.cs");

        foreach (var source in new[] { freeW, freeP })
        {
            source.Should().Contain("new AutosaveRecoveryPromptText(");
            source.Should().Contain("AutosaveApplicationProfile<");
            source.Should().Contain(": AutosaveApplicationSession<");
            source.Should().NotContain("new AutosaveDocumentSession<");
            source.Should().NotContain("AutosaveRecoveryPlannerCore");
        }
    }

    /// <summary>
    /// The host orchestration (periodic loop / dispatcher timer, emergency fan-out registration,
    /// bounded dispatcher transaction, startup/manual recovery walk, recovery prompt dialog) lives ONCE
    /// per toolkit in <c>AvaloniaAutosaveHost</c> / <c>WpfAutosaveHost</c>. FreeW's and FreeP's
    /// <c>AutosaveAdapter</c> / <c>AutosaveCoordinator</c> are thin bindings that must not re-grow it.
    /// </summary>
    private static readonly string[] SharedHostOrchestrationTokens =
    [
        "AutosavePeriodicTaskLoop",
        "WpfAutosaveTimer",
        "EmergencySnapshotFanOut<",
        "AvaloniaBoundedDispatcherTransaction",
        "AvaloniaRecoveryPromptDialogComposer",
        "AvaloniaAutosaveRecoveryHost",
        "WpfAutosaveRecoveryHost",
        "AutosaveRecoveryWorkflow",
        "AutosaveRecoveryPromptMode",
    ];

    private static readonly Regex LocalRecoveryPromptDialog = new(
        @"\bclass\s+\w*RecoveryPromptDialog\b",
        RegexOptions.CultureInvariant);

    [Fact]
    public void FreeWAndFreePHostsDoNotRegrowLocalAutosaveOrchestration()
    {
        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in ProductSourceFiles())
        {
            scanned++;
            var text = File.ReadAllText(file);
            foreach (var token in SharedHostOrchestrationTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)}: {token}");
            }

            // The headless-test responder stand-in is the only RecoveryPromptDialog left, and it
            // lives in the test-support tree, never in shipping source.
            if (!file.Replace('\\', '/').Contains("/TestSupport/", StringComparison.Ordinal)
                && LocalRecoveryPromptDialog.IsMatch(text))
            {
                offenders.Add($"{Path.GetFileName(file)}: recovery prompt dialog class");
            }
        }

        scanned.Should().BeGreaterThan(100, "the scan must actually reach the sister-app sources");
        offenders.Should().BeEmpty(
            "the periodic loop, emergency fan-out, recovery walk and recovery prompt are owned by "
            + "Free.Shared.Shell.Avalonia / Free.Shared.Shell.Wpf; the apps only bind them");
    }

    [Theory]
    [InlineData("freew", "FreeW.App.Avalonia", "AutosaveAdapter.cs", "AvaloniaAutosaveHost<")]
    [InlineData("freep", "FreeP.App.Avalonia", "AutosaveAdapter.cs", "AvaloniaAutosaveHost<")]
    [InlineData("freew", "FreeW.App.Host", "AutosaveCoordinator.cs", "WpfAutosaveHost<")]
    [InlineData("freep", "FreeP.App.Host", "AutosaveCoordinator.cs", "WpfAutosaveHost<")]
    public void SisterAppHostBindingsConstructTheSharedHostOverTheirProfile(
        string app,
        string project,
        string file,
        string sharedHost)
    {
        var source = Read(app, project, file);

        source.Should().Contain("new " + sharedHost);
        source.Should().Contain("AutosaveProfile.Session");
        source.Should().Contain("AutosaveRecoveryRestore<");
        source.Split('\n').Length.Should().BeLessThan(
            160, "the binding is thin: ports, restore delegate, and callbacks only");
    }

    private static IEnumerable<string> ProductSourceFiles()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        foreach (var app in SisterAppRoots)
        {
            var directory = Path.Combine(root, app);
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/obj/", StringComparison.Ordinal)
                    || normalized.Contains("/bin/", StringComparison.Ordinal)
                    || normalized.Contains(".Tests/", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(parts));
}
