using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// AV-REVIEW: Word Count dialog (modal). Shows the document's words, characters (with and without spaces),
/// paragraphs and lines, computed from the model via <see cref="DocumentStatistics"/>. Mirrors Word's
/// Review → Word Count dialog. Read-only; closes on OK.
/// </summary>
internal sealed class WordCountDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    public WordCountDialog(DocumentStatistics stats)
    {
        var plan = StatisticsDialogPlanner.Build(stats, StatisticsDialogDepth.Compact);
        Title = plan.Title;
        Width = 300;
        Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var grid = new Grid
        {
            Margin = new Thickness(16, 14, 16, 0),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        for (var index = 0; index < plan.Rows.Count; index++)
            AddStatRow(grid, index, plan.Rows[index]);

        var ok = new Button { Content = UiText.Get("Common_OkText"), IsDefault = true, IsCancel = true, MinWidth = 72 };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        ok.Click += (_, _) => Close();

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok], new Thickness(16, 12, 16, 14));

        DockPanel.SetDock(buttons, global::Avalonia.Controls.Dock.Bottom);
        Content = new DockPanel
        {
            LastChildFill = true,
            Children = { buttons, grid },
        };
    }

    private static void AddStatRow(Grid grid, int row, StatisticsDialogRow plan)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var name = new TextBlock
        {
            Text = plan.Label,
            Margin = new Thickness(0, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(name, row);
        Grid.SetColumn(name, 0);

        var amount = new TextBlock
        {
            Text = plan.Value,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(amount, row);
        Grid.SetColumn(amount, 1);

        grid.Children.Add(name);
        grid.Children.Add(amount);
    }
}
