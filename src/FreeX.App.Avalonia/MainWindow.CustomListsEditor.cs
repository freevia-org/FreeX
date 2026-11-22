using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;
using FreeX.App.Localization;

namespace FreeX.App.Avalonia;

public partial class MainWindow
{
    /// <summary>Excel's File > Options > Advanced > Edit Custom Lists, as a one-list-per-line editor.</summary>
    private static async Task ShowCustomListsEditorAsync(Window owner, FreeXOptionsDialogSession session)
    {
        var editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            // WPF's editor shows an auto vertical scrollbar and hides the horizontal one (no wrap).
            Height = 220,
            Text = CustomListsTextPlanner.Format(session.EditedCustomLists),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Hidden);
        AutomationProperties.SetAutomationId(editor, "OptionsCustomListsEditor");
        AutomationProperties.SetName(editor, UiText.Get("Options_CustomListsEditorAutomationName"));

        var ok = new Button { Content = UiText.Get("Options_OK"), MinWidth = 80, IsDefault = true };
        AutomationProperties.SetAutomationId(ok, "OptionsCustomListsOkButton");
        var cancel = new Button { Content = UiText.Get("Options_Cancel"), MinWidth = 80, IsCancel = true };
        AutomationProperties.SetAutomationId(cancel, "OptionsCustomListsCancelButton");

        var dialog = new Window
        {
            Title = UiText.Get("Options_CustomListsTitle"),
            // WPF's 460 is the outer window width; Avalonia sizes are client sizes.
            Width = 460 - WpfNoResizeFrameWidth,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = UiText.Get("Options_CustomListsDescription"), TextWrapping = TextWrapping.Wrap },
                    editor,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { ok, cancel },
                    },
                },
            },
        };
        ok.Click += (_, _) =>
        {
            session.EditedCustomLists = CustomListsTextPlanner.Parse(editor.Text)
                .Select(list => (IReadOnlyList<string>)list).ToList();
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }
}
