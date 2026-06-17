using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(48, 24),
            Content = _editor,
        };
        var workspace = new Border { Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)), Child = scroller };
        root.Children.Add(workspace);

        _editor.DocumentChanged += UpdateStatus;
        _editor.LoadDocument(LoadStartupDocument(startupArguments));
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
            Cut: Cut,
            Copy: Copy,
            Paste: Paste);

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

    // In-process clipboard. Avalonia 12 reworked IClipboard (text helpers removed in favor of the
    // data-transfer API); OS clipboard integration is a follow-up. Within FreeW this is fully functional.
    private static string _clipboardText = string.Empty;

    private void Copy()
    {
        var text = _editor.SelectedText;
        if (text.Length > 0)
            _clipboardText = text;
    }

    private void Cut()
    {
        Copy();
        _editor.TryDeleteSelection();
    }

    private void Paste()
    {
        if (_clipboardText.Length > 0)
            _editor.InsertText(_clipboardText.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '));
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
