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
