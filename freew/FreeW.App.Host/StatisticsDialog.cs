using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A small read-only modal showing document statistics — word/character/paragraph/sentence counts,
/// estimated reading time, average words per sentence, and the Flesch Reading Ease readability score —
/// computed by the pure <see cref="DocumentStatistics"/> helper. Code-only to match the rest of the
/// FreeW window style; purely informational, so it has a single Close button.
/// </summary>
internal sealed class StatisticsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    public StatisticsDialog(Window owner, DocumentStatistics stats)
    {
        Owner = owner;
        Title = "Word Count";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var grid = new Grid { Margin = new Thickness(16, 14, 16, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;
        AddRow(grid, ref row, "Words", Number(stats.Words));
        AddRow(grid, ref row, "Characters (with spaces)", Number(stats.CharactersWithSpaces));
        AddRow(grid, ref row, "Characters (no spaces)", Number(stats.CharactersWithoutSpaces));
        AddRow(grid, ref row, "Paragraphs", Number(stats.Paragraphs));
        AddRow(grid, ref row, "Sentences", Number(stats.Sentences));
        AddSeparator(grid, ref row);
        AddRow(grid, ref row, "Reading time", FormatReadingTime(stats.ReadingTimeMinutes));
        AddRow(grid, ref row, "Words per sentence", stats.AverageWordsPerSentence.ToString("0.0", CultureInfo.CurrentCulture));
        AddRow(grid, ref row, "Readability (Flesch)",
            $"{stats.FleschReadingEase.ToString("0.0", CultureInfo.CurrentCulture)} — {DescribeEase(stats.FleschReadingEase)}");

        // Reuse the shared OK-only button row (accelerator, automation name, shell strings; the single OK
        // button is IsDefault + IsCancel so Enter/Esc both close). Matches FreeX's informational dialogs.
        var buttons = DialogButtonRowFactory.CreateOkOnly(Close, buttonWidth: 84, rowMargin: new Thickness(16, 4, 16, 12));

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(buttons);
        Content = outer;
    }

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    // "less than a minute" for 0, "1 minute" / "N minutes" otherwise.
    private static string FormatReadingTime(int minutes) => minutes switch
    {
        <= 0 => "less than a minute",
        1 => "1 minute",
        _ => $"{minutes} minutes"
    };

    // The standard Flesch Reading Ease bands, summarised to a short label.
    private static string DescribeEase(double score) => score switch
    {
        >= 90 => "very easy",
        >= 70 => "easy",
        >= 60 => "plain English",
        >= 50 => "fairly difficult",
        >= 30 => "difficult",
        _ => "very difficult"
    };

    private static void AddRow(Grid grid, ref int row, string label, string value)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 4, 16, 4),
            Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40))
        };
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = value,
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(valueBlock, row);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);

        row++;
    }

    private static void AddSeparator(Grid grid, ref int row)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var line = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 6, 0, 6),
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))
        };
        Grid.SetRow(line, row);
        Grid.SetColumn(line, 0);
        Grid.SetColumnSpan(line, 2);
        grid.Children.Add(line);
        row++;
    }
}
