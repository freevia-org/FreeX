using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class CommonDialogChromeParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public void All_FreeW_dialog_windows_adopt_the_shared_base()
    {
        var dialogTypes = typeof(PageSetupDialog).Assembly.GetTypes()
            .Where(type => type.Name.EndsWith("Dialog", StringComparison.Ordinal)
                && typeof(Window).IsAssignableFrom(type)
                && type.Name != "PrintPreviewDialog")
            .ToArray();

        dialogTypes.Should().NotBeEmpty();
        dialogTypes.Should().OnlyContain(
            type => typeof(AvaloniaDialogWindow).IsAssignableFrom(type),
            "all app-owned modal/modeless dialog routes should inherit the common Avalonia dialog surface");
        dialogTypes.Should().Contain(type => type.Name == "LegalNoticesDialog");
        dialogTypes.Should().Contain(type => type.Name == "TablePropertiesDialog");
        dialogTypes.Should().Contain(type => type.Name == "FontDialog");
        dialogTypes.Should().Contain(type => type.Name == "PageSetupDialog");
        dialogTypes.Should().Contain(type => type.Name == "OptionsDialog");
        dialogTypes.Should().Contain(type => type.Name == "ParagraphDialog");
        dialogTypes.Should().Contain(type => type.Name == "ImageAdjustDialog");
        dialogTypes.Should().Contain(type => type.Name == "AboutDialog");
        dialogTypes.Should().Contain(type => type.Name == "FreeWInfoDialog");
    }

    [Fact]
    public async Task Shared_window_chrome_uses_Windows_like_typography_and_surface()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new TestDialog();

            dialog.Classes.Should().Contain(AvaloniaCompactDialogChrome.DialogWindowClass);
            dialog.FontFamily.Should().Be(AvaloniaCompactDialogChrome.WindowsUiFontFamily);
            dialog.FontSize.Should().Be(12);
            TextOptions.GetTextRenderingMode(dialog).Should().Be(TextRenderingMode.Antialias);
            dialog.Background.Should().Be(Brushes.White);
            ((ISolidColorBrush)dialog.Foreground!).Color.Should().Be(Color.FromRgb(0x1f, 0x1f, 0x1f));
            dialog.WindowStartupLocation.Should().Be(WindowStartupLocation.CenterOwner);
            dialog.ShowInTaskbar.Should().BeFalse();
        }, CancellationToken.None);
    }

    [Fact]
    public void Shared_controls_use_WPF_metrics_and_common_action_spacing()
    {
        var style = AvaloniaCompactDialogChrome.WindowsStyle;
        var button = new Button();
        var textBox = new TextBox();
        var comboBox = new ComboBox();
        var listBox = new ListBox();

        AvaloniaCompactDialogChrome.ApplyButton(button, style, minWidth: 84, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyTextBox(textBox, style);
        AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, style);
        AvaloniaCompactDialogChrome.ApplyListBox(listBox, style);
        var row = AvaloniaCompactDialogChrome.CreateActionRow([button], style: style);

        button.Height.Should().Be(26);
        button.CornerRadius.Should().Be(new CornerRadius(0));
        textBox.Height.Should().Be(24);
        textBox.CornerRadius.Should().Be(new CornerRadius(0));
        comboBox.Height.Should().Be(24);
        comboBox.CornerRadius.Should().Be(new CornerRadius(0));
        button.FontSize.Should().Be(12);
        textBox.FontSize.Should().Be(12);
        comboBox.FontSize.Should().Be(12);
        comboBox.HorizontalContentAlignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Stretch);
        comboBox.HorizontalAlignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Stretch);
        ((ISolidColorBrush)button.Background!).Color.Should().Be(Color.FromRgb(221, 221, 221));
        ((ISolidColorBrush)button.BorderBrush!).Color.Should().Be(Color.FromRgb(0, 120, 215));
        ((ISolidColorBrush)listBox.Background!).Color.Should().Be(Colors.White);
        ((ISolidColorBrush)listBox.BorderBrush!).Color.Should().Be(Color.FromRgb(171, 173, 179));
        listBox.BorderThickness.Should().Be(new Thickness(1));
        ((ISolidColorBrush)comboBox.Background!).Color.Should().Be(Color.FromRgb(240, 240, 240));
        row.Spacing.Should().Be(style.ActionSpacing);
    }

    [Fact]
    public void Combo_box_chrome_is_idempotent_for_local_template_state()
    {
        var comboBox = new ComboBox();
        var style = AvaloniaCompactDialogChrome.WindowsStyle;

        AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, style);
        var styleCount = comboBox.Styles.Count;
        var classCount = comboBox.Classes.Count;

        AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, style);

        comboBox.Styles.Count.Should().Be(styleCount);
        comboBox.Classes.Count.Should().Be(classCount);
        comboBox.Classes.Should().Contain(AvaloniaCompactDialogChrome.CompactComboBoxClass);
    }

    [Fact]
    public async Task Shared_descendant_chrome_normalizes_non_table_text_and_checkbox_controls()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new ChromeProbeDialog();
            try
            {
                dialog.Width = 300;
                dialog.Height = 120;
                dialog.Show();
                dialog.Measure(new Size(300, 120));
                dialog.Arrange(new Rect(0, 0, 300, 120));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                dialog.Label.FontFamily.Should().Be(AvaloniaCompactDialogChrome.WindowsUiFontFamily);
                dialog.Label.FontSize.Should().Be(12);
                ((ISolidColorBrush)dialog.Label.Foreground!).Color.Should().Be(Color.FromRgb(0x1f, 0x1f, 0x1f));
                dialog.Check.FontFamily.Should().Be(AvaloniaCompactDialogChrome.WindowsUiFontFamily);
                dialog.Check.FontSize.Should().Be(12);
                ((ISolidColorBrush)dialog.Check.Foreground!).Color.Should().Be(Color.FromRgb(0x1f, 0x1f, 0x1f));
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Shared_descendant_chrome_preserves_explicit_text_block_typography()
    {
        await Session.Dispatch(() =>
        {
            var family = new FontFamily("Consolas");
            var foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x6D, 0x8C));
            var dialog = new ChromeProbeDialog(new TextBlock
            {
                Text = "Hint",
                FontFamily = family,
                FontSize = 11,
                Foreground = foreground,
            });
            try
            {
                dialog.Width = 300;
                dialog.Height = 120;
                dialog.Show();
                dialog.Measure(new Size(300, 120));
                dialog.Arrange(new Rect(0, 0, 300, 120));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                dialog.LocalLabel.FontFamily.Should().Be(family);
                dialog.LocalLabel.FontSize.Should().Be(11);
                dialog.LocalLabel.Foreground.Should().Be(foreground);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void Compact_checkbox_uses_a_closed_thirteen_pixel_indicator_template()
    {
        var checkBox = new CheckBox { Content = "Bold" };

        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(
            checkBox,
            AvaloniaCompactDialogChrome.WindowsStyle);

        checkBox.Height.Should().Be(18);
        checkBox.Template.Should().NotBeNull();
    }

    [Fact]
    public void Wpf_expander_uses_an_unframed_full_width_template()
    {
        var expander = new Expander { Header = "More" };

        AvaloniaCompactDialogChrome.ApplyWpfExpander(expander);

        expander.Template.Should().NotBeNull();
        expander.HorizontalAlignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Stretch);
        expander.HorizontalContentAlignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Stretch);
        expander.BorderThickness.Should().Be(new Thickness(0));
        expander.Background.Should().Be(Brushes.Transparent);
    }

    [Fact]
    public void Shared_ok_cancel_row_uses_shell_strings_and_WPF_action_semantics()
    {
        var accepted = false;
        var cancelled = false;

        var row = AvaloniaCompactDialogChrome.CreateOkCancelRow(
            () => accepted = true,
            () => cancelled = true,
            buttonWidth: 72,
            margin: new Thickness(0, 4, 0, 0));
        var buttons = row.Children.OfType<Button>().ToArray();

        buttons.Should().HaveCount(2);
        UserFacingButtonText(buttons[0]).Should().Be(Free.Shared.Shell.ShellStrings.Current.Ok);
        buttons[0].IsDefault.Should().BeTrue();
        buttons[0].IsCancel.Should().BeFalse();
        UserFacingButtonText(buttons[1]).Should().Be(Free.Shared.Shell.ShellStrings.Current.Cancel);
        buttons[1].IsCancel.Should().BeTrue();
        buttons[1].IsDefault.Should().BeFalse();
        buttons.Should().OnlyContain(button => button.MinWidth == 72);
        row.Spacing.Should().Be(AvaloniaCompactDialogChrome.WindowsStyle.ActionSpacing);

        buttons[0].Command?.Execute(null);
        accepted.Should().BeFalse("the runtime Click event, not a synthetic command, owns acceptance");
        cancelled.Should().BeFalse();
    }

    [Fact]
    public void Shared_button_row_factory_matches_WPF_order_and_automation_contract()
    {
        var accepted = false;
        var cancelled = false;

        var row = AvaloniaDialogButtonRowFactory.CreateOkCancel(
            () => accepted = true,
            () => cancelled = true,
            buttonWidth: 72,
            rowMargin: new Thickness(2, 3, 4, 5));
        var buttons = row.Children.OfType<Button>().ToArray();

        buttons.Should().HaveCount(2);
        UserFacingButtonText(buttons[0]).Should().Be(Free.Shared.Shell.ShellStrings.Current.Ok);
        buttons[0].IsDefault.Should().BeTrue();
        buttons[0].IsCancel.Should().BeFalse();
        UserFacingButtonText(buttons[1]).Should().Be(Free.Shared.Shell.ShellStrings.Current.Cancel);
        buttons[1].IsDefault.Should().BeFalse();
        buttons[1].IsCancel.Should().BeTrue();
        buttons.Should().OnlyContain(button => button.MinWidth == 72);
        buttons.Should().OnlyContain(button =>
            !string.IsNullOrWhiteSpace(global::Avalonia.Automation.AutomationProperties.GetName(button)));
        row.Spacing.Should().Be(AvaloniaCompactDialogChrome.WindowsStyle.ActionSpacing);
        row.Margin.Should().Be(new Thickness(2, 3, 4, 5));
        accepted.Should().BeFalse();
        cancelled.Should().BeFalse();
    }

    [Fact]
    public void Classic_tabs_are_contiguous_and_idempotent()
    {
        var tabs = new TabControl();

        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(tabs);
        var styleCount = tabs.Styles.Count;
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(tabs);

        tabs.Classes.Should().Contain(AvaloniaCompactDialogChrome.ClassicTabClass);
        styleCount.Should().Be(4);
        tabs.Styles.Count.Should().Be(styleCount, "reapplying the window chrome must not duplicate tab styles");
    }

    [Fact]
    public void Classic_tabs_accept_an_authority_specific_content_pane_metric()
    {
        var tabs = new TabControl();

        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(
            tabs,
            AvaloniaCompactDialogChrome.WindowsStyle with { ControlHeight = 21 },
            contentPaneMargin: new Thickness(-11, 0, -11, 0));

        tabs.Classes.Should().Contain(AvaloniaCompactDialogChrome.ClassicTabClass);
        tabs.Styles.Count.Should().Be(4);
        var hasAuthorityPaneMargin = tabs.Styles
            .OfType<Style>()
            .SelectMany(style => style.Setters)
            .OfType<Setter>()
            .Any(setter => setter.Property == global::Avalonia.Layout.Layoutable.MarginProperty
                && setter.Value is Thickness margin
                && margin == new Thickness(-11, 0, -11, 0));
        hasAuthorityPaneMargin.Should().BeTrue();
        var hasAuthorityTabHeight = tabs.Styles
            .OfType<Style>()
            .SelectMany(style => style.Setters)
            .OfType<Setter>()
            .Any(setter => setter.Property == global::Avalonia.Layout.Layoutable.MinHeightProperty
                && setter.Value is double height
                && height == 21);
        hasAuthorityTabHeight.Should().BeTrue(
            "classic tab chrome owns the tab minimum height through its public style contract");
    }

    [Fact]
    public async Task Font_dialog_keeps_editable_size_and_tab_pane_at_Wpf_width()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new FontDialog(new FreeW.Core.Model.RunFormatting { FontSizePt = 12 });
            try
            {
                dialog.Width = 460;
                dialog.Height = 340;
                dialog.Show();
                dialog.Measure(new Size(460, 340));
                dialog.Arrange(new Rect(0, 0, 460, 340));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var editableCombo = dialog.GetVisualDescendants()
                    .OfType<ComboBox>()
                    .Single(combo => combo.IsEditable);
                var editableTextBox = editableCombo.GetVisualDescendants()
                    .OfType<TextBox>()
                    .Single(textBox => textBox.Name == "PART_EditableTextBox");
                var selectedPane = dialog.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .Single(presenter => presenter.Name == "PART_SelectedContentHost");

                editableTextBox.Bounds.Width.Should().BeGreaterThan(300);
                editableTextBox.Bounds.Height.Should().BeLessThanOrEqualTo(24);
                selectedPane.Bounds.X.Should().Be(0);
                selectedPane.Bounds.Width.Should().BeGreaterThanOrEqualTo(420);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    // AvaloniaDialogButtonContent wraps mnemonic-bearing text ("_OK") in an AccessText so Avalonia's
    // Fluent button template actually registers and renders the access key (WPF does this automatically
    // for a plain string; Avalonia does not). Read the user-facing text back out for content comparisons.
    private static string? UserFacingButtonText(Button button) => button.Content switch
    {
        string text => text,
        AccessText accessText => accessText.Text,
        _ => button.Content?.ToString(),
    };

    private sealed class TestDialog : AvaloniaDialogWindow
    {
    }

    private sealed class ChromeProbeDialog : AvaloniaDialogWindow
    {
        internal TextBlock Label { get; } = new() { Text = "Probe" };
        internal CheckBox Check { get; } = new() { Content = "Probe" };
        internal TextBlock LocalLabel { get; } = new() { Text = "Local" };

        public ChromeProbeDialog(TextBlock? localLabel = null)
        {
            LocalLabel = localLabel ?? new TextBlock { Text = "Local" };
            Content = new StackPanel
            {
                Children =
                {
                    Label,
                    Check,
                    LocalLabel,
                },
            };
        }
    }
}
