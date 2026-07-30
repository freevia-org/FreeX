using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using FreeW.Core.Model;

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

            family.Height.Should().Be(18);
            family.FocusAdorner.Should().BeNull();
            size.Height.Should().Be(22);
            color.Height.Should().Be(22);
            buttons.Should().HaveCount(2);
            buttons.Should().OnlyContain(button => button.Height == 20);
            ((ISolidColorBrush)buttons.Single(button => button.IsCancel).BorderBrush!).Color
                .Should().Be(Color.FromRgb(0x70, 0x70, 0x70));
        }, CancellationToken.None);
    }

    private static T Field<T>(FontDialog dialog, string name) where T : class =>
        (T)(typeof(FontDialog)
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(dialog)
            ?? throw new InvalidOperationException($"Missing FontDialog field {name}."));
}
