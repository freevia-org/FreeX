using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.IO;
using FreeW.Core.Model;
using TextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Avalonia;

public sealed class MainWindow : Window
{
    private static readonly FilePickerFileType DocxFileType = new("Word document")
    {
        Patterns = ["*.docx"],
        MimeTypes = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
    };

    private readonly DocumentView _editor = new();
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
    private readonly TextBox _findBox = new() { Width = 220, VerticalAlignment = VerticalAlignment.Center };
    private Border? _findBar;
    private ScrollViewer? _scroller;
    private string? _currentPath;

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
            Child = _status,
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
            Content = _editor,
        };
        var workspace = new Border { Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)), Child = _scroller };
        root.Children.Add(workspace);

        _editor.DocumentChanged += UpdateStatus;
        _editor.ScrollToCaretRequested += ScrollCaretIntoView;
        _editor.LoadDocument(LoadStartupDocument(startupArguments));
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
        var ribbon = AvaloniaRibbonRenderer.Build(FreeWRibbon.BuildDefinition(), registry, afterExecute: () => _editor.Focus());
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

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 4),
            Children = { new TextBlock { Text = "Find:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, _findBox, next },
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

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            ToggleFindBar(show: true);
            e.Handled = true;
        }
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
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open document",
            AllowMultiple = false,
            FileTypeFilter = [DocxFileType],
        });

        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (path is null)
            return;

        try
        {
            _editor.LoadDocument(DocxReader.Read(path));
            _currentPath = path;
            Title = $"FreeW - {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Open failed: {ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        var path = _currentPath;
        if (path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save document",
                DefaultExtension = "docx",
                SuggestedFileName = "Document.docx",
                FileTypeChoices = [DocxFileType],
            });
            path = file?.TryGetLocalPath();
        }

        if (path is null)
            return;

        try
        {
            DocxWriter.Write(_editor.Document, path);
            _currentPath = path;
            Title = $"FreeW - {Path.GetFileName(path)}";
            _status.Text = $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Save failed: {ex.Message}";
        }
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
