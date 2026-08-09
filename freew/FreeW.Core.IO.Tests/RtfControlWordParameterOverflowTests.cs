using System.IO;
using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// A control-word parameter is an unbounded digit run in the source text, and the source can be a
/// pasted clipboard payload. int.Parse threw OverflowException on a long enough run, and the paste
/// path guards only InvalidDataException and ArgumentException — OverflowException derives from
/// ArithmeticException, so it escaped and took the app down. The reader clamps instead now.
/// </summary>
public class RtfControlWordParameterOverflowTests
{
    private static TextDocument Read(string rtf)
    {
        using var stream = new MemoryStream(Encoding.Latin1.GetBytes(rtf));
        return RtfReader.Read(stream);
    }

    [Fact]
    public void Read_ControlWordParameterTooLargeForInt_ReadsTheTextInsteadOfThrowing()
    {
        var document = Read(@"{\rtf1\ansi\f99999999999 hello}");

        document.PlainText.Should().Contain("hello");
    }

    [Fact]
    public void Read_NegativeControlWordParameterTooLargeForInt_ReadsTheTextInsteadOfThrowing()
    {
        var document = Read(@"{\rtf1\ansi\li-99999999999 hello}");

        document.PlainText.Should().Contain("hello");
    }

    [Fact]
    public void Read_FontAndColorTableParametersTooLargeForInt_ReadTheTextInsteadOfThrowing()
    {
        // The font-table and colour-table parsers read their own digit runs, separately from the
        // generic control-word path.
        var document = Read(
            @"{\rtf1\ansi{\fonttbl{\f99999999999 Arial;}}{\colortbl;\red255\green0\blue0;}" +
            @"\cf99999999999 hello}");

        document.PlainText.Should().Contain("hello");
    }

    [Fact]
    public void Read_OrdinaryControlWordParameter_StillApplies()
    {
        // The clamp must not disturb parameters in the normal range.
        var document = Read(@"{\rtf1\ansi\fs48 sized}");

        document.PlainText.Should().Contain("sized");
    }
}
