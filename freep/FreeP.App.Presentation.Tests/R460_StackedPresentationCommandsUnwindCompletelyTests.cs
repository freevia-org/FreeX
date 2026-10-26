using System.Text;
using FluentAssertions;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r460: stacking two presentation commands and undoing both must return the deck exactly.
///
/// <para>The FreeX sibling of this test (r459) found nothing; porting the redo contract the same way
/// in r458 found a real defect here on its first run, so a clean result in one app says nothing about
/// the others. That is the whole reason each contract is carried to all three rather than assumed to
/// generalise.</para>
///
/// <para>Runs through the real <see cref="PresentationCommandBus"/> rather than calling Apply/Revert
/// directly, so the stack's own bookkeeping -- push order, redo invalidation, the depth and byte
/// budgets -- is part of what is exercised.</para>
/// </summary>
public sealed class R460_StackedPresentationCommandsUnwindCompletelyTests
{
    private const string MasterId = "master1";
    private const string LayoutId = "layout1";

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

        presentation.Masters.Add(new SlideMaster { Id = MasterId });
        presentation.Layouts.Add(new SlideLayout { Id = LayoutId });
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

        if (type == typeof(MasterEditTarget)) return MasterEditTarget.Master(MasterId);
        if (type == typeof(ShapeFill)) return new ShapeFill.Solid(SrgbColor.FromRgb(0x33AA66));
        if (type == typeof(ShapeOutline))
            return new ShapeOutline.Visible(new ThemeAwareColor(SrgbColor.FromRgb(0xFF0000)), widthPt: 1.5);

        if (type == typeof(TextBody))
        {
            var body = new TextBody();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = "probe" });
            body.Paragraphs.Add(paragraph);
            return body;
        }

        if (type == typeof(SlideShape))
        {
            return new SlideShape
            {
                Id = 99,
                Name = "Probe",
                OffsetXEmu = 100000,
                OffsetYEmu = 200000,
                ExtentCxEmu = 500000,
                ExtentCyEmu = 400000,
            };
        }

        if (type == typeof(Slide)) return new Slide();

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

    private static IPresentationCommand? Build(Type type)
    {
        var constructor = type.GetConstructors()
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault(candidate => candidate.GetParameters()
                .All(parameter => ValueFor(parameter.ParameterType) is not null));

        if (constructor is null)
            return null;

        // r441: a command told its PRIOR value restores whatever the factory invented, which is a
        // limit of driving blindly rather than a defect.
        if (constructor.GetParameters().Any(parameter =>
                parameter.Name?.StartsWith("old", StringComparison.OrdinalIgnoreCase) == true))
        {
            return null;
        }

        return (IPresentationCommand)constructor.Invoke(
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
    public void UndoingAStackOfTwoCommandsRestoresThePresentationExactly()
    {
        var commandTypes = typeof(IPresentationCommand).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true }
                           && typeof(IPresentationCommand).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        var usable = new List<Type>();
        foreach (var type in commandTypes)
        {
            try
            {
                var presentation = Setup();
                var command = Build(type);
                if (command is null || !command.HasEffect(presentation))
                    continue;

                var before = Describe(presentation);
                command.Apply(presentation);
                if (Describe(presentation) != before)
                    usable.Add(type);
            }
            catch
            {
                // Factory limits, counted by r441's census rather than here.
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
                var presentation = Setup();
                var bus = new PresentationCommandBus(presentation);
                var before = Describe(presentation);

                var firstCommand = Build(first);
                var secondCommand = Build(second);
                if (firstCommand is null || secondCommand is null)
                    continue;

                bus.Execute(firstCommand);
                bus.Execute(secondCommand);
                pairsExecuted++;

                while (bus.CanUndo)
                    bus.Undo();

                var after = Describe(presentation);
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
            "undoing every edit must return the deck the user started with -- a command that captures " +
            "its undo state at construction, or assumes the deck still looks the way it did when it " +
            "was built, survives every single-command test and fails here. " + census + "\n" +
            string.Join("\n", failures));

        pairsExecuted.Should().BeGreaterThanOrEqualTo(
            8,
            "the sweep must still be stacking real pairs -- if this falls it has quietly stopped " +
            "testing rather than the commands having improved. " + census);
    }
}
