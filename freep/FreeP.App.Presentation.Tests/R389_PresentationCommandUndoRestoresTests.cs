using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r389: FreeP's presentation commands must undo exactly, the same contract r387/r388 pinned for
/// FreeW.
///
/// <para><c>IPresentationCommand</c> has 137 implementations, each with <c>Apply</c> and
/// <c>Revert</c>. A revert that restores most of the state leaves a deck the user believes they
/// undid -- and a slide deck makes that easy to miss, because the difference may be a property that
/// only shows on one slide.</para>
///
/// <para>Comparison is by the hash of the written .pptx, so anything the format carries counts. The
/// test asserts the deck ACTUALLY CHANGED between apply and revert: without that, a command whose
/// Apply silently did nothing would pass the undo assertion trivially. In FreeW that guard caught two
/// bad fixtures of mine before they became false "verified" results.</para>
/// </summary>
public sealed class R389_PresentationCommandUndoRestoresTests
{
    private static Presentation BuildPresentation()
    {
        var presentation = new Presentation();

        for (var i = 0; i < 3; i++)
        {
            var slide = new Slide();
            var shape = new SlideShape
            {
                Id = (uint)(i + 2),
                Name = "Body " + i,
                TextBody = new TextBody(),
            };

            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = "slide " + i });
            shape.TextBody!.Paragraphs.Add(paragraph);
            slide.Shapes.Add(shape);

            // r412: a SECOND shape, offset so it does not coincide with the first. Z-order and
            // alignment commands are no-ops on a single shape, and the change-gate below rejects a
            // no-op -- correctly, since undoing nothing proves nothing. One shape per slide silently
            // capped what this harness could cover.
            slide.Shapes.Add(new SlideShape
            {
                Id = (uint)(i + 20),
                Name = "Second " + i,
                OffsetXEmu = 914400,
                OffsetYEmu = 457200,
                ExtentCxEmu = 1828800,
                ExtentCyEmu = 914400,
            });

            presentation.Slides.Add(slide);
        }

        return presentation;
    }

    /// <summary>
    /// Compares the package CONTENT, not its bytes. Every ZIP entry is stamped with the wall clock --
    /// in FreeP and FreeX alike, as Office itself does -- so two writes of an identical deck differ in
    /// bytes once a second ticks between them. Hashing the archive made this test report a
    /// broken undo for InsertSlide that was purely the clock moving.
    /// </summary>
    private static string Serialize(Presentation presentation)
    {
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        var builder = new System.Text.StringBuilder();
        foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            builder.AppendLine(entry.FullName);
            using var entryStream = entry.Open();
            builder.AppendLine(new StreamReader(entryStream).ReadToEnd());
        }

        return builder.ToString();
    }

    private static void Check(string label, Func<Presentation, IPresentationCommand> factory)
    {
        var presentation = BuildPresentation();
        var before = Serialize(presentation);

        var command = factory(presentation);
        command.Apply(presentation);

        Serialize(presentation).Should().NotBe(before,
            "{0} must actually change the deck, or the undo assertion below proves nothing", label);

        command.Revert(presentation);

        Serialize(presentation).Should().Be(before,
            "{0}: undo must restore the deck exactly", label);
    }

    /// <summary>
    /// Writing a deck must not renumber slides that already have an id.
    ///
    /// <para>This is what the undo test above actually caught. The failure LOOKED like a broken
    /// InsertSlideCommand.Revert, but the command was fine: the writer allocated ids while walking
    /// slides in document order, so an inserted slide with no id yet took the next counter value --
    /// 257, which the following slide already owned -- and the collision path then reassigned that
    /// slide to 258 and its successor to 259, storing the new ids back onto the model. Saving the
    /// deck permanently changed the identity of slides the user never touched.</para>
    ///
    /// <para>Slide ids are not cosmetic: zoom targets (<c>p:sldZmObj/@sldId</c>), custom shows and
    /// section membership all reference slides by id, so a save that renumbers them silently
    /// repoints those references. PowerPoint never renumbers an existing slide on insert.</para>
    /// </summary>
    [Fact]
    public void WritingADeckNeverChangesTheIdOfASlideThatAlreadyHasOne()
    {
        var presentation = BuildPresentation();

        // First write assigns the initial ids.
        Serialize(presentation);
        var original = presentation.Slides.Select(slide => slide.NumericId).ToList();
        original.Should().NotContain((uint?)null, "the first write assigns every slide an id");

        // Insert a slide with no id BEFORE existing slides -- the case that used to renumber them.
        presentation.Slides.Insert(1, new Slide());
        Serialize(presentation);

        var inserted = presentation.Slides[1];
        inserted.NumericId.Should().NotBeNull("the new slide must be given an id");
        original.Should().NotContain(inserted.NumericId,
            "the inserted slide must take a FRESH id, not one an existing slide already owns");

        var survivors = presentation.Slides.Where(slide => !ReferenceEquals(slide, inserted))
            .Select(slide => slide.NumericId);
        survivors.Should().Equal(original,
            "saving must not change the id of any slide that already had one -- zoom targets, custom " +
            "shows and section membership reference slides by id");
    }

    [Fact]
    public void EveryCoveredCommandUndoesExactly()
    {
        Check("InsertSlide", _ => new InsertSlideCommand(1, new Slide()));
        Check("DeleteSlide", _ => new DeleteSlideCommand(1));
        Check("DuplicateSlide", _ => new DuplicateSlideCommand(1));
        Check("MoveSlide", _ => new MoveSlideCommand(0, 2));
        Check("SetSlideHidden", _ => new SetSlideHiddenCommand(1, true));
        Check("SetShapeHidden", p => new SetShapeHiddenCommand(1, p.Slides[1].Shapes[0].Id, true));

        // r412: shape-level commands, which the original six never reached -- they covered slide
        // structure only, so a Revert that restored slides but mangled a shape would have passed.
        Check("AddShape", _ => new AddShapeCommand(1, new SlideShape
        {
            Id = 900,
            Name = "Added",
            OffsetXEmu = 100000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 300000,
            ExtentCyEmu = 400000,
        }));

        Check("DeleteShape", p => new DeleteShapeCommand(1, p.Slides[1].Shapes[0].Id));

        Check("MoveShape", p => new MoveShapeCommand(1, p.Slides[1].Shapes[0].Id, 123456, 654321));

        // Deliberately the SECOND shape: the first has no explicit transform, and flipping it
        // changes nothing in the written package -- see FlippingAShapeWithNoTransformIsNotPersisted
        // below, which pins that as measured behaviour rather than leaving it as a silent skip.
        Check("FlipShape", p => new FlipShapeCommand(1, p.Slides[1].Shapes[1].Id, horizontal: true));

        Check("BringToFront", p => new BringToFrontCommand(1, p.Slides[1].Shapes[0].Id));

        Check("PasteSlide", _ => new PasteSlideCommand(1, new Slide()));
    }
}
