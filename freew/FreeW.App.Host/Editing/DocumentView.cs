using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;
using WpfTextAlignment = System.Windows.TextAlignment;
using ModelParagraph = FreeW.Core.Model.Paragraph;
using ModelRun = FreeW.Core.Model.Run;
using ModelTextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Host.Editing;

/// <summary>
/// The FreeW editing surface: a RichTextBox that renders a <see cref="TextDocument"/> into a
/// WPF FlowDocument (resolving run/paragraph formatting through styles + document defaults) and
/// commits edits back into the model. Caret, selection, typing, delete and Enter come from the
/// RichTextBox; <see cref="CommitToModel"/> maps the edited view back to the model.
/// </summary>
public sealed class DocumentView : RichTextBox
{
    private const double PxPerPoint = 96.0 / 72.0;

    private TextDocument _model = TextDocument.CreateEmpty();
    private readonly DocumentCommandBus _commands;

    public DocumentView()
    {
        AcceptsTab = true;
        IsDocumentEnabled = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
        Background = Brushes.White;
        Padding = new Thickness(48);

        _commands = new DocumentCommandBus(new ViewContext(this));
        _commands.Changed += Render;
    }

    public TextDocument Model => _model;

    /// <summary>Undo/redo command bus over this view's model (backed by the shared UndoRedoStack).</summary>
    public DocumentCommandBus Commands => _commands;

    /// <summary>Render a model document into the editable surface.</summary>
    public void LoadModel(TextDocument document)
    {
        _model = document;
        Render();
    }

    private void Render()
    {
        var flow = new FlowDocument { PagePadding = new Thickness(0) };
        flow.FontFamily = new FontFamily(_model.DefaultRun.FontFamily ?? "Calibri");
        flow.FontSize = (_model.DefaultRun.FontSizePt ?? 11) * PxPerPoint;

        foreach (var paragraph in _model.Paragraphs)
            flow.Blocks.Add(BuildParagraph(paragraph, _model));

        Document = flow;
    }

    private sealed class ViewContext(DocumentView view) : IDocumentCommandContext
    {
        public TextDocument Document => view._model;
    }

    /// <summary>Read the edited FlowDocument back into the model (text + run formatting).</summary>
    public void CommitToModel()
    {
        _model.Paragraphs.Clear();
        foreach (var block in Document.Blocks)
        {
            if (block is not WpfParagraph wpfParagraph)
                continue;

            var modelParagraph = new ModelParagraph
            {
                Formatting = ReadParagraphFormatting(wpfParagraph)
            };
            foreach (var inline in wpfParagraph.Inlines)
            {
                if (inline is WpfRun run && run.Text.Length > 0)
                    modelParagraph.Runs.Add(new ModelRun(run.Text, ReadRunFormatting(run)));
            }
            _model.Paragraphs.Add(modelParagraph);
        }

        if (_model.Paragraphs.Count == 0)
            _model.Paragraphs.Add(new ModelParagraph());
    }

    // --- model -> view ---

    private static WpfParagraph BuildParagraph(ModelParagraph paragraph, TextDocument document)
    {
        var paraFmt = Resolve(paragraph, document);
        var wpf = new WpfParagraph
        {
            TextAlignment = ToWpfAlignment(paraFmt.Alignment),
            Margin = new Thickness(
                paraFmt.IndentLeftPt * PxPerPoint,
                paraFmt.SpaceBeforePt * PxPerPoint,
                paraFmt.IndentRightPt * PxPerPoint,
                paraFmt.SpaceAfterPt * PxPerPoint),
            TextIndent = paraFmt.FirstLineIndentPt * PxPerPoint,
            LineHeight = paraFmt.LineSpacing > 0
                ? paraFmt.LineSpacing * (document.DefaultRun.FontSizePt ?? 11) * PxPerPoint
                : double.NaN
        };

        foreach (var run in paragraph.Runs)
            wpf.Inlines.Add(BuildRun(run, paragraph, document));

        return wpf;
    }

    private static WpfRun BuildRun(ModelRun run, ModelParagraph paragraph, TextDocument document)
    {
        var fmt = Resolve(run, paragraph, document);
        var wpf = new WpfRun(run.Text)
        {
            FontWeight = fmt.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = fmt.Italic ? FontStyles.Italic : FontStyles.Normal
        };
        if (fmt.FontFamily is { Length: > 0 } family)
            wpf.FontFamily = new FontFamily(family);
        if (fmt.FontSizePt is { } size)
            wpf.FontSize = size * PxPerPoint;
        if (TryParseColor(fmt.ColorHex, out var color))
            wpf.Foreground = new SolidColorBrush(color);

        var decorations = new TextDecorationCollection();
        if (fmt.Underline)
            decorations.Add(TextDecorations.Underline);
        if (fmt.Strikethrough)
            decorations.Add(TextDecorations.Strikethrough);
        if (decorations.Count > 0)
            wpf.TextDecorations = decorations;

        return wpf;
    }

    // --- view -> model ---

    private static RunFormatting ReadRunFormatting(WpfRun run) => new()
    {
        Bold = run.FontWeight >= FontWeights.Bold,
        Italic = run.FontStyle == FontStyles.Italic,
        Underline = run.TextDecorations?.Contains(TextDecorations.Underline[0]) == true,
        Strikethrough = run.TextDecorations?.Contains(TextDecorations.Strikethrough[0]) == true,
        FontFamily = run.FontFamily.Source,
        FontSizePt = run.FontSize / PxPerPoint,
        ColorHex = run.Foreground is SolidColorBrush brush ? ToHex(brush.Color) : null
    };

    private static ParagraphFormatting ReadParagraphFormatting(WpfParagraph paragraph) =>
        ParagraphFormatting.Default with
        {
            Alignment = FromWpfAlignment(paragraph.TextAlignment),
            SpaceBeforePt = paragraph.Margin.Top / PxPerPoint,
            SpaceAfterPt = paragraph.Margin.Bottom / PxPerPoint,
            IndentLeftPt = paragraph.Margin.Left / PxPerPoint,
            IndentRightPt = paragraph.Margin.Right / PxPerPoint,
            FirstLineIndentPt = paragraph.TextIndent / PxPerPoint
        };

    // --- formatting resolution (run/paragraph -> style -> document default) ---

    private static RunFormatting Resolve(ModelRun run, ModelParagraph paragraph, TextDocument document)
    {
        var style = StyleRun(paragraph, document);
        var d = document.DefaultRun;
        var r = run.Formatting;
        return new RunFormatting
        {
            Bold = r.Bold || style.Bold || d.Bold,
            Italic = r.Italic || style.Italic || d.Italic,
            Underline = r.Underline || style.Underline || d.Underline,
            Strikethrough = r.Strikethrough || style.Strikethrough || d.Strikethrough,
            FontFamily = r.FontFamily ?? style.FontFamily ?? d.FontFamily,
            FontSizePt = r.FontSizePt ?? style.FontSizePt ?? d.FontSizePt,
            ColorHex = r.ColorHex ?? style.ColorHex ?? d.ColorHex
        };
    }

    private static ParagraphFormatting Resolve(ModelParagraph paragraph, TextDocument document)
    {
        // Explicit paragraph formatting wins; otherwise fall back to the style's paragraph props.
        if (paragraph.StyleId is { } id && document.Styles.TryGetValue(id, out var style))
        {
            var sp = style.Paragraph;
            var p = paragraph.Formatting;
            return p == ParagraphFormatting.Default ? sp : p;
        }
        return paragraph.Formatting;
    }

    private static RunFormatting StyleRun(ModelParagraph paragraph, TextDocument document) =>
        paragraph.StyleId is { } id && document.Styles.TryGetValue(id, out var style)
            ? style.Run
            : RunFormatting.Default;

    private static WpfTextAlignment ToWpfAlignment(ModelTextAlignment alignment) => alignment switch
    {
        ModelTextAlignment.Center => WpfTextAlignment.Center,
        ModelTextAlignment.Right => WpfTextAlignment.Right,
        ModelTextAlignment.Justify => WpfTextAlignment.Justify,
        _ => WpfTextAlignment.Left
    };

    private static ModelTextAlignment FromWpfAlignment(WpfTextAlignment alignment) => alignment switch
    {
        WpfTextAlignment.Center => ModelTextAlignment.Center,
        WpfTextAlignment.Right => ModelTextAlignment.Right,
        WpfTextAlignment.Justify => ModelTextAlignment.Justify,
        _ => ModelTextAlignment.Left
    };

    private static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
            return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
