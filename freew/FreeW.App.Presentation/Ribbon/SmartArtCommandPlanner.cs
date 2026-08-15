using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>Shared command-state and dialog-value planning for SmartArt contextual commands.</summary>
public static class SmartArtCommandPlanner
{
    public static RibbonCommandId StyleCommandId(SmartArtStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return new RibbonCommandId($"freew.smartart-style-{style.Id}");
    }

    public static bool IsEnabled(SmartArt? smartArt, SmartArtStructureOperation operation) =>
        MutateSmartArtStructureCommand.CanApply(smartArt, operation);

    public static bool CanEdit(SmartArt? smartArt) => smartArt is not null;

    public static IReadOnlyList<string> StyleNames { get; } =
        SmartArtStyle.Catalog.Select(style => style.Name).ToArray();

    public static SmartArtStyle? ResolveStyle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return SmartArtStyle.Catalog.FirstOrDefault(style =>
            string.Equals(style.Id, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(style.Name, value, StringComparison.OrdinalIgnoreCase));
    }

    public static string BuildNodeText(SmartArt smartArt) =>
        string.Join(Environment.NewLine, smartArt.Nodes.Select(node => node.Text));

    public static SmartArt? BuildEditedContent(SmartArtKind kind, string? nodeText)
    {
        var lines = (nodeText ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        return lines.Length == 0 ? null : SmartArt.Create(kind, lines);
    }
}
