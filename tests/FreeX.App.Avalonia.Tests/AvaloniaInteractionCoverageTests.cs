using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaInteractionCoverageTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void Options_ParseAndRemoveInteractionValidationArguments()
    {
        var parsed = InteractionValidationOptions.TryParse(
            ["--interaction-validation", "/work/validation", "book.xlsx"],
            out var options,
            out var startupArguments,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal("/work/validation", options!.OutputDirectory);
        Assert.Equal(["book.xlsx"], startupArguments);
    }

    [Fact]
    public void Options_RejectMissingOutputDirectory()
    {
        Assert.False(InteractionValidationOptions.TryParse(
            ["--interaction-validation"],
            out _,
            out _,
            out var error));

        Assert.Contains("requires an output directory", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RibbonSurfaceInventory_PreservesEveryDeclaredPlacement()
    {
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var rows = AvaloniaRibbonComposition.EnumerateSurfaceRows(definition).ToArray();

        // 574 canonical shared placements plus the 42 runtime shape-gallery leaves.
        Assert.Equal(616, rows.Length);
        Assert.Equal(294, rows.Count(row => row.Kind != nameof(RibbonMenuItem)));
        Assert.Equal(322, rows.Count(row => row.Kind == nameof(RibbonMenuItem)));
        Assert.Equal(573, rows.Select(row => row.CommandId).Distinct().Count());
        Assert.Equal(73, definition.Tabs.Sum(tab => tab.Groups.Count));
    }

    [Fact]
    public async Task LiveWindow_AllRibbonCommandsAreFunctionalOrExplicitlyDisabled()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var registry = Assert.IsAssignableFrom<IRibbonCommandRegistry>(window.RibbonCommandRegistryForTest);
            var unresolved = AvaloniaRibbonComposition
                .EnumerateCommandIds(AvaloniaRibbonComposition.BuildDefinition())
                .Distinct()
                .Where(id => !registry.TryGet(id, out var command) || command is EmptyRibbonCommand)
                .Select(id => id.Value)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            window.Close();

            Assert.True(
                unresolved.Length == 0,
                "Live Avalonia ribbon commands still bound to EmptyRibbonCommand: " +
                string.Join(", ", unresolved));
        }, CancellationToken.None);
    }
}
