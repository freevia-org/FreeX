using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class AvaloniaLabeledFormRowTests
{
    [Fact]
    public void CreateCompactGrid_RealizesTheFreeWDialogGridContract()
    {
        var grid = AvaloniaLabeledFormRow.CreateCompactGrid(3);

        grid.ColumnDefinitions.Should().HaveCount(2);
        grid.ColumnDefinitions[0].Width.Should().Be(GridLength.Auto);
        grid.ColumnDefinitions[1].Width.Should().Be(new GridLength(1, GridUnitType.Star));
        grid.RowDefinitions.Should().HaveCount(3)
            .And.OnlyContain(row => row.Height == GridLength.Auto);
    }

    [Fact]
    public void AddCompact_PreservesFreeWLabelSpacingAndPlacement()
    {
        var grid = AvaloniaLabeledFormRow.CreateCompactGrid(2);
        var first = new TextBox();
        var second = new ComboBox();

        AvaloniaLabeledFormRow.AddCompact(grid, "Width", first, 0);
        AvaloniaLabeledFormRow.AddCompact(grid, "Style", second, 1);

        var firstLabel = grid.Children[0].Should().BeOfType<TextBlock>().Subject;
        firstLabel.Margin.Should().Be(new Thickness(0, 0, 8, 0));
        firstLabel.VerticalAlignment.Should().Be(VerticalAlignment.Center);
        Grid.GetRow(firstLabel).Should().Be(0);
        Grid.GetColumn(firstLabel).Should().Be(0);
        grid.Children[1].Should().BeSameAs(first);
        Grid.GetColumn(first).Should().Be(1);

        var secondLabel = grid.Children[2].Should().BeOfType<TextBlock>().Subject;
        secondLabel.Margin.Should().Be(new Thickness(0, 4, 8, 0));
        Grid.GetRow(secondLabel).Should().Be(1);
        grid.Children[3].Should().BeSameAs(second);
        Grid.GetRow(second).Should().Be(1);
        Grid.GetColumn(second).Should().Be(1);
    }

    [Fact]
    public void Place_AddsControlAtTheRequestedCell()
    {
        var grid = AvaloniaLabeledFormRow.CreateCompactGrid(2);
        var control = new Button();

        AvaloniaLabeledFormRow.Place(grid, control, 1, 1);

        grid.Children.Should().ContainSingle().Which.Should().BeSameAs(control);
        Grid.GetRow(control).Should().Be(1);
        Grid.GetColumn(control).Should().Be(1);
    }

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
    public void FreeWCompactDialogsDelegateGridAndPlacementToTheSharedRenderer()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var renderer = Path.Combine(root, "freew", "FreeW.App.Avalonia");
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(renderer, "MediaDialogParity.cs")),
            File.ReadAllText(Path.Combine(renderer, "PictureFormattingDialogs.cs")),
            File.ReadAllText(Path.Combine(renderer, "DesignDialogs.cs")),
            File.ReadAllText(Path.Combine(renderer, "FootnoteEndnoteOptionsDialog.cs")),
            File.ReadAllText(Path.Combine(renderer, "ImageAndTableConversionDialogs.cs")),
        };

        sources.Should().OnlyContain(source => source.Contains("AvaloniaLabeledFormRow.", StringComparison.Ordinal));
        sources[0].Should().NotContain("public static Grid CreateGrid(")
            .And.NotContain("public static void AddField(")
            .And.NotContain("public static void Place(");
        sources[1].Should().NotContain("public static Grid CreateGrid(")
            .And.NotContain("public static void AddField(")
            .And.NotContain("public static void Place(");
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
