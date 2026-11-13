using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Free.Shared.Shell;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// Excel's Insert Hyperlink dialog. The "Link to" bar switches the body between Excel's four panes:
/// Existing File or Web Page (Look in folder, its files, Browse), Place in This Document (sheets and
/// defined names), Create New Document (full path, Change...) and E-mail Address (subject, recently
/// used addresses). The pane's main field is always <see cref="_targetBox"/>, relabelled per pane.
/// </summary>
public sealed class HyperlinkDialog : Window
{
    private readonly TextBox _targetBox = new();
    private readonly TextBox _displayBox = new();
    private readonly TextBox _subjectBox = new();
    private readonly Button _screenTipButton = new() { Content = UiText.Get("Hyperlink_ScreenTip") };
    private readonly Button _bookmarkButton = new() { Content = UiText.Get("Hyperlink_Bookmark") };
    private readonly Button _browseButton = new() { Content = UiText.Get("Hyperlink_Browse") };
    private readonly Button _changePathButton = new() { Content = "Change..." };
    private readonly ListBox _linkTypes = new();
    private readonly ListBox _folderEntries = new();
    private readonly ListBox _documentPlaces = new();
    private readonly ListBox _recentEmails = new();
    private readonly TextBox _folderText = new();
    private readonly TextBlock _fullPathText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Label _targetLabel;
    private readonly Grid _existingPane = new();
    private readonly Grid _placePane = new();
    private readonly Grid _newDocumentPane = new();
    private readonly Grid _emailPane = new();
    private string _screenTip = "";
    private string _bookmark = "";
    private string _currentFolder;
    private string _newDocumentFolder;

    public HyperlinkDialogPlan Result { get; private set; }

    public HyperlinkDialog(string target = "", string displayText = "", string? currentFilePath = null)
    {
        Result = CreateResult(target, displayText);
        _currentFolder = HyperlinkDialogPanePlanner.DefaultFolder(currentFilePath);
        _newDocumentFolder = _currentFolder;
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

        // ── Text to display + ScreenTip ─────────────────────────────────────────
        var headerGrid = CreateFieldRow(new Thickness(HyperlinkDialogPlanner.LinkTypeColumnWidth + HyperlinkDialogPlanner.LinkTypeColumnGap, 0, 0, 4));
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
        AutomationProperties.SetName(_displayBox, UiText.Get("Hyperlink_TextToDisplay"));
        AutomationProperties.SetAutomationId(_displayBox, "HyperlinkDisplayTextBox");
        AutomationProperties.SetHelpText(_displayBox, UiText.Get("Hyperlink_EnterTheTextShownInTheCellForTheHyperlink"));
        _screenTipButton.Click += ScreenTipButton_Click;
        _screenTipButton.Width = HyperlinkDialogPlanner.SecondaryButtonWidth;
        _screenTipButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        AutomationProperties.SetName(_screenTipButton, UiText.Get("Hyperlink_SetScreenTip"));
        AutomationProperties.SetAutomationId(_screenTipButton, "HyperlinkScreenTipButton");
        AutomationProperties.SetHelpText(_screenTipButton, UiText.Get("Hyperlink_SetTheTextShownWhenPointingToTheHyperlink"));
        headerGrid.Children.Add(_screenTipButton);
        Grid.SetColumn(_screenTipButton, 2);

        // ── Link to: (Excel order) ───────────────────────────────────────────────
        var linkTypePanel = new DockPanel
        {
            Width = HyperlinkDialogPlanner.LinkTypeColumnWidth,
            Margin = new Thickness(0, 0, HyperlinkDialogPlanner.LinkTypeColumnGap, 0)
        };
        _linkTypes.Width = HyperlinkDialogPlanner.LinkTypeColumnWidth;
        _linkTypes.ItemsSource = HyperlinkDialogPanePlanner.LinkTypeOrder.Select(LinkTypeLabel).ToArray();
        _linkTypes.SelectedIndex = 0;
        AutomationProperties.SetName(_linkTypes, UiText.Get("Hyperlink_LinkTo2"));
        AutomationProperties.SetAutomationId(_linkTypes, "HyperlinkLinkTypeList");
        AutomationProperties.SetHelpText(_linkTypes, UiText.Get("Hyperlink_ChooseTheKindOfHyperlinkToInsert"));
        var linkToLabel = new Label { Content = UiText.Get("Hyperlink_LinkTo"), Target = _linkTypes, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(linkToLabel, Dock.Top);
        linkTypePanel.Children.Add(linkToLabel);
        linkTypePanel.Children.Add(_linkTypes);

        // ── Panes ────────────────────────────────────────────────────────────────
        BuildExistingFilePane(currentFilePath);
        BuildPlacePane();
        BuildNewDocumentPane();
        BuildEmailPane();
        var paneHost = new Grid();
        paneHost.Children.Add(_existingPane);
        paneHost.Children.Add(_placePane);
        paneHost.Children.Add(_newDocumentPane);
        paneHost.Children.Add(_emailPane);

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.LinkTypeColumnWidth) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.LinkTypeColumnGap) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainGrid.Children.Add(linkTypePanel);
        mainGrid.Children.Add(paneHost);
        Grid.SetColumn(paneHost, 2);
        Grid.SetRowSpan(linkTypePanel, 2);

        // ── The pane's main field (Address / cell reference / name / e-mail) ─────
        var addressGrid = CreateFieldRow(new Thickness(HyperlinkDialogPlanner.LinkTypeColumnWidth + HyperlinkDialogPlanner.LinkTypeColumnGap, 4, 0, 0));
        _targetLabel = new Label
        {
            Content = UiText.Get("Hyperlink_Address"),
            Target = _targetBox,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        _targetBox.Text = target;
        _targetBox.Height = HyperlinkDialogPlanner.FieldHeight;
        _targetBox.TextChanged += (_, _) => UpdateNewDocumentFullPath();
        addressGrid.Children.Add(_targetLabel);
        addressGrid.Children.Add(_targetBox);
        Grid.SetColumn(_targetBox, 1);
        _bookmarkButton.Click += BookmarkButton_Click;
        _bookmarkButton.Width = HyperlinkDialogPlanner.SecondaryButtonWidth;
        _bookmarkButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        AutomationProperties.SetName(_bookmarkButton, UiText.Get("Hyperlink_SelectPlaceInDocument"));
        AutomationProperties.SetAutomationId(_bookmarkButton, "HyperlinkBookmarkButton");
        AutomationProperties.SetHelpText(_bookmarkButton, UiText.Get("Hyperlink_ChooseABookmarkDefinedNameOrCellReferenceInThisWorkbook"));
        addressGrid.Children.Add(_bookmarkButton);
        Grid.SetColumn(_bookmarkButton, 2);
        AutomationProperties.SetAutomationId(_targetBox, "HyperlinkTargetTextBox");
        _linkTypes.SelectionChanged += (_, _) => UpdateTargetFieldForLinkType();
        UpdateTargetFieldForLinkType();

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

    /// <summary>
    /// Opens the dialog on an existing link: its pane, metadata, and the workbook's sheets, defined
    /// names and recently used e-mail addresses for the Place in This Document and E-mail panes.
    /// </summary>
    public void ApplyPrefill(HyperlinkDialogPrefill prefill, Workbook? workbook)
    {
        ArgumentNullException.ThrowIfNull(prefill);
        _documentPlaces.ItemsSource = HyperlinkDialogPanePlanner.DocumentPlaces(workbook)
            .Select(place => new ListBoxItem { Content = place.IsDefinedName ? "  " + place.Name : place.Name, Tag = place })
            .ToArray();
        _recentEmails.ItemsSource = RecentEmailAddresses(workbook);
        _screenTip = prefill.ScreenTip;
        _bookmark = prefill.Bookmark;
        _screenTipButton.ToolTip = string.IsNullOrWhiteSpace(_screenTip) ? null : _screenTip;
        _bookmarkButton.ToolTip = string.IsNullOrWhiteSpace(_bookmark) ? null : _bookmark;

        var index = Math.Max(0, IndexOf(prefill.LinkType));
        _linkTypes.SelectedIndex = index;
        switch (prefill.LinkType)
        {
            case HyperlinkTargetKind.PlaceInThisDocument:
                var (place, reference) = HyperlinkDialogPanePlanner.SplitDocumentLocation(prefill.Target);
                SelectDocumentPlace(place);
                _targetBox.Text = SelectedDocumentPlace is { IsDefinedName: true } ? string.Empty : reference;
                break;
            case HyperlinkTargetKind.EmailAddress:
                var (address, subject) = HyperlinkDialogPanePlanner.SplitEmailTarget(prefill.Target);
                _targetBox.Text = address;
                _subjectBox.Text = subject;
                break;
            case HyperlinkTargetKind.CreateNewDocument when System.IO.Path.IsPathRooted(prefill.Target):
                _newDocumentFolder = System.IO.Path.GetDirectoryName(prefill.Target) ?? _newDocumentFolder;
                _targetBox.Text = System.IO.Path.GetFileName(prefill.Target);
                break;
        }

        if (SelectedDocumentPlace is null && _documentPlaces.Items.Count > 0)
            _documentPlaces.SelectedIndex = 0;
        UpdateNewDocumentFullPath();
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

    private HyperlinkTargetKind SelectedLinkType =>
        _linkTypes.SelectedIndex >= 0 && _linkTypes.SelectedIndex < HyperlinkDialogPanePlanner.LinkTypeOrder.Count
            ? HyperlinkDialogPanePlanner.LinkTypeOrder[_linkTypes.SelectedIndex]
            : HyperlinkTargetKind.ExistingFileOrWebPage;

    private HyperlinkDocumentPlace? SelectedDocumentPlace =>
        (_documentPlaces.SelectedItem as ListBoxItem)?.Tag as HyperlinkDocumentPlace;

    private static int IndexOf(HyperlinkTargetKind kind)
    {
        for (var i = 0; i < HyperlinkDialogPanePlanner.LinkTypeOrder.Count; i++)
        {
            if (HyperlinkDialogPanePlanner.LinkTypeOrder[i] == kind)
                return i;
        }

        return -1;
    }

    private static string LinkTypeLabel(HyperlinkTargetKind kind) => kind switch
    {
        HyperlinkTargetKind.PlaceInThisDocument => UiText.Get("Hyperlink_LinkTypePlaceInThisDocument"),
        HyperlinkTargetKind.CreateNewDocument => UiText.Get("Hyperlink_LinkTypeCreateNewDocument"),
        HyperlinkTargetKind.EmailAddress => UiText.Get("Hyperlink_LinkTypeEmailAddress"),
        _ => UiText.Get("Hyperlink_LinkTypeExistingFileOrWebPage"),
    };

    private static Grid CreateFieldRow(Thickness margin)
    {
        var grid = new Grid { Margin = margin };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.LabelColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HyperlinkDialogPlanner.SecondaryButtonWidth + HyperlinkDialogPlanner.ButtonGap) });
        return grid;
    }

    private void BuildExistingFilePane(string? currentFilePath)
    {
        _browseButton.Click += BrowseButton_Click;
        _browseButton.MinWidth = HyperlinkDialogPlanner.SecondaryButtonWidth;
        _browseButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        AutomationProperties.SetName(_browseButton, UiText.Get("Hyperlink_BrowseForAFile"));
        AutomationProperties.SetAutomationId(_browseButton, "HyperlinkBrowseButton");
        AutomationProperties.SetHelpText(_browseButton, UiText.Get("Hyperlink_ChooseALocalFileForThisHyperlink"));

        _folderText.Text = _currentFolder;
        _folderText.IsReadOnly = true;
        _folderText.IsTabStop = false;
        _folderText.Height = HyperlinkDialogPlanner.FieldHeight;
        _folderText.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
        AutomationProperties.SetName(_folderText, UiText.Get("Hyperlink_CurrentFolder"));
        AutomationProperties.SetAutomationId(_folderText, "HyperlinkCurrentFolderText");
        var browseGrid = CreateFieldRow(new Thickness(0, 0, 0, 4));
        var folderLabel = new Label
        {
            Content = UiText.Get("Hyperlink_LookIn"),
            Target = _folderText,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        var upButton = new Button { Content = "↑", Width = 26, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Up One Folder" };
        upButton.Click += (_, _) => NavigateUpFolder();
        AutomationProperties.SetAutomationId(upButton, "HyperlinkUpFolderButton");
        var folderRow = new DockPanel();
        DockPanel.SetDock(upButton, Dock.Right);
        folderRow.Children.Add(upButton);
        folderRow.Children.Add(_folderText);
        browseGrid.Children.Add(folderLabel);
        browseGrid.Children.Add(folderRow);
        Grid.SetColumn(folderRow, 1);
        browseGrid.Children.Add(_browseButton);
        Grid.SetColumn(_browseButton, 2);

        // Excel's left-hand switcher inside this pane: Current Folder / Recent Files.
        var currentFolderContext = new ListBox
        {
            ItemsSource = new[] { UiText.Get("Hyperlink_CurrentFolder"), "Recent Files" },
            SelectedIndex = 0,
            Width = 92,
            Margin = new Thickness(0, 0, 6, 0),
        };
        AutomationProperties.SetName(currentFolderContext, UiText.Get("Hyperlink_CurrentFolderLinkLocation"));
        AutomationProperties.SetAutomationId(currentFolderContext, "HyperlinkCurrentFolderContext");
        currentFolderContext.SelectionChanged += (_, _) =>
        {
            if (currentFolderContext.SelectedIndex == 1)
                ShowRecentFiles();
            else
                ShowFolder(_currentFolder);
        };

        AutomationProperties.SetName(_folderEntries, UiText.Get("Hyperlink_CurrentFolder"));
        AutomationProperties.SetAutomationId(_folderEntries, "HyperlinkFolderEntries");
        _folderEntries.SelectionChanged += (_, _) =>
        {
            if ((_folderEntries.SelectedItem as ListBoxItem)?.Tag is HyperlinkFolderEntry { IsFolder: false } file)
                _targetBox.Text = file.FullPath;
        };
        _folderEntries.MouseDoubleClick += (_, _) =>
        {
            if ((_folderEntries.SelectedItem as ListBoxItem)?.Tag is HyperlinkFolderEntry { IsFolder: true } folder)
                ShowFolder(folder.FullPath);
        };
        ShowFolder(_currentFolder);

        var listRow = new DockPanel();
        DockPanel.SetDock(currentFolderContext, Dock.Left);
        listRow.Children.Add(currentFolderContext);
        listRow.Children.Add(_folderEntries);

        _existingPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _existingPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _existingPane.Children.Add(browseGrid);
        _existingPane.Children.Add(listRow);
        Grid.SetRow(listRow, 1);
    }

    private void BuildPlacePane()
    {
        var caption = new TextBlock { Text = "Or select a place in this document:", Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetName(_documentPlaces, UiText.Get("Hyperlink_SelectPlaceInDocument"));
        AutomationProperties.SetAutomationId(_documentPlaces, "HyperlinkDocumentPlaces");
        _documentPlaces.SelectionChanged += (_, _) =>
        {
            if (SelectedDocumentPlace is { IsDefinedName: true })
                _targetBox.Text = string.Empty;
            else if (string.IsNullOrWhiteSpace(_targetBox.Text) && SelectedLinkType == HyperlinkTargetKind.PlaceInThisDocument)
                _targetBox.Text = HyperlinkDialogPanePlanner.DefaultCellReference;
        };
        _placePane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _placePane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _placePane.Children.Add(caption);
        _placePane.Children.Add(_documentPlaces);
        Grid.SetRow(_documentPlaces, 1);
    }

    private void BuildNewDocumentPane()
    {
        var pathRow = CreateFieldRow(new Thickness(0));
        pathRow.Children.Add(new TextBlock { Text = "Full path:", VerticalAlignment = System.Windows.VerticalAlignment.Top });
        pathRow.Children.Add(_fullPathText);
        Grid.SetColumn(_fullPathText, 1);
        AutomationProperties.SetAutomationId(_fullPathText, "HyperlinkNewDocumentFullPath");
        _changePathButton.Width = HyperlinkDialogPlanner.SecondaryButtonWidth;
        _changePathButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        _changePathButton.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        _changePathButton.Click += ChangePathButton_Click;
        AutomationProperties.SetAutomationId(_changePathButton, "HyperlinkChangePathButton");
        pathRow.Children.Add(_changePathButton);
        Grid.SetColumn(_changePathButton, 2);
        _newDocumentPane.Children.Add(pathRow);
    }

    private void BuildEmailPane()
    {
        var subjectRow = CreateFieldRow(new Thickness(0, 0, 0, 8));
        var subjectLabel = new Label { Content = "_Subject:", Target = _subjectBox, Padding = new Thickness(0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
        _subjectBox.Height = HyperlinkDialogPlanner.FieldHeight;
        AutomationProperties.SetName(_subjectBox, "Subject");
        AutomationProperties.SetAutomationId(_subjectBox, "HyperlinkEmailSubjectTextBox");
        subjectRow.Children.Add(subjectLabel);
        subjectRow.Children.Add(_subjectBox);
        Grid.SetColumn(_subjectBox, 1);

        var recentCaption = new TextBlock { Text = "Recently used e-mail addresses:", Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetName(_recentEmails, "Recently used e-mail addresses");
        AutomationProperties.SetAutomationId(_recentEmails, "HyperlinkRecentEmailAddresses");
        _recentEmails.SelectionChanged += (_, _) =>
        {
            if (_recentEmails.SelectedItem is string recent)
            {
                var (address, subject) = HyperlinkDialogPanePlanner.SplitEmailTarget(recent);
                _targetBox.Text = address;
                _subjectBox.Text = subject;
            }
        };

        _emailPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _emailPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _emailPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _emailPane.Children.Add(subjectRow);
        _emailPane.Children.Add(recentCaption);
        Grid.SetRow(recentCaption, 1);
        _emailPane.Children.Add(_recentEmails);
        Grid.SetRow(_recentEmails, 2);
    }

    private void ShowFolder(string folder)
    {
        _currentFolder = folder;
        _folderText.Text = folder;
        _folderEntries.ItemsSource = HyperlinkDialogPanePlanner.ListFolder(folder)
            .Select(entry => new ListBoxItem { Content = (entry.IsFolder ? "📁 " : "    ") + entry.Name, Tag = entry })
            .ToArray();
    }

    private void ShowRecentFiles()
    {
        _folderEntries.ItemsSource = Free.Shared.AppServices.RecentFilesStore.Load().Snapshot()
            .Select(entry => entry.Path)
            .Where(System.IO.File.Exists)
            .Select(path => new ListBoxItem
            {
                Content = "    " + path,
                Tag = new HyperlinkFolderEntry(System.IO.Path.GetFileName(path), path, IsFolder: false),
            })
            .ToArray();
    }

    private void NavigateUpFolder()
    {
        if (System.IO.Directory.GetParent(_currentFolder) is { } parent)
            ShowFolder(parent.FullName);
    }

    private void UpdateTargetFieldForLinkType()
    {
        var linkType = SelectedLinkType;
        var (label, automationName, helpText) = linkType switch
        {
            HyperlinkTargetKind.CreateNewDocument => (UiText.Get("Hyperlink_NewDocumentLabel"), UiText.Get("Hyperlink_NewDocumentAutomationName"), UiText.Get("Hyperlink_NewDocumentHelpText")),
            HyperlinkTargetKind.PlaceInThisDocument => (UiText.Get("Hyperlink_CellReferenceLabel"), UiText.Get("Hyperlink_CellReferenceAutomationName"), UiText.Get("Hyperlink_CellReferenceHelpText")),
            HyperlinkTargetKind.EmailAddress => (UiText.Get("Hyperlink_EmailAddressLabel"), UiText.Get("Hyperlink_EmailAddressAutomationName"), UiText.Get("Hyperlink_EmailAddressHelpText")),
            _ => (UiText.Get("Hyperlink_Address"), UiText.Get("Hyperlink_AddressAutomationName"), UiText.Get("Hyperlink_AddressHelpText"))
        };

        _targetLabel.Content = label;
        AutomationProperties.SetName(_targetBox, automationName);
        AutomationProperties.SetHelpText(_targetBox, helpText);

        _existingPane.Visibility = linkType == HyperlinkTargetKind.ExistingFileOrWebPage ? Visibility.Visible : Visibility.Collapsed;
        _placePane.Visibility = linkType == HyperlinkTargetKind.PlaceInThisDocument ? Visibility.Visible : Visibility.Collapsed;
        _newDocumentPane.Visibility = linkType == HyperlinkTargetKind.CreateNewDocument ? Visibility.Visible : Visibility.Collapsed;
        _emailPane.Visibility = linkType == HyperlinkTargetKind.EmailAddress ? Visibility.Visible : Visibility.Collapsed;
        _bookmarkButton.Visibility = linkType == HyperlinkTargetKind.ExistingFileOrWebPage ? Visibility.Visible : Visibility.Hidden;
        UpdateNewDocumentFullPath();
    }

    private void UpdateNewDocumentFullPath() =>
        _fullPathText.Text = HyperlinkDialogPanePlanner.ComposeNewDocumentPath(_newDocumentFolder, _targetBox.Text) is { Length: > 0 } path
            ? path
            : _newDocumentFolder;

    private void SelectDocumentPlace(string name)
    {
        foreach (var item in _documentPlaces.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is HyperlinkDocumentPlace place &&
                string.Equals(place.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _documentPlaces.SelectedItem = item;
                return;
            }
        }
    }

    /// <summary>The link target the selected pane's fields describe.</summary>
    private string ComposeTarget() => SelectedLinkType switch
    {
        HyperlinkTargetKind.PlaceInThisDocument => SelectedDocumentPlace is { } place
            ? HyperlinkDialogPanePlanner.ComposeDocumentLocation(place, _targetBox.Text)
            : _targetBox.Text,
        HyperlinkTargetKind.CreateNewDocument => HyperlinkDialogPanePlanner.ComposeNewDocumentPath(_newDocumentFolder, _targetBox.Text),
        HyperlinkTargetKind.EmailAddress => HyperlinkDialogPanePlanner.ComposeEmailTarget(_targetBox.Text, _subjectBox.Text),
        _ => _targetBox.Text,
    };

    private static IReadOnlyList<string> RecentEmailAddresses(Workbook? workbook) =>
        workbook is null
            ? []
            : workbook.Sheets
                .SelectMany(sheet => sheet.Hyperlinks.Values)
                .Where(target => target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToArray();

    private void Accept()
    {
        if (!TryCreateResult(ComposeTarget(), _displayBox.Text, SelectedLinkType, _screenTip, _bookmark, out var result, out var error))
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

    private void ChangePathButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Create New Document",
            InitialDirectory = _newDocumentFolder,
        };
        if (dialog.ShowDialog(this) != true)
            return;

        _newDocumentFolder = dialog.FolderName;
        UpdateNewDocumentFullPath();
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

    internal static IReadOnlyList<string> CurrentFolderContextForTest(string? currentFilePath) =>
        HyperlinkDialogPanePlanner.ListFolder(HyperlinkDialogPanePlanner.DefaultFolder(currentFilePath)).Select(entry => entry.Name).ToArray();

    /// <summary>The plan OK would produce for the current pane, without closing the dialog.</summary>
    internal HyperlinkDialogPlan? ComposeResultForTest() =>
        TryCreateResult(ComposeTarget(), _displayBox.Text, SelectedLinkType, _screenTip, _bookmark, out var result, out _)
            ? result
            : null;
}
