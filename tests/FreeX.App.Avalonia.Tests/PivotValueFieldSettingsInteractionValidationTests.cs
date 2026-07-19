using System.Threading;
using Avalonia.Headless;
using Xunit.Abstractions;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class PivotValueFieldSettingsInteractionValidationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private readonly ITestOutputHelper _output;

    public PivotValueFieldSettingsInteractionValidationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ValueFieldSettings_ParityCapture_OpensFocusesTabsAndCancelsTheProductionDialog()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-pivot-value-settings-interaction-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var dialogIndex = MainWindow.InteractiveValidationDialogRoutes
                        .Select(route => route.CatalogId)
                        .ToList()
                        .IndexOf("dialog.PivotValueFieldSettingsDialog");
                    dialogIndex.Should().BeGreaterThanOrEqualTo(0);

                    var results = await window.RunInteractionValidationAsync(
                        outputDirectory,
                        dialogStart: dialogIndex,
                        dialogCount: 1,
                        includeCoreResults: false,
                        ribbonCommandCount: 0);

                    foreach (var result in results)
                        _output.WriteLine($"{result.Id} [{result.Category}]: {result.Status} | {result.Evidence}");

                    results.Should().HaveCount(3);
                    results.Select(result => (result.Id, result.Category)).Should().Equal(
                        ("dialog.PivotValueFieldSettings", "dialog"),
                        ("dialog.PivotValueFieldSettingsDialog", "dialog-inventory"),
                        ("dialog.PivotValueFieldSettingsDialog", "dialog-contract"));
                    results.Should().OnlyContain(result => result.Status == "passed",
                        string.Join(Environment.NewLine, results.Select(result =>
                            $"{result.Id} [{result.Category}]: {result.Evidence}")));
                }
                finally
                {
                    if (window.IsVisible)
                        window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
        }
    }
}
