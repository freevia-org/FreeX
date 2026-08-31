using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Free.Shared.Shell;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class HyperlinkDialog : Window
{
    private readonly TextBox _targetBox = new();
    private readonly TextBox _displayBox = new();
    private readonly Button _screenTipButton = new() { Content = UiText.Get("Hyperlink_ScreenTip") };
    private readonly Button _bookmarkButton = new() { Content = UiText.Get("Hyperlink_Bookmark") };
    private readonly Button _browseButton = new() { Content = "_Browse..." };
    private readonly ListBox _linkTypes = new();
    private readonly Label _targetLabel;
    private string _screenTip = "";
    private string _bookmark = "";

    public HyperlinkDialogPlan Result { get; private set; }

    public HyperlinkDialog(string target = "", string displayText = "", string? currentFilePath = null)
    {
        Result = CreateResult(target, displayText);
        Title = UiText.Get("Hyperlink_InsertHyperlink");
        Width = HyperlinkDialogPlanner.Width;
        Height = HyperlinkDialogPlanner.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(HyperlinkDialogPlanner.DialogMargin) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.LabelColumnWidth) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.SecondaryButtonWidth + HyperlinkDialogPlanner.ButtonGap) });
        var displayLabel = new Label
        {
            Content = UiText.Get("Hyperlink_TextToDisplay2"),
            Target = _displayBox,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        _displayBox.Text = displayText;
        _displayBox.Height = HyperlinkDialogPlanner.FieldHeight;
        headerGrid.Children.Add(displayLabel);
        headerGrid.Children.Add(_displayBox);
        Grid.SetColumn(_displayBox, 1);
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
        AutomationProperties.SetName(_displayBox, UiText.Get("Hyperlink_TextToDisplay"));
        AutomationProperties.SetAutomationId(_displayBox, "HyperlinkDisplayTextBox");
        AutomationProperties.SetHelpText(_displayBox, UiText.Get("Hyperlink_EnterTheTextShownInTheCellForTheHyperlink"));

        _screenTipButton.Click += ScreenTipButton_Click;
        _bookmarkButton.Click += BookmarkButton_Click;
        _browseButton.Click += BrowseButton_Click;
        _browseButton.MinWidth = HyperlinkDialogPlanner.SecondaryButtonWidth;
        AutomationProperties.SetName(_browseButton, "Browse for a file");
        AutomationProperties.SetAutomationId(_browseButton, "HyperlinkBrowseButton");
        AutomationProperties.SetHelpText(_browseButton, "Choose a local file for this hyperlink.");

        var folderText = new TextBox
        {
            Text = CurrentFolderText(currentFilePath),
            IsReadOnly = true,
            IsTabStop = false,
            Height = HyperlinkDialogPlanner.FieldHeight,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
        };
        AutomationProperties.SetName(folderText, "Current folder");
        AutomationProperties.SetAutomationId(folderText, "HyperlinkCurrentFolderText");
        var browseGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        browseGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.LabelColumnWidth) });
        browseGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        browseGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.SecondaryButtonWidth + HyperlinkDialogPlanner.ButtonGap) });
        var folderLabel = new Label
        {
            Content = "Look _in:",
            Target = folderText,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        browseGrid.Children.Add(folderLabel);
        browseGrid.Children.Add(folderText);
        Grid.SetColumn(folderText, 1);
        browseGrid.Children.Add(_browseButton);
        Grid.SetColumn(_browseButton, 2);
        _browseButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;

        var currentFolderContext = new ListBox
        {
            ItemsSource = CurrentFolderContext(currentFilePath),
            SelectedIndex = 0,
            IsTabStop = false,
        };
        AutomationProperties.SetName(currentFolderContext, "Current folder link location");
        AutomationProperties.SetAutomationId(currentFolderContext, "HyperlinkCurrentFolderContext");

        var detailPanel = new Grid();
        detailPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        detailPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        detailPanel.Children.Add(browseGrid);
        detailPanel.Children.Add(currentFolderContext);
        Grid.SetRow(currentFolderContext, 1);

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.LinkTypeColumnWidth) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.LinkTypeColumnGap) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainGrid.Children.Add(linkTypePanel);
        mainGrid.Children.Add(detailPanel);
        Grid.SetColumn(detailPanel, 2);

        var addressGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        addressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.LabelColumnWidth) });
        addressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.SecondaryButtonWidth + HyperlinkDialogPlanner.ButtonGap) });
        _targetLabel = new Label
        {
            Content = UiText.Get("Hyperlink_Address"),
            Target = _targetBox,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        _targetBox.Text = target;
        _targetBox.Height = HyperlinkDialogPlanner.FieldHeight;
        addressGrid.Children.Add(_targetLabel);
        addressGrid.Children.Add(_targetBox);
        Grid.SetColumn(_targetBox, 1);
        addressGrid.Children.Add(_bookmarkButton);
        Grid.SetColumn(_bookmarkButton, 2);
        _bookmarkButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        AutomationProperties.SetAutomationId(_targetBox, "HyperlinkTargetTextBox");
        _linkTypes.SelectionChanged += (_, _) => UpdateTargetFieldForLinkType();
        UpdateTargetFieldForLinkType();
        AutomationProperties.SetName(_screenTipButton, UiText.Get("Hyperlink_SetScreenTip"));
        AutomationProperties.SetAutomationId(_screenTipButton, "HyperlinkScreenTipButton");
        AutomationProperties.SetHelpText(_screenTipButton, UiText.Get("Hyperlink_SetTheTextShownWhenPointingToTheHyperlink"));
        AutomationProperties.SetName(_bookmarkButton, UiText.Get("Hyperlink_SelectPlaceInDocument"));
        AutomationProperties.SetAutomationId(_bookmarkButton, "HyperlinkBookmarkButton");
        AutomationProperties.SetHelpText(_bookmarkButton, UiText.Get("Hyperlink_ChooseABookmarkDefinedNameOrCellReferenceInThisWorkbook"));
        _screenTipButton.Width = HyperlinkDialogPlanner.SecondaryButtonWidth;
        _bookmarkButton.Width = HyperlinkDialogPlanner.SecondaryButtonWidth;
        headerGrid.Children.Add(_screenTipButton);
        Grid.SetColumn(_screenTipButton, 2);
        _screenTipButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;

        var actionButtons = DialogButtonRowFactory.Create(
            Accept,
            HyperlinkDialogPlanner.ActionButtonWidth,
            new Thickness(0, 8, 0, 0));

        root.Children.Add(headerGrid);
        Grid.SetRow(headerGrid, 0);
        root.Children.Add(mainGrid);
        Grid.SetRow(mainGrid, 1);
        root.Children.Add(addressGrid);
        Grid.SetRow(addressGrid, 2);
        root.Children.Add(actionButtons);
        Grid.SetRow(actionButtons, 3);
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

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var result = WpfFileDialogService.ShowOpenDialog(
            this,
            "All files (*.*)|*.*",
            title: "Select a file to link to");
        if (!result.Chosen)
            return;

        _targetBox.Text = result.FileName!;
        DialogFocus.FocusAndSelect(_targetBox);
    }

    private static string CurrentFolderText(string? currentFilePath)
    {
        var directory = string.IsNullOrWhiteSpace(currentFilePath)
            ? null
            : System.IO.Path.GetDirectoryName(currentFilePath);
        return string.IsNullOrWhiteSpace(directory) ? "Current Folder" : directory;
    }

    private static IReadOnlyList<string> CurrentFolderContext(string? currentFilePath)
    {
        var fileName = string.IsNullOrWhiteSpace(currentFilePath)
            ? null
            : System.IO.Path.GetFileName(currentFilePath);
        return string.IsNullOrWhiteSpace(fileName)
            ? ["Current Folder", "Use Browse... to choose a file"]
            : ["Current Folder", fileName, "Use Browse... to choose another file"];
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
            .DescribeValidationError(error)
            .Message.Resolve(UiText.Get, UiText.Format);

}
