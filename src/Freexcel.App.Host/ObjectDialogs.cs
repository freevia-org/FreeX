using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum HyperlinkLinkType
{
    ExistingFileOrWebPage,
    CreateNewDocument,
    PlaceInThisDocument,
    EmailAddress
}

public sealed record HyperlinkDialogResult(
    HyperlinkLinkType LinkType,
    string Target,
    string DisplayText,
    string ScreenTip,
    string Bookmark);

public sealed class HyperlinkDialog : Window
{
    private readonly TextBox _targetBox = new();
    private readonly TextBox _displayBox = new();
    private readonly Button _screenTipButton = new() { Content = "_ScreenTip..." };
    private readonly Button _bookmarkButton = new() { Content = "_Bookmark..." };
    private readonly ListBox _linkTypes = new();
    private string _screenTip = "";
    private string _bookmark = "";

    public HyperlinkDialogResult Result { get; private set; }

    public HyperlinkDialog(string target = "https://", string displayText = "")
    {
        Result = CreateResult(target, displayText);
        Title = "Insert Hyperlink";
        Width = 560;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16) };
        _linkTypes.Width = 170;
        _linkTypes.Margin = new Thickness(0, 0, 12, 0);
        _linkTypes.ItemsSource = new[]
        {
            "Existing File or Web Page",
            "Create New Document",
            "Place in This Document",
            "E-mail Address"
        };
        _linkTypes.SelectedIndex = 0;
        DockPanel.SetDock(_linkTypes, Dock.Left);
        root.Children.Add(_linkTypes);

        var grid = DialogGrid(3);
        AddTextRow(grid, 0, "Text to _display:", _displayBox, displayText);
        AddTextRow(grid, 1, "_Address:", _targetBox, target);
        _screenTipButton.Click += ScreenTipButton_Click;
        _bookmarkButton.Click += BookmarkButton_Click;
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        _screenTipButton.Width = 96;
        _screenTipButton.Margin = new Thickness(0, 0, 8, 0);
        _bookmarkButton.Width = 96;
        buttonRow.Children.Add(_screenTipButton);
        buttonRow.Children.Add(_bookmarkButton);
        grid.Children.Add(buttonRow);
        Grid.SetRow(buttonRow, 2);
        Grid.SetColumn(buttonRow, 1);

        grid.Children.Add(DialogButtonRowFactory.Create(() =>
        {
            Result = CreateResult(_targetBox.Text, _displayBox.Text, SelectedLinkType, _screenTip, _bookmark);
            DialogResult = true;
        }, 72));
        Grid.SetRow(grid.Children[^1], 3);
        Grid.SetColumnSpan(grid.Children[^1], 2);
        root.Children.Add(grid);
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static HyperlinkDialogResult CreateResult(
        string target,
        string? displayText,
        HyperlinkLinkType linkType = HyperlinkLinkType.ExistingFileOrWebPage,
        string? screenTip = "",
        string? bookmark = "")
    {
        var normalizedTarget = target.Trim();
        var normalizedDisplay = string.IsNullOrWhiteSpace(displayText)
            ? normalizedTarget
            : displayText.Trim();
        return new HyperlinkDialogResult(
            linkType,
            normalizedTarget,
            normalizedDisplay,
            (screenTip ?? "").Trim(),
            (bookmark ?? "").Trim());
    }

    private HyperlinkLinkType SelectedLinkType => _linkTypes.SelectedIndex switch
    {
        1 => HyperlinkLinkType.CreateNewDocument,
        2 => HyperlinkLinkType.PlaceInThisDocument,
        3 => HyperlinkLinkType.EmailAddress,
        _ => HyperlinkLinkType.ExistingFileOrWebPage
    };

    private void ScreenTipButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ScreenTipDialog(_screenTip) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _screenTip = dialog.Result.Text;
        _screenTipButton.ToolTip = string.IsNullOrWhiteSpace(_screenTip) ? null : _screenTip;
    }

    private void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BookmarkDialog(_bookmark) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _bookmark = dialog.Result.Text;
        _bookmarkButton.ToolTip = string.IsNullOrWhiteSpace(_bookmark) ? null : _bookmark;
    }

    private void FocusInitialKeyboardTarget()
    {
        _targetBox.Focus();
        _targetBox.SelectAll();
        Keyboard.Focus(_targetBox);
    }

    private static Grid DialogGrid(int inputRows)
    {
        var grid = new Grid { Margin = new Thickness(16) };
        for (var index = 0; index < inputRows; index++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static void AddTextRow(Grid grid, int row, string label, TextBox box, string value)
    {
        grid.Children.Add(new Label
        {
            Content = label,
            Target = box,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);

        box.Text = value;
        box.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
    }
}

public sealed class ScreenTipDialog : TextEntryDialog
{
    public ScreenTipDialog(string? initialText = "")
        : base("Set Hyperlink ScreenTip", "_ScreenTip text:", initialText)
    {
    }
}

public sealed class BookmarkDialog : TextEntryDialog
{
    public BookmarkDialog(string? initialText = "")
        : base("Select Place in Document", "_Bookmark or cell reference:", initialText)
    {
    }
}

public sealed record TextEntryDialogResult(string Text);

public class TextEntryDialog : Window
{
    private readonly TextBox _textBox = new();

    public TextEntryDialogResult Result { get; private set; }

    public TextEntryDialog(string title, string label, string? initialText = "")
    {
        Result = CreateResult(initialText);
        Title = title;
        Width = 420;
        Height = 170;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _textBox.Text = initialText ?? "";
        Content = ObjectSizeDialog.CreateSingleInputContent(label, _textBox, () =>
        {
            Result = CreateResult(_textBox.Text);
            DialogResult = true;
        });
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static TextEntryDialogResult CreateResult(string? text) => new((text ?? "").Trim());

    private void FocusInitialKeyboardTarget()
    {
        _textBox.Focus();
        _textBox.SelectAll();
        Keyboard.Focus(_textBox);
    }
}

public sealed record ThreadedCommentDialogResult(string? ReplyText, bool IsResolved);

public sealed class ThreadedCommentDialog : Window
{
    private readonly TextBox _replyBox = new() { AcceptsReturn = true, MinLines = 3, MaxLines = 6 };
    private readonly CheckBox _resolveBox;

    public ThreadedCommentDialogResult Result { get; private set; } = new(null, false);

    public ThreadedCommentDialog(string cellRef, ThreadedComment? existing)
    {
        Title = $"Comment — {cellRef}";
        Width = 480;
        MinHeight = 280;
        MaxHeight = 600;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _resolveBox = new CheckBox
        {
            Content = "_Mark as Resolved",
            IsChecked = existing?.IsResolved ?? false,
            Margin = new Thickness(0, 4, 0, 8)
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        // Button row at bottom
        var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
        ok.Click += (_, _) =>
        {
            Result = new ThreadedCommentDialogResult(
                string.IsNullOrWhiteSpace(_replyBox.Text) ? null : _replyBox.Text.Trim(),
                _resolveBox.IsChecked == true);
            DialogResult = true;
        };
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        DockPanel.SetDock(btnRow, Dock.Bottom);
        root.Children.Add(btnRow);

        // Content
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 300 };
        var threadPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (existing is not null)
        {
            threadPanel.Children.Add(BuildMessage(existing.Author, existing.Text, isRoot: true));
            foreach (var reply in existing.Replies)
                threadPanel.Children.Add(BuildMessage(reply.Author, reply.Text, isRoot: false));
        }
        scroll.Content = threadPanel;

        var inner = new StackPanel();
        inner.Children.Add(scroll);
        inner.Children.Add(new Label { Content = existing is null ? "_Comment:" : "_Reply:", Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 2) });
        inner.Children.Add(_replyBox);
        inner.Children.Add(_resolveBox);
        root.Children.Add(inner);

        Content = root;
        Loaded += (_, _) => { _replyBox.Focus(); Keyboard.Focus(_replyBox); };
    }

    private static Border BuildMessage(string author, string text, bool isRoot)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(new TextBlock
        {
            Text = author,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Foreground = new SolidColorBrush(isRoot ? Color.FromRgb(0x1F, 0x49, 0x7D) : Color.FromRgb(0x40, 0x40, 0x40))
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 2, 0, 0)
        });
        return new Border
        {
            Child = panel,
            Background = new SolidColorBrush(isRoot ? Color.FromRgb(0xF0, 0xF4, 0xF8) : Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4)
        };
    }
}
