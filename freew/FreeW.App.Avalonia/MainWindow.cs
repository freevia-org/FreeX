using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FreeW.App.Avalonia.Editing;
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

        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

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

    private Control BuildToolbar()
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 6),
            Spacing = 4,
        };

        bar.Children.Add(MakeButton("Open", async () => await OpenAsync()));
        bar.Children.Add(MakeButton("Save", async () => await SaveAsync()));
        bar.Children.Add(Separator());
        bar.Children.Add(MakeButton("B", _editor.ToggleBold, bold: true));
        bar.Children.Add(MakeButton("I", _editor.ToggleItalic, italic: true));
        bar.Children.Add(MakeButton("U", _editor.ToggleUnderline, underline: true));
        bar.Children.Add(Separator());
        bar.Children.Add(MakeButton("Left", () => _editor.SetAlignment(TextAlignment.Left)));
        bar.Children.Add(MakeButton("Center", () => _editor.SetAlignment(TextAlignment.Center)));
        bar.Children.Add(MakeButton("Right", () => _editor.SetAlignment(TextAlignment.Right)));
        bar.Children.Add(Separator());
        bar.Children.Add(MakeButton("Undo", _editor.Undo));
        bar.Children.Add(MakeButton("Redo", _editor.Redo));

        HasToolbar = true;
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
        };
    }

    private static Control Separator() => new Border
    {
        Width = 1,
        Margin = new Thickness(4, 2),
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
    };

    private Button MakeButton(string text, Action onClick, bool bold = false, bool italic = false, bool underline = false)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            TextDecorations = underline ? TextDecorations.Underline : null,
        };
        var button = new Button { Content = label, Padding = new Thickness(10, 4), MinWidth = 34 };
        button.Click += (_, _) =>
        {
            onClick();
            _editor.Focus();
        };
        return button;
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
