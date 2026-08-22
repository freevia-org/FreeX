using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeW.App.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class ChartDialogVisualHarnessSemanticParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Chart_dialogs_keep_the_Wpf_authority_focus_and_action_contract()
    {
        await Session.Dispatch(async () =>
        {
            await AssertContract(
                CreatePrivate<ChartTitleDialog>([typeof(string)], [null]),
                "ChartTitleTextBox");
            await AssertContract(
                CreatePrivate<ChartAxisTitlesDialog>([typeof(string), typeof(string)], [null, null]),
                "ChartCategoryAxisTitleTextBox");
            await AssertContract(
                CreatePrivate<ChartSizeDialog>([typeof(double), typeof(double)], [360d, 240d]),
                "ChartSizeWidthTextBox");
            return true;
        }, CancellationToken.None);
    }

    private static async Task AssertContract(Window dialog, string focusAutomationId)
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var resultTask = dialog.ShowDialog<object?>(owner);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

            var buttons = dialog.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.IsEffectivelyVisible)
                .ToArray();
            buttons.Select(AutomationProperties.GetName)
                .Should().Equal("OK", "Cancel");
            buttons.Single(button => button.IsDefault)
                .Should().Be(buttons[0]);
            buttons.Single(button => button.IsCancel)
                .Should().Be(buttons[1]);

            dialog.GetVisualDescendants()
                .OfType<Control>()
                .Single(control => AutomationProperties.GetAutomationId(control) == focusAutomationId)
                .IsFocused.Should().BeTrue();

            dialog.Close(null);
            await resultTask;
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    private static T CreatePrivate<T>(Type[] parameterTypes, object?[] args)
    {
        var constructor = typeof(T).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null)
            ?? throw new InvalidOperationException($"No private constructor found for {typeof(T).Name}.");
        return (T)constructor.Invoke(args);
    }
}
