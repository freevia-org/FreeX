using System.Collections;
using System.Reflection;
using System.Text;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit.Abstractions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R175-commands-failure-outcome-runtime-audit-1.
///
/// CompositeWorkbookCommand.Apply deliberately does NOT revert a child that RETURNED a failure
/// outcome (only one that threw) -- see the comment on its !outcome.Success branch, and
/// Apply_WhenCommandRejectsWithoutMutating_DoesNotRevertThatCommand, which pins that decision
/// because a blanket revert corrupts commands like SetCalculationModeCommand. CommandBus.Execute
/// makes the same choice: it TryReverts on a throw and does nothing on a failure outcome.
///
/// That decision is only safe while the codebase honours a convention: <b>a command that returns a
/// failure outcome must not have mutated the workbook</b>. Otherwise the partial edit is stranded
/// -- no undo entry is pushed for a failed command, so nothing can ever reach it.
///
/// The static audit that accompanied the decision could confirm the convention only for mutations
/// written directly in Apply. It could not follow mutations made through helper call chains
/// (StructuredTableEditEffects.Apply, the DuplicateSheetDrawingCloner family, the planners), where
/// a purely textual scan cannot resolve which branch actually runs -- that pass produced 365 hits
/// across 73 files, almost all of them mutually exclusive branches, and was too noisy to trust.
///
/// This harness closes that gap by construction rather than by reading: it runs commands for real
/// and compares a deep reflective fingerprint of the whole workbook (see
/// <see cref="WorkbookFingerprint"/>) across the call. Because the fingerprint walks the live object
/// graph, it sees a mutation regardless of which helper made it, how deep the call chain went, or
/// whether the serializer would have persisted it.
/// </summary>
public sealed class FailureOutcomeMutationAuditTests(ITestOutputHelper output)
{
    /// <summary>A command whose Apply returned failure after changing the workbook.</summary>
    private sealed record Violation(string Command, string Scenario, IReadOnlyList<string> ChangedPaths);

    /// <summary>
    /// The invariant. Every discovered IWorkbookCommand that can be constructed from the synthesized
    /// argument bank is applied against each fixture; whenever Apply returns a failure outcome, the
    /// workbook must be byte-for-byte the state it was in before the call.
    /// </summary>
    [Fact]
    public void CommandsThatReturnFailureOutcomes_DoNotMutateTheWorkbook()
    {
        var violations = new List<Violation>();
        var covered = new SortedSet<string>(StringComparer.Ordinal);
        var failureOutcomes = 0;
        var skipped = new SortedDictionary<string, string>(StringComparer.Ordinal);

        var commandTypes = typeof(EditCellsCommand).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .Where(t => typeof(IWorkbookCommand).IsAssignableFrom(t))
            // The composite is the thing under audit, not a subject: applying it with a synthesized
            // child list would just re-enter the code path this harness exists to protect.
            .Where(t => t != typeof(CompositeWorkbookCommand))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        commandTypes.Should().NotBeEmpty("the audit is worthless if type discovery silently finds nothing");

        foreach (var scenario in Scenarios())
        {
            // Stability check once per scenario, not once per command. A lazily computed/caching
            // property would read as a phantom mutation, but that is a property of the FIXTURE, so
            // re-verifying it for all ~230 commands only burned CPU -- and this assembly also hosts
            // wall-clock budget tests (GroupCommandTests' 2s O(N^2) guard) that xunit may run in
            // parallel with this one, so the audit has to stay a good citizen about load.
            var probe = scenario.Build().Workbook;
            var probeFingerprint = WorkbookFingerprint.Capture(probe);
            WorkbookFingerprint.Capture(probe).Should().Be(probeFingerprint,
                $"the fingerprint must be stable for the '{scenario.Name}' fixture, or every "
                + "comparison below is meaningless");

            foreach (var type in commandTypes)
            {
                // Each command gets a pristine fixture: a command that legitimately succeeds would
                // otherwise leave edits behind that make the next command's comparison meaningless.
                var (workbook, bank) = scenario.Build();

                if (!TryConstruct(type, bank, out var command, out var reason))
                {
                    skipped.TryAdd(type.Name, reason);
                    continue;
                }

                var ctx = new TestCommandContext(workbook);
                var before = WorkbookFingerprint.Capture(workbook);

                CommandOutcome outcome;
                try
                {
                    outcome = command!.Apply(ctx);
                }
                catch
                {
                    // The throw path is a different contract -- CommandBus.Execute and
                    // CompositeWorkbookCommand both DO revert a command that threw, so a partial
                    // mutation there is already handled and is not what this audit is about.
                    continue;
                }

                covered.Add(type.Name);
                if (outcome.Success)
                    continue;

                failureOutcomes++;
                var after = WorkbookFingerprint.Capture(workbook);
                if (after != before)
                    violations.Add(new Violation(type.Name, scenario.Name, WorkbookFingerprint.Diff(before, after)));
            }
        }

        output.WriteLine($"discovered   : {commandTypes.Count} IWorkbookCommand types");
        output.WriteLine($"exercised    : {covered.Count} (Apply ran and returned an outcome)");
        output.WriteLine($"failure paths: {failureOutcomes} failure outcomes observed");
        output.WriteLine($"skipped      : {skipped.Count} (could not synthesize arguments)");
        foreach (var (name, reason) in skipped)
            output.WriteLine($"    skip {name}: {reason}");

        // Coverage floors: without these the audit degrades silently into a no-op the day a
        // constructor signature changes and every command starts landing in `skipped`. Set just
        // below the levels actually achieved (229 of 232 exercised, 409 failure outcomes) so
        // ordinary churn does not trip them but a collapse in coverage does. The 3 stragglers need
        // an IFilterCriterion, a WorkbookTheme, and a FormControlInteractionCommand constructor the
        // synthesizer cannot reach; they are listed by name in the output above.
        covered.Count.Should().BeGreaterThan(200,
            "the audit must exercise essentially the whole command surface, not a shrinking slice");
        failureOutcomes.Should().BeGreaterThan(350,
            "the audit is only meaningful if it observes real failure outcomes");

        if (violations.Count > 0)
        {
            var report = new StringBuilder();
            report.AppendLine("Commands returned a failure outcome AFTER mutating the workbook.");
            report.AppendLine("Such an edit is stranded: no undo entry is pushed for a failed command,");
            report.AppendLine("and neither CommandBus nor CompositeWorkbookCommand reverts this path.");
            foreach (var violation in violations)
            {
                report.AppendLine();
                report.AppendLine($"  {violation.Command}  [{violation.Scenario}]");
                foreach (var path in violation.ChangedPaths.Take(12))
                    report.AppendLine($"      {path}");
                if (violation.ChangedPaths.Count > 12)
                    report.AppendLine($"      ... and {violation.ChangedPaths.Count - 12} more");
            }

            Assert.Fail(report.ToString());
        }
    }

    /// <summary>Self-check: the fingerprint must actually notice an ordinary edit. Without this the
    /// audit above could pass by comparing two identically-blind snapshots.</summary>
    [Fact]
    public void Fingerprint_DetectsAnOrdinaryCellEdit()
    {
        var (workbook, bank) = ProtectedSheetScenario().Build();
        var sheet = workbook.Sheets[0];
        var before = WorkbookFingerprint.Capture(workbook);

        sheet.SetCell(new CellAddress(sheet.Id, 40, 40), new TextValue("audit-probe"));

        var after = WorkbookFingerprint.Capture(workbook);
        after.Should().NotBe(before);
        WorkbookFingerprint.Diff(before, after).Should().NotBeEmpty();
        _ = bank;
    }

    /// <summary>
    /// Self-check for the fingerprint's most important blind spot. Sheet keeps its cells in a
    /// PRIVATE dictionary exposed only through methods (EnumerateCells/GetUsedCells), never a
    /// property -- so a plain property/field reflection walk cannot see cell contents at all, and
    /// an in-place value change (which moves neither CellCount nor any other public property) would
    /// be completely invisible. That is the single most common mutation in the product, so the
    /// fingerprint special-cases Sheet cells; this test is what keeps that special case honest.
    /// </summary>
    [Fact]
    public void Fingerprint_DetectsAnInPlaceCellValueChange()
    {
        var (workbook, _) = PlainScenario().Build();
        var sheet = workbook.Sheets[0];
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.GetValue(address).Should().Be(new TextValue("r2c2"), "the fixture seeds this cell");
        var countBefore = sheet.CellCount;
        var before = WorkbookFingerprint.Capture(workbook);

        // Overwrite an EXISTING cell: the cell count does not move, so only a fingerprint that
        // actually reads cell contents can notice.
        sheet.SetCell(address, new TextValue("overwritten"));
        sheet.CellCount.Should().Be(countBefore, "this must be an in-place change, not an insert");

        var after = WorkbookFingerprint.Capture(workbook);
        var diff = WorkbookFingerprint.Diff(before, after);
        diff.Should().NotBeEmpty("an in-place cell value change must be visible to the audit");

        // Specifically through the cell contents, not incidentally through Sheet.ContentVersion.
        // Before the fingerprint read cells explicitly, that counter was the ONLY thing that moved
        // here -- which would have left the audit blind to any mutation path that does not happen
        // to bump it. Requiring a real cell path keeps the coverage where it belongs.
        diff.Should().Contain(line => line.Contains(".<cells>[", StringComparison.Ordinal),
            "the diff must name the changed cell, not just a version counter");
        diff.Should().Contain(line => line.Contains("overwritten", StringComparison.Ordinal),
            "the new cell value itself must appear in the fingerprint");
    }

    /// <summary>Self-check: the fingerprint must see a mutation made through a HELPER call chain,
    /// not just a direct SetCell -- that is the entire reason this harness exists.</summary>
    [Fact]
    public void Fingerprint_DetectsAMutationMadeThroughAHelperCallChain()
    {
        var (workbook, _) = PlainScenario().Build();
        var sheet = workbook.Sheets[0];
        var ctx = new TestCommandContext(workbook);
        var before = WorkbookFingerprint.Capture(workbook);

        // EditCellsCommand mutates via its own internal loop AND via StructuredTableEditEffects /
        // DataTableAutoRefreshEffects -- exactly the helper-mediated shape the static scan could
        // not follow.
        EditCellsCommand
            .ForValue(sheet.Id, new CellAddress(sheet.Id, 2, 2), new TextValue("via-helper"))
            .Apply(ctx)
            .Success.Should().BeTrue();

        var after = WorkbookFingerprint.Capture(workbook);
        WorkbookFingerprint.Diff(before, after).Should().NotBeEmpty();
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────

    private sealed record Scenario(string Name, Func<(Workbook Workbook, ArgumentBank Bank)> Build);

    private static IEnumerable<Scenario> Scenarios()
    {
        yield return ProtectedSheetScenario();
        yield return PlainScenario();
        yield return MissingTargetScenario();
    }

    /// <summary>Protection is by far the largest failure family (the CommandGuards.Reject* sites),
    /// so a protected sheet drives the most failure outcomes per run.</summary>
    private static Scenario ProtectedSheetScenario() => new("protected sheet", () =>
    {
        var (workbook, bank) = BuildFixture();
        foreach (var sheet in workbook.Sheets)
            sheet.IsProtected = true;
        workbook.IsStructureProtected = true;
        return (workbook, bank);
    });

    /// <summary>An ordinary editable workbook: catches commands that reject on their own input
    /// validation rather than on protection.</summary>
    private static Scenario PlainScenario() => new("plain workbook", BuildFixture);

    /// <summary>Every object id points at nothing, driving the "target no longer exists" family --
    /// the other big source of failure outcomes.</summary>
    private static Scenario MissingTargetScenario() => new("missing targets", () =>
    {
        var (workbook, bank) = BuildFixture();
        bank.OverrideGuid(Guid.NewGuid());
        return (workbook, bank);
    });

    private static (Workbook Workbook, ArgumentBank Bank) BuildFixture()
    {
        var workbook = new Workbook("audit");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        for (uint row = 1; row <= 6; row++)
        {
            for (uint col = 1; col <= 4; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue($"r{row}c{col}"));
        }

        var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 2, 3), Width = 100, Height = 80 };
        sheet.Pictures.Add(picture);

        var bank = new ArgumentBank(workbook, sheet, picture.Id);
        return (workbook, bank);
    }

    // ── Argument synthesis ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Supplies a plausible value for each constructor parameter type so commands can be built
    /// generically. Anything it cannot produce makes the command "skipped" and reported, rather
    /// than silently dropped.
    /// </summary>
    private sealed class ArgumentBank(Workbook workbook, Sheet sheet, Guid objectId)
    {
        private Guid _guid = objectId;

        public void OverrideGuid(Guid value) => _guid = value;

        public bool TryGet(Type type, out object? value)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying is not null)
            {
                // Prefer a real value over null: a null argument usually short-circuits the command
                // into a trivial no-op and exercises nothing.
                if (TryGet(underlying, out var inner))
                {
                    value = inner;
                    return true;
                }

                value = null;
                return true;
            }

            if (type == typeof(SheetId)) { value = sheet.Id; return true; }
            if (type == typeof(WorkbookId)) { value = workbook.Id; return true; }
            if (type == typeof(CellAddress)) { value = new CellAddress(sheet.Id, 2, 2); return true; }
            if (type == typeof(GridRange))
            {
                value = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
                return true;
            }

            if (type == typeof(Guid)) { value = _guid; return true; }
            if (type == typeof(string)) { value = "Audit"; return true; }
            if (type == typeof(bool)) { value = true; return true; }
            if (type == typeof(int)) { value = 1; return true; }
            if (type == typeof(uint)) { value = 1u; return true; }
            if (type == typeof(long)) { value = 1L; return true; }
            if (type == typeof(double)) { value = 1d; return true; }
            if (type == typeof(float)) { value = 1f; return true; }
            if (type == typeof(decimal)) { value = 1m; return true; }
            if (type == typeof(byte)) { value = (byte)1; return true; }
            if (type == typeof(byte[])) { value = new byte[] { 1, 2, 3, 4 }; return true; }

            if (type.IsEnum)
            {
                var values = Enum.GetValues(type);
                if (values.Length > 0) { value = values.GetValue(0); return true; }
                value = null;
                return false;
            }

            if (type == typeof(ScalarValue) || type.IsSubclassOf(typeof(ScalarValue)))
            {
                value = new TextValue("Audit");
                return type.IsInstanceOfType(value);
            }

            if (type == typeof(Cell)) { value = Cell.FromValue(new TextValue("Audit")); return true; }

            // Collections: an EMPTY collection is deliberate. A synthesized element would be a
            // guess at the command's domain shape, and a command handed an empty batch either
            // no-ops (harmlessly skipped, it reports success) or rejects -- and a rejection is
            // precisely the failure outcome this audit wants to inspect.
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                var element = type.GetGenericArguments()[0];
                if (definition == typeof(IReadOnlyList<>) || definition == typeof(IList<>)
                    || definition == typeof(List<>) || definition == typeof(IEnumerable<>)
                    || definition == typeof(IReadOnlyCollection<>) || definition == typeof(ICollection<>))
                {
                    value = Activator.CreateInstance(typeof(List<>).MakeGenericType(element));
                    return true;
                }
            }

            if (type.IsArray && type.GetElementType() is { } arrayElement)
            {
                value = Array.CreateInstance(arrayElement, 0);
                return true;
            }

            // Last resort for the model's own domain types (ConditionalFormat, StyleDiff, CellColor,
            // DataValidation, WorksheetPageMargins...): build one recursively from whichever
            // constructor we can satisfy. Without this, every command taking such a parameter was
            // skipped -- 22 of them, including whole families (page setup, styles, validation,
            // colour filters) whose failure paths would then never be audited at all.
            return TryConstructDomainType(type, out value, 0);
        }

        private bool TryConstructDomainType(Type type, out object? value, int depth)
        {
            value = null;
            if (depth > 3 || type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                return false;

            // Never reflect a constructor on these. A delegate's real constructor is
            // (object, IntPtr); invoking it reflectively with synthesized arguments is an
            // UNCATCHABLE "Internal CLR error (0x80131506)" that kills the test host outright --
            // it took the whole run down before this guard existed, and no try/catch can save it.
            // Pointer/ByRef types are excluded for the same "cannot meaningfully instantiate"
            // reason, and reflection types because building one is never what a command wants.
            if (typeof(Delegate).IsAssignableFrom(type)
                || type.IsPointer || type.IsByRef
                || typeof(Type).IsAssignableFrom(type)
                || typeof(MemberInfo).IsAssignableFrom(type)
                || typeof(Assembly).IsAssignableFrom(type))
            {
                return false;
            }

            // Narrowest constructor first here (the opposite of command construction): the goal is
            // merely to obtain a valid instance, and each extra parameter is another chance to fail.
            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(c => c.GetParameters().Length))
            {
                var parameters = constructor.GetParameters();
                var args = new object?[parameters.Length];
                var ok = true;

                for (var i = 0; i < parameters.Length; i++)
                {
                    if (TryGetShallow(parameters[i].ParameterType, out var inner, depth))
                        args[i] = inner;
                    else if (parameters[i].HasDefaultValue)
                        args[i] = parameters[i].DefaultValue;
                    else { ok = false; break; }
                }

                if (!ok)
                    continue;

                try
                {
                    value = constructor.Invoke(args);
                    return true;
                }
                catch
                {
                    // This constructor validates its arguments and did not like ours; try the next.
                }
            }

            // A parameterless value type is always constructible even with no public constructor.
            if (type.IsValueType)
            {
                value = Activator.CreateInstance(type);
                return true;
            }

            return false;
        }

        /// <summary>TryGet, but routing nested domain types through the depth-limited recursion so a
        /// self-referential model type cannot spin forever.</summary>
        private bool TryGetShallow(Type type, out object? value, int depth)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying is not null)
            {
                value = null;
                return true;
            }

            if (IsDirectlyKnown(type))
                return TryGet(type, out value);

            return TryConstructDomainType(type, out value, depth + 1);
        }

        private static bool IsDirectlyKnown(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(Guid) || type == typeof(byte[]) || type.IsArray || type.IsGenericType
            || type == typeof(SheetId) || type == typeof(WorkbookId) || type == typeof(CellAddress)
            || type == typeof(GridRange) || type == typeof(Cell) || type == typeof(ScalarValue);
    }

    private static bool TryConstruct(Type type, ArgumentBank bank, out IWorkbookCommand? command, out string reason)
    {
        command = null;
        reason = "no usable constructor";

        // Widest constructor first: it usually exercises the most of the command's behaviour, and a
        // narrower overload is the fallback when a wide one needs a domain type we cannot build.
        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                     .OrderByDescending(c => c.GetParameters().Length))
        {
            var parameters = constructor.GetParameters();
            var args = new object?[parameters.Length];
            var ok = true;

            for (var i = 0; i < parameters.Length; i++)
            {
                if (bank.TryGet(parameters[i].ParameterType, out var value))
                {
                    args[i] = value;
                }
                else if (parameters[i].HasDefaultValue)
                {
                    args[i] = parameters[i].DefaultValue;
                }
                else
                {
                    ok = false;
                    reason = $"cannot synthesize {parameters[i].ParameterType.Name}";
                    break;
                }
            }

            if (!ok)
                continue;

            try
            {
                command = (IWorkbookCommand)constructor.Invoke(args);
                return true;
            }
            catch (Exception ex)
            {
                // A constructor that validates its arguments and rejects our synthesized ones is a
                // skip, not a failure -- record why so the skip list stays reviewable.
                reason = $"constructor threw {(ex.InnerException ?? ex).GetType().Name}";
            }
        }

        return false;
    }
}
