using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>Avalonia projection of the shared Word-style Change Case picker.</summary>
internal sealed class ChangeCaseDialog : FreeWDialogWindow
{
    private ChangeCaseDialog()
    {
        Title = UiText.Get("Ribbon_Command_ChangeCase_Label");
        Width = 232;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var panel = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 4,
        };
        foreach (var choice in ChangeCaseDialogPlanner.Choices)
        {
            var captured = choice;
            var button = new Button
            {
                Content = choice.Label,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            button.Click += (_, _) => Close(captured.Kind);
            panel.Children.Add(button);
        }

        Content = panel;
        Opened += (_, _) => panel.Children[0].Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            Close(null);
            e.Handled = true;
        };
    }

    public static Task<CaseKind?> ShowAsync(Window owner) =>
        new ChangeCaseDialog().ShowDialog<CaseKind?>(owner);
}
