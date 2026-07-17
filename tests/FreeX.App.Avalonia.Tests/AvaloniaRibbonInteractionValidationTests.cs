using Avalonia.Headless;
using FreeX.App.Avalonia.Charts;
using FreeX.App.Avalonia.Ribbon;
using Xunit.Abstractions;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaRibbonInteractionValidationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);
    private readonly ITestOutputHelper _output;

    public AvaloniaRibbonInteractionValidationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task BehaviorEvidence_CoversEveryRuntimeCommandAndPlacementExactlyOnce()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var results = new List<InteractionValidationResult>();

            window.AddRibbonInteractionExecutionResults(results);

            var commands = results.Where(result => result.Category == "ribbon-command-behavior").ToArray();
            var placements = results.Where(result => result.Category == "ribbon-placement-behavior").ToArray();
            Assert.Equal(573, commands.Length);
            Assert.Equal(573, commands.Select(result => result.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(616, placements.Length);
            Assert.Equal(616, placements.Select(result => result.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.DoesNotContain(results, result => result.EvidenceLevel == "registry-bound");
            foreach (var group in commands.GroupBy(result => result.EvidenceLevel).OrderBy(group => group.Key, StringComparer.Ordinal))
                _output.WriteLine($"{group.Key}: {group.Count()}");

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BehaviorEvidence_ExecutesSharedMutationsInDisposableSessions()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var results = new List<InteractionValidationResult>();

            window.AddRibbonInteractionExecutionResults(results);

            var executed = results
                .Where(result => result.Category == "ribbon-command-behavior" && result.EvidenceLevel == "executed-mutation")
                .ToArray();
            Assert.Contains(executed, result => result.Id.EndsWith("/Bold", StringComparison.Ordinal));
            Assert.Contains(executed, result => result.Id.EndsWith("/Italic", StringComparison.Ordinal));
            Assert.Contains(executed, result => result.Id.EndsWith("/Underline", StringComparison.Ordinal));

            var expectedChartCommands = AvaloniaRibbonComposition
                .EnumerateSurfaceRows(AvaloniaRibbonComposition.BuildDefinition())
                .Select(row => row.CommandId.Value)
                .Distinct(StringComparer.Ordinal)
                .Count(commandId => InsertChartCommandFactory.ChartTypeForRibbonCommand(commandId) is not null);
            Assert.Equal(expectedChartCommands + 3, executed.Length);
            Assert.All(executed, result => Assert.Equal("passed", result.Status));

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BehaviorEvidence_NeverReportsClassifiedOrMissingCommandsAsInvoked()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var results = new List<InteractionValidationResult>();

            window.AddRibbonInteractionExecutionResults(results);

            var commands = results.Where(result => result.Category == "ribbon-command-behavior").ToArray();
            Assert.DoesNotContain(commands, result =>
                result.EvidenceLevel.StartsWith("classified-", StringComparison.Ordinal) && result.Status == "passed");
            Assert.DoesNotContain(commands, result => result.EvidenceLevel is "empty-command-gap" or "unregistered-command");
            Assert.Contains(commands, result => result.EvidenceLevel == "classified-native-external" && result.Status == "skipped");
            Assert.Contains(commands, result => result.EvidenceLevel == "classified-destructive" && result.Status == "skipped");
            Assert.Contains(commands, result => result.EvidenceLevel == "classified-modal" && result.Status == "skipped");
            Assert.Contains(commands, result => result.EvidenceLevel == "classified-context-required" && result.Status == "skipped");
            Assert.Contains(commands, result => result.EvidenceLevel == "explicitly-disabled" && result.Status == "passed");

            window.Close();
        }, CancellationToken.None);
    }
}
