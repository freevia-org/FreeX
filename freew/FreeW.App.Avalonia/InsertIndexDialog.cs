using Avalonia;
using Avalonia.Controls;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Avalonia;

internal sealed class InsertIndexDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle Chrome =
        AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly TextBox _identifier = new() { MinWidth = 300 };

    internal InsertIndexDialog(string? identifier = null)
    {
        var state = InsertIndexDialogPlanner.BuildInitialState(identifier);
        Title = InsertIndexDialogPlanner.Title;
        Width = InsertIndexDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _identifier.Text = state.Identifier;
        AvaloniaCompactDialogChrome.ApplyTextBox(_identifier, Chrome);

        var insert = Button(InsertIndexDialogPlanner.InsertButtonLabel, Accept, isDefault: true);
        var cancel = Button("Cancel", () => Close(null), isCancel: true);
        Content = new StackPanel
        {
            Margin = new Thickness(16, 14),
            Children =
            {
                new TextBlock
                {
                    Text = InsertIndexDialogPlanner.IdentifierLabel,
                    Margin = new Thickness(0, 0, 0, 6)
                },
                _identifier,
                new TextBlock
                {
                    Text = InsertIndexDialogPlanner.IdentifierHint,
                    Opacity = 0.72,
                    Margin = new Thickness(0, 5, 0, 0)
                },
                AvaloniaCompactDialogChrome.CreateActionRow(
                    [insert, cancel],
                    new Thickness(0, 14, 0, 0))
            }
        };

        Opened += (_, _) =>
        {
            _identifier.Focus();
            _identifier.SelectAll();
        };
    }

    internal static Task<InsertIndexDialogResult?> ShowAsync(Window owner, string? identifier = null) =>
        new InsertIndexDialog(identifier).ShowDialog<InsertIndexDialogResult?>(owner);

    internal InsertIndexDialogResult BuildResultForTests(string? identifier)
    {
        _identifier.Text = identifier;
        return BuildResult();
    }

    private InsertIndexDialogResult BuildResult() =>
        InsertIndexDialogPlanner.BuildResult(new InsertIndexDialogState(_identifier.Text ?? string.Empty));

    private void Accept() => Close(BuildResult());

    private static Button Button(string label, Action click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, Chrome, minWidth: 84, isDefault: isDefault);
        button.Click += (_, _) => click();
        return button;
    }
}
