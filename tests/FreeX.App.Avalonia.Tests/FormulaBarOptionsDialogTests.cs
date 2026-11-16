using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class FormulaBarOptionsDialogTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task ViewOptions_ApplyFormulaBarDraftOnlyOnOk() =>
        Session.Dispatch(async () =>
        {
            var saved = new AppOptions { FormulaBarExpanded = true };
            var options = new FreeXOptionsRuntimeSession(
                load: () => saved.Clone(),
                save: next =>
                {
                    saved = next.Clone();
                    return true;
                });
            var owner = new MainWindow([], null!, options);
            try
            {
                owner.Show();
                var formulaBar = GetByAutomationId<Control>(owner, "FormulaBarRow");
                var formulaBox = GetByAutomationId<TextBox>(owner, "FormulaBox");

                var (dialog, completion) = OpenViewOptions(owner);
                var show = GetByAutomationId<CheckBox>(dialog, "OptionsShowFormulaBarCheckBox");
                var expand = GetByAutomationId<CheckBox>(dialog, "OptionsFormulaBarExpandedCheckBox");
                show.IsChecked.Should().BeTrue();
                expand.IsChecked.Should().BeTrue();
                expand.IsEnabled.Should().BeTrue();

                show.IsChecked = false;
                expand.IsEnabled.Should().BeFalse();
                expand.IsChecked.Should().BeTrue();
                show.IsChecked = true;
                expand.IsEnabled.Should().BeTrue();
                expand.IsChecked.Should().BeTrue();
                expand.IsChecked = false;
                formulaBox.AcceptsReturn.Should().BeTrue();
                Click(dialog, "OptionsCancelButton");
                await completion;
                saved.ShowFormulaBar.Should().BeTrue();
                saved.FormulaBarExpanded.Should().BeTrue();
                formulaBar.IsVisible.Should().BeTrue();
                formulaBox.AcceptsReturn.Should().BeTrue();

                (dialog, completion) = OpenViewOptions(owner);
                GetByAutomationId<CheckBox>(dialog, "OptionsShowFormulaBarCheckBox").IsChecked = false;
                GetByAutomationId<CheckBox>(dialog, "OptionsFormulaBarExpandedCheckBox").IsChecked.Should().BeTrue();
                Click(dialog, "OptionsOkButton");
                dialog.IsVisible.Should().BeFalse();
                await completion;

                saved.ShowFormulaBar.Should().BeFalse();
                saved.FormulaBarExpanded.Should().BeFalse();
                formulaBar.IsVisible.Should().BeFalse();
                formulaBox.AcceptsReturn.Should().BeFalse();
            }
            finally
            {
                Close(owner);
            }
            return true;
        }, CancellationToken.None);

    [Fact]
    public Task ViewOptions_OkAppliesExpansionSavedAfterTheDialogOpened() =>
        Session.Dispatch(async () =>
        {
            var saved = new AppOptions();
            var options = new FreeXOptionsRuntimeSession(
                load: () => saved.Clone(),
                save: next =>
                {
                    saved = next.Clone();
                    return true;
                });
            var owner = new MainWindow([], null!, options);
            try
            {
                owner.Show();
                var formulaBox = GetByAutomationId<TextBox>(owner, "FormulaBox");
                var (dialog, completion) = OpenViewOptions(owner);
                GetByAutomationId<CheckBox>(dialog, "OptionsFormulaBarExpandedCheckBox").IsChecked.Should().BeFalse();
                formulaBox.AcceptsReturn.Should().BeFalse();

                saved = new AppOptions { FormulaBarExpanded = true };
                Click(dialog, "OptionsOkButton");
                dialog.IsVisible.Should().BeFalse();
                await completion;

                saved.FormulaBarExpanded.Should().BeTrue();
                formulaBox.AcceptsReturn.Should().BeTrue();
            }
            finally
            {
                Close(owner);
            }
            return true;
        }, CancellationToken.None);

    private static (Window Dialog, Task Completion) OpenViewOptions(MainWindow owner)
    {
        var completion = owner.ShowOptionsDialogForTestAsync();
        var dialog = owner.OwnedWindows.Single(window => AutomationProperties.GetAutomationId(window) == "OptionsDialog");
        var categories = GetByAutomationId<StackPanel>(dialog, "OptionsCategoryList");
        categories.Tag.Should().BeOfType<Action<int>>().Subject(11);
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
        return (dialog, completion);
    }

    private static T GetByAutomationId<T>(Window window, string automationId) where T : Control =>
        window.GetVisualDescendants().OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static void Click(Window dialog, string automationId)
    {
        var button = GetByAutomationId<Button>(dialog, automationId);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }

    private static void Close(MainWindow owner)
    {
        foreach (var dialog in owner.OwnedWindows.ToArray())
            dialog.Close();

        owner.AllowCloseWithoutDirtyPromptForParityCapture();
        owner.Close();
    }
}
