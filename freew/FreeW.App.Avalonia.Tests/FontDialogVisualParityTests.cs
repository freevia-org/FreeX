using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeW.Core.Model;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Tests;

public sealed class FontDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Font_uses_Wpf_authority_control_metrics_and_action_chrome()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new FontDialog(new RunFormatting { FontSizePt = 12 });
            var family = Field<TextBox>(dialog, "_familyBox");
            var size = Field<ComboBox>(dialog, "_sizeBox");
            var color = Field<ComboBox>(dialog, "_colorBox");
            var buttons = dialog.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => button is not global::Avalonia.Controls.Primitives.ToggleButton)
                .ToArray();

            family.Height.Should().Be(25);
            FontDialogPlanner.VisualMetrics.AvaloniaLabelLineHeight.Should().Be(17);
            var colorBrush = color.Background;
            colorBrush.Should().BeOfType<LinearGradientBrush>();
            ((LinearGradientBrush)colorBrush!).GradientStops.Select(stop => stop.Color)
                .Should().Equal(Color.FromRgb(240, 240, 240), Color.FromRgb(229, 229, 229));
            ((SolidColorBrush)color.BorderBrush!)
                .Color.Should().Be(Color.FromRgb(172, 172, 172));
            family.FocusAdorner.Should().BeNull();
            size.Height.Should().Be(24);
            color.Height.Should().Be(24);
            buttons.Should().HaveCount(2);
            buttons.Should().OnlyContain(button => button.Height == 26);
            ((ISolidColorBrush)buttons.Single(button => button.IsCancel).BorderBrush!).Color
                .Should().Be(Color.FromRgb(0x70, 0x70, 0x70));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Font_uses_grayscale_text_rendering_to_match_Wpf_capture_authority()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new FontDialog(new RunFormatting { FontSizePt = 12 });
            TextOptions.GetTextRenderingMode(dialog).Should().Be(TextRenderingMode.Antialias);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Font_combo_glyph_uses_the_compact_Wpf_arrow_geometry_and_trailing_alignment()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new FontDialog(new RunFormatting { FontSizePt = 12 });
            try
            {
                dialog.Width = 460;
                dialog.Height = 340;
                dialog.Show();
                dialog.Measure(new Size(460, 340));
                dialog.Arrange(new Rect(0, 0, 460, 340));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var glyphs = dialog.GetVisualDescendants()
                    .OfType<PathIcon>()
                    .Where(path => path.Name == "DropDownGlyph")
                    .ToArray();

                glyphs.Should().NotBeEmpty();
                glyphs.Should().OnlyContain(path =>
                    path.Width == 8
                    && path.Height == 5
                    && path.HorizontalAlignment == global::Avalonia.Layout.HorizontalAlignment.Right);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Font_dialog_materializes_Wpf_textbox_and_checkbox_geometry()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new FontDialog(new RunFormatting { FontSizePt = 12 });
            try
            {
                dialog.Width = 460;
                dialog.Height = 340;
                dialog.Show();
                dialog.Measure(new Size(460, 340));
                dialog.Arrange(new Rect(0, 0, 460, 340));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var textBoxes = dialog.GetVisualDescendants()
                    .OfType<TextBox>()
                    .Where(box => box.Name != "PART_EditableTextBox")
                    .ToArray();
                textBoxes.Should().NotBeEmpty();
                textBoxes.Should().OnlyContain(box => box.Bounds.Height == 25);

                var wideBorders = dialog.GetVisualDescendants()
                    .OfType<Border>()
                    .Where(border => border.Name == "PART_BorderElement" && border.Bounds.Width > 300)
                    .ToArray();
                wideBorders.Should().NotBeEmpty();
                wideBorders.Should().OnlyContain(border => border.Bounds.Height == 25);
                ((ISolidColorBrush)wideBorders.First().BorderBrush!).Color
                    .Should().Be(Color.FromRgb(0x56, 0x9D, 0xE5));

                var indicators = dialog.GetVisualDescendants()
                    .OfType<CheckBox>()
                    .SelectMany(check => check.GetVisualDescendants().OfType<Border>())
                    .Where(border => border.Bounds.Width == 14 && border.Bounds.Height == 13)
                    .ToArray();
                indicators.Should().HaveCount(10);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    private static T Field<T>(FontDialog dialog, string name) where T : class =>
        (T)(typeof(FontDialog)
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(dialog)
            ?? throw new InvalidOperationException($"Missing FontDialog field {name}."));
}
