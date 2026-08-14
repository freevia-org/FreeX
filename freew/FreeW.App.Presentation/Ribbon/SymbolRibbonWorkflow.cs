using Free.Shared.Ribbon;
using FreeW.App.Localization;

namespace FreeW.App.Presentation.Ribbon;

public sealed record FreeWRibbonSymbolChoice(
    string CommandId,
    string Glyph,
    string Label);

public sealed record SymbolRibbonPorts(
    Action PrepareExecution,
    Action<string> InsertSymbol);

/// <summary>
/// Owns the stable command identity and exact text payload for the Insert &gt; Symbol palette.
/// Renderers retain only their native root picker and an adapter that inserts ordinary text.
/// </summary>
public static class SymbolRibbonWorkflow
{
    public static IReadOnlyList<FreeWRibbonSymbolChoice> Choices =>
    [
        new("freew.symbol.euro", "€", Loc.Get("Ribbon_Palette_Symbol_Euro_Label")),
        new("freew.symbol.pound", "£", Loc.Get("Ribbon_Palette_Symbol_Pound_Label")),
        new("freew.symbol.yen", "¥", Loc.Get("Ribbon_Palette_Symbol_Yen_Label")),
        new("freew.symbol.cent", "¢", Loc.Get("Ribbon_Palette_Symbol_Cent_Label")),
        new("freew.symbol.copyright", "©", Loc.Get("Ribbon_Palette_Symbol_Copyright_Label")),
        new("freew.symbol.registered", "®", Loc.Get("Ribbon_Palette_Symbol_Registered_Label")),
        new("freew.symbol.trademark", "™", Loc.Get("Ribbon_Palette_Symbol_Trademark_Label")),
        new("freew.symbol.degree", "°", Loc.Get("Ribbon_Palette_Symbol_Degree_Label")),
        new("freew.symbol.plusminus", "±", Loc.Get("Ribbon_Palette_Symbol_PlusMinus_Label")),
        new("freew.symbol.multiply", "×", Loc.Get("Ribbon_Palette_Symbol_Multiplication_Label")),
        new("freew.symbol.divide", "÷", Loc.Get("Ribbon_Palette_Symbol_Division_Label")),
        new("freew.symbol.notequal", "≠", Loc.Get("Ribbon_Palette_Symbol_NotEqual_Label")),
        new("freew.symbol.lessequal", "≤", Loc.Get("Ribbon_Palette_Symbol_LessOrEqual_Label")),
        new("freew.symbol.greaterequal", "≥", Loc.Get("Ribbon_Palette_Symbol_GreaterOrEqual_Label")),
        new("freew.symbol.bullet", "•", Loc.Get("Ribbon_Palette_Symbol_Bullet_Label")),
        new("freew.symbol.ellipsis", "…", Loc.Get("Ribbon_Palette_Symbol_Ellipsis_Label")),
        new("freew.symbol.emdash", "—", Loc.Get("Ribbon_Palette_Symbol_EmDash_Label")),
        new("freew.symbol.endash", "–", Loc.Get("Ribbon_Palette_Symbol_EnDash_Label")),
        new("freew.symbol.arrow-right", "→", Loc.Get("Ribbon_Palette_Symbol_RightArrow_Label")),
        new("freew.symbol.arrow-left", "←", Loc.Get("Ribbon_Palette_Symbol_LeftArrow_Label")),
    ];

    public static void Register(IRibbonCommandRegistry registry, SymbolRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ports);

        foreach (var choice in Choices)
        {
            var captured = choice;
            registry.Register(
                captured.CommandId,
                new PreparedSymbolCommand(
                    ports.PrepareExecution,
                    () => ports.InsertSymbol(captured.Glyph)));
        }
    }

    private sealed class PreparedSymbolCommand(Action prepare, Action insert) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            prepare();
            insert();
        }
    }
}
