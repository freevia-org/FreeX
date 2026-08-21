using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private bool _suppressWorksheetContextMiniToolbar;
    private Popup? _worksheetContextMiniToolbar;

    private void ShowWorksheetContextMiniToolbar(
        WorksheetContextMenuTargetKind targetKind,
        Point gridPosition)
    {
        CloseWorksheetContextMiniToolbar();
        if (targetKind != WorksheetContextMenuTargetKind.Worksheet)
            return;

        var toolbar = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(127, 127, 127)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(4),
            SnapsToDevicePixels = true,
            Child = CreateWorksheetContextMiniToolbarContent()
        };
        AutomationProperties.SetName(toolbar, "Mini toolbar");
        AutomationProperties.SetAutomationId(toolbar, "WorksheetContextMiniToolbar");

        _worksheetContextMiniToolbar = new Popup
        {
            AllowsTransparency = true,
            PlacementTarget = SheetGrid,
            Placement = PlacementMode.RelativePoint,
            HorizontalOffset = gridPosition.X,
            VerticalOffset = gridPosition.Y - 44,
            StaysOpen = true,
            Child = toolbar,
            IsOpen = true
        };
    }

    private UIElement CreateWorksheetContextMiniToolbarContent()
    {
        var commands = new StackPanel { Orientation = Orientation.Horizontal };

        var fontName = new ComboBox
        {
            Width = 76,
            Height = 26,
            IsEditable = true,
            Text = "Aptos Narrow",
            Margin = new Thickness(0, 0, 3, 0),
            ItemsSource = new[] { "Aptos Narrow", "Aptos", "Calibri", "Arial" }
        };
        fontName.SelectionChanged += FontNameBox_SelectionChanged;
        fontName.KeyDown += FontNameBox_KeyDown;
        fontName.LostKeyboardFocus += FontNameBox_LostKeyboardFocus;
        AutomationProperties.SetName(fontName, "Font");
        commands.Children.Add(fontName);

        var fontSize = new ComboBox
        {
            Width = 42,
            Height = 26,
            IsEditable = true,
            Text = "11",
            Margin = new Thickness(0, 0, 3, 0),
            ItemsSource = new[] { "8", "9", "10", "11", "12", "14", "16", "18" }
        };
        fontSize.SelectionChanged += FontSizeBox_SelectionChanged;
        fontSize.KeyDown += FontSizeBox_KeyDown;
        fontSize.LostKeyboardFocus += FontSizeBox_LostKeyboardFocus;
        AutomationProperties.SetName(fontSize, "Font Size");
        commands.Children.Add(fontSize);

        commands.Children.Add(CreateWorksheetContextMiniToolbarButton("B", "Bold", () => ApplyFontToggleShortcut(FontToggleShortcut.Bold), FontWeights.Bold));
        commands.Children.Add(CreateWorksheetContextMiniToolbarButton("I", "Italic", () => ApplyFontToggleShortcut(FontToggleShortcut.Italic), fontStyle: FontStyles.Italic));
        commands.Children.Add(CreateWorksheetContextMiniToolbarButton("≡", "Center", () => ApplyHorizontalAlignment(FreeX.Core.Model.HorizontalAlignment.Center)));
        commands.Children.Add(CreateWorksheetContextMiniToolbarButton("▰", "Fill Color", () => FillColorBtn_Click(this, new RoutedEventArgs())));

        return commands;
    }

    private static Button CreateWorksheetContextMiniToolbarButton(
        string glyph,
        string automationName,
        Action action,
        FontWeight? fontWeight = null,
        FontStyle? fontStyle = null)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontWeight = fontWeight ?? FontWeights.Normal,
                FontStyle = fontStyle ?? FontStyles.Normal,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Width = 27,
            Height = 26,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 2, 0),
            Background = Brushes.White,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        button.Click += (_, _) => action();
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    private void CloseWorksheetContextMiniToolbar()
    {
        if (_worksheetContextMiniToolbar is not { } toolbar)
            return;

        toolbar.IsOpen = false;
        _worksheetContextMiniToolbar = null;
    }
}
