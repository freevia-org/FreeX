using System.Reflection;
namespace FreeW.Core.Model.Tests;
/// <summary>
/// r203: extends r202's declaration contract from FreeP to FreeW. Same class, same mechanism --
/// <c>IDocumentCommand.HasEffect</c> defaults to true, so a command that never overrides it has
/// inherited "always changes something" without anyone deciding that, and
/// <c>DocumentCommandBus.Execute</c> then pushes an undo entry whose push CLEARS REDO.
/// <para>
/// r199 found one instance here by reading (ChangeDrawingGroupChildZOrderCommand, whose sibling in
/// the same file had the override all along). This test asks the question of every command at once,
/// and requires a declaration rather than an inherited default.
/// </para>
/// </summary>
public sealed class R203_CommandDeclaresHasEffectContractTests
{
    /// <summary>
    /// Commands that legitimately always change something, with the reason. Populated by the r203
    /// census; every entry is a judgement someone made, not a blanket exemption.
    /// </summary>
    private static readonly Dictionary<string, string> DeliberatelyInheritsTheDefault = new()
    {
        // Judged by the r207 census (the last 52), with its reason.
        ["AcceptAllRevisionsCommand"] =
            "the caller checks TrackChanges.HasRevisions first, and Resolve re-checks it synchronously",
        ["AcceptRevisionCommand"] =
            "only built by RevisionResolutionCoordinator, which has no production caller",
        ["RejectAllRevisionsCommand"] =
            "as AcceptAllRevisionsCommand",
        ["RejectRevisionCommand"] =
            "as AcceptRevisionCommand",
        ["RevisionResolutionCommand"] =
            "as AcceptRevisionCommand",
        ["AddCommentCommand"] =
            "the caller excludes both early returns -- bad block index and no commentable range",
        ["AddCommentReplyCommand"] =
            "the caller looks the comment up first and mints an id strictly above every existing one",
        ["ApplyCitationStyleCommand"] =
            "the caller returns early when the style already equals the document's",
        ["ApplyManualHyphenationCommand"] =
            "every edit comes from the hyphenation planner, whose candidates cannot already carry a soft hyphen",
        ["CarryMergedCellContentCommand"] =
            "never executed standalone -- always batched with a MergeCells command that does mutate",
        ["CompositeDocumentCommand"] =
            "a batch wrapper; it is only pushed when the batch is non-empty",
        ["DeleteCommentCommand"] =
            "the caller confirms the comment exists",
        ["DeleteNoteCommand"] =
            "the caller confirms the note exists",
        ["DeleteParagraphCommand"] =
            "removes a block unconditionally once the index resolves",
        ["DeleteTableColumnCommand"] =
            "the caller gates the last-column case",
        ["DeleteTableRowCommand"] =
            "the caller gates the last-row case",
        ["EnsureHeaderFooterCommand"] =
            "creates the header/footer when absent; the caller only issues it when one is needed",
        ["GroupFloatingObjectsCommand"] =
            "requires two or more targets, and grouping them always restructures the run",
        ["InsertBlockCommand"] =
            "an insert has no already-there case",
        ["InsertCrossReferenceCommand"] =
            "inserts a new field run",
        ["InsertNoteCommand"] =
            "inserts a new note and its reference mark",
        ["InsertParagraphCommand"] =
            "inserts a block unconditionally",
        ["InsertShapeTextParagraphBreakCommand"] =
            "splits a paragraph, which always changes the paragraph list",
        ["InsertTableCellFormulaCommand"] =
            "inserts a field into the cell",
        ["InsertTableCellNoteCommand"] =
            "inserts a note reference into the cell",
        ["InsertTableColumnCommand"] =
            "an insert has no already-there case",
        ["InsertTableRowCommand"] =
            "an insert has no already-there case",
        ["MergeCellsHorizontalCommand"] =
            "the caller requires two distinct cells",
        ["MergeCellsVerticalCommand"] =
            "the caller requires two distinct cells",
        ["MergeShapeTextParagraphWithPreviousCommand"] =
            "the caller requires a previous paragraph to merge into",
        ["NudgeImagePositionCommand"] =
            "the caller never issues a zero delta",
        ["RemoveBookmarkAtCommand"] =
            "the caller confirms a bookmark is there",
        ["RemoveBookmarkCommand"] =
            "the caller confirms the bookmark exists",
        ["RemoveFloatingRunCommand"] =
            "removes a run unconditionally once it resolves",
        ["ReorderBlocksCommand"] =
            "the caller excludes the identity ordering",
        ["SpliceCellParagraphsCommand"] =
            "splices a paragraph list, which always changes it",
        ["SpliceHeaderFooterParagraphsCommand"] =
            "as SpliceCellParagraphsCommand",
        ["StyleCatalogCommand"] =
            "the caller only issues it for a style that differs",
        ["ToggleObjectWrappingCommand"] =
            "a toggle: the caller passes the opposite of the current wrapping",
        ["EditHeaderFooterParagraphCommand"] =
            "r207 census claimed a no-op; two verifiers REFUTED it on the replace path",
        ["UngroupFloatingObjectsCommand"] =
            "r207 census claimed a no-op; two verifiers REFUTED it -- the reader enforces the >=2-children invariant",
        // Judged by the r205 census, with its reason.
        ["ReplaceCellContentControlRunSpanCommand"] =
            "its three callers each return before invoking it when there is nothing to insert or "
            + "nothing of the field left to delete, so every reachable call changes the span",
        ["ReplaceContentControlRunSpanCommand"] = "same caller gates as its cell twin",
        ["ReplaceTableCellParagraphRunsCommand"] =
            "its one caller builds every address in the same synchronous pass and always splices in "
            + "a new index-mark run",
        ["SetChartLegendCommand"] =
            "reached only through ToggleChartLegend, which passes !IsLegendVisible -- always a flip",
        ["SetCommentResolvedCommand"] =
            "both UI entry points call TryToggleCommentResolved, which passes !comment.Resolved; the "
            + "one API that could pass the current value has no production caller",
        ["SetTableFormattingCommand"] =
            "every production caller passes a negating transform on a record read in the same call",
        ["SetRunFormattingCommand"] =
            "DEAD: zero call sites anywhere, tests included. Formatting goes through "
            + "FormatParagraphRunsCommand. See finding 92 -- the right fix is deletion",
        // The r205 census claimed these could no-op; two verifiers each refuted it.
        ["SetCellBorderPayloadCommand"] = "refuted on reachability: no caller supplies the current payload",
        ["SetMultiLevelNumberFormatsCommand"] = "refuted: the caller only issues it after a real edit",
        ["SetTableCellContentCommand"] =
            "refuted: both label-sheet callers regenerate content that differs from what is there",
        // Judged by the r203 census, with its reason.
        ["SetShapeCustomGeometryCommand"] =
            "its only caller, ConvertShapeToFreeform, returns NoChange before constructing it "
            + "when the shape already has a custom geometry",
        ["SetShapeTextRunCommand"] =
            "both call sites exclude the degenerate case first -- an empty insert, or a delete "
            + "outside the run -- so every reachable path changes the text",
        ["SetShapeRotationCommand"] =
            "DEAD: never constructed anywhere. Rotation routes through SetFloatingRotationCommand "
            + "instead. See finding 90 -- the right fix is deletion, not an override",
        ["SetShapeWrappingCommand"] =
            "DEAD: never constructed anywhere. Wrapping routes through SetFloatingWrapCommand. "
            + "See finding 90",
    };
    /// <summary>
    /// Commands the r203 census CONFIRMED can be invoked where they mutate nothing, each checked by
    /// two independent verifiers, and which do not have their override yet.
    /// </summary>
    /// <remarks>
    /// These are not unknowns -- they are known defects with the evidence recorded in
    /// docs/review/region-coverage.md findings 89 and 91.
    /// <para>
    /// Two of them -- ReplaceParagraphRunsCommand and ReplaceCellParagraphRunsCommand -- CANNOT be
    /// fixed by a HasEffect override at all: they take an opaque Action&lt;Paragraph&gt; rebuild
    /// delegate, so the only way to know whether it changes anything is to run it, and running it
    /// mutates. They need a bus-level before/after comparison instead. See finding 93.
    /// </para> They are listed separately from
    /// <see cref="NotYetAdjudicated"/> because conflating "nobody looked" with "we looked and it is
    /// broken" would let the second hide inside the first.
    /// </remarks>
    private static readonly HashSet<string> KnownNoOpCapableNotYetFixed =
    [
        "ApplyShapeStyleCommand",
        "ApplyTableStyleCommand",
        "ArrangeFloatingObjectsCommand",
        "DesignCatalogCommand",
        "FormatParagraphRunsCommand",
        "MoveShapeEditPointCommand",
        "MutateSmartArtStructureCommand",
        "ResetImageSizeCommand",
        "SplitCellCommand",
        "ReplaceBlocksCommand",
        "ReplaceCellParagraphRunsCommand",
        "ReplaceChartDataCommand",
        "ReplaceContentControlRunCommand",
        "ReplaceNoteContentCommand",
        "ReplaceParagraphRunsCommand",
        "ReplaceShapeTextParagraphsCommand",
        "ReplaceSmartArtContentCommand",
        "ReplaceSourcesCommand",
        "SetCellParagraphMarkRevisionCommand",
        "SetNoteNumberingOptionsCommand",
        "SetPageSettingsCommand",
        "SetParagraphBookmarkNameCommand",
        "SetParagraphMarkRevisionCommand",
        "SetTableAutoFitCommand",
    ];
    /// <summary>
    /// Commands nobody has judged yet. This is DEBT, named as debt -- and as of r207 it is EMPTY:
    /// every one of FreeW's 128 commands has been judged. It stays because the contract needs a
    /// place for the next unjudged command to be refused, not to hold one.
    /// </summary>
    /// <remarks>
    /// FreeW had 128 commands inheriting the default when this contract was written. A blanket
    /// exemption for all of them would be a guard that guards nothing, so the population is split
    /// three ways -- judged with a reason, known-broken, and unexamined -- and
    /// <see cref="TheDebtOnlyEverShrinks"/> holds the total to a ceiling each round
    /// lowers.
    /// <para>
    /// What the contract buys while these lists are non-empty: a NEW command cannot join them
    /// silently. Anything not named here fails outright, so the debt is closed to new entrants
    /// before it is paid down.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> NotYetAdjudicated =
    [
    ];
    /// <summary>
    /// The ceiling on <see cref="NotYetAdjudicated"/>. Lower it as rounds adjudicate; never raise it.
    /// </summary>
    /// <summary>The ceiling on the two debt lists together. Lower it as rounds pay down; never raise it.</summary>
    private const int DebtCeiling = 24;
    [Fact]
    public void EveryDocumentCommandDeclaresWhetherItHasEffect()
    {
        var undeclared = CommandTypes()
            .Where(type => !DeclaresHasEffect(type) && !IsCoveredByAnEntry(type))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        CommandTypes().Should().HaveCountGreaterThan(30,
            "the reflection walk must actually be finding commands -- an empty walk would make this "
            + "test pass while guarding nothing");
        undeclared.Should().BeEmpty(
            "a command that never overrides HasEffect inherits 'always changes something' without "
            + "anyone deciding that. If it can be invoked where it would mutate nothing, override "
            + "HasEffect so the bus skips it and no redo-clearing undo entry is pushed. If it "
            + "genuinely always changes something, add it below with the reason. Undeclared "
            + "(" + undeclared.Count + "):\n" + string.Join("\n", undeclared));
    }
    [Fact]
    public void TheDebtOnlyEverShrinks()
    {
        (KnownNoOpCapableNotYetFixed.Count + NotYetAdjudicated.Count).Should().BeLessThanOrEqualTo(
            DebtCeiling,
            "this list is debt. A command may leave it -- by gaining a HasEffect override, or by "
            + "being judged and moved up with a reason -- but nothing may join it, and the ceiling "
            + "must be lowered to match whenever it shrinks, never raised to accommodate a new "
            + "entry. A new command that needs judging fails the contract above instead.");
    }
    [Fact]
    public void EveryEntryStillNamesALiveCommandThatStillLacksAnOverride()
    {
        var live = CommandTypes().ToDictionary(type => type.Name, DeclaresHasEffect);
        DeliberatelyInheritsTheDefault.Keys
            .Concat(KnownNoOpCapableNotYetFixed)
            .Concat(NotYetAdjudicated)
            .Where(name => !live.TryGetValue(name, out var declares) || declares)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Should().BeEmpty(
                "remove entries whose command is gone or which now override HasEffect -- a stale "
                + "entry would silently cover a future command of the same name, and one left in "
                + "the debt list would overstate the debt");
    }
    [Fact]
    public void NoCommandIsInBothLists()
    {
        DeliberatelyInheritsTheDefault.Keys.Intersect(NotYetAdjudicated).Should().BeEmpty(
            "a command is either judged or not; being in both hides which");
    }
    private static List<Type> CommandTypes() =>
        [.. typeof(IDocumentCommand).Assembly.GetTypes()
            .Where(type => type.IsClass && typeof(IDocumentCommand).IsAssignableFrom(type))];
    private static bool IsCoveredByAnEntry(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (DeliberatelyInheritsTheDefault.ContainsKey(current.Name)
                || KnownNoOpCapableNotYetFixed.Contains(current.Name)
                || NotYetAdjudicated.Contains(current.Name))
            {
                return true;
            }
        }
        return false;
    }
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
