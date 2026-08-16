using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
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
    public async Task ValueFieldSettings_InvalidShowValuesAsRestoresWpfValidationFocus()
    {
        await Session.Dispatch(() =>
        {
            var baseFieldBox = new ComboBox { ItemsSource = new[] { "(Automatic)", "Quarter" } };
            var baseItemBox = new TextBox { Text = "Q1" };
            var tabs = new TabControl
            {
                Items =
                {
                    new TabItem { Header = "Summarize", Content = new TextBox() },
                    new TabItem
                    {
                        Header = "Show Values As",
                        Content = new StackPanel { Children = { baseFieldBox, baseItemBox } },
                    },
                },
                SelectedIndex = 0,
            };
            var dialog = new Window { Content = tabs, Width = 320, Height = 180 };

            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                MainWindow.FocusInvalidShowValuesAsInput(tabs, baseFieldBox, baseItemBox, baseFieldIndex: null);
                tabs.SelectedIndex.Should().Be(1);
                baseFieldBox.IsFocused.Should().BeTrue();

                MainWindow.FocusInvalidShowValuesAsInput(tabs, baseFieldBox, baseItemBox, baseFieldIndex: 0);
                tabs.SelectedIndex.Should().Be(1);
                baseItemBox.IsFocused.Should().BeTrue();
                baseItemBox.SelectionStart.Should().Be(0);
                baseItemBox.SelectionEnd.Should().Be(2);
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ValueFieldSettings_ParityCapture_OpensFocusesTabsAndCancelsTheProductionDialog()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-pivot-value-settings-interaction-"))
        {
            var outputDirectory = temporaryDirectory.Path;
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
                    window.AllowCloseWithoutDirtyPromptForParityCapture();

                    if (window.IsVisible)
                        window.Close();
                }
                return true;
            }, CancellationToken.None);
        }
    }
}
