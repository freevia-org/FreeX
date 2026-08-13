using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>Avalonia counterpart of WPF's seeded Insert SmartArt dialog used by Edit Text.</summary>
internal sealed class SmartArtEditDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle ChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;
    private readonly ComboBox _kindBox;
    private readonly TextBox _nodeTextBox;
    private readonly TextBlock _status;

    private SmartArtEditDialog(SmartArt seed)
    {
        var text = SmartArtDialogPlanner.ResolveText(UiText.Get);
        Title = text.EditTitle;
        Width = 430;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var state = SmartArtDialogPlanner.BuildInitialState(seed);
        _kindBox = new ComboBox
        {
            ItemsSource = Enum.GetValues<SmartArtKind>(),
            SelectedItem = state.Kind,
            MinWidth = 180,
        };
        _nodeTextBox = new TextBox
        {
            Text = string.Join(Environment.NewLine, state.NodeTexts),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 170,
            MinWidth = 360,
        };
        _status = new TextBlock();

        AvaloniaCompactDialogChrome.ApplyComboBox(_kindBox, ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_nodeTextBox, ChromeStyle, fixedHeight: false);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, ChromeStyle, new Thickness(0, 6, 0, 0));

        var ok = CreateButton("OK", isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = CreateButton("Cancel", isCancel: true);
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = text.LayoutLabel },
                _kindBox,
                new TextBlock { Text = text.EditNodeTextLabel, Margin = new Thickness(0, 8, 0, 0) },
                _nodeTextBox,
                _status,
                AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 10, 0, 0)),
            },
        };

        Opened += (_, _) =>
        {
            _nodeTextBox.Focus();
            _nodeTextBox.SelectAll();
        };
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
                return;
            args.Handled = true;
            Close(null);
        };
    }

    public static Task<SmartArt?> ShowAsync(Window owner, SmartArt seed) =>
        new SmartArtEditDialog(seed).ShowDialog<SmartArt?>(owner);

    private void Accept()
    {
        var kind = _kindBox.SelectedItem is SmartArtKind selected ? selected : SmartArtKind.List;
        if (!SmartArtDialogPlanner.TryBuildResult(
                kind,
                (_nodeTextBox.Text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
                out var result,
                out var errorMessage,
                UiText.Get))
        {
            _status.Text = errorMessage ?? SmartArtDialogPlanner.ResolveText(UiText.Get).EmptyNodesValidationMessage;
            return;
        }
        Close(result);
    }

    private static Button CreateButton(string text, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, ChromeStyle, minWidth: 84, isDefault: isDefault);
        return button;
    }
}
