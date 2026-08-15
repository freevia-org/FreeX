using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record FormattingGalleryRibbonPorts(
    Action PrepareExecution,
    Action<string?> ApplyFontColor,
    Action<string?> ApplyParagraphShading,
    Action<string?> ApplyCharacterShading,
    Action<string?> ApplyCharacterBorderColor,
    Action<string?> ApplyHighlightColor,
    Action<string> ApplyNamedStyle,
    Action<string> PreviewNamedStyle,
    Action CancelNamedStylePreview,
    Action<string> CommitNamedStylePreview);

/// <summary>
/// Owns the command identity and payload mapping for Home-tab formatting palettes and the
/// built-in Styles gallery. Renderers provide only editor-effect adapters and native root pickers.
/// </summary>
public static class FormattingGalleryRibbonWorkflow
{
    public static string StyleCommandId(string styleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(styleId);
        return $"freew.style.{styleId}";
    }

    public static void Register(
        IRibbonCommandRegistry registry,
        FormattingGalleryRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ports);

        RegisterPalette(FreeWRibbonPaletteCatalog.FontColors, ports.ApplyFontColor);
        RegisterPalette(FreeWRibbonPaletteCatalog.ParagraphShading, ports.ApplyParagraphShading);
        RegisterPalette(FreeWRibbonPaletteCatalog.CharacterShading, ports.ApplyCharacterShading);
        RegisterPalette(FreeWRibbonPaletteCatalog.CharacterBorders, ports.ApplyCharacterBorderColor);
        RegisterPalette(FreeWRibbonPaletteCatalog.Highlights, ports.ApplyHighlightColor);

        foreach (var descriptor in BuiltInStyles.Gallery)
        {
            var captured = descriptor;
            registry.Register(
                StyleCommandId(captured.Id),
                captured.Type == StyleType.Paragraph
                    ? new PreviewableNamedStyleCommand(captured.Id, ports)
                    : Prepared(() => ports.ApplyNamedStyle(captured.Id)));
        }

        void RegisterPalette(
            IReadOnlyList<FreeWRibbonPaletteChoice> choices,
            Action<string?> apply)
        {
            foreach (var choice in choices)
            {
                var captured = choice;
                registry.Register(captured.CommandId, Prepared(() => apply(captured.Hex)));
            }
        }

        IRibbonCommand Prepared(Action execute) =>
            new PreparedActionCommand(ports.PrepareExecution, execute);
    }

    private sealed class PreparedActionCommand(Action prepare, Action execute) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            prepare();
            execute();
        }
    }

    private sealed class PreviewableNamedStyleCommand(
        string styleId,
        FormattingGalleryRibbonPorts ports) : IRibbonPreviewCommand
    {
        public void BeginPreview(RibbonCommandContext context) => ports.PreviewNamedStyle(styleId);

        public void CancelPreview() => ports.CancelNamedStylePreview();

        public void Execute(RibbonCommandContext context)
        {
            ports.CommitNamedStylePreview(styleId);
            ports.PrepareExecution();
        }
    }
}
