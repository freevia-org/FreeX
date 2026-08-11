using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class AvaloniaLabeledFormRowTests
{
    [Fact]
    public void FreePAndFreeWOptionsDialogsDelegateLabeledRowsToTheSharedRenderer()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "OptionsDialog.cs")),
            File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "OptionsDialog.cs")),
        };

        foreach (var source in sources)
        {
            source.Should().Contain("AvaloniaLabeledFormRow.Add(")
                .And.NotContain("private static void AddRow(");
        }
    }

    [Fact]
    public void Add_RealizesTheSharedTwoColumnRowWithoutHint()
    {
        var grid = new Grid();
        var field = new TextBox();

        AvaloniaLabeledFormRow.Add(grid, 2, "Language", field);

        grid.RowDefinitions.Should().ContainSingle()
            .Which.Height.Should().Be(GridLength.Auto);
        var label = grid.Children[0].Should().BeOfType<TextBlock>().Subject;
        label.Text.Should().Be("Language");
        label.Margin.Should().Be(new Thickness(0, 4, 12, 4));
        label.VerticalAlignment.Should().Be(VerticalAlignment.Center);
        Grid.GetRow(label).Should().Be(2);
        Grid.GetColumn(label).Should().Be(0);
        grid.Children[1].Should().BeSameAs(field);
        field.Margin.Should().Be(new Thickness(0, 4, 0, 4));
        Grid.GetRow(field).Should().Be(2);
        Grid.GetColumn(field).Should().Be(1);
    }

    [Fact]
    public void Add_WrapsFieldAndHintWithTheSharedVisualContract()
    {
        var grid = new Grid();
        var field = new ComboBox();

        AvaloniaLabeledFormRow.Add(grid, 1, "Format", field, "Used for new files.");

        var value = grid.Children[1].Should().BeOfType<StackPanel>().Subject;
        Grid.GetRow(value).Should().Be(1);
        Grid.GetColumn(value).Should().Be(1);
        value.Children[0].Should().BeSameAs(field);
        field.Margin.Should().Be(new Thickness(0, 4, 0, 4));
        var hint = value.Children[1].Should().BeOfType<TextBlock>().Subject;
        hint.Text.Should().Be("Used for new files.");
        hint.FontSize.Should().Be(11);
        hint.Foreground.Should().BeSameAs(Brushes.Gray);
        hint.TextWrapping.Should().Be(TextWrapping.Wrap);
        hint.Margin.Should().Be(new Thickness(0, 0, 0, 4));
    }
}
