using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class EditingInteractionValidationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task RoutedEditingValidation_CoversInlineAndFormulaPointModes()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var results = await window.RunEditingInteractionValidationForTestAsync();

                results.Should().HaveCount(3);
                results.Select(result => result.Id).Should().Equal(
                    "cell-inline-edit",
                    "cell-inline-formula-edit-point-mode",
                    "formula-bar-edit-point-mode");
                results.Should().OnlyContain(result => result.Status == "passed", because:
                    string.Join(Environment.NewLine, results.Select(result => $"{result.Id}: {result.Evidence}")));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
