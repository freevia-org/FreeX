using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Backstage;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Pdf;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

public sealed class MainWindow : Window
{
    private const string DefaultTitle = "FreeW";
    private static readonly SisterAppFileTextSpec FileText = SisterAppFileTextPlanner.Document;

    private static readonly FilePickerFileType PdfFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            FreeWFileTextResources.PdfFileTypeName,
            ["*.pdf"],
            ["application/pdf"]);

    private readonly DocumentPersistenceWorkflow _documentPersistence = new();
    private readonly DocumentView _editor = new();
    private readonly TextBlock _status = SisterAppStatusBarChrome.CreateInfoText(margin: new Thickness(8, 0));
    // AV-MAIL: the Mailings engine (recipients / merge fields / preview / finish-merge) shared with the ribbon.
    private MailMergeEngine? _mailMerge;
    private readonly TextBox _findBox = new() { Width = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _replaceBox = new() { Width = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _zoomLabel = SisterAppStatusBarChrome.CreateInfoText("100%", margin: new Thickness(8, 0));
    private readonly ScaleTransform _zoom = new(1, 1);
    // Status-bar view-mode buttons (Print / Web / Draft).
    private readonly Button _btnPrintLayout  = MakeViewModeButton("Print");
    private readonly Button _btnWebLayout    = MakeViewModeButton("Web");
    private readonly Button _btnDraftView    = MakeViewModeButton("Draft");
    private readonly SisterAvaloniaFileCommandWorkflow _fileWorkflow;
    private readonly FreeWOptions _options;
    private readonly ApplicationOptionsStore<FreeWOptions> _optionsStore;
    private readonly AutosaveAdapter _autosave;
    private readonly NavigationPane _navPane;
    private readonly ReviewingPane _reviewingPane;
    private readonly RevealFormattingPane _revealPane;
    private Border? _findBar;
    private FindReplaceDialog? _findReplaceDialog;
    private ScrollViewer? _scroller;
    private double _zoomScale = 1.0;
    private bool _multiplePagesMode;
    private bool _sideToSideMode;
    private bool _suppressEditorDirty;
    private bool _closingConfirmed;

    public MainWindow()
        : this(Array.Empty<string>())
    {
    }

    public MainWindow(IReadOnlyList<string> startupArguments)
        : this(
            startupArguments,
            null,
            ApplicationOptionsStore<FreeWOptions>.Create(PlatformApplicationDataPathProvider.LocalInstance))
    {
    }

    internal MainWindow(
        IReadOnlyList<string> startupArguments,
        FreeWOptions? options,
        ApplicationOptionsStore<FreeWOptions> optionsStore)
    {
        _optionsStore = optionsStore;
        _options = options ?? _optionsStore.Load();
        _options.Normalize();

        Title = DefaultTitle;
        Width = 1040;
        Height = 720;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        _fileWorkflow = new SisterAvaloniaFileCommandWorkflow(
            owner: this,
            titleSpec: new SisterAvaloniaFileTitleSpec(
                ApplicationName: DefaultTitle,
                Separator: " - ",
                CollapseCleanUntitledTitle: true),
            maxRecentEntries: () => _options.RecentFilesCap,
            onChanged: UpdateStatus,
            save: () => SaveAsync().GetAwaiter().GetResult());
        _autosave = new AutosaveAdapter(_editor, _fileWorkflow.Workflow);
        _navPane = new NavigationPane(_editor);
        _reviewingPane = new ReviewingPane(_editor);
        _revealPane = new RevealFormattingPane(_editor);

        var ribbon = BuildRibbon();
        var statusBar = SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(
            Background: Brushes.White,
            LeftContent: _status,
            RightItems: BuildStatusRightItems(),
            BorderBrush: new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness: new Thickness(0, 1, 0, 0))).Root;
        var findBar = BuildFindBar();

        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(48, 24),
            Content = new LayoutTransformControl { LayoutTransform = _zoom, Child = _editor },
        };
        _navPane.ScrollerRef = _scroller;

        var workArea = new DockPanel { LastChildFill = true };

        // Nav pane docked left; reviewing pane docked right; workspace fills the remainder.
        DockPanel.SetDock(_navPane, Dock.Left);
        workArea.Children.Add(_navPane);

        DockPanel.SetDock(_reviewingPane, Dock.Right);
        workArea.Children.Add(_reviewingPane);

        DockPanel.SetDock(_revealPane, Dock.Right);
        workArea.Children.Add(_revealPane);

        var workspace = new Border { Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)), Child = _scroller };
        workArea.Children.Add(workspace);

        _editor.DocumentChanged += OnEditorDocumentChanged;
        _editor.DocumentChanged += () => { if (_navPane.IsVisible) _navPane.Refresh(); };
        _editor.DocumentChanged += () => { if (_reviewingPane.IsVisible) _reviewingPane.Refresh(); };
        _editor.DocumentChanged += () => { if (_revealPane.IsVisible) _revealPane.Refresh(); };
        _editor.ScrollToCaretRequested += ScrollCaretIntoView;
        _editor.CaretMoved += UpdateStatus;
        _editor.ViewModeChanged += UpdateStatus;
        _editor.ViewModeChanged += UpdateViewModeButtons;
        _editor.HyperlinkActivated += OpenExternalUri;

        // Wire view-mode buttons.
        _btnPrintLayout.Click += (_, _) => SetViewMode(DocumentViewMode.PrintLayout);
        _btnWebLayout.Click   += (_, _) => SetViewMode(DocumentViewMode.WebLayout);
        _btnDraftView.Click   += (_, _) => SetViewMode(DocumentViewMode.Draft);
        UpdateViewModeButtons();
        _editor.CellEditRequested += async req =>
        {
            var result = await new CellEditDialog(req.Text).ShowDialog<string?>(this);
            if (result is not null)
                _editor.SetCellText(req.Block, req.Row, req.Col, result);
        };
        LoadDocumentAsSaved(LoadStartupDocument(startupArguments), path: null);
        KeyDown += MainWindow_KeyDown;

        // Start autosave once the window is shown; offer recovery on first open.
        Opened += async (_, _) =>
        {
            _autosave.Start();
            await _autosave.OfferRecoveryAsync(this);
        };

        // Dirty-gate on close: run async dirty-check; cancel the close synchronously
        // and let the async flow re-close if the user saves or discards.
        Closing += OnWindowClosing;

        var frame = SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(
            chrome: ribbon,
            workArea: workArea,
            statusBar: statusBar,
            bottomPanelsAboveStatus: [findBar]));

        Content = frame.Root;
        UpdateStatus();
    }

    public DocumentView Editor => _editor;
    public bool HasToolbar { get; private set; }

    /// <summary>
    /// Exposes the navigation pane for tests that need to inspect its state headlessly.
    /// </summary>
    internal NavigationPane NavPane => _navPane;

    /// <summary>
    /// Exposes the reviewing pane for tests that need to inspect its state headlessly.
    /// </summary>
    internal ReviewingPane ReviewingPane => _reviewingPane;

    /// <summary>
    /// Exposes the reveal-formatting pane for tests that need to inspect its state headlessly.
    /// </summary>
    internal RevealFormattingPane RevealPane => _revealPane;

    /// <summary>
    /// Show or hide the navigation pane and refresh its heading list when making it visible.
    /// Wired to <c>freew.navigationpane</c> ribbon toggle.
    /// </summary>
    internal void ToggleNavigationPane()
    {
        _navPane.IsVisible = !_navPane.IsVisible;
        if (_navPane.IsVisible)
            _navPane.Refresh();
    }

    /// <summary>
    /// Show or hide the reviewing pane and refresh its tracked-changes list when making it visible.
    /// Wired to <c>freew.reviewingpane</c> ribbon toggle.
    /// </summary>
    internal void ToggleReviewingPane()
    {
        _reviewingPane.IsVisible = !_reviewingPane.IsVisible;
        if (_reviewingPane.IsVisible)
            _reviewingPane.Refresh();
    }

    /// <summary>
    /// Show or hide the Reveal Formatting pane and refresh its content when making it visible.
    /// Wired to <c>freew.reveal-formatting</c> ribbon toggle (View → Show group) and Shift+F1.
    /// </summary>
    internal void ToggleRevealFormatting()
    {
        _revealPane.IsVisible = !_revealPane.IsVisible;
        if (_revealPane.IsVisible)
            _revealPane.Refresh();
    }

    /// <summary>
    /// Opens the Find &amp; Replace dialog (modeless). If an instance is already open it is
    /// brought to the front. Wired to <c>freew.find-replace-dialog</c> ribbon command and Ctrl+H.
    /// </summary>
    internal void OpenFindReplaceDialog()
    {
        if (_findReplaceDialog is not null)
        {
            _findReplaceDialog.Activate();
            return;
        }

        _findReplaceDialog = new FindReplaceDialog(_editor)
        {
            ScrollerRef = _scroller,
        };
        _findReplaceDialog.Closed += (_, _) => _findReplaceDialog = null;
        _findReplaceDialog.Show(this);
    }

    /// <summary>
    /// Opens the Font dialog (modal). Pre-populates from the caret formatting; on OK applies the
    /// changes to the selection via <see cref="DocumentView"/> formatting methods.
    /// Wired to <c>freew.font-dialog</c> ribbon command (Home → Font group).
    /// </summary>
    private Task OpenFontDialogAsync() =>
        FontDialog.ShowAndApplyAsync(this, _editor);

    /// <summary>
    /// Opens the Paragraph dialog (modal). Pre-populates from the current paragraph's formatting;
    /// on OK applies the changes via <see cref="DocumentView"/> paragraph methods.
    /// Wired to <c>freew.paragraph-dialog</c> ribbon command (Home → Paragraph group).
    /// </summary>
    private Task OpenParagraphDialogAsync() =>
        ParagraphDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenTabsDialogAsync() =>
        TabsDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenBordersAndShadingDialogAsync() =>
        BordersAndShadingDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenSortDialogAsync() =>
        SortDialog.ShowAndApplyAsync(this, _editor);

    /// <summary>
    /// Opens the Page Setup dialog (modal). Pre-populates from the document's current page
    /// geometry; on OK applies the changes as a single undoable step.
    /// Wired to <c>freew.page-setup-dialog</c> ribbon command (Layout → Page Setup group).
    /// </summary>
    private Task OpenPageSetupDialogAsync() =>
        PageSetupDialog.ShowAndApplyAsync(this, _editor);

    /// <summary>
    /// AV-DESIGN: Opens the Page Borders dialog (modal); on OK applies the chosen border via
    /// <see cref="DocumentView.SetPageBorder"/> (undoable), or removes it on "None". Wired to
    /// <c>freew.page-borders</c> (Design → Page Background group).
    /// </summary>
    private async Task OpenPageBordersDialogAsync()
    {
        var dialog = new PageBordersDialog(_editor.Document.Page.PageBorder);
        await dialog.ShowDialog(this);
        if (dialog.RemoveRequested)
            _editor.SetPageBorder(null);
        else if (dialog.Result is { } border)
            _editor.SetPageBorder(border);
    }

    /// <summary>
    /// AV-DESIGN: Opens the Custom Watermark dialog (modal); on OK applies the chosen text watermark via
    /// <see cref="DocumentView.SetWatermark"/> (undoable), or removes it on "No Watermark". Wired to
    /// <c>freew.watermark.custom</c> (Design → Page Background group).
    /// </summary>
    private async Task OpenWatermarkDialogAsync()
    {
        var dialog = new WatermarkDialog(_editor.Document.Page.EffectiveWatermark);
        await dialog.ShowDialog(this);
        if (dialog.RemoveRequested)
            _editor.SetWatermark(null);
        else if (dialog.Result is { } options)
            _editor.SetWatermark(options);
    }

    /// <summary>
    /// AV-REVIEW: Opens the Word Count dialog (modal), showing words/characters/paragraphs/lines computed
    /// from the document model. Wired to <c>freew.word-count</c> ribbon command (Review → Proofing group).
    /// </summary>
    private Task OpenWordCountDialogAsync() =>
        new WordCountDialog(_editor.ComputeStatistics()).ShowDialog(this);

    private async Task ReplyToCommentAsync()
    {
        if (_editor.CommentsAtCaret.Count == 0)
        {
            _status.Text = "Place the caret in a comment to reply.";
            _editor.Focus();
            return;
        }

        var text = await CommentReplyDialog.AskAsync(this);
        if (!string.IsNullOrWhiteSpace(text) && !_editor.ReplyToCommentAtCaret(text))
            _status.Text = "Place the caret in a comment to reply.";
        _editor.Focus();
    }

    private async Task ShowCommentsAsync(IReadOnlyList<CommentListItem> items)
    {
        await CommentListDialog.ShowAsync(this, items);
        _editor.Focus();
    }

    /// <summary>
    /// Opens the Avalonia print-preview surface over a snapshot of the current document. Native print
    /// selection remains deferred, but the preview uses the same paginated renderer as the live editor.
    /// </summary>
    private Task OpenPrintPreviewAsync()
    {
        try
        {
            var snapshot = CloneDocument(_editor.Document);
            return new PrintPreviewDialog(snapshot, _fileWorkflow.DisplayName).ShowDialog(this);
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed("Print Preview", ex.Message);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// AV-VIEW: Opens the Zoom dialog (modal). Pre-selects the preset matching the current zoom (or the
    /// custom box), and on OK applies the chosen scale through the same <see cref="ApplyZoom(double)"/>
    /// path as the quick zoom commands. Wired to <c>freew.zoom-dialog</c> (View → Zoom group).
    /// </summary>
    private async Task OpenZoomDialogAsync()
    {
        var dialog = new ZoomDialog(_zoomScale);
        await dialog.ShowDialog(this);
        if (dialog.Result is { } scale)
        {
            ApplyZoom(scale);
            _editor.Focus();
        }
    }

    /// <summary>
    /// AV-VIEW: Window → New Window. Opens a second top-level window showing the same document content.
    /// The document is round-tripped through the in-memory docx serializer so the second window edits an
    /// independent copy (TextDocument has no deep-clone), matching the spirit of Word's "new window on the
    /// same document". Wired to <c>freew.new-window</c>.
    /// </summary>
    private void OpenNewWindow()
    {
        try
        {
            using var buffer = new MemoryStream();
            DocxWriter.Write(_editor.Document, buffer);
            buffer.Position = 0;
            var copy = DocxReader.Read(buffer);

            var second = new MainWindow();
            second.LoadDocumentContent(copy);
            second.Title = Title + " : 2";
            second.Show();
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed("New window", ex.Message);
        }
    }

    /// <summary>
    /// AV-VIEW: Window → Split. A true split-pane (two scroll regions over one document) is a larger
    /// surface than this wave; the command is wired and discoverable but reports the feature as deferred
    /// in the status bar rather than pretending to act. Wired to <c>freew.split</c>.
    /// </summary>
    private void ToggleSplit()
    {
        _status.Text = "Split view is not yet available in the Avalonia shell (deferred).";
    }

    private void ZoomToOnePage()
    {
        var (_, _, wholePageFactor) = ComputeZoomFitFactors();
        ApplyZoom(wholePageFactor);
        _editor.Focus();
    }

    private void ZoomToPageWidth()
    {
        var (pageWidthFactor, _, _) = ComputeZoomFitFactors();
        ApplyZoom(pageWidthFactor);
        _editor.Focus();
    }

    private void ToggleMultiplePages()
    {
        _multiplePagesMode = !_multiplePagesMode;
        if (_multiplePagesMode)
            _sideToSideMode = false;

        _status.Text = _multiplePagesMode
            ? "Multiple Pages view is not yet available in the Avalonia shell (deferred)."
            : "Multiple Pages view is off.";
    }

    private void ToggleSideToSide()
    {
        _sideToSideMode = !_sideToSideMode;
        if (_sideToSideMode)
            _multiplePagesMode = false;

        _status.Text = _sideToSideMode
            ? "Side to Side view is not yet available in the Avalonia shell (deferred)."
            : "Side to Side view is off.";
    }

    private (double PageWidthFactor, double TextWidthFactor, double WholePageFactor) ComputeZoomFitFactors()
    {
        var page = _editor.Document.Page;
        var (pageWidthDip, pageHeightDip) = PageLayout.PageSizeDip(page);
        var (contentWidthDip, _) = PageLayout.ContentAreaDip(page);

        var viewportWidth = 0.0;
        var viewportHeight = 0.0;
        if (_scroller is not null)
        {
            viewportWidth = Math.Max(0, _scroller.Bounds.Width - _scroller.Padding.Left - _scroller.Padding.Right);
            viewportHeight = Math.Max(0, _scroller.Bounds.Height - _scroller.Padding.Top - _scroller.Padding.Bottom);
        }

        return (
            ZoomFit.PageWidth(pageWidthDip, viewportWidth),
            ZoomFit.TextWidth(contentWidthDip, viewportWidth),
            ZoomFit.WholePage(pageWidthDip, pageHeightDip, viewportWidth, viewportHeight));
    }

    /// <summary>
    /// Toggle the document orientation between Portrait and Landscape (AV-PAGE).
    /// Wired to <c>freew.page-orientation</c>.
    /// </summary>
    private void ToggleOrientation()
    {
        var page = _editor.Document.Page;
        var settings = page.Clone();
        settings.Landscape = !page.Landscape;
        // Swap width/height so the model always reflects the actual render dimensions.
        (settings.WidthPt, settings.HeightPt) = (page.HeightPt, page.WidthPt);
        _editor.SetPageSettings(settings);
    }

    /// <summary>
    /// Apply a named margin preset (AV-PAGE).  Recognised names: "normal" (72pt / 1in all
    /// sides), "narrow" (36pt / 0.5in all sides), "wide" (108pt / 1.5in left+right, 72pt top+bottom).
    /// Wired to <c>freew.page-margins-*</c> ribbon commands.
    /// </summary>
    private void ApplyMarginPreset(string preset)
    {
        var settings = _editor.Document.Page.Clone();
        switch (preset.ToLowerInvariant())
        {
            case "normal":
                settings.MarginTopPt = settings.MarginBottomPt =
                settings.MarginLeftPt = settings.MarginRightPt = 72;
                break;
            case "narrow":
                settings.MarginTopPt = settings.MarginBottomPt =
                settings.MarginLeftPt = settings.MarginRightPt = 36;
                break;
            case "wide":
                settings.MarginTopPt    = 72;
                settings.MarginBottomPt = 72;
                settings.MarginLeftPt   = 108;
                settings.MarginRightPt  = 108;
                break;
            default:
                return; // unknown preset — no-op
        }
        _editor.SetPageSettings(settings);
    }

    /// <summary>
    /// Apply a quick paper size (AV-PAGE).  Recognised names: "letter" (612 × 792 pt),
    /// "a4" (595.3 × 841.9 pt). Preserves the current orientation.
    /// Wired to <c>freew.page-size-*</c> ribbon commands.
    /// </summary>
    private void ApplyPaperSize(string name)
    {
        var page = _editor.Document.Page;
        var settings = page.Clone();
        var landscape = page.Landscape || page.WidthPt > page.HeightPt;

        (double portraitW, double portraitH) = name.ToLowerInvariant() switch
        {
            "letter" => (612.0, 792.0),
            "a4"     => (595.3, 841.9),
            _        => (page.WidthPt, page.HeightPt), // unknown — no-op
        };

        if (name.ToLowerInvariant() is not ("letter" or "a4"))
            return;

        // Apply in portrait order then swap if landscape.
        settings.WidthPt  = landscape ? portraitH : portraitW;
        settings.HeightPt = landscape ? portraitW : portraitH;
        _editor.SetPageSettings(settings);
    }

    private static TextDocument LoadStartupDocument(IReadOnlyList<string> startupArguments)
    {
        var path = startupArguments.FirstOrDefault(a => a.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) && File.Exists(a));
        if (path is null)
            return SampleDocument.Create();
        try
        {
            return DocxReader.Read(path);
        }
        catch (Exception)
        {
            return SampleDocument.Create();
        }
    }

    private Control BuildRibbon()
    {
        var callbacks = new RibbonHostCallbacks(
            Open: () => _ = OpenAsync(),
            Save: () => _ = SaveAsync(),
            ImportPdfText: () => _ = ImportPdfTextAsync(),
            Cut: () => _ = CutAsync(),
            Copy: () => _ = CopyAsync(),
            Paste: () => _ = PasteAsync(),
            PastePlainText: () => _ = PastePlainTextAsync(),
            PasteMergeFormatting: () => _ = PasteMergeFormattingAsync(),
            OpenPasteSpecial: () => _ = OpenPasteSpecialAsync(),
            Backstage: () => _ = ShowBackstageAsync(),
            NewDocument: NewDocument,
            ToggleNavigationPane: ToggleNavigationPane,
            ToggleReviewingPane: ToggleReviewingPane,
            ToggleRevealFormatting: ToggleRevealFormatting,
            OpenFindReplaceDialog: OpenFindReplaceDialog,
            SetPrintLayout: () => SetViewMode(DocumentViewMode.PrintLayout),
            SetWebLayout:   () => SetViewMode(DocumentViewMode.WebLayout),
            SetDraftView:   () => SetViewMode(DocumentViewMode.Draft),
            OpenFontDialog:      () => _ = OpenFontDialogAsync(),
            OpenParagraphDialog: () => _ = OpenParagraphDialogAsync(),
            OpenPageSetupDialog: () => _ = OpenPageSetupDialogAsync(),
            ToggleOrientation:   ToggleOrientation,
            ApplyMarginPreset:   ApplyMarginPreset,
            ApplyPaperSize:      ApplyPaperSize,
            InsertPicture:       () => _ = InsertPictureAsync(),
            OpenWordCountDialog: () => _ = OpenWordCountDialogAsync(),
            ApplyZoom: (absolute, delta) =>
            {
                var newScale = absolute.HasValue ? absolute.Value : _zoomScale + delta;
                ApplyZoom(newScale);
            },
            OpenTabsDialog: () => _ = OpenTabsDialogAsync(),
            OpenBordersAndShadingDialog: () => _ = OpenBordersAndShadingDialogAsync(),
            OpenSortDialog: () => _ = OpenSortDialogAsync(),
            OpenZoomDialog: () => _ = OpenZoomDialogAsync(),
            OpenPrintPreview: () => _ = OpenPrintPreviewAsync(),
            NewWindow:       OpenNewWindow,
            ToggleSplit:     ToggleSplit,
            ZoomOnePage:     ZoomToOnePage,
            ZoomPageWidth:   ZoomToPageWidth,
            ToggleMultiplePages: ToggleMultiplePages,
            IsMultiplePagesActive: () => _multiplePagesMode,
            ToggleSideToSide: ToggleSideToSide,
            IsSideToSideActive: () => _sideToSideMode,
            // AV-INSERT2: Insert depth 2 dialog launchers (optional callbacks).
            OpenHyperlinkDialog: () => _ = OpenHyperlinkDialogAsync(),
            OpenBookmarkDialog:  () => _ = OpenBookmarkDialogAsync(),
            OpenQuickPartDialog: () => _ = OpenQuickPartDialogAsync(),
            InsertTextFromFile:  () => _ = InsertTextFromFileAsync(),
            // AV-MAIL: surface mail-merge info messages in the status bar.
            ShowMailMergeInfo: msg => _status.Text = msg,
            // AV-DESIGN: Page Borders + Custom Watermark dialog launchers (optional callbacks).
            OpenPageBordersDialog: () => _ = OpenPageBordersDialogAsync(),
            OpenWatermarkDialog:   () => _ = OpenWatermarkDialogAsync(),
            // AV-REVIEW: route ribbon safety/protect commands through the same Backstage flows.
            MarkAsFinal: ToggleMarkAsFinal,
            RestrictEditing: () => _ = OpenRestrictEditingAsync(),
            InspectDocument: () => _ = InspectDocumentAsync(),
            CheckAccessibility: () => _ = CheckAccessibilityAsync(),
            ReplyComment: () => _ = ReplyToCommentAsync(),
            ShowComments: rows => _ = ShowCommentsAsync(rows));

        // AV-MAIL: capture the Mailings engine so the shell can drive its two dialog-bound commands
        // (Select Recipients / Insert Merge Field) with async Avalonia dialogs over the same session the
        // ribbon commands share. The remaining Mailings commands (address-block / greeting / preview /
        // next / prev / finish) are wired directly by the registry and need no shell glue.
        var registry = FreeWRibbon.BuildRegistry(_editor, callbacks, out var mailMerge);
        _mailMerge = mailMerge;
        registry.Register(new RibbonCommandId("freew.select-recipients"),
            new ActionRibbonCommand(() => _ = SelectRecipientsAsync()));
        registry.Register(new RibbonCommandId("freew.merge-field"),
            new ActionRibbonCommand(() => _ = InsertMergeFieldAsync()));
        // AV-PICTAB: merge the Table (caret-in-cell) and Floating (picture/drawing selected)
        // contextual triggers so both sets of contextual tabs can surface from one source.
        var contextSource = new CompositeRibbonContextSource(
            new TableRibbonContextSource(_editor),
            new FloatingRibbonContextSource(_editor));
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(
            FreeWRibbon.BuildDefinition(),
            registry,
            contextSource: contextSource,
            afterExecute: () => _editor.Focus());
        HasToolbar = true;
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = ribbon,
        };
    }

    // AV-MAIL: Mailings > Select Recipients. Prompt for a CSV recipient list (seeded with the document's
    // existing merge-field names as the header hint), then load it into the shared merge session.
    private async Task SelectRecipientsAsync()
    {
        if (_mailMerge is null)
            return;
        var fields = FreeW.Core.Model.MailMerge.FieldNames(_editor.Document);
        var seed = fields.Count > 0 ? string.Join(",", fields) : string.Empty;
        var csv = await MailMergeDialogs.AskRecipientCsvAsync(this, seed);
        if (string.IsNullOrWhiteSpace(csv))
            return;
        var data = _mailMerge.LoadRecipientsCsv(csv);
        _status.Text = data.Count > 0
            ? $"Loaded {data.Count} recipient(s): {string.Join(", ", data.Header)}"
            : "Recipient list is empty.";
        _editor.Focus();
    }

    // AV-MAIL: Mailings > Insert Merge Field. Pick / type a field name (seeded with the loaded recipient
    // list's columns), then insert the «Field» placeholder at the caret through the undoable edit path.
    private async Task InsertMergeFieldAsync()
    {
        if (_mailMerge is null)
            return;
        var name = await MailMergeDialogs.AskMergeFieldNameAsync(this, _mailMerge.AvailableFieldNames);
        if (string.IsNullOrWhiteSpace(name))
            return;
        _mailMerge.InsertMergeFieldNamed(name);
        _editor.Focus();
    }

    // OS clipboard via Avalonia's data-transfer API (same pattern as the FreeX shell):
    // TopLevel.Clipboard with SetTextAsync / TryGetTextAsync.
    private Control BuildFindBar()
    {
        var next = new Button { Content = "Find Next", Padding = new Thickness(10, 4), Margin = new Thickness(6, 0, 0, 0) };
        next.Click += (_, _) => DoFind();
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                DoFind();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ToggleFindBar(show: false);
                e.Handled = true;
            }
        };

        var replace = new Button { Content = "Replace", Padding = new Thickness(10, 4), Margin = new Thickness(6, 0, 0, 0) };
        replace.Click += (_, _) => DoReplace();
        var replaceAll = new Button { Content = "Replace All", Padding = new Thickness(6, 4), Margin = new Thickness(4, 0, 0, 0) };
        replaceAll.Click += (_, _) => DoReplaceAll();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 4),
            Children =
            {
                new TextBlock { Text = "Find:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) },
                _findBox,
                next,
                new TextBlock { Text = "Replace:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) },
                _replaceBox,
                replace,
                replaceAll,
            },
        };
        _findBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            IsVisible = false,
            Child = row,
        };
        return _findBar;
    }

    private IReadOnlyList<Control> BuildStatusRightItems()
    {
        var viewModeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            Children = { _btnPrintLayout, _btnWebLayout, _btnDraftView },
        };
        return [_zoomLabel, viewModeRow];
    }

    private static Button MakeViewModeButton(string label) => new()
    {
        Content = label,
        Padding = new Thickness(6, 1),
        Margin = new Thickness(1, 1),
        Height = 20,
        FontSize = 11,
    };

    private void SetViewMode(DocumentViewMode mode)
    {
        _editor.ViewMode = mode;
        _editor.Focus();
    }

    private void UpdateViewModeButtons()
    {
        var mode = _editor.ViewMode;
        // Highlight the active button by toggling opacity; a full toggle style is overkill here.
        _btnPrintLayout.Opacity = mode == DocumentViewMode.PrintLayout ? 1.0 : 0.5;
        _btnWebLayout.Opacity   = mode == DocumentViewMode.WebLayout    ? 1.0 : 0.5;
        _btnDraftView.Opacity   = mode == DocumentViewMode.Draft        ? 1.0 : 0.5;
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (!ctrl)
            return;

        switch (e.Key)
        {
            case Key.F: ToggleFindBar(show: true); e.Handled = true; break;
            case Key.H: OpenFindReplaceDialog(); e.Handled = true; break;
            case Key.N: NewDocument(); e.Handled = true; break;
            case Key.O: _ = OpenAsync(); e.Handled = true; break;
            case Key.S: _ = SaveAsync(); e.Handled = true; break;
            case Key.P when (e.KeyModifiers & KeyModifiers.Shift) != 0: _ = ExportPdfAsync(); e.Handled = true; break;
            case Key.OemPlus or Key.Add: ApplyZoom(_zoomScale + 0.1); e.Handled = true; break;
            case Key.OemMinus or Key.Subtract: ApplyZoom(_zoomScale - 0.1); e.Handled = true; break;
            case Key.D0 or Key.NumPad0: ApplyZoom(1.0); e.Handled = true; break;
        }

        // Shift+F1 (no Ctrl required) = Reveal Formatting — matches Word's shortcut.
        if (e.Key == Key.F1 && (e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            ToggleRevealFormatting();
            e.Handled = true;
        }
    }

    // ── Closing gate ─────────────────────────────────────────────────────────

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // If we already ran the async gate and decided it's OK to close, let it through.
        if (_closingConfirmed)
        {
            _ = _autosave.StopAsync(); // fire-and-forget — cleanup is best-effort on close
            return;
        }

        // Cancel this synchronous close event and run the gate asynchronously.
        e.Cancel = true;
        _ = ConfirmAndCloseAsync();
    }

    private async Task ConfirmAndCloseAsync()
    {
        // ConfirmCloseAllowed runs on the UI thread because the shared Avalonia workflow
        // shows the save-changes dialog synchronously for the dirty-gate path.
        var allowed = _fileWorkflow.ConfirmCloseAllowed("closing");
        if (!allowed)
            return;

        await _autosave.StopAsync();
        _closingConfirmed = true;
        Close();
    }

    private void ApplyZoom(double scale)
    {
        _zoomScale = Math.Clamp(Math.Round(scale, 2), 0.5, 3.0);
        _zoom.ScaleX = _zoomScale;
        _zoom.ScaleY = _zoomScale;
        _zoomLabel.Text = $"{Math.Round(_zoomScale * 100)}%";
    }

    private void NewDocument()
    {
        _fileWorkflow.New(
            FileText.NewAction,
            () => LoadDocumentContent(TextDocument.CreateEmpty()));
    }

    private void ToggleFindBar(bool show)
    {
        if (_findBar is null)
            return;
        _findBar.IsVisible = show;
        if (show)
            _findBox.Focus();
    }

    private void DoFind()
    {
        var query = _findBox.Text;
        if (string.IsNullOrEmpty(query))
            return;
        if (!_editor.FindNext(query))
            _status.Text = $"No match for \"{query}\".";
    }

    private void DoReplace()
    {
        var query = _findBox.Text;
        if (string.IsNullOrEmpty(query))
            return;
        if (!_editor.ReplaceNext(query, _replaceBox.Text ?? string.Empty))
            _status.Text = $"No match for \"{query}\".";
    }

    private void DoReplaceAll()
    {
        var query = _findBox.Text;
        if (string.IsNullOrEmpty(query))
            return;
        var n = _editor.ReplaceAll(query, _replaceBox.Text ?? string.Empty);
        _status.Text = $"Replaced {n} occurrence{(n == 1 ? "" : "s")} of \"{query}\".";
        UpdateStatus();
    }

    private void ScrollCaretIntoView()
    {
        if (_scroller is null)
            return;
        var target = Math.Max(0, _editor.CaretTop - 40);
        _scroller.Offset = new Vector(_scroller.Offset.X, target);
    }

    private async Task CopyAsync()
    {
        var text = _editor.SelectedText;
        if (text.Length == 0)
            return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private async Task CutAsync()
    {
        await CopyAsync();
        _editor.TryDeleteSelection();
    }

    private async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        var text = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text))
            _editor.InsertText(text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '));
    }

    private async Task PastePlainTextAsync()
    {
        var text = await TryGetClipboardTextAsync();
        if (!_editor.PastePlainText(text))
            _status.Text = "Clipboard does not contain text.";
    }

    private async Task PasteMergeFormattingAsync()
    {
        var text = await TryGetClipboardTextAsync();
        if (!_editor.PasteMergeFormatting(text))
            _status.Text = "Clipboard does not contain text.";
    }

    private async Task OpenPasteSpecialAsync()
    {
        var text = await TryGetClipboardTextAsync();
        if (PasteText.Normalize(text).Length == 0)
        {
            _status.Text = "Clipboard does not contain text.";
            return;
        }

        var option = await PasteSpecialDialog.ShowAsync(this);
        if (option is null)
            return;

        var pasted = option.Value == PasteSpecialOption.KeepTextOnly
            ? _editor.PastePlainText(text)
            : _editor.PasteMergeFormatting(text);
        if (!pasted)
            _status.Text = "Clipboard does not contain text.";
    }

    private async Task<string?> TryGetClipboardTextAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return null;
        return await clipboard.TryGetTextAsync();
    }

    private async Task OpenAsync()
    {
        await _fileWorkflow.OpenAsync(
            FileText.OpenAction,
            PromptOpenPathAsync,
            OpenPathAsync);
    }

    private async Task<string?> PromptOpenPathAsync()
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                FileText.OpenPickerTitle,
                DocumentFilePickerTypes.BuildOpenTypes(_documentPersistence.Adapters)));
        return file?.LocalPath;
    }

    private Task<bool> OpenPathAsync(string path)
    {
        if (!_documentPersistence.CanOpenPath(path))
        {
            _status.Text = SisterAppFileTextPlanner.FormatUnsupportedFileType(
                SisterAppFileTextPlanner.OpenCommand,
                Path.GetExtension(path));
            return Task.FromResult(false);
        }

        try
        {
            ApplyOpenResult(_documentPersistence.Open(path));

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(SisterAppFileTextPlanner.OpenCommand, ex.Message);
            return Task.FromResult(false);
        }
    }

    private async Task ImportPdfTextAsync()
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                "Import PDF (text only)",
                DocumentFilePickerTypes.BuildPdfImportTypes()));
        var path = file?.LocalPath;
        if (path is null)
            return;

        if (DocumentFileFormatResolver.FindOpenAdapter(
                DocumentFileAdapterCatalog.CreatePdfImportAdapters(),
                Path.GetExtension(path),
                out _) is not { } adapter)
        {
            _status.Text = $"PDF import failed: unsupported file type \"{Path.GetExtension(path)}\".";
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            LoadDocumentContent(adapter.Load(stream));
            _fileWorkflow.MarkDirtyWithPath(null);
            _status.Text = $"Imported PDF text from {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _status.Text = $"PDF import failed: {ex.Message}";
        }
    }

    private Task<bool> SaveAsync() =>
        _fileWorkflow.SaveAsync(SaveToCurrentPathAsync, SaveAsAsync);

    private Task<bool> SaveToCurrentPathAsync(string path) =>
        _documentPersistence.TryResolveCurrentSaveTarget(path, out var target)
            ? SaveToTargetAsync(target)
            : SaveAsAsync();

    private async Task<bool> SaveAsAsync()
    {
        var savePlan = _documentPersistence.BuildSavePickerPlan(
            _fileWorkflow.CurrentPath,
            _fileWorkflow.CurrentFileName,
            FileText.FallbackDisplayName);
        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromSavePlan(FileText.SavePickerTitle, savePlan));
        var path = file?.LocalPath;
        return path is not null && await SaveToPathAsync(path);
    }

    private Task<bool> SaveToPathAsync(string path) =>
        SaveToPathAsync(path, filterIndex: 0);

    private Task<bool> SaveToPathAsync(string path, int filterIndex)
    {
        if (!_documentPersistence.TryResolveSaveTarget(path, filterIndex, out var target))
        {
            _status.Text = SisterAppFileTextPlanner.FormatUnsupportedFileType(
                SisterAppFileTextPlanner.SaveCommand,
                Path.GetExtension(path));
            return Task.FromResult(false);
        }

        return SaveToTargetAsync(target);
    }

    private Task<bool> SaveToTargetAsync(DocumentSaveTarget target)
    {
        try
        {
            _documentPersistence.Save(_editor.Document, target);
            MarkDocumentSavedWithPath(target.Path);
            _status.Text = SisterAppFileTextPlanner.FormatSaved(Path.GetFileName(target.Path));
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(SisterAppFileTextPlanner.SaveCommand, ex.Message);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// File → Export to PDF (Ctrl+Shift+P). Builds the shared app-agnostic PDF model from the editor
    /// layout and writes a real PDF via <see cref="FreeWAvaloniaPdfExport"/> (Skia when available,
    /// dependency-free WinAnsi fallback otherwise). Mirrors the FreeX Avalonia shell's File → Export
    /// to PDF, on the shared PDF tier.
    /// </summary>
    private async Task ExportPdfAsync()
    {
        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromFileTypes(
                FreeWFileTextResources.ExportPdfPickerTitle,
                [PdfFileType],
                _fileWorkflow.CurrentFileNameWithoutExtensionOr(FileText.FallbackDisplayName) + ".pdf",
                "pdf"));
        var path = file?.LocalPath;
        if (path is null)
            return;

        try
        {
            var result = FreeWAvaloniaPdfExport.Save(_editor, path);
            _status.Text = FreeWFileTextResources.FormatPdfExported(result.PageCount, result.Backend, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(FreeWFileTextResources.PdfExportCommand, ex.Message);
        }
    }

    private static readonly FilePickerFileType ImageFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            FreeWFileTextResources.PictureFileTypeName,
            ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.tif", "*.tiff"],
            ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff"]);

    /// <summary>
    /// Insert &gt; Picture (AV-INSERT): open a file picker, read the chosen image, and insert it at the
    /// caret as an inline image. The display size is derived from the image's natural pixel dimensions
    /// (96 DPI → points), capped so a large photo does not overflow the page; the bytes are stored verbatim.
    /// </summary>
    private async Task InsertPictureAsync()
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                SisterAppFileTextPlanner.InsertPicturePickerTitle,
                [ImageFileType]));
        var path = file?.LocalPath;
        if (path is null)
            return;

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var (widthPt, heightPt) = MeasureImagePoints(bytes);
            _editor.InsertInlineImage(bytes, widthPt, heightPt);
            _editor.Focus();
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(SisterAppFileTextPlanner.InsertPictureCommand, ex.Message);
        }
    }

    /// <summary>
    /// Decode <paramref name="bytes"/> to recover the natural pixel size, convert to points at 96 DPI, and
    /// cap the longest edge so the image fits a typical page body. Falls back to a sensible default size
    /// when the bytes cannot be decoded (e.g. EMF/WMF, which Avalonia's Bitmap cannot read).
    /// </summary>
    private static (double WidthPt, double HeightPt) MeasureImagePoints(byte[] bytes)
    {
        const double maxEdgePt = 360.0; // ~5 inches — fits the body of a Letter/A4 page with 1in margins
        try
        {
            using var ms = new MemoryStream(bytes);
            var bitmap = new Bitmap(ms);
            var widthPt = bitmap.PixelSize.Width * 72.0 / 96.0;
            var heightPt = bitmap.PixelSize.Height * 72.0 / 96.0;
            if (widthPt <= 0 || heightPt <= 0)
                return (200, 150);
            var longest = Math.Max(widthPt, heightPt);
            if (longest > maxEdgePt)
            {
                var scale = maxEdgePt / longest;
                widthPt *= scale;
                heightPt *= scale;
            }
            return (widthPt, heightPt);
        }
        catch
        {
            return (200, 150); // undecodable (metafile) → default box; bytes still round-trip verbatim
        }
    }

    // ── AV-INSERT2: Insert depth 2 dialog launchers ─────────────────────────────

    /// <summary>
    /// AV-INSERT2: Opens the Insert Hyperlink dialog. Pre-fills the display field with the current selection
    /// text (Word's behaviour), and on OK inserts/converts the hyperlink via
    /// <see cref="DocumentView.InsertHyperlink"/>. Wired to <c>freew.insert-hyperlink</c> (Insert → Links).
    /// </summary>
    private async Task OpenHyperlinkDialogAsync()
    {
        var dialog = new HyperlinkDialog(initialDisplay: _editor.SelectedText);
        await dialog.ShowDialog(this);
        if (dialog.Address is { } address)
        {
            _editor.InsertHyperlink(dialog.DisplayText ?? string.Empty, address);
            _editor.Focus();
        }
    }

    /// <summary>
    /// AV-INSERT2: Opens the Bookmark dialog (add at caret / Go To existing). Lists the document's current
    /// bookmark names. Wired to <c>freew.insert-bookmark</c> (Insert → Links).
    /// </summary>
    private async Task OpenBookmarkDialogAsync()
    {
        var names = Bookmarks.List(_editor.Document)
            .Select(b => b.Name)
            .Distinct()
            .ToList();
        var dialog = new BookmarkDialog(names);
        await dialog.ShowDialog(this);
        if (dialog.BookmarkName is { } add)
            _editor.InsertBookmark(add);
        else if (dialog.GoToName is { } go)
            _editor.GoToBookmark(go);
        _editor.Focus();
    }

    /// <summary>
    /// AV-INSERT2: Opens the Insert Quick Part dialog (a free-text snippet) and inserts the entered text at
    /// the caret. Wired to <c>freew.quick-parts.snippet</c> (Insert → Text → Quick Parts).
    /// </summary>
    private async Task OpenQuickPartDialogAsync()
    {
        var dialog = new QuickPartDialog();
        await dialog.ShowDialog(this);
        if (dialog.SnippetText is { } text)
        {
            _editor.InsertQuickPartText(text);
            _editor.Focus();
        }
    }

    /// <summary>
    /// AV-INSERT2: Insert Text from File — opens a file picker for a .docx/.txt, loads it (reusing the open
    /// adapters for .docx; a plain reader for .txt), and inserts the document's plain text at the caret as a
    /// Quick-Part-style multi-paragraph insert. Wired to <c>freew.text-from-file</c> (Insert → Text).
    /// </summary>
    private async Task InsertTextFromFileAsync()
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                InsertDialogTextResources.TextFromFilePickerTitle,
                [TextFromFileType]));
        var path = file?.LocalPath;
        if (path is null)
            return;

        try
        {
            string text;
            var ext = Path.GetExtension(path);
            if (string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
            {
                text = await File.ReadAllTextAsync(path);
            }
            else
            {
                var adapter = DocumentFileFormatResolver.FindOpenAdapter(_documentPersistence.Adapters, ext, out _);
                if (adapter is null)
                {
                    _status.Text = SisterAppFileTextPlanner.FormatUnsupportedFileType("Insert text", ext);
                    return;
                }
                using var stream = File.OpenRead(path);
                var document = adapter.Load(stream);
                text = document.PlainText;
            }

            _editor.InsertQuickPartText(text);
            _editor.Focus();
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed("Insert text", ex.Message);
        }
    }

    private static readonly FilePickerFileType TextFromFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            FreeWFileTextResources.TextFromFileTypeName,
            ["*.docx", "*.txt"],
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document", "text/plain"]);

    private void ApplyOpenResult(DocumentOpenResult result) =>
        LoadDocumentAsSaved(result.Document, result.SavedPath);

    private void LoadDocumentAsSaved(TextDocument document, string? path)
    {
        LoadDocumentContent(document);

        if (path is null)
        {
            _fileWorkflow.MarkSavedWithoutPath();
        }
        else
        {
            MarkDocumentSavedWithPath(path);
        }
    }

    private void LoadDocumentContent(TextDocument document)
    {
        _suppressEditorDirty = true;
        try
        {
            _editor.LoadDocument(document);
        }
        finally
        {
            _suppressEditorDirty = false;
        }
    }

    private void OnEditorDocumentChanged()
    {
        if (!_suppressEditorDirty)
            _fileWorkflow.MarkDirty();

        UpdateStatus();
    }

    private void MarkDocumentSavedWithPath(string path)
    {
        _fileWorkflow.MarkSavedWithPath(path, suppressRecentFiles: false);
    }

    private void UpdateStatus()
    {
        var stats = _editor.ComputeStatistics();
        var plan = FreeWEditorStatusPlanner.Build(new FreeWEditorStatusSnapshot(
            stats.Words,
            stats.CharactersWithSpaces,
            stats.Paragraphs,
            CurrentPage: _editor.CaretPageIndex + 1,
            TotalPages: _editor.PageCount,
            SelectionText: _editor.SelectedText,
            IncludePageStatus: _editor.ViewMode == DocumentViewMode.PrintLayout,
            IncludeSectionStatus: false,
            IsEdited: _editor.CanUndo));
        _status.Text = plan.SummaryStatus;
    }

    // ── Backstage (File screen) ───────────────────────────────────────────────

    /// <summary>
    /// Opens the FreeW backstage (File screen) as a modal full-window overlay.
    /// The backstage renders its panes from the portable Presentation-tier planners and
    /// dispatches user actions back through this shell's file workflow and open/save paths.
    /// </summary>
    private Task ShowBackstageAsync()
    {
        var callbacks = BuildBackstageCallbacks();
        return BackstageView.ShowAsync(this, callbacks);
    }

    internal BackstageCallbacks BuildBackstageCallbacks() =>
        new BackstageCallbacks(
            DisplayName: _fileWorkflow.DisplayName,
            CurrentPath: _fileWorkflow.CurrentPath,
            GetRecentEntries: () => _fileWorkflow.RecentEntries,
            GetFileFormats: () => _documentPersistence.Adapters.SelectMany(a => a.Formats),
            GetPageSettings: () => _editor.Document.Page,
            GetCurrentOptions: () => _options,
            GetDataFolder: ResolveDataFolderLabel,

            NewDocument: NewDocument,
            OpenRecent: path =>
            {
                // Run the dirty-gate synchronously through the shared Avalonia workflow.
                if (_fileWorkflow.Open(FileText.OpenAction, () => path, p =>
                    {
                        _ = OpenPathAsync(p);
                        return true;
                    }))
                {
                    // success — OpenPathAsync was already fired
                }
            },
            OpenFolder: OpenFolderInShell,
            Browse: () => _ = OpenAsync(),
            RecoverUnsaved: () => _ = _autosave.OfferRecoveryAsync(this),
            SaveAs: () => _ = SaveAsAsync(),
            SaveAsFormat: (ext, filterIndex) => _ = SaveAsWithFormatAsync(ext, filterIndex),
            OpenContainingFolder: path =>
            {
                var folder = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(folder))
                    OpenFolderInShell(folder);
            },
            ExportPdf: () => _ = ExportPdfAsync(),
            MarkAsFinal: ToggleMarkAsFinal,
            RestrictEditing: () => _ = OpenRestrictEditingAsync(),
            InspectDocument: () => _ = InspectDocumentAsync(),
            CheckAccessibility: () => _ = CheckAccessibilityAsync(),
            OpenOptions: () => _ = OpenOptionsAsync(),
            PrintPreview: () => _ = OpenPrintPreviewAsync());

    private static TextDocument CloneDocument(TextDocument document)
    {
        using var buffer = new MemoryStream();
        DocxWriter.Write(document, buffer);
        buffer.Position = 0;
        return DocxReader.Read(buffer);
    }

    private void ToggleMarkAsFinal()
    {
        _editor.SetMarkedAsFinal(!_editor.IsMarkedAsFinal);
        _status.Text = _editor.IsMarkedAsFinal
            ? "Document marked as final."
            : "Document is no longer marked as final.";
        _editor.Focus();
    }

    private async Task OpenRestrictEditingAsync()
    {
        var dialog = new RestrictEditingDialog(_editor.Document.Protection);
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } settings)
            return;

        _editor.SetProtection(settings);
        _status.Text = settings.Mode == ProtectionMode.None
            ? "Editing restrictions removed."
            : $"Editing restricted: {settings.Mode}.";
        _editor.Focus();
    }

    private async Task InspectDocumentAsync()
    {
        var result = DocumentInspector.Inspect(_editor.Document);
        var dialog = new DocumentInspectorDialog(result);
        await dialog.ShowDialog(this);
        if (dialog.Choice is not { } choice)
            return;

        if (choice.HasAnySelection)
            _editor.ApplyInspectorRemovals(choice.Comments, choice.Revisions, choice.Properties, choice.Bookmarks);

        _status.Text = choice.HasAnySelection
            ? "Selected document data removed."
            : "Document Inspector completed.";
        _editor.Focus();
    }

    private async Task CheckAccessibilityAsync()
    {
        var report = AccessibilityChecker.Check(_editor.Document);
        var dialog = new AccessibilityReportDialog(report);
        await dialog.ShowDialog(this);
        _status.Text = report.IsClean
            ? "No accessibility issues found."
            : $"{report.Issues.Count} accessibility issue(s) found.";
        _editor.Focus();
    }

    private async Task OpenOptionsAsync()
    {
        var dialog = new OptionsDialog(_options);
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } edited)
            return;

        ApplyOptions(edited);
        if (!_optionsStore.Save(_options))
            _status.Text = _optionsStore.LastError ?? "FreeW Options could not be saved.";
        else
            _status.Text = "FreeW Options saved.";
    }

    private void ApplyOptions(FreeWOptions edited)
    {
        _options.RecentFilesCap = edited.RecentFilesCap;
        _options.DefaultSaveFormat = edited.DefaultSaveFormat;
        _options.UiLanguage = edited.UiLanguage;
        _options.AutoCorrectEnabled = edited.AutoCorrectEnabled;
        _options.AutoFormat = edited.AutoFormat;
        _options.AutoCorrect = edited.AutoCorrect;
        _options.Normalize();
    }

    private string ResolveDataFolderLabel()
    {
        try
        {
            return Path.GetDirectoryName(_optionsStore.StorePath) ?? _optionsStore.StorePath;
        }
        catch
        {
            return AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback(PlatformApplicationDataPathProvider.LocalInstance);
        }
    }

    private void OpenFolderInShell(string folder)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true,
                });
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not open folder: {ex.Message}";
        }
    }

    /// <summary>
    /// Save As targeting a specific file format chosen from the backstage planner.
    /// Builds a save-picker pre-filtered to the requested format and lets the user
    /// confirm the filename before saving.
    /// </summary>
    private async Task SaveAsWithFormatAsync(string extension, int filterIndex)
    {
        var normalizedExt = DocumentFileFormatResolver.NormalizeExtension(extension);
        if (!_documentPersistence.TryGetSaveFormat(filterIndex, out var format) &&
            !_documentPersistence.TryGetSaveFormat(normalizedExt, out format))
        {
            _status.Text = SisterAppFileTextPlanner.FormatUnsupportedExtension(extension);
            return;
        }

        var savePlan = _documentPersistence.BuildSavePickerPlan(
            _fileWorkflow.CurrentPath,
            _fileWorkflow.CurrentFileName,
            FileText.FallbackDisplayName,
            normalizedExt);

        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromFileTypes(
                SisterAppFileTextPlanner.FormatSaveAsTitle(format?.FormatName ?? extension),
                [
                    AvaloniaFilePickerTypeAdapter.CreateFileType(
                        format?.FormatName ?? extension,
                        [$"*{normalizedExt}"])
                ],
                savePlan.SuggestedFileName,
                savePlan.DefaultExtensionWithoutDot));
        var path = file?.LocalPath;
        if (path is not null)
            await SaveToPathAsync(path, filterIndex);
    }

    // Opens an external URL raised by DocumentView.HyperlinkActivated through the shared scheme allowlist.
    // Mirrors the WPF host's OnHyperlinkRequestNavigate: blocked schemes and launch failures are silently
    // dropped so a bad URL never crashes the editor.
    private static void OpenExternalUri(string url) =>
        ExternalUriLauncher.Open(
            url,
            uri => System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
}
