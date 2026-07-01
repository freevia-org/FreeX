using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.PivotUI;

namespace FreeX.App.Host;

public sealed class PivotStyleGalleryDialog : Window
{
    private readonly ListBox _styleGallery = new() { MinHeight = 260 };

    public PivotStyleGalleryValues Result { get; private set; }

    public PivotStyleGalleryDialog(string? currentStyleName)
    {
        Result = CreateResult(currentStyleName);
        Title = UiText.Get("PivotStyleGallery_PivotTableStyles");
        Width = 360;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result.StyleName);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static PivotStyleGalleryValues CreateResult(string? styleName) =>
        PivotStyleGalleryPlanner.CreateResult(styleName);

    private DockPanel CreateContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        _styleGallery.SelectionMode = SelectionMode.Single;
        _styleGallery.Margin = new Thickness(0, 0, 0, 12);
        AutomationProperties.SetName(_styleGallery, UiText.Get("PivotStyleGallery_PivotTableStyleGallery"));
        root.Children.Add(new Label { Content = UiText.Get("PivotStyleGallery_PivotTableStyle"), Target = _styleGallery, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        DockPanel.SetDock(_styleGallery, Dock.Top);
        root.Children.Add(_styleGallery);
        root.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        return root;
    }

    private void Load(string styleName)
    {
        var styleNames = PivotStyleGalleryPlanner.GetStyleNames(styleName);
        _styleGallery.ItemsSource = styleNames;
        _styleGallery.SelectedIndex = PivotStyleGalleryPlanner.FindStyleIndex(styleNames, styleName);
        _styleGallery.ScrollIntoView(_styleGallery.SelectedItem);
    }

    private void Accept()
    {
        Result = CreateResult(_styleGallery.SelectedItem?.ToString());
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _styleGallery.Focus();
        Keyboard.Focus(_styleGallery);
    }
}
