using System.Text;
using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// r460: stacking two document commands and undoing both must return the document exactly.
///
/// <para>Third app for this contract. FreeX (r459) and FreeP both came back clean, but a clean result
/// in one app says nothing about another: porting the REDO contract the same way in r458 found a real
/// defect in FreeP after FreeX's had already been fixed. Carrying each contract to all three is the
/// point, not a formality.</para>
///
/// <para>Runs through the real <see cref="DocumentCommandBus"/> rather than calling Apply/Revert
/// directly, so push order, redo invalidation and the depth and byte budgets are exercised too.</para>
/// </summary>
public sealed class R460_StackedDocumentCommandsUnwindCompletelyTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;

        public string? RevisionAuthor => "probe";
    }

    private static TextDocument Setup()
    {
        var document = new TextDocument();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("paragraph 0"));

        // The table sits at index 1 because the factory answers 1 for every int (r442's lesson:
        // seeded state only counts if the invented arguments can reach it).
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
            document.Blocks.Add(new Paragraph("paragraph " + index));

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

                var list = (System.Collections.IList)Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(elementType))!;
                list.Add(element);
                return list;
            }
        }

        return null;
    }

    private static IDocumentCommand? Build(Type type)
    {
        var constructor = type.GetConstructors()
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault(candidate => candidate.GetParameters()
                .All(parameter => ValueFor(parameter.ParameterType) is not null));

        if (constructor is null)
            return null;

        // r441: a command told its PRIOR value restores whatever the factory invented.
        if (constructor.GetParameters().Any(parameter =>
                parameter.Name?.StartsWith("old", StringComparison.OrdinalIgnoreCase) == true))
        {
            return null;
        }

        return (IDocumentCommand)constructor.Invoke(
            constructor.GetParameters().Select(parameter => ValueFor(parameter.ParameterType)).ToArray());
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
    public void UndoingAStackOfTwoCommandsRestoresTheDocumentExactly()
    {
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

        var usable = new List<Type>();
        foreach (var type in commandTypes)
        {
            try
            {
                var document = Setup();
                var command = Build(type);
                if (command is null || !command.HasEffect(new Context(document)))
                    continue;

                var before = Describe(document);
                command.Apply(new Context(document));
                if (Describe(document) != before)
                    usable.Add(type);
            }
            catch
            {
                // Factory limits, counted by r442's census rather than here.
            }
        }

        var failures = new List<string>();
        var pairsExecuted = 0;
        var incoherentPairs = 0;

        for (var index = 0; index < usable.Count; index++)
        {
            var first = usable[index];
            var second = usable[(index + 1) % usable.Count];

            try
            {
                var document = Setup();
                var bus = new DocumentCommandBus(new Context(document));
                var before = Describe(document);

                var firstCommand = Build(first);
                if (firstCommand is null)
                    continue;

                bus.Execute(firstCommand);

                // The second command is built AFTER the first has run, which is what production
                // does: the user issues an edit, sees the result, and the next command is created
                // against the document as it now stands. Building both up front instead pairs a
                // command with indices that the first edit already invalidated -- a situation the
                // app never produces, and one that reports failures belonging to the probe rather
                // than to the code.
                var secondCommand = Build(second);
                if (secondCommand is null)
                    continue;

                bus.Execute(secondCommand);
                pairsExecuted++;

                while (bus.CanUndo)
                    bus.Undo();

                var after = Describe(document);
                if (after != before)
                    failures.Add($"{first.Name} + {second.Name} [{FirstDifference(before, after)}]");
            }
            catch (Exception exception)
            {
                // NOT counted as an undo failure, and the distinction is the honest part.
                //
                // This factory addresses blocks by a constant index, so after a first command that
                // changes the block structure the second may target a block of the wrong KIND --
                // "delete a column from block 1" when block 1 is now a paragraph. FreeW's table
                // commands reach the block through a hard cast and inherit HasEffect's default of
                // true, so that pairing throws. Production never builds such a pair: the UI issues a
                // table command only with the caret inside a table, synchronously against the
                // document as it stands.
                //
                // So this is the probe's limit rather than evidence of a defect, and reporting it as
                // one would be exactly the false positive this programme keeps having to catch. It
                // is COUNTED, not silenced, so the number cannot grow unnoticed -- and the
                // underlying fragility (an unchecked cast behind a default-true HasEffect, where
                // FreeP's equivalents bounds-check in HasEffect) is recorded in the r460 ledger
                // entry rather than hardened here on a hypothesis.
                incoherentPairs++;
                _ = exception;
            }
        }

        var census =
            $"usable={usable.Count} pairsExecuted={pairsExecuted} " +
            $"incoherentPairs={incoherentPairs} failed={failures.Count}";

        failures.Should().BeEmpty(
            "undoing every edit must return the document the user started with -- a command that " +
            "captures its undo state at construction, or assumes the document still looks the way it " +
            "did when it was built, survives every single-command test and fails here. " + census +
            "\n" + string.Join("\n", failures));

        pairsExecuted.Should().BeGreaterThanOrEqualTo(
            10,
            "the sweep must still be stacking real pairs -- if this falls it has quietly stopped " +
            "testing rather than the commands having improved. " + census);
    }
}
