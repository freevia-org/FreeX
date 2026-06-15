namespace FreeX.Ribbon;

/// <summary>
/// The host owns what qualifies as each context (a chart is selected, a table is active, …)
/// and raises <see cref="ContextChanged"/> when it changes. The core only consumes keys.
/// </summary>
public interface IRibbonContextSource
{
    RibbonContextState Current { get; }
    event EventHandler? ContextChanged;
}
