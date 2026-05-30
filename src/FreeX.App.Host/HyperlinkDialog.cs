using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

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
    private readonly Button _screenTipButton = new() { Content = UiText.Get("Hyperlink_ScreenTip") };
    private readonly Button _bookmarkButton = new() { Content = UiText.Get("Hyperlink_Bookmark") };
    private readonly ListBox _linkTypes = new();
    private readonly Label _targetLabel;
    private string _screenTip = "";
    private string _bookmark = "";

    public HyperlinkDialogResult Result { get; private set; }

    public HyperlinkDialog(string target = "https://", string displayText = "")
    {
        Result = CreateResult(target, displayText);
        Title = UiText.Get("Hyperlink_InsertHyperlink");
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
            UiText.Get("Hyperlink_LinkTypeExistingFileOrWebPage"),
            UiText.Get("Hyperlink_LinkTypeCreateNewDocument"),
            UiText.Get("Hyperlink_LinkTypePlaceInThisDocument"),
            UiText.Get("Hyperlink_LinkTypeEmailAddress")
        };
        _linkTypes.SelectedIndex = 0;
        AutomationProperties.SetName(_linkTypes, UiText.Get("Hyperlink_LinkTo2"));
        AutomationProperties.SetAutomationId(_linkTypes, "HyperlinkLinkTypeList");
        AutomationProperties.SetHelpText(_linkTypes, UiText.Get("Hyperlink_ChooseTheKindOfHyperlinkToInsert"));
        linkTypePanel.Children.Add(new Label { Content = UiText.Get("Hyperlink_LinkTo"), Target = _linkTypes, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        linkTypePanel.Children.Add(_linkTypes);
        DockPanel.SetDock(linkTypePanel, Dock.Left);
        root.Children.Add(linkTypePanel);

        var grid = DialogGrid(3);
        AddTextRow(grid, 0, UiText.Get("Hyperlink_TextToDisplay2"), _displayBox, displayText);
        AutomationProperties.SetName(_displayBox, UiText.Get("Hyperlink_TextToDisplay"));
        AutomationProperties.SetAutomationId(_displayBox, "HyperlinkDisplayTextBox");
        AutomationProperties.SetHelpText(_displayBox, UiText.Get("Hyperlink_EnterTheTextShownInTheCellForTheHyperlink"));
        _targetLabel = AddTextRow(grid, 1, UiText.Get("Hyperlink_Address"), _targetBox, target);
        AutomationProperties.SetAutomationId(_targetBox, "HyperlinkTargetTextBox");
        _linkTypes.SelectionChanged += (_, _) => UpdateTargetFieldForLinkType();
        UpdateTargetFieldForLinkType();
        _screenTipButton.Click += ScreenTipButton_Click;
        _bookmarkButton.Click += BookmarkButton_Click;
        AutomationProperties.SetName(_screenTipButton, UiText.Get("Hyperlink_SetScreenTip"));
        AutomationProperties.SetAutomationId(_screenTipButton, "HyperlinkScreenTipButton");
        AutomationProperties.SetHelpText(_screenTipButton, UiText.Get("Hyperlink_SetTheTextShownWhenPointingToTheHyperlink"));
        AutomationProperties.SetName(_bookmarkButton, UiText.Get("Hyperlink_SelectPlaceInDocument"));
        AutomationProperties.SetAutomationId(_bookmarkButton, "HyperlinkBookmarkButton");
        AutomationProperties.SetHelpText(_bookmarkButton, UiText.Get("Hyperlink_ChooseABookmarkDefinedNameOrCellReferenceInThisWorkbook"));
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
                HyperlinkLinkType.PlaceInThisDocument => UiText.Get("Hyperlink_EnterValidCellReferenceOrDefinedName"),
                HyperlinkLinkType.EmailAddress => UiText.Get("Hyperlink_EnterEmailAddress"),
                HyperlinkLinkType.CreateNewDocument => UiText.Get("Hyperlink_EnterNewDocumentName"),
                _ => UiText.Get("Hyperlink_EnterAddress")
            };
            return false;
        }

        if (linkType == HyperlinkLinkType.EmailAddress && !IsValidEmailAddressTarget(result.Target))
        {
            error = UiText.Get("Hyperlink_EnterValidEmailAddress");
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

    private void UpdateTargetFieldForLinkType()
    {
        var (label, automationName, helpText) = SelectedLinkType switch
        {
            HyperlinkLinkType.CreateNewDocument => (UiText.Get("Hyperlink_NewDocumentLabel"), UiText.Get("Hyperlink_NewDocumentAutomationName"), UiText.Get("Hyperlink_NewDocumentHelpText")),
            HyperlinkLinkType.PlaceInThisDocument => (UiText.Get("Hyperlink_CellReferenceLabel"), UiText.Get("Hyperlink_CellReferenceAutomationName"), UiText.Get("Hyperlink_CellReferenceHelpText")),
            HyperlinkLinkType.EmailAddress => (UiText.Get("Hyperlink_EmailAddressLabel"), UiText.Get("Hyperlink_EmailAddressAutomationName"), UiText.Get("Hyperlink_EmailAddressHelpText")),
            _ => (UiText.Get("Hyperlink_Address"), UiText.Get("Hyperlink_AddressAutomationName"), UiText.Get("Hyperlink_AddressHelpText"))
        };

        _targetLabel.Content = label;
        AutomationProperties.SetName(_targetBox, automationName);
        AutomationProperties.SetHelpText(_targetBox, helpText);
    }

    private void Accept()
    {
        if (!TryCreateResult(_targetBox.Text, _displayBox.Text, SelectedLinkType, _screenTip, _bookmark, out var result, out var error))
        {
            ShowInvalidInputWarning(error ?? UiText.Get("Hyperlink_EnterHyperlinkDetails"));
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
        DialogFocus.FocusAndSelect(_targetBox);
    }

    private void ShowInvalidInputWarning(string message)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
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

    private static Label AddTextRow(Grid grid, int row, string label, TextBox box, string value)
    {
        var labelControl = new Label
        {
            Content = label,
            Target = box,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        };
        grid.Children.Add(labelControl);
        Grid.SetRow(labelControl, row);
        Grid.SetColumn(labelControl, 0);

        box.Text = value;
        box.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        return labelControl;
    }
}
