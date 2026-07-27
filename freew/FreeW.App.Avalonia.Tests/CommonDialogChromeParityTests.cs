using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
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
        textBox.Height.Should().Be(24);
        comboBox.Height.Should().Be(24);
        button.FontSize.Should().Be(12);
        textBox.FontSize.Should().Be(12);
        comboBox.FontSize.Should().Be(12);
        ((ISolidColorBrush)button.Background!).Color.Should().Be(Color.FromRgb(221, 221, 221));
        ((ISolidColorBrush)button.BorderBrush!).Color.Should().Be(Color.FromRgb(0, 120, 215));
        ((ISolidColorBrush)listBox.Background!).Color.Should().Be(Colors.White);
        ((ISolidColorBrush)listBox.BorderBrush!).Color.Should().Be(Color.FromRgb(130, 130, 130));
        listBox.BorderThickness.Should().Be(new Thickness(1));
        ((ISolidColorBrush)comboBox.Background!).Color.Should().Be(Color.FromRgb(240, 240, 240));
        row.Spacing.Should().Be(style.ActionSpacing);
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
        buttons[0].Content.Should().Be(Free.Shared.Shell.ShellStrings.Current.Ok);
        buttons[0].IsDefault.Should().BeTrue();
        buttons[0].IsCancel.Should().BeFalse();
        buttons[1].Content.Should().Be(Free.Shared.Shell.ShellStrings.Current.Cancel);
        buttons[1].IsCancel.Should().BeTrue();
        buttons[1].IsDefault.Should().BeFalse();
        buttons.Should().OnlyContain(button => button.MinWidth == 72);
        row.Spacing.Should().Be(AvaloniaCompactDialogChrome.WindowsStyle.ActionSpacing);

        buttons[0].Command?.Execute(null);
        accepted.Should().BeFalse("the runtime Click event, not a synthetic command, owns acceptance");
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

    private sealed class TestDialog : AvaloniaDialogWindow
    {
    }
}
