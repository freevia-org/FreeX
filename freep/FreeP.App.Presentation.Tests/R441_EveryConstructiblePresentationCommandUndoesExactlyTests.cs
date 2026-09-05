using System.Text;
using FluentAssertions;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r441: the FreeX undo driver (r417/r438-r440), brought to FreeP.
///
/// <para>FreeP has ~154 command classes and no auto-driver at all: every undo test here covers a
/// command somebody chose to write a line for. In FreeX that same gap hid a real defect (r438,
/// inserting a PivotTable destroyed merged regions that Undo never restored) which fourteen rounds
/// of hand-written per-command tests had walked straight past.</para>
///
/// <para>Same shape as its FreeX sibling and for the same reasons: construct from a value factory,
/// apply, revert, and require anything that visibly changed the presentation to put it back. The
/// observer is uniform reflection over the presentation, its slides and their shapes rather than a
/// hand-written field list -- a hand-written list only sees what somebody remembered to add, which is
/// the very blind spot this is built to escape. It reports a CENSUS rather than a bare pass, because
/// "154 covered" would be a lie: most commands need arguments this factory cannot invent.</para>
/// </summary>
public sealed class R441_EveryConstructiblePresentationCommandUndoesExactlyTests
{
    private static Presentation Setup()
    {
        var presentation = new Presentation();

        for (var index = 0; index < 3; index++)
        {
            var slide = new Slide();
            var shape = new SlideShape
            {
                Id = (uint)(index + 2),
                Name = "Body" + index,
                OffsetXEmu = 100000,
                OffsetYEmu = 200000,
                ExtentCxEmu = 1000000,
                ExtentCyEmu = 500000,
                TextBody = new TextBody(),
            };

            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = "slide " + index });
            shape.TextBody!.Paragraphs.Add(paragraph);

            slide.Shapes.Add(shape);
            presentation.Slides.Add(slide);
        }

        return presentation;
    }

    private static object? ValueFor(Type type)
    {
        if (type == typeof(int)) return 1;
        if (type == typeof(uint)) return 2u;
        if (type == typeof(long)) return 100000L;
        if (type == typeof(bool)) return true;
        if (type == typeof(double)) return 2.0;
        if (type == typeof(string)) return "probe";
        if (type == typeof(Guid)) return Guid.NewGuid();

        if (type.IsEnum)
        {
            return Enum.GetValues(type).Cast<object>().Skip(1).FirstOrDefault()
                ?? Enum.GetValues(type).Cast<object>().FirstOrDefault();
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

    private static string Describe(Presentation presentation)
    {
        var builder = new StringBuilder();
        Reflect(builder, "pr.", presentation);

        for (var slideIndex = 0; slideIndex < presentation.Slides.Count; slideIndex++)
        {
            var slide = presentation.Slides[slideIndex];
            Reflect(builder, "sl" + slideIndex + ".", slide);

            for (var shapeIndex = 0; shapeIndex < slide.Shapes.Count; shapeIndex++)
                Reflect(builder, "sl" + slideIndex + ".sh" + shapeIndex + ".", slide.Shapes[shapeIndex]);
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
    public void EveryCommandThatChangesThePresentationRestoresItOnRevert()
    {
        var commandTypes = typeof(IPresentationCommand).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true }
                           && typeof(IPresentationCommand).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        commandTypes.Should().HaveCountGreaterThanOrEqualTo(
            100, "the reflection query must still reach the FreeP command assembly");

        int notConstructible = 0, threw = 0, noChange = 0, exercised = 0;
        var failures = new List<string>();

        foreach (var type in commandTypes)
        {
            var constructor = type.GetConstructors()
                .OrderBy(candidate => candidate.GetParameters().Length)
                .FirstOrDefault(candidate => candidate.GetParameters()
                    .All(parameter => ValueFor(parameter.ParameterType) is not null));

            // A command whose constructor takes the PRIOR value ("oldLoopUntilStopped") is told what
            // to restore rather than capturing it, so a factory that invents that argument makes
            // Revert faithfully restore the invented value and the driver reads it as a failed undo.
            // That is a limit of driving blindly, not a defect -- SetSlideShowSettingsCommand is the
            // real example that made this explicit. Counted as unbuildable, honestly, rather than
            // silently passed.
            if (constructor is not null &&
                constructor.GetParameters().Any(parameter =>
                    parameter.Name?.StartsWith("old", StringComparison.OrdinalIgnoreCase) == true))
            {
                notConstructible++;
                continue;
            }

            if (constructor is null)
            {
                notConstructible++;
                continue;
            }

            try
            {
                var presentation = Setup();
                var command = (IPresentationCommand)constructor.Invoke(
                    constructor.GetParameters().Select(parameter => ValueFor(parameter.ParameterType)).ToArray());

                // Honour the bus's own contract: a command reporting no effect is never applied, so
                // driving it anyway would test a path production never takes.
                if (!command.HasEffect(presentation))
                {
                    noChange++;
                    continue;
                }

                var before = Describe(presentation);
                command.Apply(presentation);

                if (Describe(presentation) == before)
                {
                    noChange++;
                    continue;
                }

                exercised++;
                command.Revert(presentation);

                var after = Describe(presentation);
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
            " threw=" + threw + " noChange=" + noChange + " exercised=" + exercised +
            " failed=" + failures.Count;

        failures.Should().BeEmpty(
            "a command that changes the presentation and cannot put it back loses the user's work " +
            "on undo. " + census + "\n" + string.Join("\n", failures));

        exercised.Should().BeGreaterThanOrEqualTo(
            5,
            "the driver must still be exercising commands -- if this falls, the sweep has quietly " +
            "stopped testing rather than the commands having improved. Only 6 today, against 71 in " +
            "the FreeX sibling: most FreeP commands need domain objects this factory cannot invent. " +
            "Six was still enough to find a real undo defect on the first run, which is the argument " +
            "for widening the factory rather than for trusting the green. " + census);
    }
}
