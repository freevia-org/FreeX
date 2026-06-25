using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Free.Shared.AppServices;
using Free.Shared.Ribbon.Avalonia;
using FreeW.App.Avalonia.Backstage;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Pdf;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.IO;
using FreeW.Core.Model;
using TextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Avalonia;

public sealed class MainWindow : Window
{
    private const string DefaultSaveExtension = ".docx";

    /// <summary>
    /// Number of entries kept in the recent-files store for this session.
    /// A FreeWOptions-driven cap comes in a later round; this constant is the
    /// interim default (matches the WPF host's <c>FreeWOptions.DefaultRecentFilesCap</c>).
    /// </summary>
    private const int DefaultRecentFilesCap = 10;

    private static readonly FilePickerFileType PdfFileType = new("PDF document")
    {
        Patterns = ["*.pdf"],
        MimeTypes = ["application/pdf"],
    };

    private readonly IReadOnlyList<IDocumentFileAdapter> _adapters = DocumentFileAdapterCatalog.CreateDefaultAdapters();
    private readonly DocumentView _editor = new();
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
    private readonly TextBox _findBox = new() { Width = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _replaceBox = new() { Width = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _zoomLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly FileCommandWorkflow _fileWorkflow;
    private readonly AutosaveAdapter _autosave;
    private readonly NavigationPane _navPane;
    private readonly ReviewingPane _reviewingPane;
    private readonly RevealFormattingPane _revealPane;
    private Border? _findBar;
    private FindReplaceDialog? _findReplaceDialog;
    private ScrollViewer? _scroller;
    private double _zoomScale = 1.0;
    private bool _suppressEditorDirty;
    private bool _closingConfirmed;

    public MainWindow()
        : this(Array.Empty<string>())
    {
    }

    public MainWindow(IReadOnlyList<string> startupArguments)
    {
        Title = "FreeW";
        Width = 1040;
        Height = 720;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        _fileWorkflow = new FileCommandWorkflow(
            maxRecentEntries: () => DefaultRecentFilesCap,
            onChanged: UpdateStatus,
            promptSaveChanges: action => PromptSaveChangesSync(action),
            save: () => SaveAsync().GetAwaiter().GetResult());
        _autosave = new AutosaveAdapter(_editor, _fileWorkflow);
        _navPane = new NavigationPane(_editor);
        _reviewingPane = new ReviewingPane(_editor);
        _revealPane = new RevealFormattingPane(_editor);

        var root = new DockPanel();

        var ribbon = BuildRibbon();
        DockPanel.SetDock(ribbon, Dock.Top);
        root.Children.Add(ribbon);

        var statusBar = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Height = 26,
            Child = BuildStatusContent(),
        };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);

        var findBar = BuildFindBar();
        DockPanel.SetDock(findBar, Dock.Bottom);
        root.Children.Add(findBar);

        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(48, 24),
            Content = new LayoutTransformControl { LayoutTransform = _zoom, Child = _editor },
        };
        _navPane.ScrollerRef = _scroller;

        // Nav pane docked left; reviewing pane docked right; workspace fills the remainder.
        DockPanel.SetDock(_navPane, Dock.Left);
        root.Children.Add(_navPane);

        DockPanel.SetDock(_reviewingPane, Dock.Right);
        root.Children.Add(_reviewingPane);

        DockPanel.SetDock(_revealPane, Dock.Right);
        root.Children.Add(_revealPane);

        var workspace = new Border { Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)), Child = _scroller };
        root.Children.Add(workspace);

        _editor.DocumentChanged += OnEditorDocumentChanged;
        _editor.DocumentChanged += () => { if (_navPane.IsVisible) _navPane.Refresh(); };
        _editor.DocumentChanged += () => { if (_reviewingPane.IsVisible) _reviewingPane.Refresh(); };
        _editor.DocumentChanged += () => { if (_revealPane.IsVisible) _revealPane.Refresh(); };
        _editor.ScrollToCaretRequested += ScrollCaretIntoView;
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

        Content = root;
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
            Cut: () => _ = CutAsync(),
            Copy: () => _ = CopyAsync(),
            Paste: () => _ = PasteAsync(),
            Backstage: () => _ = ShowBackstageAsync(),
            ToggleNavigationPane: ToggleNavigationPane,
            ToggleReviewingPane: ToggleReviewingPane,
            ToggleRevealFormatting: ToggleRevealFormatting,
            OpenFindReplaceDialog: OpenFindReplaceDialog);

        var registry = FreeWRibbon.BuildRegistry(_editor, callbacks);
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(
            FreeWRibbon.BuildDefinition(),
            registry,
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

    private Control BuildStatusContent()
    {
        _zoomLabel.Text = "100%";
        var panel = new DockPanel();
        DockPanel.SetDock(_zoomLabel, Dock.Right);
        panel.Children.Add(_zoomLabel);
        panel.Children.Add(_status);
        return panel;
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

    /// <summary>
    /// Synchronous bridge called by <see cref="FileCommandWorkflow"/> when it needs a save-changes
    /// answer. Because the workflow's dirty-gate is synchronous, we block the UI thread here by
    /// getting the async dialog result via GetAwaiter().GetResult(). This is safe because
    /// <see cref="OnWindowClosing"/> always cancels the OS close first and then re-invokes
    /// the async path — the sync call here only happens from the New/Open dirty-gate paths
    /// which already run synchronously on the UI thread.
    /// </summary>
    private SaveChangesPrompt PromptSaveChangesSync(string action) =>
        SaveChangesDialog.ShowAsync(this, _fileWorkflow.DisplayName, action)
            .GetAwaiter().GetResult();

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
        // ConfirmCloseAllowed runs on the UI thread because PromptSaveChangesSync shows
        // an Avalonia dialog. It blocks the UI thread briefly via GetAwaiter().GetResult()
        // on the dialog task — acceptable for a synchronous dirty-gate path.
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
            "replace the current document",
            () => LoadDocumentContent(TextDocument.CreateEmpty()),
            () => Title = "FreeW");
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

    private async Task OpenAsync()
    {
        await _fileWorkflow.OpenAsync(
            "opening another document",
            PromptOpenPathAsync,
            OpenPathAsync);
    }

    private async Task<string?> PromptOpenPathAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open document",
            AllowMultiple = false,
            FileTypeFilter = [.. DocumentFilePickerTypes.BuildOpenTypes(_adapters)],
        });

        if (files.Count == 0)
            return null;

        return files[0].TryGetLocalPath();
    }

    private Task<bool> OpenPathAsync(string path)
    {
        var adapter = DocumentFileFormatResolver.FindOpenAdapter(_adapters, Path.GetExtension(path), out var format);
        if (adapter is null)
        {
            _status.Text = $"Open failed: unsupported file type \"{Path.GetExtension(path)}\".";
            return Task.FromResult(false);
        }

        try
        {
            using var stream = File.OpenRead(path);
            var document = adapter.Load(stream);

            if (format?.OpensAsTemplate == true)
            {
                // Templates seed a new untitled document: clearing the path makes the next Save a Save-As.
                LoadDocumentAsSaved(document, path: null);
                Title = "FreeW";
            }
            else
            {
                LoadDocumentAsSaved(document, path);
                Title = $"FreeW - {Path.GetFileName(path)}";
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _status.Text = $"Open failed: {ex.Message}";
            return Task.FromResult(false);
        }
    }

    private Task<bool> SaveAsync() =>
        _fileWorkflow.SaveAsync(SaveToCurrentPathAsync, SaveAsAsync);

    private Task<bool> SaveToCurrentPathAsync(string path) => SaveToPathAsync(path);

    private async Task<bool> SaveAsAsync()
    {
        var defaultExtension = _fileWorkflow.CurrentPath is null
            ? DefaultSaveExtension
            : Path.GetExtension(_fileWorkflow.CurrentPath);
        var savePlan = DocumentFileDialogRequestPlanner.BuildSavePickerPlan(
            _adapters,
            _fileWorkflow.CurrentPath is null ? null : Path.GetFileName(_fileWorkflow.CurrentPath),
            "Document",
            defaultExtension);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save document",
            DefaultExtension = savePlan.DefaultExtensionWithoutDot,
            SuggestedFileName = savePlan.SuggestedFileName,
            FileTypeChoices = [.. savePlan.FileTypes.Select(DocumentFilePickerTypes.ToFileType)],
        });

        var path = file?.TryGetLocalPath();
        return path is not null && await SaveToPathAsync(path);
    }

    private Task<bool> SaveToPathAsync(string path)
    {
        var adapter = DocumentFileFormatResolver.FindSaveAdapter(_adapters, Path.GetExtension(path), out _);
        if (adapter is null)
        {
            _status.Text = $"Save failed: unsupported file type \"{Path.GetExtension(path)}\".";
            return Task.FromResult(false);
        }

        try
        {
            using (var stream = File.Create(path))
                adapter.Save(_editor.Document, stream);
            MarkDocumentSavedWithPath(path);
            Title = $"FreeW - {Path.GetFileName(path)}";
            _status.Text = $"Saved {Path.GetFileName(path)}";
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _status.Text = $"Save failed: {ex.Message}";
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
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to PDF",
            DefaultExtension = "pdf",
            SuggestedFileName = (_fileWorkflow.CurrentPath is null ? "Document" : Path.GetFileNameWithoutExtension(_fileWorkflow.CurrentPath)) + ".pdf",
            FileTypeChoices = [PdfFileType],
        });
        var path = file?.TryGetLocalPath();
        if (path is null)
            return;

        try
        {
            var result = FreeWAvaloniaPdfExport.Save(_editor, path);
            _status.Text = $"Exported PDF ({result.PageCount} page{(result.PageCount == 1 ? "" : "s")}, {result.Backend}): {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _status.Text = $"PDF export failed: {ex.Message}";
        }
    }

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
        var text = _editor.PlainText;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var chars = text.Length;
        _status.Text = $"{words} words   {chars} characters   {_editor.ParagraphCount} paragraphs"
            + (_editor.CanUndo ? "   • edited" : "");
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
            GetFileFormats: () => _adapters.SelectMany(a => a.Formats),
            GetPageSettings: () => _editor.Document.Page,

            NewDocument: NewDocument,
            OpenRecent: path =>
            {
                // Run the dirty-gate synchronously (ConfirmDiscardOrSave calls PromptSaveChangesSync
                // which is safe because we block the UI thread only briefly for the dialog).
                if (_fileWorkflow.Open("opening another document", () => path, p =>
                    {
                        _ = OpenPathAsync(p);
                        return true;
                    }))
                {
                    // success — OpenPathAsync was already fired
                }
            },
            Browse: () => _ = OpenAsync(),
            RecoverUnsaved: () => _ = _autosave.OfferRecoveryAsync(this),
            SaveAs: () => _ = SaveAsAsync(),
            SaveAsExtension: ext => _ = SaveAsWithExtensionAsync(ext),
            OpenContainingFolder: path =>
            {
                try
                {
                    var folder = System.IO.Path.GetDirectoryName(path);
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
            },
            ExportPdf: () => _ = ExportPdfAsync());

    /// <summary>
    /// Save As targeting a specific file extension chosen from the backstage planner.
    /// Builds a save-picker pre-filtered to the requested extension and lets the user
    /// confirm the filename before saving.
    /// </summary>
    private async Task SaveAsWithExtensionAsync(string extension)
    {
        var normalizedExt = DocumentFileFormatResolver.NormalizeExtension(extension);
        var adapter = DocumentFileFormatResolver.FindSaveAdapter(_adapters, normalizedExt, out var format);
        if (adapter is null)
        {
            _status.Text = $"Save failed: unsupported extension \"{extension}\".";
            return;
        }

        var suggestedName = (_fileWorkflow.CurrentPath is null
            ? "Document"
            : System.IO.Path.GetFileNameWithoutExtension(_fileWorkflow.CurrentPath)) + normalizedExt;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Save as {format?.FormatName ?? extension}",
            DefaultExtension = normalizedExt.TrimStart('.'),
            SuggestedFileName = suggestedName,
            FileTypeChoices = [DocumentFilePickerTypes.ToFileType(
                new Free.Shared.IO.FileDialogPickerTypeDescriptor(
                    format?.FormatName ?? extension,
                    [$"*{normalizedExt}"]))],
        });

        var path = file?.TryGetLocalPath();
        if (path is not null)
            await SaveToPathAsync(path);
    }
}
