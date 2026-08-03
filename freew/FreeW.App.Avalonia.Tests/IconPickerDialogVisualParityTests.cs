using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Tests;

public sealed class IconPickerDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Initial_layout_uses_Wpf_authority_geometry_and_real_icon_tiles()
    {
        await Session.Dispatch(() =>
        {
            var dialog = CreateDialog();
            var category = Field<ComboBox>(dialog, "_category");
            var search = Field<TextBox>(dialog, "_search");
            var tiles = Field<WrapPanel>(dialog, "_tiles");
            var actions = dialog.GetLogicalDescendants().OfType<Button>().ToArray();

            dialog.Width.Should().Be(496);
            dialog.Height.Should().Be(480);
            category.Width.Should().Be(120);
            search.Width.Should().Be(160);
            tiles.Children.Should().HaveCount(61);
            Field<TextBlock>(dialog, "_status").Text.Should().Be("61 icons");
            foreach (var tile in tiles.Children)
            {
                tile.Should().BeOfType<Border>();
                var border = (Border)tile;
                border.Width.Should().Be(54);
                border.Height.Should().Be(54);
                border.Child.Should().BeOfType<Image>();
                var image = (Image)border.Child!;
                image.Width.Should().Be(38);
                image.Height.Should().Be(38);
                image.Source.Should().BeOfType<DrawingImage>();
                image.Stretch.Should().Be(Stretch.Fill);
                image.RenderTransform.Should().BeNull();
            }
            var firstDrawing = (DrawingImage)((Image)((Border)tiles.Children[0]).Child!).Source!;
            var firstGroup = firstDrawing.Drawing.Should().BeOfType<DrawingGroup>().Subject;
            var firstStroke = firstGroup.Children[0].Should().BeOfType<GeometryDrawing>().Subject;
            firstStroke.Geometry.Should().BeOfType<LineGeometry>();
            firstStroke.Pen.Should().NotBeNull();
            actions.Select(button => button.Content?.ToString()).Should().Equal("OK", "Cancel");
            actions.Single(button => button.IsDefault).Content.Should().Be("OK");
            actions.Single(button => button.IsCancel).Content.Should().Be("Cancel");

            var firstTile = (Border)tiles.Children[0];
            var firstEntry = (IconPickerEntry)firstTile.Tag!;
            Invoke(dialog, "Select", firstEntry, firstTile);
            Field<IconPickerEntry>(dialog, "_selected").Should().Be(firstEntry);
            firstTile.BorderBrush.Should().NotBe(Brushes.Transparent);
            firstTile.Background.Should().NotBe(Brushes.Transparent);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Search_and_validation_keep_the_Wpf_bottom_action_layout()
    {
        await Session.Dispatch(() =>
        {
            var dialog = CreateDialog();
            var search = Field<TextBox>(dialog, "_search");
            var tiles = Field<WrapPanel>(dialog, "_tiles");
            var status = Field<TextBlock>(dialog, "_status");

            search.Text = "12";
            Invoke(dialog, "Refresh");

            tiles.Children.Should().BeEmpty();
            status.Text.Should().Be("No icons match.");
            status.FontStyle.Should().Be(FontStyle.Italic);
        }, CancellationToken.None);
    }

    private static IconPickerDialog CreateDialog() =>
        (IconPickerDialog)(typeof(IconPickerDialog)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!
            .Invoke(null));

    private static T Field<T>(IconPickerDialog dialog, string name) where T : class =>
        (T)(typeof(IconPickerDialog)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)
            ?? throw new InvalidOperationException($"Missing IconPickerDialog field {name}."));

    private static void Invoke(IconPickerDialog dialog, string name, params object?[] arguments) =>
        typeof(IconPickerDialog)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dialog, arguments);
}
