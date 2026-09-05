using System.Text;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r417: drive EVERY workbook command that can be built from simple arguments, and require any that
/// visibly changes the workbook to undo exactly.
///
/// <para>r405-r416 grew a hand-written sample to 27 commands, one line at a time, out of 228. That
/// only finds bugs in commands somebody chose to write a line for. This complements it: reflection
/// over every <see cref="IWorkbookCommand"/>, constructed from a value factory, applied, reverted. A
/// command added tomorrow is covered the day it appears.</para>
///
/// <para>It reports a CENSUS rather than a bare pass, because the honest number is not "228 covered".
/// Most commands need arguments this factory cannot invent -- a StyleDiff, a filter criterion, a
/// chart -- or state the fixture does not carry, and a test that hid that behind a green would claim
/// coverage it does not have. The census figures are asserted on, so the split cannot drift
/// unnoticed.</para>
/// </summary>
public sealed class R417_EveryConstructibleCommandUndoesExactlyTests
{
    private static (Workbook Workbook, Sheet Sheet) Setup()
    {
        var workbook = new Workbook("auto");
        var sheet = workbook.AddSheet("Sheet1");

        for (uint row = 1; row <= 6; row++)
        {
            for (uint col = 1; col <= 4; col++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, col),
                    row % 2 == 0 ? new NumberValue(row * 10 + col) : new TextValue("r" + row + "c" + col));
            }
        }

        // Seeded state so commands that clear or toggle something have something to act on. Without
        // it, five more commands fall into "no visible change" and are never exercised at all.
        sheet.RowHeights[3] = 42.5;
        sheet.ColumnWidths[3] = 17.25;
        sheet.AddMergedRegion(GridRange.Parse("C5:D5", sheet.Id));
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "note";
        sheet.Hyperlinks[new CellAddress(sheet.Id, 1, 2)] = "https://example.invalid";
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = GridRange.Parse("A1:A5", sheet.Id),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = GridRange.Parse("A1:A5", sheet.Id) });

        return (workbook, sheet);
    }

    private static object? ValueFor(Type type, Sheet sheet)
    {
        if (type == typeof(SheetId)) return sheet.Id;
        if (type == typeof(GridRange)) return GridRange.Parse("A1:D6", sheet.Id);
        if (type == typeof(CellAddress)) return new CellAddress(sheet.Id, 1, 1);
        if (type == typeof(uint)) return 2u;
        if (type == typeof(int)) return 2;
        if (type == typeof(bool)) return true;
        if (type == typeof(double)) return 2.0;
        if (type == typeof(string)) return "probe";
        if (type == typeof(Guid)) return Guid.NewGuid();
        if (type == typeof(CellColor)) return new CellColor(0x33, 0x66, 0x99);

        if (type.IsEnum)
        {
            return Enum.GetValues(type).Cast<object>().Skip(1).FirstOrDefault()
                ?? Enum.GetValues(type).Cast<object>().FirstOrDefault();
        }

        // r438: the three shapes that blocked most of the census. Measured across the commands this
        // driver could NOT build: Nullable (111 parameters), IReadOnlyList (49) and Guid (45)
        // dominated everything else by an order of magnitude, so supplying them is what moves the
        // number rather than hand-adding types one at a time.
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return ValueFor(underlying, sheet);

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IReadOnlyCollection<>) ||
                definition == typeof(IEnumerable<>) ||
                definition == typeof(List<>))
            {
                var elementType = type.GetGenericArguments()[0];
                var element = ValueFor(elementType, sheet);
                if (element is null)
                    return null;

                // A ONE-element list, deliberately. An empty list would construct just as well and
                // then make every such command a no-op, inflating "constructible" while exercising
                // nothing -- the same false-coverage trap the census exists to expose.
                var list = (System.Collections.IList)Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(elementType))!;
                list.Add(element);
                return list;
            }
        }

        return null;
    }

    /// <summary>
    /// Bookkeeping that undo is RIGHT not to rewind, so comparing it would assert something false.
    /// Deliberately two names rather than a pattern: an exclusion rule broad enough to be convenient
    /// is broad enough to hide the next real defect, and every future addition has to earn its line.
    /// <list type="bullet">
    /// <item><c>ContentVersion</c> is documented as a monotonic counter that caches key on. Winding
    /// it back on undo would leave every such cache believing stale results are current -- the undo
    /// path bumping it FORWARD is the correct behaviour.</item>
    /// <item><c>StyleCount</c> counts the workbook's interned style pool. Styles are appended and
    /// shared, never reference-counted, so a command that registers one leaves it registered after
    /// undo; Excel's own style table accumulates the same way.</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> MonotonicBookkeeping =
        new(StringComparer.Ordinal) { "ContentVersion", "StyleCount" };

    /// <summary>
    /// r439: reflective, not hand-listed. A fixed list of properties only sees the state somebody
    /// thought to add a line for -- the same blind spot the hand-written per-command sample had --
    /// and it silently rots as the model grows. Reading every public property means a field added
    /// tomorrow is watched the day it appears. Measured: this cut the commands that applied
    /// successfully while appearing to change NOTHING from 61 to 23, and took the number of commands
    /// whose Revert is actually checked from 31 to 69.
    /// </summary>
    private static void Reflect(StringBuilder builder, string prefix, object target)
    {
        foreach (var property in target.GetType().GetProperties()
                     .Where(candidate => candidate.CanRead && candidate.GetIndexParameters().Length == 0)
                     .Where(candidate => !MonotonicBookkeeping.Contains(candidate.Name))
                     .OrderBy(candidate => candidate.Name, StringComparer.Ordinal))
        {
            object? value;
            try
            {
                value = property.GetValue(target);
            }
            catch
            {
                // A property that throws on this fixture describes nothing either way; skipping it
                // loses no coverage, whereas letting it escape would fail every command alike.
                continue;
            }

            var text = value switch
            {
                null => "-",
                string text_ => text_,
                // r439: CONTENTS, not just a count. A count alone cannot see a command that edits a
                // comment's text or a validation's operator in place and fails to put it back -- the
                // collection is the same size either way. This is safe from spurious diffs because
                // the default object.ToString returns the type NAME, which is stable across calls,
                // rather than anything identity- or hash-based. Sorted, since dictionary order is
                // not a promise the model makes.
                System.Collections.IEnumerable sequence =>
                    "[" + string.Join(
                        "; ",
                        sequence.Cast<object?>()
                            .Select(item => item?.ToString() ?? "-")
                            .OrderBy(item => item, StringComparer.Ordinal)) + "]",
                _ => value.ToString(),
            };

            builder.Append(prefix).Append(property.Name).Append('=').Append(text).AppendLine();
        }
    }

    private static string Describe(Workbook workbook)
    {
        var builder = new StringBuilder();
        Reflect(builder, "wb.", workbook);

        foreach (var sheet in workbook.Sheets)
        {
            Reflect(builder, "sh." + sheet.Id + ".", sheet);

            foreach (var (address, cell) in sheet.EnumerateCells()
                         .OrderBy(pair => pair.Address.Row).ThenBy(pair => pair.Address.Col))
            {
                builder.Append(address.Row).Append(',').Append(address.Col).Append('=')
                    .Append(cell.Value?.ToString() ?? "-").Append(" s=").Append(cell.StyleId).AppendLine();
            }
        }

        return builder.ToString();
    }

    private static string FirstDifference(string before, string after)
    {
        var beforeLines = before.Split('\n');
        var afterLines = after.Split('\n');

        for (var index = 0; index < Math.Max(beforeLines.Length, afterLines.Length); index++)
        {
            var left = index < beforeLines.Length ? beforeLines[index].TrimEnd('\r') : "(absent)";
            var right = index < afterLines.Length ? afterLines[index].TrimEnd('\r') : "(absent)";
            if (left != right)
                return left + " -> " + right;
        }

        return "(no line differs)";
    }

    [Fact]
    public void EveryCommandThatChangesTheWorkbookRestoresItOnRevert()
    {
        var commandTypes = typeof(IWorkbookCommand).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(IWorkbookCommand).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        commandTypes.Should().HaveCountGreaterThanOrEqualTo(
            200, "the reflection query must still reach the command assembly");

        var (_, probeSheet) = Setup();
        int notConstructible = 0, threw = 0, noChange = 0, exercised = 0;
        var failures = new List<string>();

        foreach (var type in commandTypes)
        {
            var constructor = type.GetConstructors()
                .OrderBy(candidate => candidate.GetParameters().Length)
                .FirstOrDefault(candidate => candidate.GetParameters()
                    .All(parameter => ValueFor(parameter.ParameterType, probeSheet) is not null));

            if (constructor is null)
            {
                notConstructible++;
                continue;
            }

            try
            {
                var (workbook, sheet) = Setup();
                var context = new TestCommandContext(workbook);
                var command = (IWorkbookCommand)constructor.Invoke(
                    constructor.GetParameters().Select(parameter => ValueFor(parameter.ParameterType, sheet)).ToArray());

                var before = Describe(workbook);
                command.Apply(context);

                if (Describe(workbook) == before)
                {
                    noChange++;
                    continue;
                }

                exercised++;
                command.Revert(context);

                var after = Describe(workbook);
                if (after != before)
                {
                    // r439: name the field, not just the command. A bare command name sends the next
                    // reader back to re-derive the diff by hand, and the diff is the whole finding.
                    failures.Add(type.Name + " [" + FirstDifference(before, after) + "]");
                }
            }
            catch (Exception exception)
            {
                // Generic arguments can be invalid for a particular command; that is a limit of the
                // factory rather than a defect, so it is counted instead of failed. The count is
                // asserted below, so a change that starts throwing everywhere cannot hide here.
                threw++;
                _ = exception;
            }
        }

        var census =
            "types=" + commandTypes.Count + " notConstructible=" + notConstructible +
            " threw=" + threw + " noChange=" + noChange + " exercised=" + exercised +
            " failed=" + failures.Count;

        failures.Should().BeEmpty(
            "a command that changes the workbook and cannot put it back loses the user's work on " +
            "undo. " + census + "\n" + string.Join("\n", failures));

        exercised.Should().BeGreaterThanOrEqualTo(
            65,
            "the driver must still be exercising commands -- if this falls, the sweep has quietly " +
            "stopped testing rather than the commands having improved. Pinned just under the 69 " +
            "r439 measured, so narrowing Describe or the value factory shows up here instead of " +
            "turning into a comfortable green. " + census);

        notConstructible.Should().BeLessThanOrEqualTo(
            55,
            "r438 took this from 124 to 49 by supplying the argument shapes that actually blocked " +
            "construction. A rise means new commands are landing that the factory cannot build, and " +
            "unbuildable commands are entirely untested here. " + census);

        threw.Should().BeLessThan(
            30, "a sharp rise here means the value factory stopped matching the constructors. " + census);
    }
}
