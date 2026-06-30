using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.IO;
using FreeP.Core.Model;
using System.Linq;

namespace FreeP.App.Avalonia;

/// <summary>
/// FreeP cross-platform main window. Viewer + navigator + file lifecycle (Wave 14B v1).
///
/// Layout:
///   ┌──────────────────────────────────────────┐
///   │  Ribbon (Home: File / Slides / Edit)     │
///   ├──────────────────────────────────────────┤
///   │  Body                                    │
///   │  ┌──────────┬───────────────────────────┐│
///   │  │ Slide    │  Stage (SlideCanvas)       ││
///   │  │ Pane     │                           ││
///   │  │ ~180 px  ├───────────────────────────┤│
///   │  │          │  Notes pane (TextBox)      ││
///   │  └──────────┴───────────────────────────┘│
///   ├──────────────────────────────────────────┤
///   │  Status bar ("Slide N / M")              │
///   └──────────────────────────────────────────┘
///
/// Commands wired (v1):
///   File:   New, Open, Save, Save As
///   Slide:  New Slide, Duplicate, Delete
///   Insert: Text Box, Table, Chart, Picture, Rectangle, Ellipse
///   Edit:   Undo, Redo
///   Keyboard: Ctrl+N/O/S/Shift+S, Ctrl+Z/Y
///
/// Deferred to later Avalonia parity: transitions, animations, font/format ribbon,
///   find/replace, clipboard (full), drag-reorder thumbnails.
/// </summary>
public sealed class MainWindow : Window
{
    private const string DefaultTitle = "FreeP";
    private const int DefaultRecentFilesCap = ApplicationOptionsNormalizer.DefaultRecentFilesCap;
    private static readonly SisterAppFileTextSpec FileText = SisterAppFileTextPlanner.Presentation;

    private static readonly FilePickerFileType PictureFileType = new(PresentationFileTextResources.PictureFileTypeName)
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.svg"],
        MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/svg+xml"],
    };

    // ── Presentation model ─────────────────────────────────────────────────────

    private Presentation _presentation = Presentation.CreateEmpty();
    private readonly SisterAvaloniaFileCommandWorkflow _fileWorkflow;

    // ── Editing session ────────────────────────────────────────────────────────

    internal EditingSession Editor { get; private set; } = null!;

    // ── UI elements ────────────────────────────────────────────────────────────

    private readonly SlideCanvas _slideCanvas;
    private readonly ListBox _slidePaneList;
    private readonly TextBox _notesBox;
    private readonly TextBlock _statusText;

    // ── Interaction layer (Theme 15) ────────────────────────────────────────────

    private SelectionAdornerLayer?       _adorner;
    private AvaloniaCanvasGestureHandler? _gestureHandler;
    private AvaloniaInCanvasTextEditor?  _textEditor;

    private bool _notesRefreshing;
    private bool _slidePaneRefreshing;

    // ── Smoke surface ──────────────────────────────────────────────────────────

    /// <summary>True once the ribbon has been built. Read by the launch-smoke coordinator.</summary>
    internal bool HasToolbar { get; private set; }

    /// <summary>Current slide count — read by the launch-smoke coordinator.</summary>
    internal int SlideCount => _presentation.Slides.Count;

    /// <summary>Current slide index (0-based) — read by the launch-smoke coordinator.</summary>
    internal int CurrentSlideIndex => Editor?.CurrentSlideIndex ?? -1;

    internal bool IsDirty => _fileWorkflow.IsDirty;

    internal string? CurrentPath => _fileWorkflow.CurrentPath;

    internal IReadOnlyList<RecentFileEntry> RecentEntries => _fileWorkflow.RecentEntries;

    // ── Constructors ───────────────────────────────────────────────────────────

    public MainWindow()
        : this(Array.Empty<string>())
    {
    }

    public MainWindow(IReadOnlyList<string> startupArguments)
        : this(startupArguments, loadRecentFilesStore: null)
    {
    }

    internal MainWindow(
        IReadOnlyList<string> startupArguments,
        Func<RecentFilesStore>? loadRecentFilesStore)
    {
        Title = DefaultTitle;
        Width = 1280;
        Height = 760;
        MinWidth = 800;
        MinHeight = 500;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        // Build editing session around the initial empty presentation.
        RebuildEditor();

        // ── Core UI elements ──────────────────────────────────────────────────

        _slideCanvas = new SlideCanvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            Margin              = new Thickness(24),
        };

        _slidePaneList = new ListBox
        {
            Width       = 180,
            Padding     = new Thickness(4),
            Background  = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
        };
        _slidePaneList.SelectionChanged += OnSlidePaneSelectionChanged;

        _notesBox = new TextBox
        {
            AcceptsReturn   = true,
            TextWrapping    = TextWrapping.Wrap,
            PlaceholderText = "Click to add notes",
            MinHeight       = 64,
            MaxHeight       = 120,
            Padding         = new Thickness(8, 4),
            FontSize        = 12,
            Background      = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xF0)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
        };
        _notesBox.TextChanged += OnNotesTextChanged;

        _statusText = SisterAppStatusBarChrome.CreateInfoText(foreground: Brushes.White, margin: new Thickness(8, 0));
        _fileWorkflow = new SisterAvaloniaFileCommandWorkflow(
            owner: this,
            titleSpec: new SisterAvaloniaFileTitleSpec(
                ApplicationName: DefaultTitle,
                Separator: " \u2014 "),
            maxRecentEntries: () => DefaultRecentFilesCap,
            onChanged: UpdateStatus,
            save: () => FileSaveAsync().GetAwaiter().GetResult(),
            loadRecentFilesStore: loadRecentFilesStore);

        // ── Root layout ───────────────────────────────────────────────────────

        var ribbon = BuildRibbon();
        var statusBar = SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(
            Background: new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
            LeftContent: _statusText)).Root;
        var frame = SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(
            chrome: ribbon,
            workArea: BuildBody(),
            statusBar: statusBar));

        // ── Keyboard shortcuts ────────────────────────────────────────────────

        KeyDown += MainWindow_KeyDown;

        // ── Initial content ───────────────────────────────────────────────────

        var startupPresentation = startupArguments
            .FirstOrDefault(a => IsSupportedPresentationPath(a) && File.Exists(a));

        if (startupPresentation is not null)
            TryLoadPresentationFile(startupPresentation);
        else
            LoadPresentationAsSaved(_presentation, path: null);

        Content = frame.Root;
        UpdateStatus();
    }

    // ── Editor construction ────────────────────────────────────────────────────

    private void RebuildEditor()
    {
        var bus = new PresentationCommandBus(_presentation);
        Editor  = new EditingSession(_presentation, bus);

        Editor.Changed             += OnEditorChanged;
        Editor.CurrentSlideChanged += OnCurrentSlideChanged;
    }

    private void RebuildEditorAndRewireInteraction()
    {
        RebuildEditor();
        // Only re-wire if the interaction layer has already been built (BuildBody sets it up).
        if (_adorner is not null)
            RewireInteractionToEditor();
    }

    // ── Body layout ────────────────────────────────────────────────────────────

    private Control BuildBody()
    {
        // Right: canvas (fills) + notes pane (auto height) stacked in a Grid.
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── Interaction overlay stack ───────────────────────────────────────────
        // A Panel stack: SlideCanvas at the bottom, SelectionAdornerLayer on top (transparent to
        // pointer events), and a Canvas for the text-edit TextBox overlay on the very top.
        _adorner = new SelectionAdornerLayer
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            IsHitTestVisible    = false,
        };

        // Text-overlay: a Canvas that hosts TextBox children during text editing.
        var textOverlay = new Canvas
        {
            IsVisible        = false,
            IsHitTestVisible = false,
        };

        // Stack all three in a Panel (Grid with single cell).
        var canvasStack = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        canvasStack.Children.Add(_slideCanvas);
        canvasStack.Children.Add(_adorner);
        canvasStack.Children.Add(textOverlay);

        var canvasHost = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            Child      = canvasStack,
        };
        Grid.SetRow(canvasHost, 0);
        Grid.SetRow(_notesBox,  1);
        rightGrid.Children.Add(canvasHost);
        rightGrid.Children.Add(_notesBox);

        // Wire interaction after the overlay panel is built.
        WireInteraction(textOverlay);

        // Left (slide pane) + right split.
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_slidePaneList, 0);
        Grid.SetColumn(rightGrid,      1);
        body.Children.Add(_slidePaneList);
        body.Children.Add(rightGrid);

        return body;
    }

    // ── Interaction wiring (Theme 15) ───────────────────────────────────────────

    private void WireInteraction(Canvas textOverlay)
    {
        if (_adorner is null) return;

        // Allow the canvas to receive keyboard focus for arrow/delete keys.
        _slideCanvas.Focusable = true;

        // Gesture handler drives selection, move, resize, rotate.
        _gestureHandler = new AvaloniaCanvasGestureHandler(_slideCanvas, Editor, _adorner);

        // Text editor: double-click a shape to edit its text.
        _textEditor = new AvaloniaInCanvasTextEditor(_slideCanvas, Editor, textOverlay);
    }

    /// <summary>
    /// Re-wires the interaction layer to the new <see cref="Editor"/> instance after a
    /// file open / new operation.
    /// </summary>
    private void RewireInteractionToEditor()
    {
        if (_adorner is null) return;
        // The gesture handler and text editor subscribe to the canvas's pointer events,
        // so we must create new instances to bind to the new EditingSession.
        // Find the textOverlay in the visual tree (it's the 3rd child of the canvasStack).
        // We can retrieve it from the existing text editor's overlay or re-find it:
        Canvas? textOverlay = null;
        if (_textEditor is not null)
        {
            // Cancel any active edit before we destroy the old editor.
            _textEditor.Cancel();
        }

        // Detach old gesture handler's pointer event subscriptions by creating a new instance.
        // The old handlers go out of scope and GC naturally; Avalonia weak event subscriptions
        // allow this. New instances re-subscribe.
        // Re-find the overlay canvas from the canvasStack structure.
        if (_slideCanvas.Parent is Grid canvasStack && canvasStack.Children.Count >= 3
            && canvasStack.Children[2] is Canvas ov)
        {
            textOverlay = ov;
        }

        if (textOverlay is not null)
        {
            _gestureHandler = new AvaloniaCanvasGestureHandler(_slideCanvas, Editor, _adorner);
            _textEditor     = new AvaloniaInCanvasTextEditor(_slideCanvas, Editor, textOverlay);
        }
    }

    // ── Ribbon ─────────────────────────────────────────────────────────────────

    private Control BuildRibbon()
    {
        var registry = BuildCommandRegistry();

        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(
            FreePRibbonAvalonia.Build(),
            registry,
            afterExecute: null);

        HasToolbar = true;
        return new Border
        {
            Background      = Brushes.White,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child           = ribbon,
        };
    }

    internal RibbonCommandRegistry BuildCommandRegistry()
    {
        var r = new RibbonCommandRegistry();

        // File operations
        r.Register("freep.file.new",     new ActionRibbonCommand(FileNew));
        r.Register("freep.file.open",    new ActionRibbonCommand(() => _ = FileOpenAsync()));
        r.Register("freep.file.save",    new ActionRibbonCommand(() => _ = FileSaveAsync()));
        r.Register("freep.file.save-as", new ActionRibbonCommand(() => _ = FileSaveAsAsync()));

        // Slide navigation/management
        r.Register("freep.new-slide",       new ActionRibbonCommand(() => Editor.InsertSlide()));
        r.Register("freep.duplicate-slide", new ActionRibbonCommand(() => Editor.DuplicateCurrentSlide()));
        r.Register("freep.delete-slide",    new ActionRibbonCommand(() => Editor.DeleteCurrentSlide()));

        // Insert objects/text
        foreach (var plan in SlideObjectInsertionPlanner.BuiltInPlans)
        {
            if (plan.RequiresPicturePayload)
            {
                r.Register(plan.CommandId, new ActionRibbonCommand(() => _ = InsertPictureFromFileAsync()));
                continue;
            }

            r.Register(plan.CommandId, new ActionRibbonCommand(() =>
                SlideObjectInsertionPlanner.Apply(Editor, plan)));
        }

        r.Register(ChartDataDialogPlanner.EditDataCommandId, new ActionRibbonCommand(OpenChartDataDialog));

        // Undo / Redo
        r.Register("freep.undo", new ActionRibbonCommand(() => Editor.Undo()));
        r.Register("freep.redo", new ActionRibbonCommand(() => Editor.Redo()));

        // Slide show
        r.Register("freep.slideshow.from-beginning",
            new ActionRibbonCommand(() => StartSlideShow(fromStart: true)));
        r.Register("freep.slideshow.from-current",
            new ActionRibbonCommand(() => StartSlideShow(fromStart: false)));

        return r;
    }

    private async Task InsertPictureFromFileAsync()
    {
        if (!StorageProvider.CanOpen)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(SisterAppFileTextPlanner.InsertPictureCommand);
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = SisterAppFileTextPlanner.InsertPicturePickerTitle,
            AllowMultiple = false,
            FileTypeFilter = [PictureFileType],
        });

        if (files.Count == 0)
            return;

        try
        {
            var file = files[0];
            await using var source = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory);

            var payload = SlideObjectInsertionPlanner.CreatePicturePayload(memory.ToArray(), file.Name);
            var added = SlideObjectInsertionPlanner.ApplyCommand(
                Editor,
                SlideObjectInsertionPlanner.PictureCommandId,
                payload);

            if (added is not null)
                _statusText.Text = SisterAppFileTextPlanner.FormatInserted(file.Name);
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(SisterAppFileTextPlanner.InsertPictureCommand, ex.Message);
        }
    }

    // ── File lifecycle ─────────────────────────────────────────────────────────

    internal void OpenChartDataDialog()
    {
        if (Editor.SelectedChart is null)
            return;

        var dialog = new ChartDataDialog(Editor);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    private void FileNew()
    {
        _fileWorkflow.New(
            FileText.NewAction,
            () => LoadPresentationContent(Presentation.CreateEmpty()));
    }

    private Task<bool> FileOpenAsync() =>
        _fileWorkflow.OpenAsync(
            FileText.OpenAction,
            PromptOpenPathAsync,
            path => Task.FromResult(TryLoadPresentationFile(path)));

    private async Task<string?> PromptOpenPathAsync()
    {
        if (!StorageProvider.CanOpen)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(SisterAppFileTextPlanner.OpenCommand);
            return null;
        }

        var plan = PresentationFileDialogPlanner.BuildOpenPickerPlan();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = FileText.OpenPickerTitle,
            AllowMultiple = false,
            FileTypeFilter = AvaloniaFilePickerTypeAdapter.ToFileTypes(plan.FileTypes),
        });

        if (files.Count == 0)
            return null;

        var path = files[0].TryGetLocalPath();
        if (path is null)
            _statusText.Text = SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(SisterAppFileTextPlanner.OpenCommand);

        return path;
    }

    private Task<bool> FileSaveAsync() =>
        _fileWorkflow.SaveAsync(
            path => Task.FromResult(TrySavePresentationFile(path)),
            FileSaveAsAsync);

    private async Task<bool> FileSaveAsAsync()
    {
        if (!StorageProvider.CanSave)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(SisterAppFileTextPlanner.SaveCommand);
            return false;
        }

        var plan = PresentationFileDialogPlanner.BuildSavePickerPlan(_fileWorkflow.CurrentFileName);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = FileText.SavePickerTitle,
            DefaultExtension  = plan.DefaultExtensionWithoutDot,
            SuggestedFileName = plan.SuggestedFileName,
            FileTypeChoices   = AvaloniaFilePickerTypeAdapter.ToFileTypes(plan.FileTypes),
        });

        var path = file?.TryGetLocalPath();
        if (path is null)
        {
            if (file is not null)
                _statusText.Text = SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(SisterAppFileTextPlanner.SaveCommand);

            return false;
        }

        return TrySavePresentationFile(path);
    }

    private bool TryLoadPresentationFile(string path)
    {
        try
        {
            var presentation = PresentationFileDialogPlanner.IsLegacyPresentationPath(path)
                ? FxpFormat.Read(path)
                : PptxPackageReader.Read(path);
            LoadPresentationAsSaved(presentation, path);
            _statusText.Text = SisterAppFileTextPlanner.FormatOpened(Path.GetFileName(path));
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(SisterAppFileTextPlanner.OpenCommand, ex.Message);
            return false;
        }
    }

    private bool TrySavePresentationFile(string path)
    {
        try
        {
            if (PresentationFileDialogPlanner.IsLegacyPresentationPath(path))
            {
                FxpFormat.Write(_presentation, path);
            }
            else
            {
                using var stream = File.Create(path);
                PptxPackageWriter.Write(_presentation, stream);
            }

            _fileWorkflow.MarkSavedWithPath(path, suppressRecentFiles: false);
            _statusText.Text = SisterAppFileTextPlanner.FormatSaved(Path.GetFileName(path));
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(SisterAppFileTextPlanner.SaveCommand, ex.Message);
            return false;
        }
    }

    private static bool IsSupportedPresentationPath(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(
                   extension,
                   PresentationFileDialogPlanner.DefaultPresentationExtension,
                   StringComparison.OrdinalIgnoreCase)
               || PresentationFileDialogPlanner.IsLegacyPresentationPath(path);
    }

    // ── Presentation load ──────────────────────────────────────────────────────

    private void LoadPresentationAsSaved(Presentation presentation, string? path)
    {
        LoadPresentationContent(presentation);

        if (path is null)
            _fileWorkflow.MarkSavedWithoutPath();
        else
            _fileWorkflow.MarkSavedWithPath(path, suppressRecentFiles: false);
    }

    private void LoadPresentationContent(Presentation presentation)
    {
        _presentation = presentation;

        RebuildEditorAndRewireInteraction();
        RefreshSlidePane();
        RefreshCanvas();
        RefreshNotesPane();
        UpdateStatus();
    }

    // ── Canvas refresh ─────────────────────────────────────────────────────────

    private void RefreshCanvas()
    {
        _slideCanvas.Presentation = _presentation;
        _slideCanvas.Slide        = Editor.CurrentSlide;
        _slideCanvas.SlideIndex   = Editor.CurrentSlideIndex;
        _slideCanvas.Refresh();
    }

    // ── Slide pane ─────────────────────────────────────────────────────────────

    private void RefreshSlidePane()
    {
        _slidePaneRefreshing = true;
        try
        {
            _slidePaneList.Items.Clear();

            for (int i = 0; i < _presentation.Slides.Count; i++)
            {
                var slideIdx = i;
                var slide    = _presentation.Slides[i];

                // Small SlideCanvas thumbnail (148 × 84 px letterboxed).
                var thumb = new SlideCanvas
                {
                    Presentation = _presentation,
                    Slide        = slide,
                    SlideIndex   = slideIdx,
                    Width        = 148,
                    Height       = 84,
                };

                // Slide number label beneath thumbnail.
                var label = new TextBlock
                {
                    Text                = $"{slideIdx + 1}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize            = 10,
                    Margin              = new Thickness(0, 2, 0, 0),
                };

                var panel = new StackPanel
                {
                    Margin   = new Thickness(4),
                    Children = { thumb, label },
                };

                _slidePaneList.Items.Add(new ListBoxItem { Content = panel, Padding = new Thickness(2) });
            }

            // Restore selection.
            var current = Editor.CurrentSlideIndex;
            if (current >= 0 && current < _slidePaneList.Items.Count)
                _slidePaneList.SelectedIndex = current;
        }
        finally
        {
            _slidePaneRefreshing = false;
        }
    }

    private void OnSlidePaneSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_slidePaneRefreshing)
            return;

        var idx = _slidePaneList.SelectedIndex;
        if (idx < 0 || idx >= _presentation.Slides.Count)
            return;

        Editor.SelectSlide(idx);
    }

    // ── Notes pane ─────────────────────────────────────────────────────────────

    private void RefreshNotesPane()
    {
        _notesRefreshing = true;
        try
        {
            var notes = Editor.CurrentSlideNotes;
            _notesBox.Text = notes is null
                ? string.Empty
                : string.Join(
                    Environment.NewLine,
                    notes.Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.Text))));
        }
        finally
        {
            _notesRefreshing = false;
        }
    }

    private void OnNotesTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_notesRefreshing)
            return;
        Editor.SetCurrentSlideNotesText(_notesBox.Text);
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void OnEditorChanged()
    {
        _fileWorkflow.MarkDirty();
        RefreshSlidePane();
        RefreshCanvas(); // refresh canvas so shape moves/resizes are reflected immediately
        UpdateStatus();
    }

    private void OnCurrentSlideChanged(object? sender, EventArgs e)
    {
        // Sync slide-pane selection without re-triggering OnSlidePaneSelectionChanged.
        _slidePaneRefreshing = true;
        try { _slidePaneList.SelectedIndex = Editor.CurrentSlideIndex; }
        finally { _slidePaneRefreshing = false; }

        RefreshCanvas();
        RefreshNotesPane();
        UpdateStatus();
    }

    // ── Status ─────────────────────────────────────────────────────────────────

    private void UpdateStatus()
    {
        var count   = _presentation.Slides.Count;
        var current = Editor.CurrentSlideIndex;
        _statusText.Text = SisterAppStatusBarTextPlanner.FormatPresentationSlideStatus(current, count);
    }

    // ── Keyboard shortcuts ─────────────────────────────────────────────────────

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        // ── Ctrl shortcuts ──────────────────────────────────────────────────────
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.N: FileNew(); e.Handled = true; return;
                case Key.O: _ = FileOpenAsync(); e.Handled = true; return;
                case Key.S when (e.KeyModifiers & KeyModifiers.Shift) != 0:
                    _ = FileSaveAsAsync(); e.Handled = true; return;
                case Key.S: _ = FileSaveAsync(); e.Handled = true; return;
                case Key.Z: Editor.Undo(); e.Handled = true; return;
                case Key.Y: Editor.Redo(); e.Handled = true; return;
                case Key.A: Editor.SelectAll(); e.Handled = true; return;
            }
        }

        // ── Slide show keys (no modifier) ─────────────────────────────────────
        if (!ctrl)
        {
            switch (e.Key)
            {
                case Key.F5 when (e.KeyModifiers & KeyModifiers.Shift) != 0:
                    StartSlideShow(fromStart: false);
                    e.Handled = true;
                    return;
                case Key.F5:
                    StartSlideShow(fromStart: true);
                    e.Handled = true;
                    return;
            }
        }

        // ── Arrow / Delete keys — delegate to gesture handler (Theme 15) ────────
        if (_gestureHandler is not null)
        {
            // Skip if text editor is active (keys go into the TextBox).
            if (_textEditor is { IsActive: true }) return;

            if (_gestureHandler.HandleKeyDown(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                // Refresh canvas + adorner after model change.
                _slideCanvas.Refresh();
            }
        }
    }

    // ── Slide show launch ──────────────────────────────────────────────────────

    /// <summary>
    /// Opens the Avalonia fullscreen slide show window.
    /// </summary>
    /// <param name="fromStart">
    ///   true  = start from slide 0 (F5 / "From Beginning").
    ///   false = start from the currently selected slide (Shift+F5 / "From Current").
    /// </param>
    internal void StartSlideShow(bool fromStart)
    {
        if (_presentation.Slides.Count == 0)
            return; // nothing to show

        int startIdx = fromStart ? 0 : Math.Max(0, Editor.CurrentSlideIndex);
        var slideShow = new SlideShowWindow(_presentation, startIdx);

        // DA5: restore the editor's selected slide to wherever the slideshow ended.
        slideShow.Closed += (_, _) =>
        {
            int exitIdx = slideShow.Controller.CurrentSlideIndex;
            if (exitIdx >= 0 && exitIdx < _presentation.Slides.Count)
                Editor.SelectSlide(exitIdx);
        };

        slideShow.Show();
    }
}
