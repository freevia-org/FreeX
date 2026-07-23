using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Free.Shared.Shell;
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
            var controls = dialog.GetLogicalDescendants().OfType<Control>().ToArray();
            var combos = controls.OfType<ComboBox>().ToArray();
            var textBoxes = controls.OfType<TextBox>().ToArray();
            var buttons = controls.OfType<Button>().ToArray();

            dialog.Width.Should().Be(380);
            combos.Should().HaveCount(4);
            combos[0].MinWidth.Should().Be(80);
            combos.Skip(1).Should().OnlyContain(combo => combo.MinWidth == 130);
            textBoxes.Should().HaveCount(2);
            textBoxes.Should().OnlyContain(textBox => textBox.MinWidth == 60);
            combos.Should().OnlyContain(combo => combo.HorizontalAlignment == HorizontalAlignment.Stretch);
            textBoxes.Should().OnlyContain(textBox => textBox.HorizontalAlignment == HorizontalAlignment.Stretch);
            combos.Should().OnlyContain(combo => combo.Height == 20);
            textBoxes.Should().OnlyContain(textBox => textBox.Height == 20);
            buttons.Select(button => button.Content?.ToString()).Should().Equal(ShellStrings.Current.Ok, ShellStrings.Current.Cancel);
            buttons.Single(button => button.IsDefault).Content.Should().Be(ShellStrings.Current.Ok);
            buttons.Single(button => button.IsCancel).Content.Should().Be(ShellStrings.Current.Cancel);
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
    }
}
