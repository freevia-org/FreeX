using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class PasteStyleDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task PasteSpecial_uses_WPF_order_labels_and_compact_content_size()
    {
        await Session.Dispatch(() =>
        {
            var dialog = Create<PasteSpecialDialog>();
            var buttons = Buttons(dialog);

            dialog.Width.Should().Be(380);
            dialog.SizeToContent.Should().Be(SizeToContent.Height);
            buttons.Select(button => button.Content?.ToString()).Should().Equal(
                ShellStrings.Current.Ok,
                ShellStrings.Current.Cancel);
            buttons[0].IsDefault.Should().BeTrue();
            buttons[1].IsCancel.Should().BeTrue();
            buttons.Should().OnlyContain(button => button.MinWidth == 72);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Style_uses_WPF_content_sizing_copy_and_validation_surface()
    {
        await Session.Dispatch(() =>
        {
            var dialog = Create<StyleDialog>(
                "New Style",
                new Dictionary<string, string>(),
                null,
                null,
                RunFormatting.Default,
                ParagraphFormatting.Default,
                null);
            dialog.Show();
            var labels = dialog.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
            var name = dialog.GetLogicalDescendants().OfType<TextBox>().Single();
            var comboBoxes = dialog.GetLogicalDescendants().OfType<ComboBox>().ToArray();
            var checkBoxes = dialog.GetLogicalDescendants().OfType<CheckBox>().ToArray();
            var buttons = Buttons(dialog);

            dialog.SizeToContent.Should().Be(SizeToContent.WidthAndHeight);
            labels.Should().Contain("Text colour:");
            labels.Should().NotContain(StyleDialogPlanner.ValidationMessageFor(StyleDialogValidationError.EmptyName));
            comboBoxes.Should().HaveCount(5);
            comboBoxes.Should().OnlyContain(comboBox => comboBox.MinWidth == 280);
            name.IsFocused.Should().BeTrue();
            comboBoxes.Should().NotContain(comboBox => comboBox.IsFocused);
            checkBoxes.Should().OnlyContain(checkBox => checkBox.Height == 18 && checkBox.Template != null);
            buttons.Select(button => button.Content?.ToString()).Should().Equal(
                ShellStrings.Current.Ok,
                ShellStrings.Current.Cancel);
            buttons[0].IsDefault.Should().BeTrue();
            buttons[1].IsCancel.Should().BeTrue();
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ManageStyles_uses_WPF_action_order_glyphs_and_metrics()
    {
        await Session.Dispatch(() =>
        {
            var dialog = Create<ManageStylesDialog>(new TextDocument(), null);
            var buttons = Buttons(dialog);

            dialog.SizeToContent.Should().Be(SizeToContent.WidthAndHeight);
            buttons.Select(button => button.Content?.ToString()).Should().Equal(
                "Apply",
                "Modify\u2026",
                "Delete",
                "Close");
            buttons[0].IsDefault.Should().BeTrue();
            buttons[3].IsCancel.Should().BeTrue();
            buttons.Should().OnlyContain(button => button.MinWidth == 80);
        }, CancellationToken.None);
    }

    [Fact]
    public void Visual_harness_uses_WPF_authority_dimensions_and_excludes_toggle_buttons()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness.Avalonia",
            "Program.cs"));

        source.Should().Contain("--wpf-authority");
        source.Should().Contain("scenario.RouteId is \"paste-special\" or \"style\" or \"manage-styles\"");
        source.Should().Contain("authorityCapture!.LogicalWidth");
        source.Should().Contain("authorityCapture!.LogicalHeight");
        source.Should().Contain("Where(button => button is not ToggleButton)");
    }

    private static T Create<T>(params object?[] arguments) where T : Window
    {
        var constructor = typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == arguments.Length);
        return (T)constructor.Invoke(arguments);
    }

    private static Button[] Buttons(Window dialog) =>
        dialog.GetLogicalDescendants().OfType<Button>().Where(button => button is not global::Avalonia.Controls.Primitives.ToggleButton).ToArray();
}
