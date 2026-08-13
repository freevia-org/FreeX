using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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

    public HyperlinkDialogPlan Result { get; private set; }

    public HyperlinkDialog(string target = "https://", string displayText = "")
    {
        Result = CreateResult(target, displayText);
        Title = UiText.Get("Hyperlink_InsertHyperlink");
        Width = HyperlinkDialogPlanner.Width;
        Height = HyperlinkDialogPlanner.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16) };
        var linkTypePanel = new StackPanel
        {
            Width = HyperlinkDialogPlanner.LinkTypeColumnWidth,
            Margin = new Thickness(0, 0, HyperlinkDialogPlanner.LinkTypeColumnGap, 0)
        };
        _linkTypes.Width = HyperlinkDialogPlanner.LinkTypeColumnWidth;
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
        _screenTipButton.Width = HyperlinkDialogPlanner.SecondaryButtonWidth;
        _screenTipButton.Margin = new Thickness(0, 0, HyperlinkDialogPlanner.ButtonGap, 0);
        _bookmarkButton.Width = HyperlinkDialogPlanner.SecondaryButtonWidth;
        buttonRow.Children.Add(_screenTipButton);
        buttonRow.Children.Add(_bookmarkButton);
        grid.Children.Add(buttonRow);
        Grid.SetRow(buttonRow, 2);
        Grid.SetColumn(buttonRow, 1);

        grid.Children.Add(DialogButtonRowFactory.Create(Accept, HyperlinkDialogPlanner.ActionButtonWidth));
        Grid.SetRow(grid.Children[^1], 3);
        Grid.SetColumnSpan(grid.Children[^1], 2);
        root.Children.Add(grid);
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static HyperlinkDialogPlan CreateResult(
        string target,
        string? displayText,
        HyperlinkTargetKind linkType = HyperlinkTargetKind.ExistingFileOrWebPage,
        string? screenTip = "",
        string? bookmark = "")
    => HyperlinkDialogPlanner.Plan(
            target,
            displayText,
            linkType,
            screenTip,
            bookmark);

    public static bool TryCreateResult(
        string? target,
        string? displayText,
        HyperlinkTargetKind linkType,
        string? screenTip,
        string? bookmark,
        out HyperlinkDialogPlan result,
        out string? error)
    {
        if (HyperlinkDialogPlanner.TryPlan(
                target,
                displayText,
                linkType,
                screenTip,
                bookmark,
                out var plan,
                out var validationError))
        {
            result = plan;
            error = null;
            return true;
        }

        result = plan;
        error = GetValidationErrorText(validationError);
        return false;
    }

    private HyperlinkTargetKind SelectedLinkType => _linkTypes.SelectedIndex switch
    {
        1 => HyperlinkTargetKind.CreateNewDocument,
        2 => HyperlinkTargetKind.PlaceInThisDocument,
        3 => HyperlinkTargetKind.EmailAddress,
        _ => HyperlinkTargetKind.ExistingFileOrWebPage
    };

    private void UpdateTargetFieldForLinkType()
    {
        var (label, automationName, helpText) = SelectedLinkType switch
        {
            HyperlinkTargetKind.CreateNewDocument => (UiText.Get("Hyperlink_NewDocumentLabel"), UiText.Get("Hyperlink_NewDocumentAutomationName"), UiText.Get("Hyperlink_NewDocumentHelpText")),
            HyperlinkTargetKind.PlaceInThisDocument => (UiText.Get("Hyperlink_CellReferenceLabel"), UiText.Get("Hyperlink_CellReferenceAutomationName"), UiText.Get("Hyperlink_CellReferenceHelpText")),
            HyperlinkTargetKind.EmailAddress => (UiText.Get("Hyperlink_EmailAddressLabel"), UiText.Get("Hyperlink_EmailAddressAutomationName"), UiText.Get("Hyperlink_EmailAddressHelpText")),
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
        DialogFocus.ShowWarningAndFocus(this, message, Title, _targetBox);
    }

    private static string GetValidationErrorText(HyperlinkDialogValidationError error) =>
        HyperlinkDialogPlanner
            .DescribeValidationError(error, HyperlinkDialogTextProfile.Wpf)
            .Message.Resolve(UiText.Get, UiText.Format);

    private static Grid DialogGrid(int inputRows)
    {
        var grid = new Grid { Margin = new Thickness(16) };
        for (var index = 0; index < inputRows; index++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.LabelColumnWidth) });
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
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, HyperlinkDialogPlanner.ButtonGap, HyperlinkDialogPlanner.FieldBottomMargin)
        };
        grid.Children.Add(labelControl);
        Grid.SetRow(labelControl, row);
        Grid.SetColumn(labelControl, 0);

        box.Text = value;
        box.Margin = new Thickness(0, 0, 0, HyperlinkDialogPlanner.FieldBottomMargin);
        grid.Children.Add(box);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        return labelControl;
    }
}
