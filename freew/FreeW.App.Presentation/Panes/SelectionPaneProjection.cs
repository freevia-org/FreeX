using FreeW.Core.Model;

namespace FreeW.App.Presentation.Panes;

/// <summary>
/// Renderer-neutral projection for the Layout selection pane. The coordinates deliberately remain
/// model coordinates so a host can select the exact floating object without re-identifying it from
/// the rendered canvas.
/// </summary>
public sealed record SelectionPaneItem(int BlockIndex, int RunIndex, string Kind, string Name)
{
    public override string ToString() => Name;
}

public static class SelectionPaneProjection
{
    public static IReadOnlyList<SelectionPaneItem> Build(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var items = new List<SelectionPaneItem>();
        var sequence = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not Paragraph paragraph)
                continue;

            for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
            {
                var kind = FloatingKind(paragraph.Runs[runIndex]);
                if (kind is null)
                    continue;

                sequence.TryGetValue(kind, out var count);
                count++;
                sequence[kind] = count;
                items.Add(new SelectionPaneItem(blockIndex, runIndex, kind, $"{kind} {count}"));
            }
        }

        // The last item is top-most in the model's draw sequence, matching Word's pane ordering.
        items.Reverse();
        return items;
    }

    private static string? FloatingKind(Run run) =>
        run.Image is { IsFloating: true } ? "Picture" :
        run.Shape is { IsFloating: true } ? "Shape" :
        run.Chart is { IsFloating: true } ? "Chart" :
        run.SmartArt is { IsFloating: true } ? "SmartArt" :
        run.WordArt is { IsFloating: true } ? "WordArt" :
        run.DrawingGroup is { IsFloating: true } ? "Group" : null;
}
