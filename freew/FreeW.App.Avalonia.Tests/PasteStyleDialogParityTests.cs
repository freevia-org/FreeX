using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
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
            buttons.Select(UserFacingButtonText).Should().Equal(
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
            try
            {
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
                comboBoxes.Select(comboBox => comboBox.MinWidth).Should().Equal(280, 280, 100, 160, 160);
                name.IsFocused.Should().BeTrue();
                comboBoxes.Should().NotContain(comboBox => comboBox.IsFocused);
                checkBoxes.Should().OnlyContain(checkBox => checkBox.Height == StyleDialogMetrics.CheckBoxHeight && checkBox.Template != null);
                buttons.Select(UserFacingButtonText).Should().Equal(
                    ShellStrings.Current.Ok,
                    ShellStrings.Current.Cancel);
                buttons[0].IsDefault.Should().BeTrue();
                buttons[1].IsCancel.Should().BeTrue();
                RestingButtonBrush(buttons[1], Button.BorderBrushProperty).Color.Should().Be(Color.FromRgb(112, 112, 112));
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Style_uses_WPF_field_metrics_and_square_compact_controls()
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
            var textBox = dialog.GetLogicalDescendants().OfType<TextBox>().Single();
            var combos = dialog.GetLogicalDescendants().OfType<ComboBox>().ToArray();

            textBox.MinWidth.Should().Be(280);
            combos.Select(combo => combo.MinWidth).Should().Equal(280, 280, 100, 160, 160);
            combos.Skip(2).Should().OnlyContain(combo => combo.HorizontalAlignment == global::Avalonia.Layout.HorizontalAlignment.Stretch);
            Assert.All(combos, combo => combo.Background.Should().BeOfType<LinearGradientBrush>());
            var gradients = combos.Select(combo => (LinearGradientBrush)combo.Background!).ToArray();
            gradients.Should().OnlyContain(gradient => gradient.GradientStops[0].Color == Color.FromRgb(240, 240, 240));
            gradients.Should().OnlyContain(gradient => gradient.GradientStops[gradient.GradientStops.Count - 1].Color == Color.FromRgb(229, 229, 229));
            Assert.All(combos, combo => ((SolidColorBrush)combo.BorderBrush!).Color.Should().Be(Color.FromRgb(172, 172, 172)));
            textBox.CornerRadius.Should().Be(new CornerRadius(0));
            combos.Should().OnlyContain(combo => combo.CornerRadius == new CornerRadius(0));
            Buttons(dialog).Should().OnlyContain(button => button.CornerRadius == new CornerRadius(3));
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
        source.Should().Contain("FreeWDialogEvidenceCatalog.CreateCapturePlan(");
        source.Should().Contain("plan.UseWpfAuthoritySize");
        source.Should().Contain("if (button is ToggleButton or RepeatButton)");
        source.Should().Contain("scenario.RouteId == \"style\"");
        source.Should().Contain("Sample Style");
        source.Should().Contain("name.Focus(NavigationMethod.Tab)");

        var wpfSource = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "tools",
            "FreeW.DialogVisualHarness.Wpf",
            "Program.cs"));
        wpfSource.Should().Contain("FreeWDialogEvidenceCatalog.CreateCapturePlan(");
        wpfSource.Should().Contain("Sample Style");
    }

    private static T Create<T>(params object?[] arguments) where T : Window
    {
        var constructor = typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == arguments.Length);
        return (T)constructor.Invoke(arguments);
    }

    private static Button[] Buttons(Window dialog) =>
        dialog.GetLogicalDescendants().OfType<Button>().Where(button => button is not global::Avalonia.Controls.Primitives.ToggleButton).ToArray();

    private static ISolidColorBrush RestingButtonBrush(Button button, AvaloniaProperty property) =>
        button.Styles
            .OfType<Style>()
            .SelectMany(style => style.Setters)
            .OfType<Setter>()
            .Where(setter => setter.Property == property)
            .Select(setter => setter.Value)
            .OfType<ISolidColorBrush>()
            .First();

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
