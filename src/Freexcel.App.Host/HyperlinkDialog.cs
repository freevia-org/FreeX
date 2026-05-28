using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

public enum HyperlinkLinkType
{
    ExistingFileOrWebPage,
    CreateNewDocument,
    PlaceInThisDocument,
    EmailAddress
}

public sealed record HyperlinkDialogResult(
    HyperlinkLinkType LinkType,
    string Target,
    string DisplayText,
    string ScreenTip,
    string Bookmark);

public sealed class HyperlinkDialog : Window
{
    private readonly TextBox _targetBox = new();
    private readonly TextBox _displayBox = new();
    private readonly Button _screenTipButton = new() { Content = "_ScreenTip..." };
    private readonly Button _bookmarkButton = new() { Content = "_Bookmark..." };
    private readonly ListBox _linkTypes = new();
    private string _screenTip = "";
    private string _bookmark = "";

    public HyperlinkDialogResult Result { get; private set; }

    public HyperlinkDialog(string target = "https://", string displayText = "")
    {
        Result = CreateResult(target, displayText);
        Title = "Insert Hyperlink";
        Width = 560;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16) };
        var linkTypePanel = new StackPanel { Width = 170, Margin = new Thickness(0, 0, 12, 0) };
        _linkTypes.Width = 170;
        _linkTypes.ItemsSource = new[]
        {
            "Existing File or Web Page",
            "Create New Document",
            "Place in This Document",
            "E-mail Address"
        };
        _linkTypes.SelectedIndex = 0;
        AutomationProperties.SetName(_linkTypes, "Link to");
        linkTypePanel.Children.Add(new Label { Content = "Link _to:", Target = _linkTypes, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        linkTypePanel.Children.Add(_linkTypes);
        DockPanel.SetDock(linkTypePanel, Dock.Left);
        root.Children.Add(linkTypePanel);

        var grid = DialogGrid(3);
        AddTextRow(grid, 0, "Text to _display:", _displayBox, displayText);
        AutomationProperties.SetName(_displayBox, "Text to display");
        AddTextRow(grid, 1, "_Address:", _targetBox, target);
        AutomationProperties.SetName(_targetBox, "Address");
        _screenTipButton.Click += ScreenTipButton_Click;
        _bookmarkButton.Click += BookmarkButton_Click;
        AutomationProperties.SetName(_screenTipButton, "Set ScreenTip");
        AutomationProperties.SetHelpText(_screenTipButton, "Set the text shown when pointing to the hyperlink.");
        AutomationProperties.SetName(_bookmarkButton, "Select place in document");
        AutomationProperties.SetHelpText(_bookmarkButton, "Choose a bookmark, defined name, or cell reference in this workbook.");
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        _screenTipButton.Width = 96;
        _screenTipButton.Margin = new Thickness(0, 0, 8, 0);
        _bookmarkButton.Width = 96;
        buttonRow.Children.Add(_screenTipButton);
        buttonRow.Children.Add(_bookmarkButton);
        grid.Children.Add(buttonRow);
        Grid.SetRow(buttonRow, 2);
        Grid.SetColumn(buttonRow, 1);

        grid.Children.Add(DialogButtonRowFactory.Create(Accept, 72));
        Grid.SetRow(grid.Children[^1], 3);
        Grid.SetColumnSpan(grid.Children[^1], 2);
        root.Children.Add(grid);
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static HyperlinkDialogResult CreateResult(
        string target,
        string? displayText,
        HyperlinkLinkType linkType = HyperlinkLinkType.ExistingFileOrWebPage,
        string? screenTip = "",
        string? bookmark = "")
    {
        var trimmedTarget = target.Trim();
        var normalizedTarget = NormalizeTargetForLinkType(trimmedTarget, linkType);
        var normalizedDisplay = string.IsNullOrWhiteSpace(displayText)
            ? CreateDefaultDisplayText(trimmedTarget, linkType)
            : displayText.Trim();
        return new HyperlinkDialogResult(
            linkType,
            normalizedTarget,
            normalizedDisplay,
            (screenTip ?? "").Trim(),
            (bookmark ?? "").Trim());
    }

    public static bool TryCreateResult(
        string? target,
        string? displayText,
        HyperlinkLinkType linkType,
        string? screenTip,
        string? bookmark,
        out HyperlinkDialogResult result,
        out string? error)
    {
        result = CreateResult(target ?? "", displayText, linkType, screenTip, bookmark);
        if (string.IsNullOrWhiteSpace(result.Target))
        {
            error = linkType switch
            {
                HyperlinkLinkType.PlaceInThisDocument => "Enter a valid cell reference or defined name.",
                HyperlinkLinkType.EmailAddress => "Enter an email address.",
                HyperlinkLinkType.CreateNewDocument => "Enter a new document name.",
                _ => "Enter an address."
            };
            return false;
        }

        if (linkType == HyperlinkLinkType.EmailAddress && !IsValidEmailAddressTarget(result.Target))
        {
            error = "Enter a valid email address.";
            return false;
        }

        error = null;
        return true;
    }

    private HyperlinkLinkType SelectedLinkType => _linkTypes.SelectedIndex switch
    {
        1 => HyperlinkLinkType.CreateNewDocument,
        2 => HyperlinkLinkType.PlaceInThisDocument,
        3 => HyperlinkLinkType.EmailAddress,
        _ => HyperlinkLinkType.ExistingFileOrWebPage
    };

    private void Accept()
    {
        if (!TryCreateResult(_targetBox.Text, _displayBox.Text, SelectedLinkType, _screenTip, _bookmark, out var result, out var error))
        {
            ShowInvalidInputWarning(error ?? "Enter hyperlink details.");
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void ScreenTipButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ScreenTipDialog(_screenTip) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _screenTip = dialog.Result.Text;
        _screenTipButton.ToolTip = string.IsNullOrWhiteSpace(_screenTip) ? null : _screenTip;
    }

    private void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BookmarkDialog(_bookmark) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _bookmark = dialog.Result.Text;
        _bookmarkButton.ToolTip = string.IsNullOrWhiteSpace(_bookmark) ? null : _bookmark;
    }

    private void FocusInitialKeyboardTarget()
    {
        _targetBox.Focus();
        _targetBox.SelectAll();
        Keyboard.Focus(_targetBox);
    }

    private void ShowInvalidInputWarning(string message)
    {
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        _targetBox.Focus();
        _targetBox.SelectAll();
        Keyboard.Focus(_targetBox);
    }

    private static bool IsValidEmailAddressTarget(string target)
    {
        var address = target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? target["mailto:".Length..]
            : target;
        return address.IndexOf('@') > 0 &&
            address.IndexOf('@') == address.LastIndexOf('@') &&
            address.LastIndexOf('.') > address.IndexOf('@') + 1 &&
            address.IndexOfAny([' ', '\t', '\r', '\n']) < 0;
    }

    private static string NormalizeTargetForLinkType(string target, HyperlinkLinkType linkType)
    {
        if (linkType != HyperlinkLinkType.EmailAddress ||
            target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(target))
            return target;

        return "mailto:" + target;
    }

    private static string CreateDefaultDisplayText(string target, HyperlinkLinkType linkType)
    {
        if (linkType != HyperlinkLinkType.EmailAddress ||
            !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return target;

        var address = target["mailto:".Length..];
        var queryStart = address.IndexOf('?', StringComparison.Ordinal);
        return queryStart < 0 ? address : address[..queryStart];
    }

    private static Grid DialogGrid(int inputRows)
    {
        var grid = new Grid { Margin = new Thickness(16) };
        for (var index = 0; index < inputRows; index++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static void AddTextRow(Grid grid, int row, string label, TextBox box, string value)
    {
        grid.Children.Add(new Label
        {
            Content = label,
            Target = box,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        });
        Grid.SetRow(grid.Children[^1], row);
        Grid.SetColumn(grid.Children[^1], 0);

        box.Text = value;
        box.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
    }
}
