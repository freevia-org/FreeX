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
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var results = await window.RunQuickAnalysisDrawingInteractionValidationForTestAsync();

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
                    .Evidence.Should().Contain("commandAdded=false", StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
