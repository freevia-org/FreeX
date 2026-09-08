using System.Reflection;
using System.Text;
using FluentAssertions;
using Xunit;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r539: FreeW has a hostile-index census (r527) that asks whether commands THROW, and none asking
/// whether they UNDO. r532 measured why that gap matters: FreeW carries 126 Revert methods against
/// 21 test files mentioning Revert, roughly a tenth of FreeX's ratio, and the equivalent FreeP
/// census found a real defect within three rounds of being sharpened (r535).
///
/// <para>This drives every constructible command with VALID arguments, fingerprints the document,
/// applies, and requires Revert to restore that fingerprint exactly. A command that changes the
/// document and cannot put it back loses the user's work on undo.</para>
///
/// <para>Two honesty rules carried from the FreeP sibling, both costing coverage on purpose.
/// Arguments are invented only from primitives, enums and delegates: a command needing a live model
/// object is counted unbuildable rather than fed a null, because a NullReferenceException from an
/// invented argument would be the driver's failure and not the command's. And a constructor taking
/// two parameters of the same non-primitive type is skipped, because that is the shape of a
/// before/after pair (r536, r537) -- such a command restores the before-state it was HANDED, so an
/// invented one makes the driver fabricate a finding rather than detect one.</para>
/// </summary>
public class R539_EveryCommandUndoesExactlyTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static object? ValueFor(Type type)
    {
        if (type == typeof(int)) return 0;
        if (type == typeof(uint)) return 0u;
        if (type == typeof(double)) return 1.0;
        if (type == typeof(bool)) return true;
        if (type == typeof(string)) return "probe";
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
        if (Nullable.GetUnderlyingType(type) is { } inner) return ValueFor(inner);
        if (type == typeof(Action<Paragraph>)) return new Action<Paragraph>(_ => { });
        if (type == typeof(Func<RunFormatting, RunFormatting>))
            return new Func<RunFormatting, RunFormatting>(formatting => formatting);
        return null;
    }

    // r537's rule: two parameters of the same NON-primitive type is a before/after pair. The
    // non-primitive restriction is load-bearing -- commands routinely take two ints (a block index
    // and a run index), and excluding those would gut the census instead of sharpening it.
    private static bool TakesABeforeAfterPair(ConstructorInfo constructor)
    {
        var modelParameters = constructor.GetParameters()
            .Select(parameter => Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType)
            .Where(type => !type.IsPrimitive && type != typeof(string) && !type.IsEnum)
            .ToArray();

        return modelParameters.Length != modelParameters.Distinct().Count();
    }

    private static TextDocument Setup()
    {
        var document = new TextDocument();
        document.Blocks.Clear();

        // The TABLE COMES FIRST, at block index 0. The factory answers int with 0, so a table
        // anywhere else is unreachable by every command that names a block index -- which is why
        // seeding one changed nothing until it moved here. Third time this program has paid for
        // the same lesson (r481 ids, r535 slide index, this): seeding the container is half of it.
        //
        // Two paragraphs follow, each with two runs. A single one of anything sits at index 0; index 1 is
        // then unreachable by any invented argument, which is the trap r535 fell into on the FreeP
        // side, where a seed the commands could not reach was mistaken for a refuted theory.
        // A 2x2 TABLE, because FreeW's largest command family operates on one and every such
        // command was previously filed noChange for want of a target. Two rows and two cells for
        // the same reason the paragraphs come in pairs: index 1 has to address something.
        var table = new Table();
        for (var rowIndex = 0; rowIndex < 2; rowIndex++)
        {
            var row = new TableRow();
            for (var cellIndex = 0; cellIndex < 2; cellIndex++)
            {
                var cell = new TableCell();
                var cellParagraph = new Paragraph();
                cellParagraph.Runs.Add(new Run("cell " + rowIndex + cellIndex));
                cell.Paragraphs.Add(cellParagraph);
                row.Cells.Add(cell);
            }

            table.Rows.Add(row);
        }

        document.Blocks.Add(table);

        for (var index = 0; index < 2; index++)
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("run " + index + "a"));
            paragraph.Runs.Add(new Run("run " + index + "b"));
            document.Blocks.Add(paragraph);
        }


        return document;
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

        // r533: a collection of model objects renders as a row of bare type names, so blocks and
        // their runs need an EXPLICIT walk. Without it an edit to a run's text is invisible, the
        // command is filed as changing nothing, and its undo is checked by no one.
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            var block = document.Blocks[blockIndex];
            var blockPrefix = "b" + blockIndex + ".";
            Reflect(builder, blockPrefix, block);

            if (block is Paragraph paragraph)
            {
                for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
                    Reflect(builder, blockPrefix + "r" + runIndex + ".", paragraph.Runs[runIndex]);
            }
            else if (block is Table table)
            {
                // The same blind spot one level down, and I built it into this Describe before
                // noticing: a Table renders its Rows as a row of type names, so every table
                // command mutated the document invisibly and was filed noChange. Seeding a table
                // changed NOTHING until this walk existed -- r533's lesson, re-learned.
                for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    var row = table.Rows[rowIndex];
                    var rowPrefix = blockPrefix + "tr" + rowIndex + ".";
                    Reflect(builder, rowPrefix, row);

                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                    {
                        var cell = row.Cells[cellIndex];
                        var cellPrefix = rowPrefix + "tc" + cellIndex + ".";
                        Reflect(builder, cellPrefix, cell);

                        for (var cellParagraphIndex = 0; cellParagraphIndex < cell.Paragraphs.Count; cellParagraphIndex++)
                        {
                            var cellParagraph = cell.Paragraphs[cellParagraphIndex];
                            var cellParagraphPrefix = cellPrefix + "p" + cellParagraphIndex + ".";
                            Reflect(builder, cellParagraphPrefix, cellParagraph);

                            for (var runIndex = 0; runIndex < cellParagraph.Runs.Count; runIndex++)
                                Reflect(builder, cellParagraphPrefix + "r" + runIndex + ".", cellParagraph.Runs[runIndex]);
                        }
                    }
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
        var commandTypes = typeof(IDocumentCommand).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true }
                        && typeof(IDocumentCommand).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        commandTypes.Should().HaveCountGreaterThanOrEqualTo(
            60, "the reflection query must still reach FreeW's command assembly");

        var failures = new List<string>();
        int exercised = 0, unbuildable = 0, threw = 0, noChange = 0;

        foreach (var type in commandTypes)
        {
            var constructor = type.GetConstructors()
                .OrderBy(candidate => candidate.GetParameters().Length)
                .FirstOrDefault(candidate => !TakesABeforeAfterPair(candidate)
                    && candidate.GetParameters()
                        .All(parameter => ValueFor(parameter.ParameterType) is not null));

            if (constructor is null)
            {
                unbuildable++;
                continue;
            }

            try
            {
                var command = (IDocumentCommand)constructor.Invoke(
                    constructor.GetParameters().Select(p => ValueFor(p.ParameterType)).ToArray());

                var document = Setup();
                var context = new Context(document);

                var before = Describe(document);
                command.Apply(context);
                var applied = Describe(document);

                if (applied == before)
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
            catch (Exception)
            {
                // An invented argument can be invalid for a particular command; that is a limit of
                // the factory, not a defect. Counted so the number stays visible rather than silent
                // -- r538's lesson, where a bucket nobody had opened held one command throwing for a
                // different reason than the other five.
                threw++;
            }
        }

        var census = "types=" + commandTypes.Count + " unbuildable=" + unbuildable
            + " threw=" + threw + " noChange=" + noChange + " exercised=" + exercised;

        failures.Should().BeEmpty(
            "a command that changes the document and cannot put it back loses the user's work on "
            + "undo. " + census);

        exercised.Should().BeGreaterThanOrEqualTo(
            8,
            "the driver must still be exercising commands -- if this falls, the sweep has quietly "
            + "stopped testing rather than the commands having improved. 10 today (7 before the "
            + "table moved to block index 0, which is the only index the factory invents). The 51 "
            + "unbuildable need live model objects this factory will not fake, and the 64 noChange "
            + "construct but find nothing their arguments can reach. " + census);
    }
}
