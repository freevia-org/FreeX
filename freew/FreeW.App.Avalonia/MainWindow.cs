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
    private Border? _findBar;
    private ScrollViewer? _scroller;
    private double _zoomScale = 1.0;
    private bool _suppressEditorDirty;

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
            maxRecentEntries: () => 0,
            onChanged: UpdateStatus,
            promptSaveChanges: _ => SaveChangesPrompt.DontSave,
            save: () => true);

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
        var workspace = new Border { Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)), Child = _scroller };
        root.Children.Add(workspace);

        _editor.DocumentChanged += OnEditorDocumentChanged;
        _editor.ScrollToCaretRequested += ScrollCaretIntoView;
        _editor.CellEditRequested += async req =>
        {
            var result = await new CellEditDialog(req.Text).ShowDialog<string?>(this);
            if (result is not null)
                _editor.SetCellText(req.Block, req.Row, req.Col, result);
        };
        LoadDocumentAsSaved(LoadStartupDocument(startupArguments), path: null);
        KeyDown += MainWindow_KeyDown;
        Content = root;
        UpdateStatus();
    }

    public DocumentView Editor => _editor;
    public bool HasToolbar { get; private set; }

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
            Paste: () => _ = PasteAsync());

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
            case Key.N: NewDocument(); e.Handled = true; break;
            case Key.O: _ = OpenAsync(); e.Handled = true; break;
            case Key.S: _ = SaveAsync(); e.Handled = true; break;
            case Key.P when (e.KeyModifiers & KeyModifiers.Shift) != 0: _ = ExportPdfAsync(); e.Handled = true; break;
            case Key.OemPlus or Key.Add: ApplyZoom(_zoomScale + 0.1); e.Handled = true; break;
            case Key.OemMinus or Key.Subtract: ApplyZoom(_zoomScale - 0.1); e.Handled = true; break;
            case Key.D0 or Key.NumPad0: ApplyZoom(1.0); e.Handled = true; break;
        }
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
        _fileWorkflow.MarkSavedWithPath(path, suppressRecentFiles: true);
    }

    private void UpdateStatus()
    {
        var text = _editor.PlainText;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var chars = text.Length;
        _status.Text = $"{words} words   {chars} characters   {_editor.ParagraphCount} paragraphs"
            + (_editor.CanUndo ? "   • edited" : "");
    }
}
