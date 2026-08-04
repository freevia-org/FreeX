using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class FootnoteEndnoteOptionsDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Avalonia_dialog_uses_shared_grid_metrics_and_action_contract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new FootnoteEndnoteOptionsDialog(new(), new());
            try
            {
                dialog.Width.Should().Be(FootnoteEndnoteOptionsDialogPlanner.DialogWidth);
                dialog.FontFamily.Should().Be(AvaloniaCompactDialogChrome.WindowsUiFontFamily);

                var grids = dialog.GetLogicalDescendants().OfType<Grid>().ToArray();
                grids.Should().HaveCount(2);
                grids.Should().OnlyContain(grid =>
                    grid.ColumnDefinitions.Count == 2
                    && grid.RowDefinitions.Count == 3);

                var buttons = dialog.GetLogicalDescendants()
                    .OfType<Button>()
                    .Where(button => button is not ToggleButton)
                    .ToArray();
                buttons.Select(UserFacingButtonText)
                    .Should().Equal(
                        ShellStrings.Current.CreateAutomationName(ShellStrings.Current.Ok),
                        ShellStrings.Current.CreateAutomationName(ShellStrings.Current.Cancel));
                buttons.Should().OnlyContain(button => button.Content is AccessText);
                buttons.Select(button => ((AccessText)button.Content!).Text)
                    .Should().Equal(ShellStrings.Current.Ok, ShellStrings.Current.Cancel);
                buttons.Select(AutomationProperties.GetName).Should().Equal("OK", "Cancel");
                buttons.Select(AutomationProperties.GetAccessKey).Should().Equal("Alt+O", "Alt+C");
                buttons[0].IsDefault.Should().BeTrue();
                buttons[0].IsCancel.Should().BeFalse();
                buttons[1].IsCancel.Should().BeTrue();
                buttons[1].IsDefault.Should().BeFalse();
                AutomationProperties.GetName(buttons[0]).Should().NotBeNullOrWhiteSpace();
                AutomationProperties.GetName(buttons[1]).Should().NotBeNullOrWhiteSpace();
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Validation_state_uses_the_shared_positive_start_at_policy()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new FootnoteEndnoteOptionsDialog(new(), new());
            try
            {
                var start = (TextBox)dialog.GetLogicalDescendants().OfType<TextBox>().First();
                var status = (TextBlock)dialog.GetLogicalDescendants().OfType<TextBlock>()
                    .Single(block => block == GetField(dialog, "_status"));
                start.Text = "not-a-number";
                dialog.ValidateForTest();

                status.Text.Should().Be(FootnoteEndnoteOptionsDialogPlanner.PositiveStartAtMessage);
                // The WPF host presents this warning through its modal warning helper; the static
                // dialog capture intentionally keeps the warning window out of the route bitmap.
                status.IsVisible.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void Visual_harness_uses_normalized_automation_names_for_action_semantics()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness.Avalonia",
            "Program.cs"));

        source.Should().Contain("DialogSemanticText.TryResolveActionButtonText(");
        source.Should().Contain("ReadActionButtons(dialog)");
        source.Should().Contain("AutomationProperties.GetName(button)");
    }

    private static object GetField(FootnoteEndnoteOptionsDialog dialog, string name) =>
        typeof(FootnoteEndnoteOptionsDialog).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(dialog)!;

    private static string UserFacingButtonText(Button button) => button.Content switch
    {
        AccessText accessText => ShellStrings.Current.CreateAutomationName(accessText.Text ?? string.Empty),
        string text => ShellStrings.Current.CreateAutomationName(text),
        TextBlock textBlock => textBlock.Text ?? string.Empty,
        _ => button.Content?.ToString() ?? string.Empty,
    };
}
