using Free.Shared.Ribbon.KeyTips;

namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonKeyTipResolutionPlannerTests
{
    private sealed record Candidate(string? KeyTip, bool IsEnabled = true);

    [Fact]
    public void Resolve_DefersExactLeafWhenEnabledLongerPrefixExists()
    {
        var result = Resolve("B", new Candidate("B"), new Candidate("BI"));

        result.Kind.Should().Be(RibbonKeyTipResolutionKind.Prefix);
        result.ExactIndex.Should().Be(-1);
    }

    [Fact]
    public void Resolve_ExecutesUniqueExactLeafImmediately()
    {
        var result = Resolve("B", new Candidate("B"), new Candidate("C"));

        result.Kind.Should().Be(RibbonKeyTipResolutionKind.Exact);
        result.ExactIndex.Should().Be(0);
    }

    [Fact]
    public void Resolve_IgnoresDisabledCandidatesWhenChoosingPrefix()
    {
        var result = Resolve("B", new Candidate("B"), new Candidate("BI", IsEnabled: false));

        result.Kind.Should().Be(RibbonKeyTipResolutionKind.Exact);
        result.ExactIndex.Should().Be(0);
    }

    [Fact]
    public void Resolve_RejectsDisabledExactAndUnmatchedInput()
    {
        Resolve("B", new Candidate("B", IsEnabled: false))
            .Kind.Should().Be(RibbonKeyTipResolutionKind.NoMatch);
        Resolve("Q", new Candidate("B"))
            .Kind.Should().Be(RibbonKeyTipResolutionKind.NoMatch);
    }

    [Fact]
    public void Resolve_ReturnsPrefixForAnIncompleteSequence()
    {
        var result = Resolve("B", new Candidate("BI"));

        result.Kind.Should().Be(RibbonKeyTipResolutionKind.Prefix);
        result.ExactIndex.Should().Be(-1);
    }

    [Fact]
    public void Resolve_CanPreserveImmediateExactLeafForLongerLeafCandidates()
    {
        var result = RibbonKeyTipResolutionPlanner.Resolve(
            new[] { new Candidate("CI"), new Candidate("CIR") },
            "CI",
            candidate => candidate.KeyTip,
            candidate => candidate.IsEnabled,
            longerPrefixSelector: _ => false);

        result.Kind.Should().Be(RibbonKeyTipResolutionKind.Exact);
        result.ExactIndex.Should().Be(0);
    }

    [Fact]
    public void Resolve_DefersExactLeafForLongerComboBoxAccessKey()
    {
        RibbonControl[] controls =
        [
            new RibbonButton(new RibbonCommandId("exact-leaf"), "Exact leaf") { KeyTip = "FO" },
            new RibbonComboBox(new RibbonCommandId("font-family"), "Font family") { KeyTip = "FON" },
        ];

        var result = RibbonKeyTipResolutionPlanner.Resolve(
            controls,
            "FO",
            control => control.KeyTip,
            longerPrefixSelector: control =>
                control is RibbonDropdown or RibbonSplitButton or RibbonComboBox);

        result.Kind.Should().Be(RibbonKeyTipResolutionKind.Prefix);
        result.ExactIndex.Should().Be(-1);
    }

    [Fact]
    public void Resolve_DenseCandidatesEvaluatesEachCandidateOnce()
    {
        var candidates = Enumerable.Range(0, 1_000)
            .Select(index => new Candidate(index == 999 ? "ABZ" : $"K{index}"))
            .ToArray();
        var keyTipSelections = 0;
        var enabledSelections = 0;

        var result = RibbonKeyTipResolutionPlanner.Resolve(
            candidates,
            "AB",
            candidate =>
            {
                keyTipSelections++;
                return candidate.KeyTip;
            },
            candidate =>
            {
                enabledSelections++;
                return candidate.IsEnabled;
            });

        result.Kind.Should().Be(RibbonKeyTipResolutionKind.Prefix);
        keyTipSelections.Should().Be(candidates.Length);
        enabledSelections.Should().Be(candidates.Length);
    }

    [Fact]
    public void Resolve_SourceGuardUsesOneIndexedPassWithoutMaterializingCandidates()
    {
        var source = File.ReadAllText(TestWorkspaceFileLocator.Find(
            "shared/Free.Shared.Ribbon/KeyTips/RibbonKeyTipResolutionPlanner.cs"));

        source.Should().Contain("for (var index = 0; index < candidates.Count; index++)")
            .And.Contain("var startsWithSequence = keyTip.StartsWith(normalizedSequence")
            .And.NotContain(".ToArray()")
            .And.NotContain("normalized.FirstOrDefault")
            .And.NotContain("normalized.Any");
    }

    private static RibbonKeyTipResolution Resolve(string sequence, params Candidate[] candidates) =>
        RibbonKeyTipResolutionPlanner.Resolve(
            candidates,
            sequence,
            candidate => candidate.KeyTip,
            candidate => candidate.IsEnabled);
}
