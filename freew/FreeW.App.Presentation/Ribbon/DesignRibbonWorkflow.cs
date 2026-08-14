using Free.Shared.Ribbon;
using FreeW.App.Presentation.ContextMenus;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record DesignRibbonBindings(
    FreeWRibbonFormattingSession Formatting,
    Action PrepareExecution,
    Func<RibbonCommandContext, string?> ResolveChoice,
    Action<DocumentTheme> ApplyThemeColors,
    Action<DocumentFontSet> ApplyFontSet,
    Action<DocumentParagraphSpacingSet> ApplyParagraphSpacingSet,
    Action<DocumentEffectSet> ApplyEffectSet,
    Action ApplyDefaultStyleSet,
    Action<string?> ApplyPageColor,
    Action<string?> ApplyWatermarkText,
    IRibbonCommand CustomizeColors,
    IRibbonCommand CustomizeFonts,
    IRibbonCommand CustomParagraphSpacing,
    IRibbonCommand PageColor,
    IRibbonCommand MorePageColors,
    IRibbonCommand PageBorders,
    IRibbonCommand Watermark,
    IRibbonCommand CustomWatermark);

public sealed record DesignRibbonStatefulCommand(
    RibbonCommandId Id,
    IRibbonStatefulCommand Command);

public sealed record DesignRibbonCommands(
    IReadOnlyList<DesignRibbonStatefulCommand> StatefulCommands);

/// <summary>
/// Owns Design-tab command identity, catalog resolution, preset aliases, and shared mutation ordering.
/// Renderers provide only editor mutations and native dialog/dropdown adapters.
/// </summary>
public static class DesignRibbonWorkflow
{
    public static IRibbonCommand DropdownOpenerCommand { get; } = new DropdownCommand();

    public static string ParagraphSpacingCommandId(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"freew.para-spacing.{name.ToLowerInvariant().Replace(' ', '-')}";
    }

    public static DesignRibbonCommands Register(
        IRibbonCommandRegistry registry,
        DesignRibbonBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(bindings);

        var theme = new ResolvedChoiceCommand<DocumentTheme>(
            bindings.ResolveChoice,
            DocumentTheme.FindByName,
            value => bindings.Formatting.ApplyTheme(value.Name),
            bindings.PrepareExecution,
            bindings.Formatting.CurrentThemeName);
        registry.Bind(FreeWRibbonCommandAction.Theme, theme);
        foreach (var preset in DocumentTheme.Catalog)
        {
            var captured = preset;
            registry.Register(
                $"freew.theme.{captured.Name.ToLowerInvariant()}",
                Prepared(bindings, () => bindings.Formatting.ApplyTheme(captured.Name)));
        }

        var colors = new ResolvedChoiceCommand<DocumentTheme>(
            bindings.ResolveChoice,
            DocumentTheme.FindByName,
            bindings.ApplyThemeColors,
            bindings.PrepareExecution);
        registry.Bind(FreeWRibbonCommandAction.ThemeColors, colors);
        registry.Bind(FreeWRibbonCommandAction.CustomizeColors, bindings.CustomizeColors);
        foreach (var preset in DocumentTheme.Catalog)
        {
            var captured = preset;
            registry.Register(
                $"freew.theme-colors.{captured.Name.ToLowerInvariant()}",
                Prepared(bindings, () => bindings.ApplyThemeColors(captured)));
        }

        var fonts = new ResolvedChoiceCommand<DocumentFontSet>(
            bindings.ResolveChoice,
            DocumentFontSet.FindByName,
            bindings.ApplyFontSet,
            bindings.PrepareExecution);
        registry.Bind(FreeWRibbonCommandAction.ThemeFonts, fonts);
        registry.Bind(FreeWRibbonCommandAction.CustomizeFonts, bindings.CustomizeFonts);
        foreach (var preset in DocumentFontSet.Catalog)
        {
            var captured = preset;
            registry.Register(
                $"freew.theme-fonts.{captured.Name.ToLowerInvariant()}",
                Prepared(bindings, () => bindings.ApplyFontSet(captured)));
        }

        var spacing = new ResolvedChoiceCommand<DocumentParagraphSpacingSet>(
            bindings.ResolveChoice,
            DocumentParagraphSpacingSet.FindByName,
            bindings.ApplyParagraphSpacingSet,
            bindings.PrepareExecution);
        registry.Register("freew.paragraph-spacing", spacing);
        registry.Register("freew.para-spacing", spacing);
        registry.Bind(FreeWRibbonCommandAction.CustomParagraphSpacing, bindings.CustomParagraphSpacing);
        foreach (var preset in DocumentParagraphSpacingSet.Catalog)
        {
            var captured = preset;
            registry.Register(
                ParagraphSpacingCommandId(captured.Name),
                Prepared(bindings, () => bindings.ApplyParagraphSpacingSet(captured)));
        }

        var effects = new ResolvedChoiceCommand<DocumentEffectSet>(
            bindings.ResolveChoice,
            DocumentEffectSet.FindByName,
            bindings.ApplyEffectSet,
            bindings.PrepareExecution);
        registry.Bind(FreeWRibbonCommandAction.ThemeEffects, effects);
        for (var index = 0; index < DocumentEffectSet.Catalog.Count; index++)
        {
            var captured = DocumentEffectSet.Catalog[index];
            registry.Register(
                FreeWContextMenuPlanner.EffectsPrefix + index,
                Prepared(bindings, () => bindings.ApplyEffectSet(captured)));
        }

        var styleSet = new ResolvedChoiceCommand<DocumentStyleSet>(
            bindings.ResolveChoice,
            DocumentStyleSet.FindByName,
            value => bindings.Formatting.ApplyStyleSet(value.Name),
            bindings.PrepareExecution,
            bindings.Formatting.CurrentStyleSetName);
        registry.Bind(FreeWRibbonCommandAction.StyleSet, styleSet);
        registry.Bind(
            FreeWRibbonCommandAction.ResetStyleSet,
            Prepared(bindings, bindings.ApplyDefaultStyleSet));

        registry.Bind(FreeWRibbonCommandAction.PageColor, bindings.PageColor);
        registry.Register("freew.page-color.more", bindings.MorePageColors);
        foreach (var choice in FreeWRibbonPaletteCatalog.PageColors)
        {
            var captured = choice;
            registry.Register(
                captured.CommandId,
                Prepared(bindings, () => bindings.ApplyPageColor(captured.Hex)));
        }

        registry.Register("freew.page-border", bindings.PageBorders);
        registry.Register("freew.page-borders", bindings.PageBorders);

        registry.Bind(FreeWRibbonCommandAction.Watermark, bindings.Watermark);
        RegisterWatermarkPreset("freew.watermark.confidential", "CONFIDENTIAL");
        RegisterWatermarkPreset("freew.watermark.do-not-copy", "DO NOT COPY");
        RegisterWatermarkPreset("freew.watermark.draft", "DRAFT");
        RegisterWatermarkPreset("freew.watermark.urgent", "URGENT");
        registry.Register("freew.watermark.custom", bindings.CustomWatermark);
        registry.Register(
            "freew.watermark.none",
            Prepared(bindings, () => bindings.ApplyWatermarkText(null)));

        return new DesignRibbonCommands(
        [
            new("freew.theme", theme),
            new("freew.style-set", styleSet),
        ]);

        void RegisterWatermarkPreset(string commandId, string text) =>
            registry.Register(
                commandId,
                Prepared(bindings, () => bindings.ApplyWatermarkText(text)));
    }

    private static IRibbonCommand Prepared(DesignRibbonBindings bindings, Action execute) =>
        new PreparedActionCommand(bindings.PrepareExecution, execute);

    private sealed class PreparedActionCommand(Action prepare, Action execute) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            prepare();
            execute();
        }
    }

    private sealed class DropdownCommand : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            // Native ribbon renderers own opening the attached menu. The command exists so the
            // canonical dropdown route remains enabled without pretending to mutate the document.
        }
    }

    private sealed class ResolvedChoiceCommand<T>(
        Func<RibbonCommandContext, string?> resolveChoice,
        Func<string, T?> resolve,
        Action<T> apply,
        Action prepare,
        Func<string?>? getValue = null) : IRibbonStatefulCommand
        where T : class
    {
        public void Execute(RibbonCommandContext context)
        {
            var choice = resolveChoice(context);
            if (string.IsNullOrWhiteSpace(choice) || resolve(choice) is not { } value)
                return;

            prepare();
            apply(value);
        }

        public RibbonCommandState GetState() => new(Value: getValue?.Invoke());
    }
}
