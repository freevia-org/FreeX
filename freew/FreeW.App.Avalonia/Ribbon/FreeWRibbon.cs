using FreeW.App.Avalonia.Editing;
using Free.Shared.Ribbon;
using TextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Avalonia.Ribbon;

/// <summary>
/// FreeW's ribbon definition and command wiring for the Avalonia shell. The portable
/// <see cref="RibbonDefinition"/> model lives in Free.Shared.Ribbon (the same definition the WPF
/// host renders); the WPF host's FreeWRibbon layout can't be referenced from Avalonia, so this
/// authors an equivalent portable definition here and binds command ids to the DocumentView /
/// shell actions via <see cref="RibbonCommandRegistry"/>.
/// </summary>
internal static class FreeWRibbon
{
    public static readonly string[] FontSizes =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72"];

    public static RibbonDefinition BuildDefinition() =>
        new RibbonDefinitionBuilder()
            .Tab("file", "File", "F", tab =>
                tab.Group("document", "Document", null, 100, g =>
                {
                    g.Button("freew.open", "Open");
                    g.Button("freew.save", "Save");
                }))
            .Tab("home", "Home", "H", tab =>
            {
                tab.Group("clipboard", "Clipboard", null, 100, g =>
                {
                    g.Button("freew.cut", "Cut");
                    g.Button("freew.copy", "Copy");
                    g.Button("freew.paste", "Paste");
                });
                tab.Group("font", "Font", null, 90, g =>
                {
                    g.Toggle("freew.bold", "Bold");
                    g.Toggle("freew.italic", "Italic");
                    g.Toggle("freew.underline", "Underline");
                    g.ComboBox("freew.font-size", "Size", c => c with { Items = FontSizes, Width = 64 });
                });
                tab.Group("paragraph", "Paragraph", null, 80, g =>
                {
                    g.Button("freew.align-left", "Left");
                    g.Button("freew.align-center", "Center");
                    g.Button("freew.align-right", "Right");
                });
                tab.Group("styles", "Styles", null, 75, g =>
                {
                    g.Button("freew.style-normal", "Normal");
                    g.Button("freew.style-heading1", "Heading 1");
                    g.Button("freew.style-heading2", "Heading 2");
                    g.Button("freew.style-title", "Title");
                });
                tab.Group("editing", "Editing", null, 70, g =>
                {
                    g.Button("freew.undo", "Undo");
                    g.Button("freew.redo", "Redo");
                });
            })
            .Build();

    public static RibbonCommandRegistry BuildRegistry(DocumentView editor, RibbonHostCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(callbacks);

        var registry = new RibbonCommandRegistry();
        registry.Register("freew.bold", new RelayCommand(editor.ToggleBold));
        registry.Register("freew.italic", new RelayCommand(editor.ToggleItalic));
        registry.Register("freew.underline", new RelayCommand(editor.ToggleUnderline));
        registry.Register("freew.align-left", new RelayCommand(() => editor.SetAlignment(TextAlignment.Left)));
        registry.Register("freew.align-center", new RelayCommand(() => editor.SetAlignment(TextAlignment.Center)));
        registry.Register("freew.align-right", new RelayCommand(() => editor.SetAlignment(TextAlignment.Right)));
        registry.Register("freew.undo", new RelayCommand(editor.Undo));
        registry.Register("freew.redo", new RelayCommand(editor.Redo));
        registry.Register("freew.style-normal", new RelayCommand(() => editor.ApplyQuickStyle(11, bold: false)));
        registry.Register("freew.style-heading1", new RelayCommand(() => editor.ApplyQuickStyle(16, bold: true)));
        registry.Register("freew.style-heading2", new RelayCommand(() => editor.ApplyQuickStyle(14, bold: true)));
        registry.Register("freew.style-title", new RelayCommand(() => editor.ApplyQuickStyle(24, bold: true)));
        registry.Register("freew.font-size", new RelayValueCommand(value =>
        {
            if (double.TryParse(value, out var points) && points > 0)
                editor.SetSelectionFontSize(points);
        }));
        registry.Register("freew.open", new RelayCommand(callbacks.Open));
        registry.Register("freew.save", new RelayCommand(callbacks.Save));
        registry.Register("freew.cut", new RelayCommand(callbacks.Cut));
        registry.Register("freew.copy", new RelayCommand(callbacks.Copy));
        registry.Register("freew.paste", new RelayCommand(callbacks.Paste));
        return registry;
    }
}

internal sealed record RibbonHostCallbacks(
    Action Open,
    Action Save,
    Action Cut,
    Action Copy,
    Action Paste);

internal sealed class RelayCommand(Action execute) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context) => execute();
}

internal sealed class RelayValueCommand(Action<string?> execute) : IRibbonCommand
{
    public void Execute(RibbonCommandContext context) => execute(context.SelectedValue);
}
