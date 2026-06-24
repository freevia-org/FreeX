using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A small read-only modal showing document statistics — word/character/paragraph/sentence/line counts,
/// estimated reading time, average words per sentence, and the Flesch Reading Ease readability score —
/// computed by the pure <see cref="DocumentStatistics"/> helper. Includes a checkbox to include footnote
/// and endnote text in the counts (Word parity). Code-only to match the rest of the FreeW window style;
/// informational, so it has a single Close button.
/// </summary>
internal sealed class StatisticsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextDocument _document;
    private readonly Grid _grid;
    private bool _includeNotes;
    private int _currentRow;

    // Value TextBlocks keyed by label — updated when the checkbox changes.
    private readonly Dictionary<string, TextBlock> _valueBlocks = new();

    public StatisticsDialog(Window owner, TextDocument document)
    {
        _document = document;
        Owner = owner;
        Title = "Word Count";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _grid = new Grid { Margin = new Thickness(16, 14, 16, 8) };
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _currentRow = 0;
        AddRow("Words", "0");
        AddRow("Characters (with spaces)", "0");
        AddRow("Characters (no spaces)", "0");
        AddRow("Paragraphs", "0");
        AddRow("Lines", "0");
        AddRow("Sentences", "0");
        AddSeparator();
        AddRow("Reading time", "—");
        AddRow("Words per sentence", "—");
        AddRow("Readability (Flesch)", "—");

        // Include footnotes/endnotes checkbox (Word parity).
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var checkbox = new CheckBox
        {
            Content = "Include footnotes and endnotes",
            Margin = new Thickness(0, 8, 0, 2),
            IsChecked = false,
        };
        checkbox.Checked += (_, _) => { _includeNotes = true; RefreshValues(); };
        checkbox.Unchecked += (_, _) => { _includeNotes = false; RefreshValues(); };
        Grid.SetRow(checkbox, _currentRow);
        Grid.SetColumn(checkbox, 0);
        Grid.SetColumnSpan(checkbox, 2);
        _grid.Children.Add(checkbox);
        _currentRow++;

        var buttons = DialogButtonRowFactory.CreateOkOnly(Close, buttonWidth: 84, rowMargin: new Thickness(16, 4, 16, 12));

        var outer = new StackPanel();
        outer.Children.Add(_grid);
        outer.Children.Add(buttons);
        Content = outer;

        // Populate with initial values (no notes).
        RefreshValues();
    }

    private void RefreshValues()
    {
        var stats = DocumentStatistics.Compute(_document, _includeNotes);
        SetValue("Words", Number(stats.Words));
        SetValue("Characters (with spaces)", Number(stats.CharactersWithSpaces));
        SetValue("Characters (no spaces)", Number(stats.CharactersWithoutSpaces));
        SetValue("Paragraphs", Number(stats.Paragraphs));
        SetValue("Lines", Number(stats.Lines));
        SetValue("Sentences", Number(stats.Sentences));
        SetValue("Reading time", FormatReadingTime(stats.ReadingTimeMinutes));
        SetValue("Words per sentence", stats.AverageWordsPerSentence.ToString("0.0", CultureInfo.CurrentCulture));
        SetValue("Readability (Flesch)",
            $"{stats.FleschReadingEase.ToString("0.0", CultureInfo.CurrentCulture)} — {DescribeEase(stats.FleschReadingEase)}");
    }

    private void SetValue(string label, string value)
    {
        if (_valueBlocks.TryGetValue(label, out var block))
            block.Text = value;
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

    private void AddRow(string label, string initialValue)
    {
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 4, 16, 4),
            Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40))
        };
        Grid.SetRow(labelBlock, _currentRow);
        Grid.SetColumn(labelBlock, 0);
        _grid.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = initialValue,
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(valueBlock, _currentRow);
        Grid.SetColumn(valueBlock, 1);
        _grid.Children.Add(valueBlock);

        _valueBlocks[label] = valueBlock;
        _currentRow++;
    }

    private void AddSeparator()
    {
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var line = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 6, 0, 6),
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))
        };
        Grid.SetRow(line, _currentRow);
        Grid.SetColumn(line, 0);
        Grid.SetColumnSpan(line, 2);
        _grid.Children.Add(line);
        _currentRow++;
    }
}
