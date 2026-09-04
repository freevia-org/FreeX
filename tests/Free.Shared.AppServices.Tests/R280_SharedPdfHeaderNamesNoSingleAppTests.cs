using FluentAssertions;
using Free.Shared.Pdf;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// r280: the shared PDF writer's default header comment read "FreeX portable PDF", and every PDF
/// FreeP exported carried it.
///
/// <para>FreeX and FreeW each pass their own name on their direct export paths, so the leak was
/// invisible there. FreeP routes vector exports through
/// <c>SkiaPdfWriter.WriteToBytesWithPortableFallback</c>, which called the portable writer with no
/// header at all and took the shared default -- and that fallback also DROPPED the header on the one
/// path where FreeW did pass one.</para>
///
/// <para>Same class as r279: a shared component that names one app, used by three. The remedy is the
/// same shape too -- the shared default is now product-neutral and each app supplies its own name,
/// rather than the shared tier guessing on their behalf.</para>
/// </summary>
public sealed class R280_SharedPdfHeaderNamesNoSingleAppTests
{
    [Theory]
    [InlineData("FreeX")]
    [InlineData("FreeW")]
    [InlineData("FreeP")]
    public void TheSharedDefaultHeaderDoesNotNameAnyOneApp(string product)
    {
        PortablePdfWriter.DefaultHeaderComment.Should().NotContain(product,
            "this default reaches every app that does not pass its own header, so naming one of them "
            + "stamps that name into the other two apps' exported files");
    }

    [Fact]
    public void TheSharedDefaultIsStillAUsablePdfComment()
    {
        PortablePdfWriter.DefaultHeaderComment.Should().NotBeNullOrWhiteSpace(
            "the header comment is written into the file after the %PDF marker");
        PortablePdfWriter.DefaultHeaderComment.Should().NotContain("\n",
            "a newline would split the comment and corrupt the header");
    }
}
