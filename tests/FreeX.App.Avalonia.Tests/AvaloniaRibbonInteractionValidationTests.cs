using Avalonia.Headless;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.Ribbon;
using Xunit.Abstractions;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaRibbonInteractionValidationTests
{
    private const int ValidationBatchSize = 8;
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);
    private static InteractionValidationResult[]? _cachedResults;
    private readonly ITestOutputHelper _output;

    public AvaloniaRibbonInteractionValidationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task BehaviorEvidence_CoversExactVisibleCommandsAndPlacements()
    {
        await Session.Dispatch(() =>
        {
            var results = GetResults();
            var commands = results.Where(result => result.Category == "ribbon-command-behavior").ToArray();
            var placements = results.Where(result => result.Category == "ribbon-placement-behavior").ToArray();
            var allCommandIds = AvaloniaRibbonComposition
                .EnumerateSurfaceRows(AvaloniaRibbonComposition.BuildDefinition())
                .Select(row => row.CommandId.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var expectedCommandIds = allCommandIds
                .Take(ValidationBatchSize)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var actualCommandIds = commands
                .Select(result => Uri.UnescapeDataString(result.Id.Split('/')[1]))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(605, allCommandIds.Length);
            Assert.Equal(expectedCommandIds, actualCommandIds);
            Assert.Equal(ValidationBatchSize, commands.Length);
            Assert.Equal(ValidationBatchSize, commands.Select(result => result.Id).Distinct(StringComparer.Ordinal).Count());
            var selectedIds = expectedCommandIds.ToHashSet(StringComparer.Ordinal);
            var expectedPlacementCount = AvaloniaRibbonComposition
                .EnumerateSurfaceRows(AvaloniaRibbonComposition.BuildDefinition())
                .Count(row => selectedIds.Contains(row.CommandId.Value));
            Assert.Equal(expectedPlacementCount, placements.Length);
            Assert.Equal(expectedPlacementCount, placements.Select(result => result.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.All(commands, result => Assert.Contains(result.Status, new[] { "passed", "skipped", "failed" }));
            Assert.All(placements, result => Assert.Contains(result.Status, new[] { "passed", "skipped", "failed" }));
            Assert.DoesNotContain(results, result => result.EvidenceLevel == "registry-bound");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BehaviorEvidence_PassesOnlyObservedExecutionOrVerifiedDisabledState()
    {
        await Session.Dispatch(() =>
        {
            var results = GetResults();
            var commands = results.Where(result => result.Category == "ribbon-command-behavior").ToArray();
            var passed = commands.Where(result => result.Status == "passed").ToArray();
            var skipped = commands.Where(result => result.Status == "skipped").ToArray();
            var failed = commands.Where(result => result.Status == "failed").ToArray();
            var placements = results.Where(result => result.Category == "ribbon-placement-behavior").ToArray();
            var commandStatusById = commands.ToDictionary(result => result.Id["ribbon-command-behavior/".Length..], result => result.Status, StringComparer.Ordinal);
            var placementCommandById = AvaloniaRibbonComposition
                .EnumerateSurfaceRows(AvaloniaRibbonComposition.BuildDefinition())
                .ToDictionary(
                    row => $"ribbon-placement-behavior/{row.RowId}",
                    row => Uri.EscapeDataString(row.CommandId.Value),
                    StringComparer.Ordinal);

            Assert.All(passed, result => Assert.Contains(
                result.EvidenceLevel,
                new[] { "executed-production-lifecycle", "disabled-state-verified" }));
            Assert.DoesNotContain(passed, result =>
                result.EvidenceLevel.Contains("route-contract", StringComparison.Ordinal) ||
                result.EvidenceLevel.Contains("unexercised", StringComparison.Ordinal) ||
                result.EvidenceLevel.Contains("unverified", StringComparison.Ordinal));
            foreach (var group in commands.GroupBy(result => $"{result.Status}/{result.EvidenceLevel}").OrderBy(group => group.Key, StringComparer.Ordinal))
                _output.WriteLine($"{group.Key}: {group.Count()}");

            foreach (var result in failed)
                _output.WriteLine($"FAILED {result.Id}: {result.Note}");

            Assert.NotEmpty(passed.Concat(skipped));
            Assert.All(failed, result => Assert.Contains(
                result.EvidenceLevel,
                new[] { "unregistered-command", "empty-command-gap", "context-fixture-failed", "validation-window-route-missing", "validation-window-empty-command", "production-execution-threw" }));

            foreach (var placement in placements)
                Assert.Equal(commandStatusById[placementCommandById[placement.Id]], placement.Status);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BorderPickerMenuCommands_ObserveWpfCompatiblePickerState()
    {
        await Session.Dispatch(() =>
        {
            var commandIds = AvaloniaRibbonComposition
                .EnumerateSurfaceRows(AvaloniaRibbonComposition.BuildDefinition())
                .Select(row => row.CommandId.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var targetIds = new[] { "Black", "Gray", "Accent 1", "Accent 2", "Thin", "Medium", "Thick", "Dashed", "Dotted", "Double" };

            foreach (var targetId in targetIds)
            {
                var start = Array.IndexOf(commandIds, targetId);
                Assert.True(start >= 0, $"Ribbon command catalog is missing {targetId}.");

                var window = new MainWindow([]);
                try
                {
                    var results = new List<InteractionValidationResult>();
                    window.AddRibbonInteractionExecutionResults(results, start, 1);
                    var result = Assert.Single(results, item =>
                        item.Category == "ribbon-command-behavior" &&
                        Uri.UnescapeDataString(item.Id["ribbon-command-behavior/".Length..]) == targetId);
                    Assert.Equal("passed", result.Status);
                    Assert.Equal("executed-production-lifecycle", result.EvidenceLevel);
                    Assert.Contains("border-picker-state-changed", result.Evidence);
                }
                finally
                {
                    window.AllowCloseWithoutDirtyPromptForParityCapture();

                    window.Close();
                }
            }
        }, CancellationToken.None);
    }

    private static IReadOnlyList<InteractionValidationResult> GetResults()
    {
        if (_cachedResults is not null)
            return _cachedResults;

        var window = new MainWindow([]);
        try
        {
            var results = new List<InteractionValidationResult>();
            window.AddRibbonInteractionExecutionResults(results, commandStart: 0, commandCount: ValidationBatchSize);
            _cachedResults = results.ToArray();
            return _cachedResults;
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }
    }
}
