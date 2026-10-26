using System.Text;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r459: stacking two commands and undoing both must return the workbook exactly.
///
/// <para>Every driver so far (r417, r441, r442, r457) applies ONE command to a fresh fixture. Real
/// users stack edits and unwind them in order, and that is a different contract: a command whose undo
/// state is captured at construction rather than at Apply, or which assumes the document still looks
/// the way it did when it was built, passes every single-command test and fails here.</para>
///
/// <para>It also runs through the real <see cref="CommandBus"/> rather than calling Apply/Revert
/// directly, so the stack's own bookkeeping -- push order, redo invalidation, the byte budget -- is
/// part of what is exercised.</para>
///
/// <para>Result on introduction: 66 pairs, none failing. That is worth keeping rather than deleting,
/// because it guards a contract nothing else covers; a test that finds nothing today is still the
/// thing that catches tomorrow's regression, provided it is known to be capable of failing (proven
/// below by the neuter recorded in the ledger).</para>
/// </summary>
public sealed class R459_StackedCommandsUnwindCompletelyTests
{
    private static (Workbook Workbook, Sheet Sheet) Setup()
    {
        var workbook = new Workbook("sequence");
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

        sheet.RowHeights[3] = 42.5;
        sheet.ColumnWidths[3] = 17.25;
        sheet.AddMergedRegion(GridRange.Parse("C5:D5", sheet.Id));
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "note";
        sheet.Hyperlinks[new CellAddress(sheet.Id, 1, 2)] = "https://example.invalid";

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

                var list = (System.Collections.IList)Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(elementType))!;
                list.Add(element);
                return list;
            }
        }

        return null;
    }

    private static System.Reflection.ConstructorInfo? UsableConstructor(Type type, Sheet sheet) =>
        type.GetConstructors()
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault(candidate => candidate.GetParameters()
                .All(parameter => ValueFor(parameter.ParameterType, sheet) is not null));

    private static IWorkbookCommand? Build(Type type, Sheet sheet)
    {
        var constructor = UsableConstructor(type, sheet);
        if (constructor is null)
            return null;

        return (IWorkbookCommand)constructor.Invoke(
            constructor.GetParameters().Select(parameter => ValueFor(parameter.ParameterType, sheet)).ToArray());
    }

    private static void Reflect(StringBuilder builder, string prefix, object target)
    {
        foreach (var property in target.GetType().GetProperties()
                     .Where(candidate => candidate.CanRead && candidate.GetIndexParameters().Length == 0)
                     // The same monotonic bookkeeping r439 excludes: undo is right not to rewind a
                     // cache counter or shrink an interned pool.
                     .Where(candidate => candidate.Name is not ("ContentVersion" or "StyleCount"))
                     .OrderBy(candidate => candidate.Name, StringComparer.Ordinal))
        {
            object? value;
            try
            {
                value = property.GetValue(target);
            }
            catch
            {
                continue;
            }

            var text = value switch
            {
                null => "-",
                string plain => plain,
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
            Reflect(builder, "sh.", sheet);

            foreach (var (address, cell) in sheet.EnumerateCells()
                         .OrderBy(pair => pair.Address.Row).ThenBy(pair => pair.Address.Col))
            {
                Reflect(builder, address.Row + "," + address.Col + ".", cell);
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
    public void UndoingAStackOfTwoCommandsRestoresTheWorkbookExactly()
    {
        var commandTypes = typeof(IWorkbookCommand).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(IWorkbookCommand).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        var (_, probeSheet) = Setup();

        // Only commands that individually change this fixture are worth stacking: a pair where
        // neither half does anything proves nothing about unwinding.
        var usable = new List<Type>();
        foreach (var type in commandTypes)
        {
            try
            {
                var (workbook, sheet) = Setup();
                var command = Build(type, sheet);
                if (command is null)
                    continue;

                var before = Describe(workbook);
                if (!command.Apply(new TestCommandContext(workbook)).Success)
                    continue;

                if (Describe(workbook) != before)
                    usable.Add(type);
            }
            catch
            {
                // Factory limits, counted by the r417 census rather than here.
            }
        }

        var failures = new List<string>();
        var pairsExecuted = 0;

        for (var index = 0; index < usable.Count; index++)
        {
            var first = usable[index];
            var second = usable[(index + 1) % usable.Count];

            try
            {
                var (workbook, sheet) = Setup();
                var bus = new CommandBus(_ => new TestCommandContext(workbook));
                var before = Describe(workbook);

                var firstCommand = Build(first, sheet);
                var secondCommand = Build(second, sheet);
                if (firstCommand is null || secondCommand is null)
                    continue;

                if (!bus.Execute(workbook.Id, firstCommand).Success)
                    continue;
                if (!bus.Execute(workbook.Id, secondCommand).Success)
                    continue;

                pairsExecuted++;

                while (bus.CanUndo(workbook.Id))
                    bus.Undo(workbook.Id);

                var after = Describe(workbook);
                if (after != before)
                    failures.Add($"{first.Name} + {second.Name} [{FirstDifference(before, after)}]");
            }
            catch (Exception exception)
            {
                failures.Add($"{first.Name} + {second.Name} [threw {exception.GetType().Name}]");
            }
        }

        var census = $"usable={usable.Count} pairsExecuted={pairsExecuted} failed={failures.Count}";

        failures.Should().BeEmpty(
            "undoing every edit must return the document the user started with -- a command that " +
            "captures its undo state at construction, or assumes the document still looks the way it " +
            "did when it was built, survives every single-command test and fails here. " + census +
            "\n" + string.Join("\n", failures));

        pairsExecuted.Should().BeGreaterThanOrEqualTo(
            60,
            "the sweep must still be stacking real pairs -- if this falls, it has quietly stopped " +
            "testing rather than the commands having improved. " + census);
    }
}
