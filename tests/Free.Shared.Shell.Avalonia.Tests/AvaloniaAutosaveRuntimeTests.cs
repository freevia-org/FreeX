using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class AvaloniaAutosaveRuntimeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    [Fact]
    public void BoundedTransaction_RunsInlineWhenTheCallerHasDispatcherAccess()
    {
        var runs = 0;

        var completed = AvaloniaBoundedDispatcherTransaction.TryExecute(
            () => runs++,
            TimeSpan.FromSeconds(1),
            checkAccess: () => true,
            post: _ => throw new InvalidOperationException("must not post"));

        completed.Should().BeTrue();
        runs.Should().Be(1);
    }

    [Fact]
    public async Task BoundedTransaction_SuppressesAQueuedCallbackThatStartsAfterTheDeadline()
    {
        var posted = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;

        var executeTask = Task.Run(() => AvaloniaBoundedDispatcherTransaction.TryExecute(
            () => runs++,
            TimeSpan.FromMilliseconds(40),
            checkAccess: () => false,
            post: callback => posted.SetResult(callback)));

        var lateCallback = await posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        (await executeTask).Should().BeFalse();

        lateCallback();
        runs.Should().Be(0, "a timed-out queued snapshot transaction must never write later");
    }

    [Fact]
    public async Task BoundedTransaction_CompletesAQueuedCallbackThatStartsBeforeTheDeadline()
    {
        var posted = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;

        var executeTask = Task.Run(() => AvaloniaBoundedDispatcherTransaction.TryExecute(
            () => runs++,
            TimeSpan.FromSeconds(5),
            checkAccess: () => false,
            post: callback => posted.SetResult(callback)));

        var callback = await posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callback();

        (await executeTask).Should().BeTrue();
        runs.Should().Be(1);
    }

    [Fact]
    public async Task RecoveryPromptComposer_PreservesTheSharedWindowAndButtonContract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new Window();
            bool? result = null;

            AvaloniaRecoveryPromptDialogComposer.Compose(
                dialog,
                "Recover this draft?",
                new("Localized title", "Restore", "Ignore"),
                response => result = response);

            dialog.Title.Should().Be("Localized title");
            dialog.Width.Should().Be(420);
            dialog.Height.Should().Be(160);
            dialog.CanResize.Should().BeFalse();
            dialog.WindowStartupLocation.Should().Be(WindowStartupLocation.CenterOwner);

            var root = dialog.Content.Should().BeOfType<StackPanel>().Subject;
            var message = root.Children[0].Should().BeOfType<TextBlock>().Subject;
            message.Text.Should().Be("Recover this draft?");
            message.TextWrapping.Should().Be(global::Avalonia.Media.TextWrapping.Wrap);
            message.Margin.Should().Be(new Thickness(16, 16, 16, 20));

            var actions = root.Children[1].Should().BeOfType<StackPanel>().Subject;
            var recover = actions.Children[0].Should().BeOfType<Button>().Subject;
            var skip = actions.Children[1].Should().BeOfType<Button>().Subject;
            recover.Content.Should().Be("Restore");
            recover.IsDefault.Should().BeTrue();
            skip.Content.Should().Be("Ignore");
            skip.IsCancel.Should().BeTrue();

            recover.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            result.Should().BeTrue();
            skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            result.Should().BeFalse();
        }, CancellationToken.None);
    }

    [Fact]
    public void FreePAndFreeWRecoveryPrompts_DelegateConstructionToTheSharedComposer()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "AutosaveAdapter.cs")),
            File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "AutosaveAdapter.cs")),
        };

        foreach (var source in sources)
        {
            source.Should().Contain("AvaloniaRecoveryPromptDialogComposer.Compose(")
                .And.Contain("AutosaveRecoveryTextCatalog.Resolve(UiText.Get)")
                .And.Contain("ResolveResponseOverride(message, ref handled, ref response)")
                .And.NotContain("new TextBlock\n        {\n            Text = message")
                .And.NotContain("AvaloniaCompactDialogChrome.CreateActionRow([yes, no]");
        }
    }
}
