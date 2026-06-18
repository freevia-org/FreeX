using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.Core.Model;
using AvaloniaProofingHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Review ▸ Proofing (Thesaurus / Translate) and Insert ▸ Equation / Object.
    // Honest scope: the thesaurus uses a small built-in synonym map (see ThesaurusData),
    // translation is offline-unavailable (no network/service in this build), equation is
    // inserted as plain cell text (no true equation object), and object embedding is unsupported.

    private static string? FirstAlphabeticWord(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var builder = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
                builder.Append(ch);
            else if (builder.Length > 0)
                break;
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    private bool CommitProofingText(string text, string successStatus)
    {
        var address = _session.ActiveCell;
        _session.SelectCell(address);
        var result = _session.CommitCellText(text);
        RefreshShell(result.Success
            ? successStatus
            : result.ErrorMessage ?? "Could not update the cell.");
        return result.Success;
    }

    /// <summary>Review ▸ Thesaurus — look up synonyms for the active cell's first word.</summary>
    private async Task ShowThesaurusDialogAsync()
    {
        var address = _session.ActiveCell;
        var cellText = FormatEditText(_session.ActiveSheet.GetCell(address), address);
        var word = FirstAlphabeticWord(cellText);

        if (word is null)
        {
            RefreshShell("Thesaurus: select a cell containing a word.");
            return;
        }

        var synonyms = ThesaurusData.Lookup(word);

        var layout = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        layout.Children.Add(new TextBlock { Text = $"Looked up: {word}", FontWeight = FontWeight.SemiBold });

        var list = new ListBox
        {
            Height = 150,
            ItemsSource = synonyms,
        };
        if (synonyms.Count == 0)
            layout.Children.Add(new TextBlock { Text = "No synonyms found in the built-in word list.", TextWrapping = TextWrapping.Wrap });
        else
            layout.Children.Add(list);

        var dialog = new Window
        {
            Title = "Thesaurus",
            Width = 320,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaProofingHorizontalAlignment.Right,
        };
        var replace = new Button { Content = "Replace", Width = 90, IsEnabled = synonyms.Count > 0 };
        var close = new Button { Content = "Close", Width = 90 };
        replace.Click += (_, _) =>
        {
            var chosen = list.SelectedItem as string ?? (synonyms.Count > 0 ? synonyms[0] : null);
            if (chosen is not null && cellText is not null)
            {
                var index = cellText.IndexOf(word, System.StringComparison.OrdinalIgnoreCase);
                var updated = index >= 0
                    ? cellText.Remove(index, word.Length).Insert(index, chosen)
                    : chosen;
                CommitProofingText(updated, $"Replaced \"{word}\" with \"{chosen}\".");
            }
            dialog.Close();
        };
        close.Click += (_, _) => dialog.Close();
        buttons.Children.Add(replace);
        buttons.Children.Add(close);
        layout.Children.Add(buttons);

        dialog.Content = layout;
        await dialog.ShowDialog(this);
    }

    /// <summary>Review ▸ Translate — offline-honest notice (no translation service in this build).</summary>
    private async Task ShowTranslateDialogAsync()
    {
        var address = _session.ActiveCell;
        var cellText = FormatEditText(_session.ActiveSheet.GetCell(address), address);

        var layout = new StackPanel { Margin = new Thickness(16), Spacing = 10, Width = 360 };
        layout.Children.Add(new TextBlock { Text = "Translate", FontWeight = FontWeight.SemiBold });
        layout.Children.Add(new SelectableTextBlock
        {
            Text = string.IsNullOrEmpty(cellText) ? "(empty cell)" : cellText,
            TextWrapping = TextWrapping.Wrap,
        });
        layout.Children.Add(new TextBlock
        {
            Text = "Online translation isn't available in this build (no network service). " +
                   "The cell text is shown above for reference.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(120, 120, 120),
        });

        var close = new Button
        {
            Content = "Close",
            Width = 90,
            HorizontalAlignment = AvaloniaProofingHorizontalAlignment.Right,
        };
        var dialog = new Window
        {
            Title = "Translate",
            Width = 400,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        close.Click += (_, _) => dialog.Close();
        layout.Children.Add(close);
        dialog.Content = layout;
        await dialog.ShowDialog(this);
    }

    /// <summary>Insert ▸ Equation — type an equation; it is inserted into the active cell as text.</summary>
    private async Task ShowEquationDialogAsync()
    {
        var address = _session.ActiveCell;
        var current = FormatEditText(_session.ActiveSheet.GetCell(address), address);

        var input = new TextBox
        {
            Text = current,
            Width = 360,
            AcceptsReturn = false,
        };

        var symbols = new WrapPanel { MaxWidth = 360 };
        foreach (var symbol in new[] { "±", "×", "÷", "≤", "≥", "≠", "√", "π", "∑", "∞", "→", "²", "³" })
        {
            var btn = new Button { Content = symbol, Width = 40, Margin = new Thickness(2) };
            btn.Click += (_, _) =>
            {
                input.Text += symbol;
                input.CaretIndex = input.Text?.Length ?? 0;
            };
            symbols.Children.Add(btn);
        }

        var layout = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        layout.Children.Add(new TextBlock { Text = "Equation (inserted into the cell as text):", FontWeight = FontWeight.SemiBold });
        layout.Children.Add(input);
        layout.Children.Add(symbols);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaProofingHorizontalAlignment.Right,
        };
        var ok = new Button { Content = "Insert", Width = 90 };
        var cancel = new Button { Content = "Cancel", Width = 90 };
        ok.Click += (_, _) =>
        {
            var text = input.Text ?? string.Empty;
            CommitProofingText(text, "Inserted equation as cell text.");
            input.Tag = "ok";
            ((Window)layout.Parent!.Parent!).Close();
        };
        cancel.Click += (_, _) => ((Window)layout.Parent!.Parent!).Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        layout.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "Insert Equation",
            Width = 410,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = layout,
        };
        await dialog.ShowDialog(this);
    }

    /// <summary>Insert ▸ Object — embedding external OLE objects is not supported in this build.</summary>
    private void ShowInsertObjectUnsupported() =>
        RefreshShell("Embedding external objects isn't supported in this build.");
}
