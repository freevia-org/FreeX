using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Host;

/// <summary>
/// Adds Word's outline / shadow / glow / reflection text effects to each printed page, so Print, Print
/// Preview and PDF / XPS export show what the editor shows. The page content was laid out from a clone
/// whose effect runs carry their own foreground brushes (<see cref="DocumentView.TagTextEffectsForPrint"/>);
/// this finds those runs' glyph runs on the page and paints their effects over the page with the same
/// <see cref="TextEffectsPainter"/> the on-screen overlay uses. A page without effect text is returned
/// untouched.
/// </summary>
internal sealed class TextEffectsPrintPaginator(
    DocumentPaginator inner,
    IReadOnlyDictionary<Brush, RunTextEffectVisualPlan> plans) : DocumentPaginator
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FlowDocument, IReadOnlyDictionary<Brush, RunTextEffectVisualPlan>> Tagged = new();

    /// <summary>Records the effect brushes <see cref="DocumentView.TagTextEffectsForPrint"/> gave a print flow.</summary>
    internal static void Remember(FlowDocument flow, IReadOnlyDictionary<Brush, RunTextEffectVisualPlan>? plans)
    {
        if (plans is { Count: > 0 })
            Tagged.AddOrUpdate(flow, plans);
    }

    /// <summary>Wraps <paramref name="paginator"/> when <paramref name="flow"/> carries effect text; otherwise returns it as is.</summary>
    internal static DocumentPaginator Wrap(DocumentPaginator paginator, FlowDocument flow) =>
        Tagged.TryGetValue(flow, out var plans) ? new TextEffectsPrintPaginator(paginator, plans) : paginator;

    public override bool IsPageCountValid => inner.IsPageCountValid;

    public override int PageCount => inner.PageCount;

    public override Size PageSize
    {
        get => inner.PageSize;
        set => inner.PageSize = value;
    }

    public override IDocumentPaginatorSource Source => inner.Source;

    public override void ComputePageCount() => inner.ComputePageCount();

    public override DocumentPage GetPage(int pageNumber)
    {
        var basePage = inner.GetPage(pageNumber);
        if (basePage == DocumentPage.Missing || basePage.Visual is not { } pageVisual)
            return basePage;

        var effects = BuildEffectsVisual(pageVisual, plans);
        if (effects is null)
            return basePage;

        // The inner paginator may hand back the same cached page visual on a repeat request (PDF export
        // asks twice per page); detach it from the wrapper an earlier call put it in.
        if (VisualTreeHelper.GetParent(pageVisual) is ContainerVisual previous)
            previous.Children.Remove(pageVisual);

        var page = new ContainerVisual();
        page.Children.Add(pageVisual);
        page.Children.Add(effects);
        return new DocumentPage(page, basePage.Size, basePage.BleedBox, basePage.ContentBox);
    }

    /// <summary>The effects of every tagged run drawn on <paramref name="pageVisual"/>, or null when there are none.</summary>
    internal static ContainerVisual? BuildEffectsVisual(Visual pageVisual, IReadOnlyDictionary<Brush, RunTextEffectVisualPlan> plans)
    {
        var runs = TextEffectsPainter.CollectPageGlyphLines(pageVisual, plans);
        if (runs.Count == 0)
            return null;

        var container = new ContainerVisual();
        var top = new DrawingVisual();
        using (var context = top.RenderOpen())
        {
            foreach (var (brush, plan, lines) in runs)
            {
                var glyphBrush = plan.Fill is { IsNone: true } ? null : brush;
                TextEffectsPainter.Paint(plan, lines, glyphBrush, context, layer => container.Children.Add(layer));
            }
        }

        container.Children.Add(top);
        return container;
    }
}
