using System.Text.Json;
using System.Threading;

using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class QuickAnalysisDrawingInteractionValidationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task BoundedValidation_RecordsQuickAnalysisAndProductionDrawingPostconditions()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-quick-analysis-drawing-validation-"))
        {
            var outputDirectory = temporaryDirectory.Path;
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                window.Show();
                try
                {
                    var results = await window.RunInteractionValidationAsync(
                        outputDirectory,
                        dialogStart: 0,
                        dialogCount: 0,
                        includeCoreResults: true,
                        ribbonCommandStart: 0,
                        ribbonCommandCount: 0,
                        ribbonOnly: false,
                        coreSection: "quick-analysis-drawing");

                    results.Select(result => result.Id).Should().Equal(
                        "quick-analysis.conditional-format",
                        "quick-analysis.total",
                        "drawing.shape.move",
                        "drawing.shape.resize",
                        "drawing.shape.rotate",
                        "drawing.shape.capture-loss-no-op");
                    results.Should().OnlyContain(result => result.Status == "passed",
                        string.Join(Environment.NewLine, results.Select(result =>
                            $"{result.Id}: {result.Status}; {result.Evidence}")));
                    results.Should().OnlyContain(result =>
                        result.EvidenceLevel == "production-model-observed" &&
                        result.Evidence.Contains("undo=", StringComparison.Ordinal));
                    results.Single(result => result.Id == "drawing.shape.capture-loss-no-op")
                        .Evidence.Should().Contain("commandAdded=false");

                    var options = new InteractionValidationOptions(
                        outputDirectory,
                        DialogCount: 0,
                        IncludeCoreResults: true,
                        RibbonCommandCount: 0,
                        CoreSection: "quick-analysis-drawing");
                    InteractionValidationCoordinator.WriteManifestForTest(outputDirectory, options, results);

                    var manifestPath = Path.Combine(outputDirectory, "interaction-validation.json");
                    File.Exists(manifestPath).Should().BeTrue();
                    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                    manifest.RootElement.GetProperty("validationSection").GetString()
                        .Should().Be("quick-analysis-drawing");
                    manifest.RootElement.GetProperty("summary").GetProperty("total").GetInt32()
                        .Should().Be(6);
                    manifest.RootElement.GetProperty("results").EnumerateArray()
                        .Select(result => result.GetProperty("id").GetString())
                        .Should().Equal(results.Select(result => result.Id));
                }
                finally
                {
                    window.AllowCloseWithoutDirtyPromptForParityCapture();

                    window.Close();
                }
                return true;
            }, CancellationToken.None);
        }
    }
}
