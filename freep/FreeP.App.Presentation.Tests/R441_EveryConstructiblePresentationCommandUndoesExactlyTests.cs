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
            // r533: seed the three containers the extended Describe can now see into. Same lesson
            // as r442, r447 and r481 one level further in -- walking a collection is only half of
            // it, something has to BE there and the invented arguments have to REACH it. The
            // factory answers uint with 2 and int with 1, so every slide gets these and each list
            // gets TWO entries: a single entry sits at index 0, which an invented index of 1 can
            // never address. Seeding slide 0 alone had exactly that effect and moved nothing.
            {
                var group = new SlideShape
                {
                    Id = 3,
                    Name = "Group0",
                    Kind = SlideShapeKind.Group,
                    OffsetXEmu = 500000,
                    OffsetYEmu = 600000,
                    ExtentCxEmu = 900000,
                    ExtentCyEmu = 700000,
                };
                group.Children.Add(new SlideShape { Id = 5, Name = "GroupChild1", ExtentCxEmu = 100000, ExtentCyEmu = 100000 });
                group.Children.Add(new SlideShape
                {
                    Id = 4,
                    Name = "GroupChild0",
                    OffsetXEmu = 510000,
                    OffsetYEmu = 610000,
                    ExtentCxEmu = 100000,
                    ExtentCyEmu = 100000,
                });
                slide.Shapes.Add(group);

                for (var animationSeed = 0; animationSeed < 2; animationSeed++)
                {
                    slide.Animations.Add(new ShapeAnimation
                    {
                        ShapeId = 2,
                        Motion = new MotionPath(),
                    });
                }

                var comment = new SlideComment
                {
                    AuthorId = 2,
                    Author = "Author",
                    Text = "seeded comment",
                };
                comment.Replies.Add(new SlideCommentReply
                {
                    AuthorId = 2,
                    Author = "Author",
                    Text = "seeded reply",
                });
                slide.Comments.Add(comment);
                slide.Comments.Add(new SlideComment { AuthorId = 2, Author = "Author", Text = "second" });
            }

            presentation.Slides.Add(slide);
        }

        // r447: a master and a layout, because MasterEditTarget was the second most common thing
        // blocking construction (6 constructors) and every one of those commands addresses a master
        // or layout BY ID. Seeding them is only half of it -- the factory below answers with these
        // exact ids, since state the invented arguments cannot reach changes nothing (r442's lesson).
        var master = new SlideMaster { Id = MasterId };
        var layout = new SlideLayout { Id = LayoutId };

        // r481: seed a placeholder the invented arguments can actually ADDRESS. Add/Delete/Move/
        // Resize/Rotate/SetFill in Slide Master view all name a shape by id, and the factory below
        // answers uint with 2, so an empty master left every one of them with nothing to act on --
        // they applied nothing, matched their own before-state, and were filed as noChange. Same
        // lesson as r447 and r442 one level in: seeding the container is only half of it, the ids
        // have to line up too.
        foreach (var placeholders in new[] { master.Placeholders, layout.Placeholders })
        {
            placeholders.Add(new SlideShape
            {
                Id = 2,
                Name = "MasterBody",
                OffsetXEmu = 300000,
                OffsetYEmu = 400000,
                ExtentCxEmu = 800000,
                ExtentCyEmu = 600000,
            });
        }

        presentation.Masters.Add(master);
        presentation.Layouts.Add(layout);

        return presentation;
    }

    private const string MasterId = "master1";
    private const string LayoutId = "layout1";

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

        // r447: the domain types that actually blocked construction, measured across every command
        // this factory could NOT build rather than guessed: MasterEditTarget (6 constructors),
        // ShapeFill (5), SlideShape / ShapeOutline / TextBody (3 each), Slide (2). FreeP has no
        // single dominant blocker the way FreeX had Nullable and IReadOnlyList, so this is a short
        // tail rather than one sweeping addition.
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

    /// <summary>
    /// r533: a shape is not a leaf. A GROUP holds its members in Children, and a shape carries
    /// several other model-object collections -- custom geometry, connection sites, media bookmarks
    /// and caption tracks -- every one of which Reflect renders as a row of bare type names. Walking
    /// them is what makes an edit inside a group, or to a shape's geometry, visible to the census at
    /// all; without it such a command applies a real change, fingerprints identically, and is filed
    /// as noChange with its undo never checked.
    /// </summary>
    private static void ReflectShape(StringBuilder builder, string prefix, object shape, int depth)
    {
        Reflect(builder, prefix, shape);

        // A group that (through a malformed file or a bad command) contains itself would otherwise
        // recurse forever and take the test host down with it -- an uncatchable StackOverflow, as
        // r501 established. The cap is far above any real nesting depth.
        if (depth >= 16)
            return;

        foreach (var (name, items) in ShapeChildCollections(shape))
        {
            var index = 0;
            foreach (var item in items)
            {
                if (item is null)
                    continue;

                var childPrefix = prefix + name + index + ".";
                if (string.Equals(name, "ch", StringComparison.Ordinal))
                    ReflectShape(builder, childPrefix, item, depth + 1);
                else
                    Reflect(builder, childPrefix, item);
                index++;
            }
        }
    }

    private static IEnumerable<(string Name, System.Collections.IEnumerable Items)> ShapeChildCollections(object shape)
    {
        foreach (var (property, name) in new[]
                 {
                     ("Children", "ch"),
                     ("CustomGeometry", "cg"),
                     ("CustomConnectionSites", "cs"),
                     ("Bookmarks", "bk"),
                     ("CaptionTracks", "ct"),
                 })
        {
            if (shape.GetType().GetProperty(property)?.GetValue(shape) is System.Collections.IEnumerable items
                and not string)
            {
                yield return (name, items);
            }
        }
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
                ReflectShape(builder, "sl" + slideIndex + ".sh" + shapeIndex + ".", slide.Shapes[shapeIndex], depth: 0);

            // r533: the same blind spot r481 fixed for masters and layouts, one level further in.
            // Reflect renders a collection element as its ToString(), which for a model object is
            // the bare type name, so a slide's ANIMATIONS and COMMENTS read as "[...ShapeAnimation]"
            // however they were edited. Any command that retimes an animation, or edits a comment or
            // one of its replies, produced an identical fingerprint, was filed as noChange, and had
            // its undo and redo checked by nothing at all.
            for (var animationIndex = 0; animationIndex < slide.Animations.Count; animationIndex++)
            {
                var animation = slide.Animations[animationIndex];
                var animationPrefix = "sl" + slideIndex + ".an" + animationIndex + ".";
                Reflect(builder, animationPrefix, animation);

                // A motion path is reached through Animation.Motion, and its Segments are the shape of
                // the path -- edit them and the animation object itself is unchanged.
                if (animation.Motion is { } motion)
                {
                    Reflect(builder, animationPrefix + "mp.", motion);
                    for (var segmentIndex = 0; segmentIndex < motion.Segments.Count; segmentIndex++)
                        Reflect(builder, animationPrefix + "mp.seg" + segmentIndex + ".", motion.Segments[segmentIndex]);
                }
            }

            for (var commentIndex = 0; commentIndex < slide.Comments.Count; commentIndex++)
            {
                var comment = slide.Comments[commentIndex];
                var commentPrefix = "sl" + slideIndex + ".co" + commentIndex + ".";
                Reflect(builder, commentPrefix, comment);

                for (var replyIndex = 0; replyIndex < comment.Replies.Count; replyIndex++)
                    Reflect(builder, commentPrefix + "re" + replyIndex + ".", comment.Replies[replyIndex]);
            }
        }

        // r481: masters and layouts need the SAME explicit walk the slides get above. Reflect renders
        // a collection element as its ToString(), which for a model object is the constant type name,
        // so `pr.Masters` read as "[...SlideMaster]" no matter what happened inside it. Every command
        // authored in Slide Master view therefore applied a real change, produced an identical
        // fingerprint, and was filed as noChange -- counted by the census while nothing about its
        // undo or redo was ever checked. The contents-not-counts comment above defends against an
        // edit hiding behind an unchanged collection SIZE; this is the same defect one level down,
        // an edit hiding behind an unchanged element RENDERING.
        for (var masterIndex = 0; masterIndex < presentation.Masters.Count; masterIndex++)
        {
            var master = presentation.Masters[masterIndex];
            Reflect(builder, "ma" + masterIndex + ".", master);

            for (var shapeIndex = 0; shapeIndex < master.Placeholders.Count; shapeIndex++)
                ReflectShape(builder, "ma" + masterIndex + ".ph" + shapeIndex + ".", master.Placeholders[shapeIndex], depth: 0);
        }

        for (var layoutIndex = 0; layoutIndex < presentation.Layouts.Count; layoutIndex++)
        {
            var layout = presentation.Layouts[layoutIndex];
            Reflect(builder, "la" + layoutIndex + ".", layout);

            for (var shapeIndex = 0; shapeIndex < layout.Placeholders.Count; shapeIndex++)
                ReflectShape(builder, "la" + layoutIndex + ".ph" + shapeIndex + ".", layout.Placeholders[shapeIndex], depth: 0);
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

        int notConstructible = 0, threw = 0, noChange = 0, exercised = 0, claimedNoEffect = 0;
        var failures = new List<string>();
        var falseNoEffect = new List<string>();
        var redoFailures = new List<string>();

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

                // r443: the bus skips a command reporting no effect ENTIRELY -- no Apply, no undo
                // entry. So a command that says false and would in fact have changed something
                // makes the user's action vanish: they click, nothing happens, and there is no
                // error and nothing to undo. Check the claim instead of trusting it.
                if (!command.HasEffect(presentation))
                {
                    claimedNoEffect++;
                    var unchangedBefore = Describe(presentation);
                    command.Apply(presentation);

                    if (Describe(presentation) != unchangedBefore)
                    {
                        falseNoEffect.Add(
                            type.Name + " [" + FirstDifference(unchangedBefore, Describe(presentation)) + "]");
                    }

                    noChange++;
                    continue;
                }

                var before = Describe(presentation);
                command.Apply(presentation);

                var applied = Describe(presentation);
                if (applied == before)
                {
                    noChange++;
                    continue;
                }

                exercised++;
                command.Revert(presentation);

                var after = Describe(presentation);
                if (after != before)
                {
                    failures.Add(type.Name + " [" + FirstDifference(before, after) + "]");
                    continue;
                }

                // r458: REDO, the other half of the contract. Ctrl+Y after Ctrl+Z must put back
                // exactly what Ctrl+Z removed; a command whose Revert does not reset the state its
                // Apply captured produces a third state the user never made. r441's own fix in this
                // app had to clear an "I created this placeholder" flag on Revert precisely so a
                // second Apply would work, and r457 then found a real defect of this shape in FreeX.
                command.Apply(presentation);
                var redone = Describe(presentation);
                if (redone != applied)
                    redoFailures.Add(type.Name + " [" + FirstDifference(applied, redone) + "]");
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
            " exercised=" + exercised +
            " failed=" + failures.Count;

        failures.Should().BeEmpty(
            "a command that changes the presentation and cannot put it back loses the user's work " +
            "on undo. " + census + "\n" + string.Join("\n", failures));

        falseNoEffect.Should().BeEmpty(
            "the bus skips a command reporting HasEffect false entirely, so one that would in fact " +
            "have changed the presentation makes the user's action vanish: they click, nothing " +
            "happens, there is no error and nothing to undo. " + census + "\n" +
            string.Join("\n", falseNoEffect));

        redoFailures.Should().BeEmpty(
            "r458: redo is the other half of the contract -- a command whose Revert does not reset " +
            "the state its Apply captured redoes something DIFFERENT from what undo removed. " +
            census + "\n" + string.Join("\n", redoFailures));

        claimedNoEffect.Should().BeGreaterThan(
            0,
            "the HasEffect check above is only worth having if commands actually reach it -- if no " +
            "command in the census ever reports no effect, that assertion is vacuous. " + census);

        exercised.Should().BeGreaterThanOrEqualTo(
            17,
            "the driver must still be exercising commands -- if this falls, the sweep has quietly " +
            "stopped testing rather than the commands having improved. 19 today (6 at first writing, " +
            "12 before r481, 18 before r533) against 71 in the FreeX sibling: most FreeP commands still need domain " +
            "objects this factory cannot invent. r481 took the last step this message argued for, " +
            "widening the fixture rather than trusting the green -- and found that the six Slide " +
            "Master commands had been applying REAL changes the whole time while landing in noChange, " +
            "because Describe never walked masters or layouts. Widening the factory keeps paying; " +
            "the floor is deliberately below the current count so a genuine gain is not required to " +
            "keep the suite green, but a regression is caught. " + census);
    }
}
