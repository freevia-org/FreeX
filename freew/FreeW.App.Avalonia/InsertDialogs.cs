using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

/// <summary>
/// AV-INSERT2: Small modal input dialogs for the second tier of Insert-tab commands — Insert Hyperlink,
/// Insert Bookmark (+ Go To), and Insert Quick Part. Each mirrors the existing Avalonia dialog pattern
/// (a non-resizable, owner-centred <see cref="Window"/> that returns its result via a public property,
/// awaited by the <c>MainWindow</c> launcher). The dialogs are deliberately thin: they collect input and
/// hand it to the editor's model-backed, undoable insert methods — no model logic lives here.
/// </summary>
public sealed class HyperlinkDialog : FreeWDialogWindow
{
    private readonly TextBox _displayBox = new()
    {
        MinWidth = 280,
        PlaceholderText = InsertDialogTextResources.Hyperlink.DisplayPlaceholder,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly TextBox _addressBox = new()
    {
        MinWidth = 280,
        PlaceholderText = InsertDialogTextResources.Hyperlink.AddressPlaceholder,
        Margin = new Thickness(0, 6, 0, 0),
    };

    /// <summary>The display text the user typed (may be empty — the editor falls back to the address).</summary>
    public string? DisplayText { get; private set; }

    /// <summary>
    /// The link target the user typed, or null when the dialog was cancelled. An absolute URL is an external
    /// link; a value beginning with <c>'#'</c> is an internal bookmark anchor.
    /// </summary>
    public string? Address { get; private set; }

    public HyperlinkDialog(string? initialDisplay = null, string? initialAddress = null, string? title = null)
    {
        Title = title ?? InsertDialogTextResources.Hyperlink.Title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _displayBox.Text = initialDisplay ?? string.Empty;
        _addressBox.Text = initialAddress ?? string.Empty;
        AvaloniaCompactDialogChrome.ApplyTextBox(_displayBox, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_addressBox, InsertDialogLayout.ChromeStyle);

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, InsertDialogTextResources.Hyperlink.DisplayLabel, _displayBox);
        InsertDialogLayout.AddLabeledRow(grid, 1, InsertDialogTextResources.Hyperlink.AddressLabel, _addressBox);

        var buttons = InsertDialogLayout.OkCancelRow(
            ok: () =>
            {
                var addr = _addressBox.Text?.Trim();
                if (string.IsNullOrEmpty(addr))
                    return; // address is required; keep the dialog open
                DisplayText = _displayBox.Text?.Trim();
                Address = addr;
                Close();
            },
            cancel: Close);

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(buttons);
        Content = outer;

        _addressBox.KeyDown += (_, e) => InsertDialogLayout.HandleEnterEscape(e, buttons);
        _displayBox.KeyDown += (_, e) => InsertDialogLayout.HandleEnterEscape(e, buttons);
    }
}

/// <summary>
/// AV-LINKS: Edits the ScreenTip for the hyperlink at the caret. A blank OK result clears the ScreenTip.
/// </summary>
public sealed class ScreenTipDialog : FreeWDialogWindow
{
    private readonly TextBox _tipBox = new()
    {
        MinWidth = 280,
        PlaceholderText = InsertDialogTextResources.ScreenTip.Placeholder,
        Margin = new Thickness(0, 6, 0, 0),
    };

    /// <summary>The entered ScreenTip, empty to clear it, or null when cancelled.</summary>
    public string? ScreenTip { get; private set; }

    public ScreenTipDialog(string? initialTip = null)
    {
        Title = InsertDialogTextResources.ScreenTip.Title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _tipBox.Text = initialTip ?? string.Empty;
        AvaloniaCompactDialogChrome.ApplyTextBox(_tipBox, InsertDialogLayout.ChromeStyle);

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, InsertDialogTextResources.ScreenTip.Label, _tipBox);

        var buttons = InsertDialogLayout.OkCancelRow(
            ok: () =>
            {
                ScreenTip = _tipBox.Text?.Trim() ?? string.Empty;
                Close();
            },
            cancel: Close);

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(buttons);
        Content = outer;

        _tipBox.KeyDown += (_, e) => InsertDialogLayout.HandleEnterEscape(e, buttons);
    }
}

/// <summary>
/// AV-INSERT2: Insert Bookmark dialog. Collects a bookmark name to mark the caret's paragraph, and offers a
/// "Go To" list of the document's existing bookmark names so the user can jump to one. Returns either a
/// <see cref="BookmarkName"/> (to add) or a <see cref="GoToName"/> (to navigate), never both.
/// </summary>
public sealed class BookmarkDialog : FreeWDialogWindow
{
    private readonly TextBox _nameBox = new()
    {
        MinWidth = 240,
        PlaceholderText = InsertDialogTextResources.Bookmark.NamePlaceholder,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly ComboBox _existing = new()
    {
        MinWidth = 240,
        Margin = new Thickness(0, 6, 0, 0),
    };

    /// <summary>The new bookmark name to add at the caret, or null when none was requested.</summary>
    public string? BookmarkName { get; private set; }

    /// <summary>The existing bookmark name to navigate to, or null when none was requested.</summary>
    public string? GoToName { get; private set; }

    public BookmarkDialog(IReadOnlyList<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(existingNames);

        Title = InsertDialogTextResources.Bookmark.Title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _existing.ItemsSource = existingNames;
        if (existingNames.Count > 0)
            _existing.SelectedIndex = 0;
        _existing.IsEnabled = existingNames.Count > 0;
        AvaloniaCompactDialogChrome.ApplyTextBox(_nameBox, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_existing, InsertDialogLayout.ChromeStyle);

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, InsertDialogTextResources.Bookmark.NameLabel, _nameBox);
        InsertDialogLayout.AddLabeledRow(grid, 1, InsertDialogTextResources.Bookmark.GoToLabel, _existing);

        // Add (creates the bookmark), Go To (navigates), Close.
        var addButton = InsertDialogLayout.MakeButton(InsertDialogTextResources.Bookmark.AddButton, (_, _) =>
        {
            var name = _nameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name))
                return;
            BookmarkName = name;
            Close();
        });
        var goToButton = InsertDialogLayout.MakeButton(InsertDialogTextResources.Bookmark.GoToButton, (_, _) =>
        {
            if (_existing.SelectedItem is string s && !string.IsNullOrEmpty(s))
            {
                GoToName = s;
                Close();
            }
        });
        var closeButton = InsertDialogLayout.MakeButton(InsertDialogTextResources.Bookmark.CloseButton, (_, _) => Close());
        var btnRow = AvaloniaCompactDialogChrome.CreateActionRow([addButton, goToButton, closeButton], new Thickness(14, 12, 14, 12));

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(btnRow);
        Content = outer;

        _nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
    }
}

/// <summary>
/// AV-LINKS: Picks an existing bookmark and links the current selection to it.
/// </summary>
public sealed class LinkBookmarkDialog : FreeWDialogWindow
{
    private readonly ComboBox _existing = new()
    {
        MinWidth = 260,
        Margin = new Thickness(0, 6, 0, 0),
    };

    /// <summary>The chosen bookmark name, or null when cancelled.</summary>
    public string? BookmarkName { get; private set; }

    public LinkBookmarkDialog(IReadOnlyList<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(existingNames);

        Title = InsertDialogTextResources.LinkBookmark.Title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _existing.ItemsSource = existingNames;
        if (existingNames.Count > 0)
            _existing.SelectedIndex = 0;
        _existing.IsEnabled = existingNames.Count > 0;
        AvaloniaCompactDialogChrome.ApplyComboBox(_existing, InsertDialogLayout.ChromeStyle);

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, InsertDialogTextResources.LinkBookmark.BookmarkLabel, _existing);

        var linkButton = InsertDialogLayout.MakeButton(InsertDialogTextResources.LinkBookmark.LinkButton, (_, _) =>
        {
            if (_existing.SelectedItem is string s && !string.IsNullOrWhiteSpace(s))
            {
                BookmarkName = s;
                Close();
            }
        });
        var closeButton = InsertDialogLayout.MakeButton(InsertDialogTextResources.LinkBookmark.CloseButton, (_, _) => Close());
        var btnRow = AvaloniaCompactDialogChrome.CreateActionRow([linkButton, closeButton], new Thickness(14, 12, 14, 12));

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(btnRow);
        Content = outer;
    }
}

/// <summary>
/// AV-INSERT2: Insert Quick Part dialog. Collects a snippet body (multi-line) to insert at the caret; if a
/// <see cref="QuickPartLibrary"/>-style name list is supplied the user can also pick a saved snippet. Returns
/// the text to insert via <see cref="SnippetText"/>, or null on cancel.
/// </summary>
public sealed class QuickPartDialog : FreeWDialogWindow
{
    private readonly TextBox _textBox = new()
    {
        MinWidth = 320,
        MinHeight = 90,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        PlaceholderText = InsertDialogTextResources.QuickPart.SnippetPlaceholder,
        Margin = new Thickness(0, 6, 0, 0),
    };

    /// <summary>The snippet body to insert, or null when the dialog was cancelled / empty.</summary>
    public string? SnippetText { get; private set; }

    public QuickPartDialog(string? initialText = null)
    {
        Title = InsertDialogTextResources.QuickPart.Title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _textBox.Text = initialText ?? string.Empty;
        AvaloniaCompactDialogChrome.ApplyTextBox(_textBox, InsertDialogLayout.ChromeStyle, fixedHeight: false);

        var label = new TextBlock
        {
            Text = InsertDialogTextResources.QuickPart.TextLabel,
            Margin = new Thickness(14, 12, 14, 0),
        };

        var box = new Border { Margin = new Thickness(14, 0, 14, 0), Child = _textBox };

        var buttons = InsertDialogLayout.OkCancelRow(
            ok: () =>
            {
                var text = _textBox.Text;
                if (string.IsNullOrEmpty(text))
                    return;
                SnippetText = text;
                Close();
            },
            cancel: Close);

        var outer = new StackPanel();
        outer.Children.Add(label);
        outer.Children.Add(box);
        outer.Children.Add(buttons);
        Content = outer;
    }
}

/// <summary>Shared layout helpers for the AV-INSERT2 input dialogs (label rows, OK/Cancel, key handling).</summary>
internal static class InsertDialogLayout
{
    public static readonly AvaloniaCompactDialogChromeStyle ChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    public static void AddLabeledRow(
        Grid grid,
        int row,
        string label,
        Control field,
        double? rowHeight = null,
        Thickness? labelMargin = null)
    {
        grid.RowDefinitions.Add(new RowDefinition
        {
            Height = rowHeight is { } height ? new GridLength(height) : GridLength.Auto,
        });

        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = labelMargin ?? new Thickness(0, 6, 8, 0),
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    public static Button MakeButton(string content, EventHandler<RoutedEventArgs> onClick)
    {
        var btn = new Button
        {
            Content = content,
        };
        AvaloniaCompactDialogChrome.ApplyButton(btn, ChromeStyle, minWidth: 84);
        btn.Click += onClick;
        return btn;
    }

    public static StackPanel OkCancelRow(Action ok, Action cancel)
    {
        var okButton = MakeButton(InsertDialogTextResources.OkButton, (_, _) => ok());
        okButton.IsDefault = true;
        var cancelButton = MakeButton(InsertDialogTextResources.CancelButton, (_, _) => cancel());
        cancelButton.IsCancel = true;
        return AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(14, 12, 14, 12));
    }

    /// <summary>Enter invokes the OK (first) button; Escape invokes Cancel (second). Used on input boxes.</summary>
    public static void HandleEnterEscape(KeyEventArgs e, StackPanel okCancelRow)
    {
        if (e.Key == Key.Enter && okCancelRow.Children.Count > 0 && okCancelRow.Children[0] is Button ok)
        {
            // Buttons here use Click handlers (not Command), so raise a click programmatically.
            ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && okCancelRow.Children.Count > 1 && okCancelRow.Children[1] is Button cancel)
        {
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
        }
    }
}
