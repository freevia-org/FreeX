using System.Reflection;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r202: retires the "a command that changed nothing still pushes an undo entry" class for FreeP the
/// only way it can be retired -- by requiring every command to DECLARE its answer.
/// <para>
/// This class is not mechanically decidable: whether a path can mutate nothing depends on what the
/// callers allow, which is why four rounds of review each found one more instance. What IS decidable
/// is whether the author made a decision. <c>IPresentationCommand.HasEffect</c> defaults to true, so
/// a command that never overrides it has silently inherited "always changes something" without
/// anyone checking. This test requires either an override or an entry below with the reason.
/// </para>
/// <para>
/// The 32 entries were produced by an r202 census that classified all 57 commands then lacking an
/// override, with every claimed no-op checked by two independent verifiers. 25 were confirmed
/// no-op-capable and now have overrides; these 32 are the rest. The reasons are the census's, and
/// they are recorded here rather than in a review document so the next person to add a command sees
/// the standard they are being held to.
/// </para>
/// </summary>
public class R202_CommandDeclaresHasEffectContractTests
{
    private static readonly Dictionary<string, string> DeliberatelyInheritsTheDefault = new()
    {
        // Unconditional mutations: no early return exists before the change.
        ["AddShapeCommand"] = "Shapes(...).Add(_shape) is the whole body",
        ["AddShapeAnimationCommand"] = "anims.Add(_animation) always runs once the slide resolves",
        ["InsertSlideCommand"] = "Slides.Insert always runs; the index is clamped, never rejected",
        ["PasteSlideCommand"] = "same unconditional-insert shape as InsertSlideCommand",
        ["PasteShapesCommand"] = "adds every pasted shape once the slide resolves",
        ["InsertTableRowCommand"] = "an insert has no 'already at the limit' case, unlike a delete",
        ["InsertTableColumnCommand"] = "as InsertTableRowCommand",

        // A real early return that every production caller already excludes.
        ["DeleteSlideCommand"] =
            "the index guard cannot trip: EditingSession checks Slides.Count and keeps "
            + "_currentSlideIndex clamped; the slide pane deletes in descending index order",
        ["DuplicateSlideCommand"] = "same clamp invariant as DeleteSlideCommand",
        ["MoveSlideCommand"] =
            "MoveInList no-ops when from == to, and SlidePanePlanner.PlanMoveAction sets "
            + "canMove = false for exactly those two target positions",
        ["ReorderShapeCommand"] =
            "all three EditingSession call sites bound the index before constructing it",
        ["ReorderShapeAnimationCommand"] = "reached only through gated animation-pane paths",
        ["RemoveShapeAnimationCommand"] = "EditingSession.RemoveAnimation range-checks first",
        ["RunFormatCommandBase"] =
            "abstract; its concrete Toggle* subclasses are only built for a resolved run",
        ["SetSlideAnimationBuildListCommand"] =
            "an equal-value setter in isolation, but its one caller only issues it after the "
            + "build list has actually been rebuilt",

        // Verified NOT reachable from any UI gesture -- the census claimed a no-op for each of
        // these and two independent verifiers refuted it on reachability. Kept as entries rather
        // than as overrides, so the claim and its refutation stay attached to the code.
        ["AddChartSeriesCommand"] = "no production caller; reached only from tests",
        ["AddChartCategoryCommand"] = "the dialog edits an in-memory planner, not this command",
        ["RemoveChartSeriesCommand"] = "no production caller; reached only from tests",
        ["RemoveChartCategoryCommand"] = "no production caller; reached only from tests",
        ["MoveChartSeriesCommand"] = "no production caller reaches the degenerate index",
        ["SetChartTitleCommand"] = "the chart dialogs route through a planner, not this command",
        ["SetChartSeriesNameCommand"] = "as SetChartTitleCommand",
        ["SetChartCategoryLabelCommand"] = "as SetChartTitleCommand",
        ["SetChartCellValueCommand"] = "as SetChartTitleCommand",
        ["DeleteTableRowCommand"] =
            "the one-row case is gated by EditingSession.TryDeleteActiveTableRow before the "
            + "command is constructed",
        ["DeleteTableColumnCommand"] = "as DeleteTableRowCommand",
        ["UpdateConnectorBoundsCommand"] =
            "never bus-executed: it is built by ConnectorRouter inside MoveShapeCommand's reroute",
        ["SetShapeFillCommand"] = "callers supply a freshly built fill, never the shape's own",
        ["SetShapeOutlineCommand"] = "as SetShapeFillCommand",
        ["SetTableCellTextCommand"] = "the caller commits only when the edited text differs",
        ["SetCustomGeometryPointCommand"] = "issued from a drag that has already moved the point",
        ["SetShapeAnimationCommand"] = "issued only from a dialog commit that changed a field",
    };

    [Fact]
    public void EveryPresentationCommandDeclaresWhetherItHasEffect()
    {
        var undeclared = CommandTypes()
            .Where(type => !DeclaresHasEffect(type) && !IsCoveredByAnEntry(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        CommandTypes().Should().HaveCountGreaterThan(100,
            "the reflection walk must actually be finding commands -- an empty walk would make this "
            + "test pass while guarding nothing");

        undeclared.Should().BeEmpty(
            "a command that never overrides HasEffect inherits 'always changes something' without "
            + "anyone deciding that. If it can be invoked where it would mutate nothing, override "
            + "HasEffect -- the bus then skips it, and the undo entry that would have CLEARED REDO "
            + "is never pushed. If it genuinely always changes something, add it below with the "
            + "reason. Undeclared:\n" + string.Join("\n", undeclared));
    }

    [Fact]
    public void EveryEntryStillNamesALiveCommandThatStillLacksAnOverride()
    {
        // Two ways an entry goes stale: the command is gone, or it has since gained an override.
        // Either way the entry is dead weight that would silently cover a future command.
        var live = CommandTypes().ToDictionary(type => type.Name, DeclaresHasEffect);

        var stale = DeliberatelyInheritsTheDefault.Keys
            .Where(name => !live.TryGetValue(name, out var declares) || declares)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        stale.Should().BeEmpty(
            "remove entries whose command is gone or which now override HasEffect:\n"
            + string.Join("\n", stale));
    }

    /// <summary>
    /// True when this type or a base of it is listed. A family whose base was judged once -- the
    /// Toggle* run-format commands under RunFormatCommandBase -- is covered by that one entry, the
    /// same way it would be covered by one override on the base.
    /// </summary>
    private static bool IsCoveredByAnEntry(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (DeliberatelyInheritsTheDefault.ContainsKey(current.Name))
                return true;
        }

        return false;
    }

    /// <summary>Every command type in the model assembly, abstract bases included.</summary>
    private static List<Type> CommandTypes() =>
        [.. typeof(IPresentationCommand).Assembly.GetTypes()
            .Where(type => type.IsClass && typeof(IPresentationCommand).IsAssignableFrom(type))];

    /// <summary>
    /// True when this type or any base declares HasEffect. A subclass inheriting a base's override
    /// counts as declared: the decision was made once, for the family.
    /// </summary>
    private static bool DeclaresHasEffect(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (current.GetMethod(
                    "HasEffect",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is not null)
            {
                return true;
            }
        }

        return false;
    }
}
