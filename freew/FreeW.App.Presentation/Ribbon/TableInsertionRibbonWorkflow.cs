using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public sealed record TableInsertionChoice(
    RibbonCommandId CommandId,
    int Rows,
    int Columns);

public sealed record TableInsertionRibbonPorts(Action<int, int> InsertTable);

/// <summary>
/// Owns Insert &gt; Table command identity and dimensions for both renderers. Hosts provide only
/// their undoable table insertion adapter; Presentation keeps the button face, menu choices, and
/// legacy route aligned.
/// </summary>
public static class TableInsertionRibbonWorkflow
{
    private static readonly TableInsertionChoice[] ChoiceItems =
    [
        new("freew.table-2x2", 2, 2),
        new("freew.table-3x3", 3, 3),
        new("freew.table-4x4", 4, 4),
        new("freew.table-5x2", 2, 5),
    ];

    public static IReadOnlyList<TableInsertionChoice> Choices => ChoiceItems;

    public static void Register(
        IRibbonCommandRegistry bindings,
        TableInsertionRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        RegisterCore(bindings.Bind, bindings.Register, ports);
    }

    public static void Register(
        FreeWRibbonEditorCommandFamilyBuilder bindings,
        TableInsertionRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        RegisterCore(bindings.Bind, bindings.Register, ports);
    }

    private static void RegisterCore(
        Func<FreeWRibbonCommandAction, IRibbonCommand, IRibbonCommand> bind,
        Action<RibbonCommandId, IRibbonCommand> register,
        TableInsertionRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.InsertTable);

        var commands = new Dictionary<(int Rows, int Columns), IRibbonCommand>();
        foreach (var choice in ChoiceItems)
        {
            var dimensions = (choice.Rows, choice.Columns);
            if (!commands.TryGetValue(dimensions, out var command))
            {
                command = CreateCommand(ports.InsertTable, choice.Rows, choice.Columns);
                commands.Add(dimensions, command);
            }

            register(choice.CommandId, command);
        }

        bind(FreeWRibbonCommandAction.Table, commands[(2, 2)]);
        register("freew.insert-table", commands[(3, 3)]);
    }

    private static IRibbonCommand CreateCommand(
        Action<int, int> insertTable,
        int rows,
        int columns) =>
        new ActionRibbonCommand(() => insertTable(rows, columns));
}
