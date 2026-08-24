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
            ((StackPanel)dialog.Content!).Margin.Should().Be(new Thickness(14, 14, 15, 14));
            combos.Should().HaveCount(4);
            combos[0].MinWidth.Should().Be(80);
            combos.Skip(1).Should().OnlyContain(combo => combo.MinWidth == 130);
            textBoxes.Should().HaveCount(2);
            textBoxes.Should().OnlyContain(textBox => textBox.MinWidth == 60);
            combos.Should().OnlyContain(combo => combo.Margin == new Thickness(0, 0, 0, 8));
            textBoxes.Should().OnlyContain(textBox => textBox.Margin == new Thickness(0, 0, 0, 8));
            combos.Should().OnlyContain(combo => combo.HorizontalAlignment == HorizontalAlignment.Stretch);
            textBoxes.Should().OnlyContain(textBox => textBox.HorizontalAlignment == HorizontalAlignment.Stretch);
            combos.Should().OnlyContain(combo => combo.Height == CompactDialogVisualTokens.ControlHeight);
            textBoxes.Should().OnlyContain(textBox => textBox.Height == 25);
            buttons.Select(UserFacingButtonText).Should().Equal(ShellStrings.Current.Ok, ShellStrings.Current.Cancel);
            UserFacingButtonText(buttons.Single(button => button.IsDefault)).Should().Be(ShellStrings.Current.Ok);
            UserFacingButtonText(buttons.Single(button => button.IsCancel)).Should().Be(ShellStrings.Current.Cancel);
            buttons.Select(button => AutomationProperties.GetName(button))
                .Should().Equal(
                    ShellStrings.Current.CreateAutomationName(ShellStrings.Current.Ok),
                    ShellStrings.Current.CreateAutomationName(ShellStrings.Current.Cancel));
            actionRow.Spacing.Should().Be(14);
            actionRow.Margin.Should().Be(new Thickness(0, 12, 0, 0));
            combos.Select(combo => combo.Bounds.Height).Should().Equal(
                CompactDialogVisualTokens.ControlHeight,
                CompactDialogVisualTokens.ControlHeight,
                CompactDialogVisualTokens.ControlHeight,
                CompactDialogVisualTokens.ControlHeight);
            textBoxes.Select(textBox => textBox.Bounds.Height).Should().Equal(
                25,
                25);
            buttons.Select(button => button.Bounds.Height).Should().Equal(
                CompactDialogVisualTokens.ButtonHeight,
                CompactDialogVisualTokens.ButtonHeight);
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
            "FreeW.DialogVisualHarness.Wpf",
            "Program.cs"));
        var avaloniaSource = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness.Avalonia",
            "Program.cs"));
        var dialogSource = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "MultilevelListDialog.cs"));
        var catalog = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness",
            "FreeWDialogEvidenceCatalog.cs"));

        source.Should().Contain("scenario.RouteId == \"multilevel-list\"");
        avaloniaSource.Should().Contain("plan.UseWpfAuthoritySize");
        avaloniaSource.Should().Contain("plan.ClientWidthAdjustment");
        avaloniaSource.Should().Contain("clientWidth += plan.ClientWidthAdjustment");
        dialogSource.Should().Contain("AvaloniaCompactDialogChrome.WindowsStyle with");
        dialogSource.Should().Contain("TextBoxHeight = 25");
        dialogSource.Should().Contain("ActionSpacing = 14");
        dialogSource.Should().Contain("selection.Margin = new Thickness(1)");
        dialogSource.Should().NotContain("Foreground = Brushes.Black");
        dialogSource.Should().NotContain("ControlHeight = 20");
        dialogSource.Should().NotContain("TextBoxHeight = 18");
        dialogSource.Should().NotContain("ComboBoxHeight = 22");
        dialogSource.Should().NotContain("ButtonHeight = 20");
        catalog.Should().Contain("Pair(\"multilevel-list\", \"MultilevelListDialog\"");
        catalog.Should().Contain("useWpfAuthoritySize: true,");
        catalog.Should().Contain("avaloniaClientWidthAdjustment: 1");
    }

    // AvaloniaDialogButtonContent wraps mnemonic-bearing text ("_OK") in an AccessText so Avalonia's
    // Fluent button template actually registers and renders the access key (WPF does this automatically
    // for a plain string; Avalonia does not). Read the user-facing text back out for content comparisons.
    private static string? UserFacingButtonText(Button button) => button.Content switch
    {
        string text => text,
        global::Avalonia.Controls.Primitives.AccessText accessText => accessText.Text,
        _ => button.Content?.ToString(),
    };
}
