using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>A change the Equation tab applies to the selected equation without opening the editor.</summary>
public enum EquationConversion
{
    /// <summary>Build the equation's linear text up into structures (Word's "Professional").</summary>
    Professional,

    /// <summary>Show the equation as its linear source text (Word's "Linear").</summary>
    Linear,

    /// <summary>Run the equation in line with the surrounding text (Word's "Change to Inline").</summary>
    Inline,

    /// <summary>Put the equation on a line of its own (Word's "Change to Display").</summary>
    Display,

    JustifyLeft,
    JustifyCenter,
    JustifyRight,
    JustifyCenterAsGroup
}

/// <summary>
/// What a renderer provides to the Equation tab. Only <see cref="InsertEquation"/> is required; a port a
/// renderer does not have leaves the commands that need it unavailable (disabled) rather than faking them.
/// </summary>
/// <param name="InsertEquation">Inserts a finished equation at the caret.</param>
/// <param name="InsertNewEquation">Opens the equation editor for a new, empty equation.</param>
/// <param name="EditSelectedEquation">Opens the equation editor on the selected equation.</param>
/// <param name="InsertLinearText">Inserts linear-format text at the equation editor's caret, opening the editor if needed.</param>
/// <param name="SelectedEquation">The equation the selection is on, or null.</param>
/// <param name="ReplaceEquation">Replaces an equation in the document (original, replacement) as one undoable edit.</param>
public sealed record EquationToolsRibbonPorts(
    Action<Equation> InsertEquation,
    Action? InsertNewEquation = null,
    Action? EditSelectedEquation = null,
    Action<string>? InsertLinearText = null,
    Func<Equation?>? SelectedEquation = null,
    Action<Equation, Equation>? ReplaceEquation = null);

/// <summary>
/// Owns the Equation contextual tab's command dispatch for both renderers: the Tools commands
/// (new/edit, Professional/Linear, inline/display, justification), the Symbols galleries and the
/// Structures galleries.
/// </summary>
public static class EquationToolsRibbonWorkflow
{
    public const string GalleryCommandId = "freew.equation-gallery";
    public const string NewCommandId = "freew.equation-new";
    public const string EditCommandId = "freew.equation-edit";
    public const string ProfessionalCommandId = "freew.equation-professional";
    public const string LinearCommandId = "freew.equation-linear";
    public const string InlineCommandId = "freew.equation-inline";
    public const string DisplayCommandId = "freew.equation-display";
    public const string JustifyCommandId = "freew.equation-justify";
    public const string JustifyLeftCommandId = "freew.equation-justify-left";
    public const string JustifyCenterCommandId = "freew.equation-justify-center";
    public const string JustifyRightCommandId = "freew.equation-justify-right";
    public const string JustifyCenterAsGroupCommandId = "freew.equation-justify-center-group";

    /// <summary>The conversion commands and what each one does, in ribbon order.</summary>
    public static IReadOnlyList<(string CommandId, EquationConversion Conversion)> Conversions { get; } =
    [
        (ProfessionalCommandId, EquationConversion.Professional),
        (LinearCommandId, EquationConversion.Linear),
        (InlineCommandId, EquationConversion.Inline),
        (DisplayCommandId, EquationConversion.Display),
        (JustifyLeftCommandId, EquationConversion.JustifyLeft),
        (JustifyCenterCommandId, EquationConversion.JustifyCenter),
        (JustifyRightCommandId, EquationConversion.JustifyRight),
        (JustifyCenterAsGroupCommandId, EquationConversion.JustifyCenterAsGroup),
    ];

    /// <summary>Every command id this workflow registers, in registration order.</summary>
    public static IReadOnlyList<string> CommandIds { get; } =
    [
        GalleryCommandId,
        NewCommandId,
        EditCommandId,
        JustifyCommandId,
        .. Conversions.Select(conversion => conversion.CommandId),
        .. EquationSymbolCatalog.Sets.Select(set => set.CommandId),
        .. EquationSymbolCatalog.Symbols.Select(symbol => symbol.CommandId),
        .. EquationStructureCatalog.Groups.Select(group => group.CommandId),
        .. EquationStructureCatalog.Structures.Select(structure => structure.CommandId),
    ];

    public static void Register(IRibbonCommandRegistry bindings, EquationToolsRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.InsertEquation);

        bindings.Register(GalleryCommandId, EmptyRibbonCommand.Instance);
        bindings.Register(JustifyCommandId, EmptyRibbonCommand.Instance);

        // Without an editor a new equation is the default sample, exactly as Insert > Equation does.
        bindings.Register(NewCommandId, new ActionRibbonCommand(
            ports.InsertNewEquation
            ?? (() => ports.InsertEquation(EquationPresetCatalog.CreateDefaultEquation()))));
        bindings.Register(EditCommandId, OrUnavailable(ports.EditSelectedEquation));

        foreach (var (commandId, conversion) in Conversions)
        {
            bindings.Register(
                commandId,
                ports.SelectedEquation is { } selected && ports.ReplaceEquation is { } replace
                    ? new ActionRibbonCommand(() => ApplyConversion(selected, replace, conversion))
                    : FreeWRibbonExecutionProfile.UnavailableCommand);
        }

        foreach (var set in EquationSymbolCatalog.Sets)
        {
            bindings.Register(set.CommandId, EmptyRibbonCommand.Instance);
            foreach (var symbol in set.Symbols)
            {
                var text = symbol.Text;
                bindings.Register(
                    symbol.CommandId,
                    ports.InsertLinearText is { } insert
                        ? new ActionRibbonCommand(() => insert(text))
                        : FreeWRibbonExecutionProfile.UnavailableCommand);
            }
        }

        foreach (var group in EquationStructureCatalog.Groups)
        {
            bindings.Register(group.CommandId, EmptyRibbonCommand.Instance);
            foreach (var structure in group.Structures)
            {
                // With an editor a structure is a template typed at its caret; without one it is still a
                // well-formed equation, so it is inserted as a new equation.
                var linearText = structure.LinearText;
                bindings.Register(
                    structure.CommandId,
                    new ActionRibbonCommand(ports.InsertLinearText is { } insert
                        ? () => insert(linearText)
                        : () => ports.InsertEquation(EquationLinearFormat.Parse(linearText))));
            }
        }
    }

    /// <summary>
    /// The equation <paramref name="conversion"/> turns <paramref name="equation"/> into, or null when it
    /// would change nothing (or, for Linear, when part of the equation cannot be written as text and the
    /// conversion is refused rather than lose it).
    /// </summary>
    public static Equation? Convert(Equation equation, EquationConversion conversion)
    {
        ArgumentNullException.ThrowIfNull(equation);

        Equation? result;
        switch (conversion)
        {
            case EquationConversion.Professional:
                result = EquationLinearFormat.BuildUp(equation);
                break;

            case EquationConversion.Linear:
                result = EquationLinearFormat.ToLinearDisplay(equation);
                break;

            case EquationConversion.Inline:
                result = equation.Clone();
                result.IsDisplayMath = false;
                result.Justification = MathJustification.CenterGroup;
                break;

            case EquationConversion.Display:
                result = equation.Clone();
                result.IsDisplayMath = true;
                break;

            default:
                // Justification belongs to display equations; justifying an inline one makes it display.
                result = equation.Clone();
                result.IsDisplayMath = true;
                result.Justification = conversion switch
                {
                    EquationConversion.JustifyLeft => MathJustification.Left,
                    EquationConversion.JustifyCenter => MathJustification.Center,
                    EquationConversion.JustifyRight => MathJustification.Right,
                    _ => MathJustification.CenterGroup
                };
                break;
        }

        return result is null || EquationStructure.AreEquivalent(result, equation, compareLayout: true)
            ? null
            : result;
    }

    private static void ApplyConversion(
        Func<Equation?> selectedEquation,
        Action<Equation, Equation> replaceEquation,
        EquationConversion conversion)
    {
        if (selectedEquation() is { } selected && Convert(selected, conversion) is { } converted)
            replaceEquation(selected, converted);
    }

    private static IRibbonCommand OrUnavailable(Action? action) =>
        action is null ? FreeWRibbonExecutionProfile.UnavailableCommand : new ActionRibbonCommand(action);
}
