using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>Thin WPF projection of the shared Link-to-Bookmark choice contract.</summary>
internal sealed class LinkBookmarkDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly LinkBookmarkDialogPresentation _presentation;
    private readonly ListBox _bookmarks = new()
    {
        MinWidth = 280,
        MinHeight = 120,
    };

    internal LinkBookmarkDialog()
        : this(LinkBookmarkDialogPlanner.Build([]))
    {
    }

    private LinkBookmarkDialog(LinkBookmarkDialogPresentation presentation)
    {
        _presentation = presentation;
        Title = presentation.Title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;

        _bookmarks.ItemsSource = presentation.BookmarkNames;
        _bookmarks.SelectedIndex = presentation.SelectedIndex;
        _bookmarks.IsEnabled = !presentation.IsEmpty;
        _bookmarks.MouseDoubleClick += (_, _) => Accept();

        var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        body.Children.Add(new TextBlock
        {
            Text = presentation.BookmarkLabel,
            Margin = new Thickness(0, 0, 0, 6),
        });
        body.Children.Add(_bookmarks);
        body.Children.Add(DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 78,
            rowMargin: new Thickness(0, 12, 0, 0),
            acceptContent: presentation.AcceptLabel,
            cancelContent: presentation.CancelLabel));
        Content = body;

        Loaded += (_, _) => _bookmarks.Focus();
    }

    public string? Result { get; private set; }

    public static string? Ask(Window? owner, LinkBookmarkDialogPresentation presentation)
    {
        var dialog = new LinkBookmarkDialog(presentation) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Accept()
    {
        if (LinkBookmarkDialogPlanner.PlanAcceptance(_presentation, _bookmarks.SelectedIndex) is not { } bookmark)
            return;

        Result = bookmark;
        DialogResult = true;
    }
}
