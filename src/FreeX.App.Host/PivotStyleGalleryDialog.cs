using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

public sealed record PivotStyleGalleryDialogResult(string StyleName);

public sealed class PivotStyleGalleryDialog : Window
{
    private readonly ListBox _styleGallery = new() { MinHeight = 260 };

    public PivotStyleGalleryDialogResult Result { get; private set; }

    public PivotStyleGalleryDialog(string? currentStyleName)
    {
        Result = CreateResult(currentStyleName);
        Title = "PivotTable Styles";
        Width = 360;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result.StyleName);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static PivotStyleGalleryDialogResult CreateResult(string? styleName) =>
        new(PivotStyleCatalog.NormalizeStyleName(styleName));

    private DockPanel CreateContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        _styleGallery.SelectionMode = SelectionMode.Single;
        _styleGallery.Margin = new Thickness(0, 0, 0, 12);
        AutomationProperties.SetName(_styleGallery, "PivotTable style gallery");
        root.Children.Add(new Label { Content = "_PivotTable style:", Target = _styleGallery, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        DockPanel.SetDock(_styleGallery, Dock.Top);
        root.Children.Add(_styleGallery);
        root.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        return root;
    }

    private void Load(string styleName)
    {
        var styleNames = PivotStyleCatalog.GetStyleNames(styleName);
        _styleGallery.ItemsSource = styleNames;
        _styleGallery.SelectedItem = styleNames.FirstOrDefault(item =>
            string.Equals(item, styleName, StringComparison.OrdinalIgnoreCase)) ?? PivotStyleCatalog.NormalizeStyleName(null);
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
