using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r453: opening a file that is not a presentation must say so, not surface a NullReferenceException.
///
/// <para>The shell shows <c>Exception.Message</c> verbatim (see
/// <c>PresentationNativeCommandOutcomePlanner</c>), so whatever the reader throws IS the sentence the
/// user reads. Well-formed JSON that is not a presentation -- <c>{}</c>, or any object missing the
/// members <c>ToModel</c> dereferences -- deserialised into a DTO of nulls and then failed deep
/// inside it, so the user was told "Object reference not set to an instance of an object" about a
/// file this reader already owns an accurate sentence for.</para>
///
/// <para>Same fix FreeX made for the same reason in r382, which converted the bare
/// NullReferenceException from a missing <c>[Content_Types].xml</c> into the adapter's own words. The
/// original is kept as InnerException, so nothing is swallowed.</para>
/// </summary>
public sealed class R453_FxpReportsAnUnreadableFileInItsOwnWordsTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "fxp_r453_" + Guid.NewGuid().ToString("N") + ".fxp");
        File.WriteAllText(path, content);
        return path;
    }

    private static void WithFile(string content, Action<string> assert)
    {
        var path = WriteTemp(content);
        try
        {
            assert(path);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void AnEmptyJsonObjectIsReportedInTheReadersOwnWords()
    {
        WithFile("{}", path =>
        {
            var open = () => FxpFormat.Read(path);

            open.Should().Throw<InvalidDataException>(
                    "the shell shows this message verbatim, and \"Object reference not set to an " +
                    "instance of an object\" tells the user nothing about their file")
                .WithMessage("*not a valid .fxp document*");
        });
    }

    [Fact]
    public void AnUnrelatedJsonObjectIsReportedTheSameWay()
    {
        WithFile("{\"somethingElse\":123}", path =>
        {
            var open = () => FxpFormat.Read(path);

            open.Should().Throw<InvalidDataException>().WithMessage("*not a valid .fxp document*");
        });
    }

    [Fact]
    public void TheOriginalFailureIsKeptAsTheInnerException()
    {
        // r382's discipline: the message the user reads is replaced, the diagnosis is not thrown
        // away. A maintainer reading a log still sees exactly where it broke.
        WithFile("{}", path =>
        {
            var open = () => FxpFormat.Read(path);

            open.Should().Throw<InvalidDataException>()
                .WithInnerException<NullReferenceException>("nothing is swallowed");
        });
    }

    [Fact]
    public void AGenuineFxpFileStillOpens()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Body",
            OffsetXEmu = 100000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 900000,
            ExtentCyEmu = 400000,
        });
        presentation.Slides.Add(slide);

        WithFile(FxpFormat.Serialize(presentation), path =>
        {
            var reloaded = FxpFormat.Read(path);

            reloaded.Slides.Should().ContainSingle("the guard must not disturb a real .fxp file");
        });
    }
}
