using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r380: the parity runner must not report a gate it never ran as PASS.
///
/// <para>`FreeX.ParityCompare` compares a Windows capture against a Linux one. The name-box contract
/// is a PAIR contract, so a single-side run (`--win-only` / `--linux-only`) cannot evaluate it at
/// all -- and the runner hard-coded <c>new NameBoxDropdownPairContractResult(true, [])</c> for that
/// case, printing "name-box pair : PASS". A gate announcing success for work it did not do is the
/// same shape as r353's assertions that could not fail and r360's backend probe, and it is worse
/// here because the output is read as evidence that Linux parity holds.</para>
///
/// <para>Asserted on the SOURCE rather than by running the tool, because the tool needs two real
/// captures and a Linux container. That is a weaker test than executing it, and the weakness is
/// stated rather than hidden: it pins the two things that made the old behaviour wrong -- the
/// single-side result is constructed as not-evaluated, and the report distinguishes the three
/// states.</para>
/// </summary>
public sealed class R380_ParityNameBoxGateReportsNotEvaluatedTests
{
    private static string RunnerSource() =>
        File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
            "tools", "FreeX.ParityCompare", "Program.cs"));

    [Fact]
    public void ASingleSideRunConstructsTheContractResultAsNotEvaluated()
    {
        RunnerSource().Should().MatchRegex(
            @"opts\.WinOnly \|\| opts\.LinuxOnly[\s\S]{0,200}?WasEvaluated:\s*false",
            "a single-side run cannot evaluate a pair contract, so it must not claim a result");
    }

    [Fact]
    public void TheReportDistinguishesNotEvaluatedFromPass()
    {
        var source = RunnerSource();

        source.Should().Contain("NOT EVALUATED",
            "the reader has to be able to tell an unrun gate from a passing one");

        var nameBoxLine = source
            .Split('\n')
            .Single(line => line.Contains("name-box pair :", StringComparison.Ordinal));

        nameBoxLine.Should().Contain("WasEvaluated",
            "the printed status must be driven by whether the contract ran, not only by IsValid");
    }

    [Fact]
    public void TheFullComparisonPathStillEvaluatesTheContract()
    {
        // The fix must not turn the gate off for the run that matters: a two-sided comparison still
        // calls Validate. Without this, "report not-evaluated" could be satisfied by never
        // evaluating anything.
        RunnerSource().Should().Contain(
            "NameBoxDropdownPairContract.Validate(winManifest, linManifest, winDir, linDir)",
            "a two-sided run must still run the real contract");
    }
}
