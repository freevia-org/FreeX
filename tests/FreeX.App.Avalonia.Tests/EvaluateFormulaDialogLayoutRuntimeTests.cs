using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class EvaluateFormulaDialogLayoutRuntimeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task EvaluateFormulaChrome_UsesSharedButtonHeightWhenAppliedAndArranged()
    {
        await Session.Dispatch(() =>
        {
            var style = MainWindow.EvaluateFormulaDialogChromeStyleForTest;
            var button = new Button { Content = "_Evaluate" };
            AvaloniaCompactDialogChrome.ApplyButton(button, style, EvaluateFormulaDialogPlanner.EvaluateButtonWidth, isDefault: true);
            var window = new Window { Width = 240, Height = 80, Content = button };
            window.Show();
            window.Measure(new Size(240, 80));
            window.Arrange(new Rect(0, 0, 240, 80));
            try
            {
                Assert.Equal(EvaluateFormulaDialogPlanner.ButtonHeight, style.ButtonHeight);
                Assert.Equal(EvaluateFormulaDialogPlanner.ButtonHeight, style.ControlHeight);
                Assert.Equal(EvaluateFormulaDialogPlanner.ButtonHeight, button.Height);
                Assert.Equal(EvaluateFormulaDialogPlanner.ButtonHeight, button.Bounds.Height);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EvaluateFormulaChrome_UsesWpfActionSpacingForTheSixButtonRow()
    {
        await Session.Dispatch(() =>
        {
            var style = MainWindow.EvaluateFormulaDialogChromeStyleForTest;
            var controls = Enumerable.Range(0, 6)
                .Select(_ => (Control)new Button { Width = 40 })
                .ToArray();
            var row = AvaloniaCompactDialogChrome.CreateActionRow(controls, style: style);

            Assert.Equal(EvaluateFormulaDialogPlanner.ActionSpacing, row.Spacing);
            Assert.Equal(6, row.Children.Count);
        }, CancellationToken.None);
    }
}
