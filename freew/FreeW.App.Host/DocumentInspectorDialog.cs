using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// The Document Inspector modal: runs the pure <see cref="DocumentInspector.Inspect(TextDocument)"/>
/// over the editor's model, lists each metadata category with the count found and a checkbox, and on
/// "Remove" returns the user's selection so the caller can apply the matching removal ops. Code-only to
/// match the rest of the FreeW window style. Categories with nothing found are shown disabled (unchecked)
/// so the report still tells the user the document is clean in that respect.
/// </summary>
internal sealed class DocumentInspectorDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly CheckBox _comments;
    private readonly CheckBox _revisions;
    private readonly CheckBox _properties;
    private readonly CheckBox _bookmarks;

    private DocumentInspectorDialog(Window owner, InspectionResult result)
    {
        Owner = owner;
        Title = UiText.Get("DocumentInspector_Title");
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = result.IsClean
                ? UiText.Get("DocumentInspector_Clean_Message")
                : UiText.Get("DocumentInspector_Review_Message"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40))
        });

        _comments = AddRow(panel, UiText.Get("DocumentInspector_Comments_Label"), result.Comments,
            UiText.Get("DocumentInspector_Comments_HelpText"));
        _revisions = AddRow(panel, UiText.Get("DocumentInspector_Revisions_Label"), result.Revisions,
            UiText.Get("DocumentInspector_Revisions_HelpText"));
        _properties = AddRow(panel, UiText.Get("DocumentInspector_Properties_Label"), result.NonEmptyProperties,
            UiText.Get("DocumentInspector_Properties_HelpText"));
        _bookmarks = AddRow(panel, UiText.Get("DocumentInspector_Bookmarks_Label"), result.Bookmarks,
            UiText.Get("DocumentInspector_Bookmarks_HelpText"));

        var removePlan = DocumentInspectorDialogPlanner.ActionButtons[0];
        var remove = new Button
        {
            Content = removePlan.Label,
            MinWidth = 120,
            IsDefault = removePlan.IsDefault,
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 8, 0)
        };
        remove.Click += (_, _) => { DialogResult = true; };

        var cancelPlan = DocumentInspectorDialogPlanner.ActionButtons[1];
        var cancel = new Button
        {
            Content = cancelPlan.Label,
            MinWidth = 84,
            IsCancel = cancelPlan.IsCancel,
            Padding = new Thickness(6, 3, 6, 3)
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 4)
        };
        buttons.Children.Add(remove);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
    }

    /// <summary>
    /// Show the inspector for <paramref name="result"/>. Returns the user's removal selection when they
    /// clicked Remove with at least one category ticked, or null when they cancelled / removed nothing.
    /// </summary>
    public static InspectorRemovalChoice? Show(Window? owner, InspectionResult result)
    {
        var dialog = new DocumentInspectorDialog(owner!, result);
        if (dialog.ShowDialog() != true)
            return null;

        var choice = new InspectorRemovalChoice(
            Comments: dialog._comments.IsChecked == true,
            Revisions: dialog._revisions.IsChecked == true,
            Properties: dialog._properties.IsChecked == true,
            Bookmarks: dialog._bookmarks.IsChecked == true);

        return choice.Any ? choice : null;
    }

    // A checkbox row: "<Category> — <count> found" plus a hint line. When nothing was found the row is
    // disabled and left unchecked; otherwise it is pre-checked so the common "remove everything" path is
    // one click.
    private static CheckBox AddRow(StackPanel panel, string category, int count, string hint)
    {
        var found = count > 0;
        var header = new TextBlock { Margin = new Thickness(0, 0, 0, 0) };
        header.Inlines.Add(new System.Windows.Documents.Run(category)
        {
            FontWeight = FontWeights.SemiBold
        });
        header.Inlines.Add(new System.Windows.Documents.Run(
            found
                ? UiText.Format(
                    "DocumentInspector_FoundCount_Format",
                    count.ToString("N0", CultureInfo.CurrentCulture))
                : UiText.Get("DocumentInspector_NoneFound")));

        var check = new CheckBox
        {
            Content = header,
            IsChecked = found,
            IsEnabled = found,
            Margin = new Thickness(0, 6, 0, 0)
        };

        panel.Children.Add(check);
        panel.Children.Add(new TextBlock
        {
            Text = hint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 1, 0, 4),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80))
        });

        return check;
    }
}
