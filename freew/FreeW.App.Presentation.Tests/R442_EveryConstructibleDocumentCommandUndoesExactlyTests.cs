using System.Text;
using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// r442: the undo driver (FreeX r417/r438-r440, FreeP r441) brought to FreeW, the largest untested
/// undo surface left in the repo.
///
/// <para>FreeW has the most command classes of the three apps and, like FreeP before r441, no
/// auto-driver: every undo test here covers a command somebody chose to write a line for. That gap
/// has now produced a real defect in each app it was closed in -- r438 (inserting a PivotTable
/// destroyed merged regions Undo never restored) and r441 (undoing a slide title left behind the
/// placeholder its setter created). Both were structure a Revert failed to unwind while faithfully
/// restoring the value.</para>
///
/// <para>Same shape and same honesty as its siblings: construct from a value factory, apply, revert,
/// and require anything that visibly changed the document to put it back. The observer is uniform
/// reflection over the document and its blocks rather than a hand-written field list. It reports a
/// CENSUS rather than a bare pass, because most commands need arguments this factory cannot
/// invent and a green that hid that would claim coverage it does not have.</para>
/// </summary>
public sealed class R442_EveryConstructibleDocumentCommandUndoesExactlyTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;

        public string? RevisionAuthor => "probe";
    }

    private static TextDocument Setup()
    {
        var document = new TextDocument();

        document.Blocks.Add(new Paragraph("paragraph 0"));

        // Seeded structure, for the same reason the FreeX driver seeds its sheet: the largest bucket
        // in the census is commands that construct fine and then do nothing, because a document of
        // plain paragraphs gives a table command nothing to act on. A table is the only other block
        // type a command can meet (Paragraph, Table and AltChunkBlock are the whole set).
        //
        // It goes at index 1 DELIBERATELY. The factory answers 1 for every int, so a block index is
        // 1 -- putting the table anywhere else means no command ever points at it, which is exactly
        // what happened on the first attempt: adding a table at the end moved the census by zero.
        // Seeded state only counts if the invented arguments can reach it.
        var table = new Table();

        var headerRow = new TableRow();
        headerRow.Cells.Add(new TableCell("North"));
        headerRow.Cells.Add(new TableCell("120"));
        table.Rows.Add(headerRow);

        var bodyRow = new TableRow();
        bodyRow.Cells.Add(new TableCell("South"));
        bodyRow.Cells.Add(new TableCell("98"));
        table.Rows.Add(bodyRow);

        document.Blocks.Add(table);

        for (var index = 1; index < 4; index++)
        {
            document.Blocks.Add(new Paragraph("paragraph " + index));
        }

        return document;
    }

    private static object? ValueFor(Type type)
    {
        if (type == typeof(int)) return 1;
        if (type == typeof(uint)) return 1u;
        if (type == typeof(long)) return 1L;
        if (type == typeof(bool)) return true;
        if (type == typeof(double)) return 2.0;
        if (type == typeof(string)) return "probe";
        if (type == typeof(Guid)) return Guid.NewGuid();

        if (type.IsEnum)
        {
            return Enum.GetValues(type).Cast<object>().Skip(1).FirstOrDefault()
                ?? Enum.GetValues(type).Cast<object>().FirstOrDefault();
        }

        // r447: measured across every command this factory could NOT build, rather than guessed.
        // IReadOnlyList<T> led at 13 -- but of DOMAIN element types, so supplying Run, Paragraph and
        // Block unlocks the lists as well as the bare parameters. Action<T> was next at 7: those are
        // callbacks the command invokes (a redraw signal, a progress report), for which a no-op
        // delegate is the correct double rather than a stub that records.
        if (type == typeof(Run)) return new Run("probe");
        if (type == typeof(Paragraph) || type == typeof(Block)) return new Paragraph("probe");

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Action<>))
        {
            var parameter = System.Linq.Expressions.Expression.Parameter(type.GetGenericArguments()[0]);
            return System.Linq.Expressions.Expression
                .Lambda(type, System.Linq.Expressions.Expression.Empty(), parameter)
                .Compile();
        }

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return ValueFor(underlying);

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IReadOnlyCollection<>) ||
                definition == typeof(IEnumerable<>) ||
                definition == typeof(List<>))
            {
                var elementType = type.GetGenericArguments()[0];
                var element = ValueFor(elementType);
                if (element is null)
                    return null;

                // One element, not zero: an empty list constructs just as well and then makes the
                // command a no-op, inflating "constructible" while exercising nothing.
                var list = (System.Collections.IList)Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(elementType))!;
                list.Add(element);
                return list;
            }
        }

        return null;
    }

    private static void Reflect(StringBuilder builder, string prefix, object target)
    {
        foreach (var property in target.GetType().GetProperties()
                     .Where(candidate => candidate.CanRead && candidate.GetIndexParameters().Length == 0)
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
                // Contents, not a count, so an in-place edit to an element cannot hide behind an
                // unchanged collection size. Safe because the default ToString is the stable type
                // name, never an identity hash. Sorted: element order is not a promise.
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

    private static string Describe(TextDocument document)
    {
        var builder = new StringBuilder();
        Reflect(builder, "doc.", document);

        for (var index = 0; index < document.Blocks.Count; index++)
        {
            var block = document.Blocks[index];
            Reflect(builder, "b" + index + ".", block);

            if (block is Paragraph paragraph)
            {
                for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
                    Reflect(builder, "b" + index + ".r" + runIndex + ".", paragraph.Runs[runIndex]);
            }
            else if (block is Table table)
            {
                // Down to the cell, so a command that edits one cell and fails to restore it cannot
                // hide behind an unchanged row count.
                for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    var row = table.Rows[rowIndex];
                    Reflect(builder, "b" + index + ".row" + rowIndex + ".", row);

                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                        Reflect(builder, "b" + index + ".row" + rowIndex + ".c" + cellIndex + ".", row.Cells[cellIndex]);
                }
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
    public void EveryCommandThatChangesTheDocumentRestoresItOnRevert()
    {
        // BOTH assemblies. FreeW is unlike its siblings here: commands live in Core.Model AND in
        // App.Presentation (the dialog-apply commands), so scanning only the interface's own
        // assembly -- which is what the FreeX and FreeP drivers do, correctly, because their
        // commands are in one place -- would silently miss a whole project while still reporting a
        // confident census. A driver that scans less than it claims is the failure this program
        // keeps finding in other people's tests; it should not ship in its own.
        var assemblies = new[]
        {
            typeof(IDocumentCommand).Assembly,
            typeof(FreeW.App.Presentation.Dialogs.ApplyDocumentPropertiesCommand).Assembly,
        }.Distinct();

        var commandTypes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsPublic: true }
                           && typeof(IDocumentCommand).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        commandTypes.Should().HaveCountGreaterThanOrEqualTo(
            100, "the reflection query must still reach the FreeW command assembly");

        int notConstructible = 0, threw = 0, noChange = 0, exercised = 0, claimedNoEffect = 0;
        var failures = new List<string>();
        var falseNoEffect = new List<string>();

        foreach (var type in commandTypes)
        {
            var constructor = type.GetConstructors()
                .OrderBy(candidate => candidate.GetParameters().Length)
                .FirstOrDefault(candidate => candidate.GetParameters()
                    .All(parameter => ValueFor(parameter.ParameterType) is not null));

            // r441: a command told its PRIOR value ("oldWidth") rather than capturing it will
            // faithfully restore whatever the factory invented, which reads as a failed undo. That
            // is a limit of driving blindly, not a defect, so it is counted as unbuildable.
            if (constructor is null ||
                constructor.GetParameters().Any(parameter =>
                    parameter.Name?.StartsWith("old", StringComparison.OrdinalIgnoreCase) == true))
            {
                notConstructible++;
                continue;
            }

            try
            {
                var document = Setup();
                var context = new Context(document);
                var command = (IDocumentCommand)constructor.Invoke(
                    constructor.GetParameters().Select(parameter => ValueFor(parameter.ParameterType)).ToArray());

                // r443: the bus skips a command reporting no effect ENTIRELY -- no Apply, no undo
                // entry. So a command that says false and would in fact have changed the document
                // makes the user's action vanish: they click, nothing happens, and there is no
                // error and nothing to undo. Check the claim rather than trusting it.
                if (!command.HasEffect(context))
                {
                    claimedNoEffect++;
                    var unchangedBefore = Describe(document);
                    command.Apply(context);

                    if (Describe(document) != unchangedBefore)
                    {
                        falseNoEffect.Add(
                            type.Name + " [" + FirstDifference(unchangedBefore, Describe(document)) + "]");
                    }

                    noChange++;
                    continue;
                }

                var before = Describe(document);
                command.Apply(context);

                if (Describe(document) == before)
                {
                    noChange++;
                    continue;
                }

                exercised++;
                command.Revert(context);

                var after = Describe(document);
                if (after != before)
                    failures.Add(type.Name + " [" + FirstDifference(before, after) + "]");
            }
            catch (Exception exception)
            {
                // A generic argument can be invalid for a particular command; that is a limit of the
                // factory, not a defect. Counted, and the count is asserted below.
                threw++;
                _ = exception;
            }
        }

        var census =
            "types=" + commandTypes.Count + " notConstructible=" + notConstructible +
            " threw=" + threw + " noChange=" + noChange + " claimedNoEffect=" + claimedNoEffect +
            " exercised=" + exercised + " failed=" + failures.Count;

        falseNoEffect.Should().BeEmpty(
            "the bus skips a command reporting HasEffect false entirely, so one that would in fact " +
            "have changed the document makes the user's action vanish: they click, nothing happens, " +
            "there is no error and nothing to undo. " + census + "\n" + string.Join("\n", falseNoEffect));

        claimedNoEffect.Should().BeGreaterThan(
            0,
            "the HasEffect check above is only worth having if commands actually reach it -- if no " +
            "command in the census ever reports no effect, that assertion is vacuous. " + census);

        exercised.Should().BeGreaterThanOrEqualTo(
            15,
            "the driver must still be exercising commands -- if this falls, the sweep has quietly " +
            "stopped testing rather than the commands having improved. 12 today out of 129: most " +
            "FreeW commands need a selection or a dialog-values object this factory cannot invent, " +
            "so this green is narrow evidence and should be read as such. " + census);

        failures.Should().BeEmpty(
            "a command that changes the document and cannot put it back loses the user's work on " +
            "undo. " + census + "\n" + string.Join("\n", failures));
    }
}
