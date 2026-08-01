using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class MultilevelListDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Geometry_and_actions_match_the_Wpf_static_prompt_contract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new MultilevelListDialog(MultiLevelListFormat.DecimalNumberFormats);
            dialog.Show();
            dialog.UpdateLayout();
            var controls = dialog.GetLogicalDescendants().OfType<Control>().ToArray();
            var combos = controls.OfType<ComboBox>().ToArray();
            var textBoxes = controls.OfType<TextBox>().ToArray();
            var buttons = controls.OfType<Button>().ToArray();
            var actionRow = controls.OfType<StackPanel>()
                .Single(panel => panel.Children.OfType<Button>().Count() == 2);

            dialog.Width.Should().Be(380);
            combos.Should().HaveCount(4);
            combos[0].MinWidth.Should().Be(80);
            combos.Skip(1).Should().OnlyContain(combo => combo.MinWidth == 130);
            textBoxes.Should().HaveCount(2);
            textBoxes.Should().OnlyContain(textBox => textBox.MinWidth == 60);
            combos.Should().OnlyContain(combo => combo.Margin == new Thickness(0, 0, 0, 8));
            textBoxes.Should().OnlyContain(textBox => textBox.Margin == new Thickness(0, 0, 0, 8));
            combos.Should().OnlyContain(combo => combo.HorizontalAlignment == HorizontalAlignment.Stretch);
            textBoxes.Should().OnlyContain(textBox => textBox.HorizontalAlignment == HorizontalAlignment.Stretch);
            combos.Should().OnlyContain(combo => combo.Height == 22);
            textBoxes.Should().OnlyContain(textBox => textBox.Height == 18);
            buttons.Select(button => button.Content?.ToString()).Should().Equal(ShellStrings.Current.Ok, ShellStrings.Current.Cancel);
            buttons.Single(button => button.IsDefault).Content.Should().Be(ShellStrings.Current.Ok);
            buttons.Single(button => button.IsCancel).Content.Should().Be(ShellStrings.Current.Cancel);
            buttons.Select(button => AutomationProperties.GetName(button))
                .Should().Equal(
                    ShellStrings.Current.CreateAutomationName(ShellStrings.Current.Ok),
                    ShellStrings.Current.CreateAutomationName(ShellStrings.Current.Cancel));
            actionRow.Spacing.Should().Be(8);
            actionRow.Margin.Should().Be(new Thickness(0, 11, 0, 0));
            combos.Select(combo => combo.Bounds.Height).Should().Equal(22, 22, 22, 22);
            textBoxes.Select(textBox => textBox.Bounds.Height).Should().Equal(18, 18);
            buttons.Select(button => button.Bounds.Height).Should().Equal(20, 20);
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Invalid_start_uses_wpf_validation_target_without_opening_a_nested_dialog()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new MultilevelListDialog(MultiLevelListFormat.DecimalNumberFormats);
            dialog.Show();
            try
            {
                dialog.UpdateLayout();
                var start = dialog.GetLogicalDescendants().OfType<TextBox>().First();
                start.Text = "0";

                dialog.ValidateForTest();

                start.IsFocused.Should().BeTrue();
                start.SelectionStart.Should().Be(0);
                start.SelectionEnd.Should().Be(start.Text?.Length ?? 0);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void Evidence_harness_uses_Wpf_authority_size_for_the_static_prompt()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness.Avalonia",
            "Program.cs"));

        source.Should().Contain(
            "scenario.RouteId is \"font\" or \"paragraph\" or \"multilevel-list\" or \"paste-special\" or \"style\" or \"manage-styles\"");
        source.Should().Contain("if (scenario.RouteId == \"multilevel-list\")");
        source.Should().Contain("clientWidth++");
    }
}
