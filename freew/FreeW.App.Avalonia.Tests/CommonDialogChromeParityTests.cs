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

        AvaloniaCompactDialogChrome.ApplyButton(button, style, minWidth: 84, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyTextBox(textBox, style);
        AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, style);
        var row = AvaloniaCompactDialogChrome.CreateActionRow([button], style: style);

        button.Height.Should().Be(26);
        textBox.Height.Should().Be(24);
        comboBox.Height.Should().Be(24);
        button.FontSize.Should().Be(12);
        textBox.FontSize.Should().Be(12);
        comboBox.FontSize.Should().Be(12);
        ((ISolidColorBrush)button.Background!).Color.Should().Be(Color.FromRgb(221, 221, 221));
        ((ISolidColorBrush)button.BorderBrush!).Color.Should().Be(Color.FromRgb(200, 200, 200));
        row.Spacing.Should().Be(style.ActionSpacing);
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
