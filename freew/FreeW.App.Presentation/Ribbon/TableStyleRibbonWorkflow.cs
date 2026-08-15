using Free.Shared.Ribbon;
using FreeW.App.Presentation.ContextMenus;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record TableStyleRibbonPorts(
    Action<DocumentTableStyle> Preview,
    Action CancelPreview,
    Action<DocumentTableStyle> Commit);

/// <summary>
/// Owns Table Styles catalog command identity and preview/cancel/commit lifecycle. Renderers contribute
/// only native caret resolution, redraw, and menu/pointer adaptation.
/// </summary>
public static class TableStyleRibbonWorkflow
{
    public const string ParentCommandId = "freew.table-styles";

    public static void Register(IRibbonCommandRegistry registry, TableStyleRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ports);

        registry.Register(ParentCommandId, DropdownOpenerCommand.Instance);
        for (var index = 0; index < DocumentTableStyle.Catalog.Count; index++)
        {
            var style = DocumentTableStyle.Catalog[index];
            registry.Register(
                FreeWContextMenuPlanner.TableStylesPrefix + index,
                new PreviewableTableStyleCommand(style, ports));
        }
    }

    private sealed class PreviewableTableStyleCommand(
        DocumentTableStyle style,
        TableStyleRibbonPorts ports) : IRibbonPreviewCommand
    {
        public void BeginPreview(RibbonCommandContext context) => ports.Preview(style);

        public void CancelPreview() => ports.CancelPreview();

        public void Execute(RibbonCommandContext context) => ports.Commit(style);
    }

    private sealed class DropdownOpenerCommand : IRibbonCommand
    {
        public static DropdownOpenerCommand Instance { get; } = new();

        public void Execute(RibbonCommandContext context)
        {
            // Native ribbon renderers open the menu attached to the parent control.
        }
    }
}
