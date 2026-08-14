using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record CitationRibbonPorts(
    IRibbonCommand InsertCitation,
    IRibbonCommand ManageSources,
    IRibbonCommand InsertBibliography,
    Action<CitationStyle> ApplyStyle,
    Func<CitationStyle> GetStyle,
    Action<RibbonCommandState>? StyleStateChanged = null);

public sealed record CitationRibbonRegistration(IRibbonStatefulCommand CitationStyleCommand);

/// <summary>
/// Owns References citation/bibliography command identity and citation-style value translation for
/// both renderers. Native dialogs and editor mutations remain renderer adapters.
/// </summary>
public static class CitationRibbonWorkflow
{
    public const string InsertCitationCompatibilityId = "freew.insert-citation";

    public static CitationRibbonRegistration Register(
        FreeWRibbonEditorCommandFamilyBuilder bindings,
        CitationRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return RegisterCore(bindings.Bind, bindings.Register, ports);
    }

    public static CitationRibbonRegistration Register(IRibbonCommandRegistry bindings, CitationRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return RegisterCore(
            (action, command) => bindings.Bind(action, command),
            bindings.Register,
            ports);
    }

    private static CitationRibbonRegistration RegisterCore(
        Func<FreeWRibbonCommandAction, IRibbonCommand, IRibbonCommand> bind,
        Action<RibbonCommandId, IRibbonCommand> register,
        CitationRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.ApplyStyle);
        ArgumentNullException.ThrowIfNull(ports.GetStyle);

        bind(FreeWRibbonCommandAction.Citation, ports.InsertCitation);
        register(InsertCitationCompatibilityId, ports.InsertCitation);
        bind(FreeWRibbonCommandAction.ManageSources, ports.ManageSources);
        bind(FreeWRibbonCommandAction.Bibliography, ports.InsertBibliography);

        var styleCommand = new FreeWRibbonChoiceCommand(
            value => ports.ApplyStyle(Citations.ParseStyle(value, ports.GetStyle())),
            () => Citations.StyleName(ports.GetStyle()),
            ports.StyleStateChanged);
        bind(FreeWRibbonCommandAction.CitationStyle, styleCommand);
        return new CitationRibbonRegistration(styleCommand);
    }
}
