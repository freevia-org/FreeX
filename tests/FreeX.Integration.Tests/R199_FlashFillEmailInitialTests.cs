using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Integration.Tests;

/// <summary>
/// r199, sixth instance of the char-slice class -- and the first found in a file the previous round
/// had already fixed. <c>GetEmailNameInitial</c> reimplemented the extraction that
/// <c>GetFirstInitial</c> does two calls away, so r198's fix to that one left all nine email-address
/// patterns still building the local part from one UTF-16 code unit. A first name beginning outside
/// the BMP therefore stored a lone high surrogate in the generated address.
/// </summary>
public sealed class R199_FlashFillEmailInitialTests
{
    private const string AstralFirstName = "\U00020000milia";

    [Fact]
    public void GeneratingAnEmailFromAnAstralFirstName_StoresNoLoneSurrogate()
    {
        var filled = FlashFillService.FillFromColumns(
            [["Josh", "Chen"], ["Mary", "Jones"]],
            ["jchen@acme.com", "mjones@acme.com"],
            [[AstralFirstName, "Wong"]]);

        filled.Should().NotBeNull("the first-initial-plus-last email pattern is one Flash Fill knows");
        HasLoneSurrogate(filled![0]).Should().BeFalse(
            "Flash Fill writes this into the cell; got '{0}'", filled[0]);
        filled[0].Should().Be(AstralFirstName[..2].ToLowerInvariant() + "wong@acme.com");
    }

    [Fact]
    public void AnOrdinaryNameIsUnaffected()
    {
        var filled = FlashFillService.FillFromColumns(
            [["Josh", "Chen"], ["Mary", "Jones"]],
            ["jchen@acme.com", "mjones@acme.com"],
            [["Alex", "Kim"]]);

        filled.Should().NotBeNull();
        filled![0].Should().Be("akim@acme.com");
    }

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return true;
                i++;
                continue;
            }

            if (char.IsLowSurrogate(text[i]))
                return true;
        }

        return false;
    }
}
