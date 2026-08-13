using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Avalonia;

internal sealed class CommentReplyDialog : FreeWDialogWindow
{
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true,
        MinHeight = 90,
        Width = 340,
        TextWrapping = TextWrapping.Wrap,
    };

    private readonly TextBlock _status = new()
    {
        Foreground = Brushes.Red,
        IsVisible = false,
        Text = CommentDialogPresentationPlanner.Text.ReplyRequiredMessage,
    };

    public CommentReplyDialog()
    {
        Title = CommentDialogPresentationPlanner.Text.ReplyTitle;
        Width = 390;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var body = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = CommentDialogPresentationPlanner.Text.ReplyFieldLabel,
            FontWeight = FontWeight.SemiBold,
        });
        body.Children.Add(_text);
        body.Children.Add(_status);

        var ok = Button(CommentDialogPresentationPlanner.Text.ReplyActionLabel, Accept, isDefault: true);
        var cancel = Button(CommentDialogPresentationPlanner.Text.CancelActionLabel, () => Close(null), isCancel: true);
        body.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 6, 0, 0)));

        Content = body;
        Opened += (_, _) => _text.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
    }

    public static Task<string?> AskAsync(Window owner) =>
        new CommentReplyDialog().ShowDialog<string?>(owner);

    private void Accept()
    {
        var acceptance = CommentDialogPresentationPlanner.PlanReplyAcceptance(_text.Text);
        if (!acceptance.IsAccepted)
        {
            _status.Text = acceptance.ValidationMessage;
            _status.IsVisible = true;
            return;
        }

        Close(acceptance.Text);
    }

    private static Button Button(string label, Action click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 78,
            IsDefault = isDefault,
            IsCancel = isCancel,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        button.Click += (_, _) => click();
        return button;
    }

}

internal sealed class CommentListDialog : FreeWDialogWindow
{
    public CommentListDialog(IReadOnlyList<CommentListItem> items)
    {
        var presentation = CommentDialogPresentationPlanner.BuildList(items);
        Title = presentation.Title;
        Width = 460;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        ShowInTaskbar = false;

        var body = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        if (presentation.Rows.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = presentation.EmptyMessage,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            foreach (var row in presentation.Rows)
                body.Children.Add(BuildRow(row));
        }

        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var close = new Button
        {
            Content = CommentDialogPresentationPlanner.Text.CloseActionLabel,
            MinWidth = 78,
            IsDefault = true,
            IsCancel = true,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        close.Click += (_, _) => Close();

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([close], new Thickness(16, 10, 16, 14));
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        Content = new DockPanel
        {
            LastChildFill = true,
            Children = { buttonRow, scroll },
        };
    }

    public static Task ShowAsync(Window owner, IReadOnlyList<CommentListItem> items) =>
        new CommentListDialog(items).ShowDialog(owner);

    private static Control BuildRow(CommentListRowPresentation row)
    {
        var title = new TextBlock
        {
            Text = row.HeadingText,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var text = new TextBlock
        {
            Text = row.Body,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        };

        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(title);
        panel.Children.Add(text);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 7, 0, 8),
            Child = panel,
        };
    }

}
